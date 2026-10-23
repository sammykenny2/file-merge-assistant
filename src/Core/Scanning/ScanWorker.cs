using System.Diagnostics;
using System.Globalization;
using FileMergeAssistant.Index;
using FileMergeAssistant.Sources;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Scanning;

/// <summary>
/// Runs queued scan jobs one at a time on a background task (SPEC-001: one scan writes at a time). Job rows in the index
/// carry the state, so a job that was running when the app stopped is found as interrupted on the next start.
/// </summary>
public sealed class ScanWorker : IDisposable
{
  private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(400);

  private readonly IndexDatabase _database;
  private readonly SourceScanner _scanner;
  private readonly IVolumeInfoProvider _volumes;
  private readonly IReadOnlyList<string> _excludedRoots;
  private readonly SemaphoreSlim _signal = new(0);
  private readonly CancellationTokenSource _shutdown = new();
  private readonly object _gate = new();
  private Task? _loop;
  private string? _runningJob;
  private CancellationTokenSource? _jobCancel;
  private bool _pauseRequested;

  /// <param name="database">The index.</param>
  /// <param name="volumes">Checks that a source's folder is on the volume it was added from (ADR-010).</param>
  /// <param name="excludedRoots">Folders never scanned, such as the app's data folder with its recycle area.</param>
  public ScanWorker(IndexDatabase database, IVolumeInfoProvider volumes, IReadOnlyList<string> excludedRoots)
  {
    _database = database;
    _scanner = new SourceScanner(database);
    _volumes = volumes;
    _excludedRoots = excludedRoots;
  }

  /// <summary>Raised on the worker thread when a job's progress or state changes; <c>true</c> when the job ended.</summary>
  public event EventHandler<bool>? JobChanged;

  /// <summary>Marks jobs left running by an earlier session as interrupted (their saved batches stay valid).</summary>
  public static void RecoverInterrupted(IndexDatabase database)
  {
    ArgumentNullException.ThrowIfNull(database);
    using var connection = database.Open();
    IndexDatabase.Execute(connection,
      "UPDATE jobs SET state = 'Interrupted', reason = '上次掃描時程式沒有正常結束。已保存的索引仍可使用；還沒保存的部分會重新處理，不會產生重複紀錄。', saved = discovered WHERE state IN ('Running', 'Pausing')");
  }

  public void Start() => _loop ??= Task.Run(LoopAsync);

  /// <summary>Wakes the worker after a job was queued.</summary>
  public void Wake() => _signal.Release();

  public bool IsRunning(string jobId)
  {
    lock (_gate)
    {
      return _runningJob == jobId;
    }
  }

  /// <summary>Asks the running job to stop after its current batch.</summary>
  public void Pause(string jobId)
  {
    lock (_gate)
    {
      if (_runningJob == jobId)
      {
        _pauseRequested = true;
      }
    }
  }

  /// <summary>Stops the running job; committed batches stay in the index.</summary>
  public void Cancel(string jobId)
  {
    lock (_gate)
    {
      if (_runningJob == jobId)
      {
        _jobCancel?.Cancel();
      }
    }
  }

  public void Dispose()
  {
    _shutdown.Cancel();
    lock (_gate)
    {
      _jobCancel?.Cancel();
    }
    try
    {
      _loop?.Wait(TimeSpan.FromSeconds(10));
    }
    catch (AggregateException)
    {
      // The loop ends with cancellation; nothing to report on shutdown.
    }
    _signal.Dispose();
    _shutdown.Dispose();
  }

