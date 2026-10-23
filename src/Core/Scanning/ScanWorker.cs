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
  private readonly Func<IReadOnlyDictionary<string, string>> _onlineRoots;
  private readonly SemaphoreSlim _signal = new(0);
  private readonly CancellationTokenSource _shutdown = new();
  private readonly object _gate = new();
  private Task? _loop;
  private string? _runningJob;
  private CancellationTokenSource? _jobCancel;
  private bool _pauseRequested;

  public ScanWorker(IndexDatabase database, Func<IReadOnlyDictionary<string, string>> onlineRoots)
  {
    _database = database;
    _scanner = new SourceScanner(database);
    _onlineRoots = onlineRoots;
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
        Run(job.Value.JobId, job.Value.Source);
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

  private (string JobId, ScanSource Source)? NextJob()
  {
    using var connection = _database.Open();
    using var command = IndexDatabase.Command(connection,
      """
      SELECT j.id, s.id, s.root, s.skip_regenerable, s.skip_cloud, s.regenerable_names, s.exclusions
      FROM jobs j JOIN sources s ON s.id = j.source_id
      WHERE j.state = 'Queued' ORDER BY j.position, j.queued_at LIMIT 1
      """);
    using var reader = command.ExecuteReader();
    if (!reader.Read())
    {
      return null;
    }
    var source = new ScanSource(reader.GetString(1), reader.GetString(2), reader.GetInt64(3) != 0, reader.GetInt64(4) != 0,
      SplitLines(reader.GetString(5)), SplitLines(reader.GetString(6)));
    return (reader.GetString(0), source);
  }

  private void Run(string jobId, ScanSource source)
  {
    var cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
    lock (_gate)
    {
      _runningJob = jobId;
      _jobCancel = cancel;
      _pauseRequested = false;
    }

    using var connection = _database.Open();
    if (!Directory.Exists(source.Root))
    {
      SetState(connection, jobId, "WaitingForSource", "硬碟離線，更新工作先保留在這裡。接上後會先確認是同一顆硬碟，再開始。");
      Finish(cancel);
      JobChanged?.Invoke(this, true);
      return;
    }

    IndexDatabase.Execute(connection, "UPDATE jobs SET state = 'Running', phase = 'Discover' WHERE id = $id", ("$id", jobId));
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
      stop = _scanner.Run(source, jobId, _onlineRoots(), Report, () =>
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
    Finish(cancel);
    JobChanged?.Invoke(this, true);
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
