using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Localization;
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
  private readonly AnalysisEngineRegistry _engines;
  private readonly IVolumeInfoProvider _volumes;
  private readonly IReadOnlyList<string> _excludedRoots;
  private readonly SemaphoreSlim _signal = new(0);
  private readonly CancellationTokenSource _shutdown = new();
  private readonly object _gate = new();
  private Task? _loop;
  private string? _runningJob;
  private CancellationTokenSource? _jobCancel;
  private bool _pauseRequested;
  private bool _cancelRequested;

  /// <param name="database">The index.</param>
  /// <param name="volumes">Checks that a source's folder is on the volume it was added from (ADR-010).</param>
  /// <param name="excludedRoots">Folders never scanned, such as the app's data folder with its recycle area.</param>
  public ScanWorker(IndexDatabase database, IVolumeInfoProvider volumes, IReadOnlyList<string> excludedRoots, AnalysisEngineRegistry? engines = null)
  {
    _database = database;
    _engines = engines ?? AnalysisEngineRegistry.ManagedOnly();
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
      "UPDATE jobs SET state = 'Interrupted', reason = $reason, saved = discovered WHERE state IN ('Running', 'Pausing', 'Cancelling') AND analysis_snapshot IS NOT NULL",
      ("$reason", AppText.Get("scan.job.recovered")));
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
    var requested = false;
    lock (_gate)
    {
      if (_runningJob == jobId)
      {
        using var connection = _database.Open();
        IndexDatabase.Execute(connection, "UPDATE jobs SET state = 'Cancelling' WHERE id = $id AND state NOT IN ('Completed', 'Canceled')", ("$id", jobId));
        _cancelRequested = true;
        requested = true;
        _jobCancel?.Cancel();
      }
    }
    if (!requested)
    {
      // Preflight may have just stopped between the caller's IsRunning check and this request.
      using var connection = _database.Open();
      MarkCanceled(connection, jobId, onlyInactive: true);
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
        Run(job.Value.JobId, job.Value.SourceId, job.Value.Snapshot);
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
  private (string JobId, string SourceId, string? Snapshot)? NextJob()
  {
    using var connection = _database.Open();
    if (IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE state IN ('Paused', 'Pausing', 'Running', 'Cancelling')") > 0)
    {
      return null;
    }
    using var command = IndexDatabase.Command(connection,
      """
      UPDATE jobs SET state = 'Running', phase = 'Discover'
      WHERE id = (SELECT id FROM jobs WHERE state = 'Queued' ORDER BY position, queued_at LIMIT 1) AND state = 'Queued'
      RETURNING id, source_id, analysis_snapshot
      """);
    using var reader = command.ExecuteReader();
    if (!reader.Read())
    {
      return null;
    }
    lock (_gate)
    {
      _runningJob = reader.GetString(0);
      _pauseRequested = false;
      _cancelRequested = false;
    }
    return (reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
  }

  private void Run(string jobId, string sourceId, string? snapshot)
  {
    var cancel = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
    lock (_gate)
    {
      _runningJob = jobId;
      _jobCancel = cancel;
      if (_cancelRequested) { cancel.Cancel(); }
    }
    try
    {
      RunClaimed(jobId, sourceId, snapshot, cancel);
    }
    finally
    {
      Finish(jobId, cancel);
      JobChanged?.Invoke(this, true);
    }
  }

  private void RunClaimed(string jobId, string sourceId, string? snapshotJson, CancellationTokenSource cancel)
  {
    using var connection = _database.Open();
    ScanJobSnapshot snapshot;
    IFileAnalysisEngine engine;
    try
    {
      snapshot = ScanJobSnapshot.Deserialize(snapshotJson ?? throw new InvalidDataException("Legacy job has no engine/scope snapshot."));
      if (snapshot.SourceId != sourceId) { throw new InvalidDataException("The job source does not match its snapshot."); }
      engine = _engines.Resolve(snapshot.Engine);
    }
    catch (AnalysisEngineUnavailableException error)
    {
      SetState(connection, jobId, "WaitingForEngine", AppText.Get("scan.job.engineUnavailable", ("error", error.Message)));
      return;
    }
    catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
    {
      SetState(connection, jobId, "NeedsNewJob", AppText.Get("scan.job.invalidSnapshot", ("error", error.Message)));
      return;
    }
    var own = snapshot.Sources.Single(item => item.Source.Id == sourceId);
    var source = own.Source with { ExcludedRoots = own.Source.ExcludedRoots.Concat(_excludedRoots).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
    if (!ScanJobSnapshot.IsOnline(own, _volumes))
    {
      // Missing, or another disk under the same letter: never index it as this source.
      SetState(connection, jobId, "WaitingForSource", AppText.Get(Directory.Exists(source.Root) ? "scan.job.otherDrive" : "scan.job.offline"));
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
    var failureState = "Failed";
    try
    {
      // The own root must never fall out of the guard between two probes.
      var available = snapshot.Sources.Where(item => item.Source.Id == sourceId || ScanJobSnapshot.IsOnline(item, _volumes)).ToArray();
      var roots = available.ToDictionary(item => item.Source.Id, item => item.Source.Root, StringComparer.Ordinal);
      void CheckScope()
      {
        if (available.Any(item => !ScanJobSnapshot.IsOnline(item, _volumes)))
        {
          throw new ScanSourceUnavailableException(AppText.Get("scan.job.scopeChanged"));
        }
      }
      CheckScope();
      var scanner = new SourceScanner(_database, engine, snapshot.Engine);
      stop = scanner.Run(source, jobId, roots, Report, () =>
      {
        lock (_gate)
        {
          return _pauseRequested;
        }
      }, cancel.Token, CheckScope, snapshot.Sources.ToDictionary(item => item.Source.Id, item => item.Source, StringComparer.Ordinal));
    }
    catch (OperationCanceledException)
    {
      stop = ScanStop.Canceled;
    }
    catch (AnalysisEngineUnavailableException error)
    {
      stop = ScanStop.Canceled;
      failure = error.Message;
      failureState = "WaitingForEngine";
    }
    catch (ScanSourceUnavailableException error)
    {
      stop = ScanStop.Canceled;
      failure = error.Message;
      failureState = "WaitingForSource";
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
      SetState(connection, jobId, "Interrupted", AppText.Get("scan.job.closed"));
    }
    else if (failure is not null)
    {
      SetState(connection, jobId, failureState, AppText.Get("scan.job.failed", ("error", failure)));
    }
    else if (stop == ScanStop.Paused)
    {
      SetState(connection, jobId, "Paused", AppText.Get("scan.job.paused"));
    }
    else if (stop == ScanStop.Canceled)
    {
      IndexDatabase.Execute(connection, "UPDATE jobs SET state = 'Canceled', finished_at = $at, current_path = NULL WHERE id = $id",
        ("$id", jobId), ("$at", now));
    }
    else
    {
      var errors = last?.Errors ?? 0;
      IndexDatabase.Execute(connection,
        "UPDATE jobs SET state = 'Completed', phase = 'Done', finished_at = $at, current_path = NULL WHERE id = $id",
        ("$at", now), ("$id", jobId));
      IndexDatabase.Execute(connection, "UPDATE sources SET last_scan_at = $at, last_scan_errors = $e, last_seen_at = $at WHERE id = $id",
        ("$at", now), ("$e", errors), ("$id", source.Id));
      // Completed snapshots remain available as provenance for persisted evidence.
    }
  }

  private void Finish(string jobId, CancellationTokenSource cancel)
  {
    try
    {
      lock (_gate)
      {
        try
        {
          // Cancellation wins over a preflight wait/error only once all engine/probe work has stopped.
          if (cancel.IsCancellationRequested && !_shutdown.IsCancellationRequested)
          {
            using var connection = _database.Open();
            MarkCanceled(connection, jobId);
          }
        }
        finally
        {
          _runningJob = null;
          _jobCancel = null;
          _pauseRequested = false;
          _cancelRequested = false;
        }
      }
    }
    finally { cancel.Dispose(); }
  }

  private static void MarkCanceled(SqliteConnection connection, string jobId, bool onlyInactive = false) =>
    IndexDatabase.Execute(connection,
      """
      UPDATE jobs SET state = 'Canceled', finished_at = $at, current_path = NULL
      WHERE id = $id AND state NOT IN ('Completed', 'Canceled')
        AND ($inactive = 0 OR state NOT IN ('Running', 'Pausing', 'Cancelling'))
      """,
      ("$id", jobId), ("$at", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)), ("$inactive", onlyInactive ? 1 : 0));

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
    IndexDatabase.Execute(connection, "UPDATE jobs SET state = $state, reason = $reason, saved = discovered WHERE id = $id AND (state <> 'Cancelling' OR $state = 'Interrupted')",
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