  private async Task LoopAsync()
  {
    while (!_shutdown.IsCancellationRequested)
    {
      try
      {
        var job = NextJob();
        if (job is null)
        {
          await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
          continue;
        }
        Run(job.Value.JobId, job.Value.Source, job.Value.Serial);
      }
      catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
      {
        return;
      }
#pragma warning disable CA1031 // The loop outlives any one failure (for example a locked index); it retries after a pause.
      catch (Exception)
#pragma warning restore CA1031
      {
        try
        {
          await Task.Delay(TimeSpan.FromSeconds(2), _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
          return;
        }
      }
    }
  }

  /// <summary>
  /// Claims the next queued job in one statement, so two workers can never run the same job. A paused job holds the
  /// queue until it is resumed or cancelled.
  /// </summary>
  private (string JobId, ScanSource Source, string? Serial)? NextJob()
  {
    using var connection = _database.Open();
    if (IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE state IN ('Paused', 'Pausing', 'Running')") > 0)
    {
      return null;
    }
    var jobId = IndexDatabase.Scalar<string>(connection,
      """
      UPDATE jobs SET state = 'Running', phase = 'Discover'
      WHERE id = (SELECT id FROM jobs WHERE state = 'Queued' ORDER BY position, queued_at LIMIT 1) AND state = 'Queued'
      RETURNING id
      """);
    if (jobId is null)
    {
      return null;
    }
    lock (_gate)
    {
      _runningJob = jobId;
    }
    using var command = IndexDatabase.Command(connection,
      """
      SELECT s.id, s.root, s.skip_regenerable, s.skip_cloud, s.regenerable_names, s.exclusions, s.volume_serial
      FROM jobs j JOIN sources s ON s.id = j.source_id WHERE j.id = $id
      """, ("$id", jobId));
    using var reader = command.ExecuteReader();
    if (!reader.Read())
    {
      return null;
    }
    var source = new ScanSource(reader.GetString(0), reader.GetString(1), reader.GetInt64(2) != 0, reader.GetInt64(3) != 0,
      SplitLines(reader.GetString(4)), SplitLines(reader.GetString(5)))
    {
      ExcludedRoots = _excludedRoots,
    };
    return (jobId, source, reader.IsDBNull(6) ? null : reader.GetString(6));
  }

  /// <summary>True when the folder exists and is on the same volume it was added from.</summary>
  private bool IsOnline(string root, string? serial) =>
    Directory.Exists(root) && (serial is null || _volumes.Describe(root)?.Serial is not { } current || current == serial);

  /// <summary>Roots of every source that is online now, read fresh at the start of each job.</summary>
  private Dictionary<string, string> OnlineRoots()
  {
    var roots = new Dictionary<string, string>(StringComparer.Ordinal);
    using var connection = _database.Open();
    using var command = IndexDatabase.Command(connection, "SELECT id, root, volume_serial FROM sources");
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      if (IsOnline(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)))
      {
        roots[reader.GetString(0)] = reader.GetString(1);
      }
    }
    return roots;
  }

  private void Run(string jobId, ScanSource source, string? serial)
  {
    var cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
    lock (_gate)
    {
      _runningJob = jobId;
      _jobCancel = cancel;
      _pauseRequested = false;
    }
    try
    {
      RunClaimed(jobId, source, serial, cancel);
    }
    finally
    {
      Finish(cancel);
      JobChanged?.Invoke(this, true);
    }
  }

  private void RunClaimed(string jobId, ScanSource source, string? serial, CancellationTokenSource cancel)
  {
    using var connection = _database.Open();
    if (!IsOnline(source.Root, serial))
    {
      // Missing, or another disk under the same letter: never index it as this source.
      SetState(connection, jobId, "WaitingForSource", Directory.Exists(source.Root)
        ? "這個位置現在是另一顆硬碟（磁碟區序號不同），所以沒有掃描。接上原本那顆後再試。"
        : "硬碟離線，更新工作先保留在這裡。接上後會先確認是同一顆硬碟，再開始。");
      return;
    }
    JobChanged?.Invoke(this, false);

    var lastSave = Stopwatch.StartNew();
    ScanProgress? last = null;
    void Report(ScanProgress progress)
    {
      last = progress;
      if (lastSave.Elapsed < ProgressInterval)
      {
        return;
      }
      SaveProgress(connection, jobId, progress);
      lastSave.Restart();
      JobChanged?.Invoke(this, false);
    }

    ScanStop stop;
    string? failure = null;
    try
    {
      var roots = OnlineRoots();
      // This job's own root passed the volume check just now, whatever the snapshot says.
      roots[source.Id] = source.Root;
      stop = _scanner.Run(source, jobId, roots, Report, () =>
      {
        lock (_gate)
        {
          return _pauseRequested;
        }
      }, cancel.Token);
    }
    catch (OperationCanceledException)
    {
      stop = ScanStop.Canceled;
    }
#pragma warning disable CA1031 // A scan must never take the worker down; the job is marked interrupted with the reason.
    catch (Exception e)
#pragma warning restore CA1031
    {
      stop = ScanStop.Canceled;
      failure = e.Message;
    }

    if (last is not null)
    {
      SaveProgress(connection, jobId, last);
    }
    SaveCounts(connection, source.Id);
    var now = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
    if (_shutdown.IsCancellationRequested)
    {
      SetState(connection, jobId, "Interrupted", "程式結束時掃描還沒完成。已保存的索引仍可使用；繼續時只會重做還沒保存的部分。");
    }
    else if (failure is not null)
    {
      SetState(connection, jobId, "Interrupted", $"掃描中斷：{failure}");
    }
    else if (stop == ScanStop.Paused)
    {
      SetState(connection, jobId, "Paused", "已暫停，不會再安排新檔案。佇列裡的其他工作也會等這個繼續或取消。");
    }
    else if (stop == ScanStop.Canceled)
    {
      IndexDatabase.Execute(connection, "DELETE FROM jobs WHERE id = $id", ("$id", jobId));
    }
    else
    {
      var errors = last?.Errors ?? 0;
      IndexDatabase.Execute(connection,
        "UPDATE jobs SET state = 'Completed', phase = 'Done', finished_at = $at, current_path = NULL WHERE id = $id",
        ("$at", now), ("$id", jobId));
      IndexDatabase.Execute(connection, "UPDATE sources SET last_scan_at = $at, last_scan_errors = $e, last_seen_at = $at WHERE id = $id",
        ("$at", now), ("$e", errors), ("$id", source.Id));
      // Keep only the latest finished job per source.
      IndexDatabase.Execute(connection, "DELETE FROM jobs WHERE source_id = $s AND state = 'Completed' AND id <> $id",
        ("$s", source.Id), ("$id", jobId));
    }
  }

  private void Finish(CancellationTokenSource cancel)
  {
    lock (_gate)
    {
      _runningJob = null;
      _jobCancel = null;
      _pauseRequested = false;
    }
    cancel.Dispose();
  }

  /// <summary>Stores a source's counts for the interface (one pass over the source's rows, after a scan stops).</summary>
  private static void SaveCounts(SqliteConnection connection, string sourceId) =>
    IndexDatabase.Execute(connection,
      """
      UPDATE sources SET
        indexed_files = (SELECT COUNT(*) FROM files f WHERE f.source_id = $s AND f.cloud_only = 0),
        cloud_files = (SELECT COUNT(*) FROM files f WHERE f.source_id = $s AND f.cloud_only = 1),
        regenerable_files = (SELECT COALESCE(SUM(files), 0) FROM regenerable_dirs r WHERE r.source_id = $s)
      WHERE id = $s
      """,
      ("$s", sourceId));

  private static void SetState(SqliteConnection connection, string jobId, string state, string reason) =>
    IndexDatabase.Execute(connection, "UPDATE jobs SET state = $state, reason = $reason, saved = discovered WHERE id = $id",
      ("$state", state), ("$reason", reason), ("$id", jobId));

  private static void SaveProgress(SqliteConnection connection, string jobId, ScanProgress p) =>
    IndexDatabase.Execute(connection,
      """
      UPDATE jobs SET phase = $phase, discovered = $d, candidates = $c, hashed = $h, succeeded = $ok, skipped = $sk, errors = $e,
        current_path = $path, current_size = $size, last_checkpoint_at = COALESCE($cp, last_checkpoint_at)
      WHERE id = $id
      """,
      ("$phase", p.Phase.ToString()), ("$d", p.Discovered), ("$c", p.Candidates), ("$h", p.Hashed), ("$ok", p.Succeeded),
      ("$sk", p.Skipped), ("$e", p.Errors), ("$path", p.CurrentPath), ("$size", p.CurrentSize),
      ("$cp", p.CheckpointAt?.ToString("o", CultureInfo.InvariantCulture)), ("$id", jobId));

  internal static IReadOnlyList<string> SplitLines(string text) =>
    text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
