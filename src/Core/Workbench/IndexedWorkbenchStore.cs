using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FileMergeAssistant.Index;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Workbench;

/// <summary>
/// The workbench over the real index: sources and scan jobs in SQLite, stacks detected from the index, decisions and
/// the pending plan stored as decisions, and runs of the plan with a journal for retry and restore (PLAN-001 Phase 5).
/// Only <see cref="PlanExecutor"/> changes the user's files, and only when the user runs the plan.
/// </summary>
/// <remarks>
/// <para>
/// Every decision remembers each copy's file list as a digest (<see cref="CopyDigest"/>). When the index later shows a
/// different list, the stack goes back to the list for another look (ADR-006); before the plan runs, every copy that will
/// be touched, the keeper included, is walked on disk and must still match what the user saw (PLAN-001 Task 5.2).
/// </para>
/// <para>
/// Scans run on <see cref="ScanWorker"/>'s background task. One background refresh runs at a time; snapshots carry a
/// sequence number and an older one never replaces a newer one. They are applied through the synchronization context
/// captured at construction.
/// </para>
/// </remarks>
public sealed class IndexedWorkbenchStore : IWorkbenchStore, IDisposable
{
  private const string Pending = "Pending";
  private const string KeptAll = "KeptAll";
  private const string Reopened = "Reopened";
  private const string Converged = "Converged";

  private static readonly string[] DefaultExclusions = ["Thumbs.db", "desktop.ini", ".DS_Store", "~$*", "*.tmp", "*" + ScanFilter.PartialSuffix];
  private static readonly TimeSpan DetectInterval = TimeSpan.FromSeconds(4);
  private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

  private readonly IndexDatabase _database;
  private readonly IVolumeInfoProvider _volumes;
  private readonly SynchronizationContext? _context;
  private readonly ScanWorker _worker;
  private readonly PlanExecutor _executor;
  private readonly RecycleAreaLocator _areas;
  private readonly object _refreshGate = new();

  // Guards decisions, plan item states and the derived stacks and plan. On the UI thread it is uncontended; it matters
  // when no synchronization context is given and refreshes are applied on a worker thread.
  private readonly object _stateGate = new();
  private readonly Stopwatch _sinceDetect = Stopwatch.StartNew();
  private readonly Dictionary<string, StoredDecision> _decisions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, (bool Enabled, bool Skip)> _toggles = new(StringComparer.Ordinal);
  private readonly Dictionary<string, (PlanCheck Check, string? Detail)> _checks = new(StringComparer.Ordinal);

  private IReadOnlyList<ExecutionBatch> _history = [];
  private IReadOnlyList<RestoreConflict> _conflicts = [];
  private IReadOnlyList<SourceInfo> _sources = [];
  private IReadOnlyList<ScanJobInfo> _jobs = [];
  private IReadOnlyList<Stack> _detected = [];
  private IReadOnlyList<Stack> _stacks = [];
  private List<PlanEntry> _plan = [];
  private Dictionary<string, string> _onlineRoots = new(StringComparer.Ordinal);
  private bool _refreshRunning;
  private bool _refreshDirty;
  private bool _detectQueued;
  private bool _disposed;
  private long _buildSequence;
  private long _appliedSequence;
  private long _detectedSequence;

  private IndexedWorkbenchStore(IndexDatabase database, IVolumeInfoProvider volumes, SynchronizationContext? context, bool detectNow)
  {
    _database = database;
    _volumes = volumes;
    _context = context;
    _areas = new RecycleAreaLocator(Path.GetDirectoryName(Path.GetFullPath(database.Path))!);
    _executor = new PlanExecutor(_areas);
    ScanWorker.RecoverInterrupted(database);
    _worker = new ScanWorker(database, volumes, [_areas.AppDataRoot]);
    _worker.JobChanged += (_, ended) => QueueRefresh(detect: ended || _sinceDetect.Elapsed > DetectInterval);
    LoadDecisions();
    RecoverInterruptedRun();
    LoadHistory();
    if (detectNow)
    {
      Apply(Build(detect: true));
    }
    else
    {
      // A large index takes seconds to detect; show the sources at once and the stacks when they are ready.
      Apply(Build(detect: false));
      QueueRefresh(detect: true);
    }
  }

  public event EventHandler<WorkbenchChangedEventArgs>? Changed;

  /// <summary>Opens the index and starts the scan worker.</summary>
  /// <param name="path">Index file, normally <c>AppDataPaths.Root\index.db</c>.</param>
  /// <param name="volumes">Volume facts; the Win32 provider by default.</param>
  /// <param name="context">Where <see cref="Changed"/> is raised; the caller's context by default.</param>
  /// <param name="startWorker">False keeps queued jobs waiting (tests start it themselves).</param>
  /// <param name="detectNow">True detects stacks before returning; false detects them in the background (the app).</param>
  public static IndexedWorkbenchStore Open(string path, IVolumeInfoProvider? volumes = null, SynchronizationContext? context = null,
    bool startWorker = true, bool detectNow = true)
  {
    var store = new IndexedWorkbenchStore(IndexDatabase.OpenOrCreate(path), volumes ?? new Win32VolumeInfoProvider(),
      context ?? SynchronizationContext.Current, detectNow);
    if (startWorker)
    {
      store.StartScanning();
    }
    return store;
  }

  public bool IsDemo => false;
  public DateTimeOffset Now => DateTimeOffset.Now;
  public IReadOnlyList<SourceInfo> Sources => _sources;
  public IReadOnlyList<ScanJobInfo> Jobs => _jobs;
  public IReadOnlyList<Stack> Stacks => _stacks;
  public IReadOnlyList<PlanEntry> Plan => _plan;
  public DateTimeOffset? PlanCheckedAt { get; private set; }
  public IReadOnlyList<ExecutionBatch> History => _history;
  public IReadOnlyList<RestoreConflict> RestoreConflicts => _conflicts;
  public SinceLastVisit? SinceLastVisit => null;
  public bool CanExecute => true;
  public string? ExecuteUnavailableReason => null;

  /// <summary>What each volume's recycle area holds: items moved there and not yet restored or emptied.</summary>
  public IReadOnlyList<RecycleArea> RecycleAreas =>
    _history.SelectMany(b => b.Items)
      .Where(i => i.RecycleVolume is not null && i.RecycledPath is not null && i.Kind is PlanItemKind.Recycle or PlanItemKind.Replace
        && i.State is BatchItemState.Done or BatchItemState.Failed or BatchItemState.Running)
      .GroupBy(i => i.RecycleVolume!, StringComparer.OrdinalIgnoreCase)
      .Select(g => new RecycleArea(g.Key, g.Sum(i => i.FileCount > 0 && i.FailedFiles > 0 ? i.Bytes * (i.FileCount - i.FailedFiles) / i.FileCount : i.Bytes)))
      .ToList();

  public int ConvergedCount
  {
    get
    {
      lock (_stateGate)
      {
        return _decisions.Values.Count(d => d.Status == Converged);
      }
    }
  }

  public void StartScanning()
  {
    _worker.Start();
    _worker.Wake();
  }

  /// <summary>Rebuilds everything from the index on the calling thread, including stack detection.</summary>
  public void RefreshNow() => Apply(Build(detect: true));

  /// <summary>Waits until no job is queued or running (for tests and shutdown).</summary>
  public async Task WaitForScansAsync(TimeSpan timeout)
  {
    var deadline = Stopwatch.StartNew();
    while (deadline.Elapsed < timeout)
    {
      using (var connection = _database.Open())
      {
        var busy = IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE state IN ('Queued', 'Running', 'Pausing')");
        if (busy == 0)
        {
          return;
        }
      }
      await Task.Delay(50).ConfigureAwait(false);
    }
    throw new TimeoutException("Scans did not finish in time.");
  }

  public StackDecision? DecisionFor(string stackId)
  {
    lock (_stateGate)
    {
      return _decisions.TryGetValue(stackId, out var d) ? d.Decision : null;
    }
  }

  public IReadOnlyList<SuggestedLocation> SuggestLocations() => LocationSuggester.Suggest(_volumes);

  public Task<Stack> WithDetailsAsync(Stack stack) => Task.Run(() => GitStatusReader.Enrich(stack));

  public Task<IReadOnlyList<string>> AddSourcesAsync(IReadOnlyList<string> paths, SourceOptions options, bool startScan)
  {
    ArgumentNullException.ThrowIfNull(paths);
    ArgumentNullException.ThrowIfNull(options);
    var problems = new List<string>();
    using (var connection = _database.Open())
    {
      // Compare real locations: a junction, subst drive or mapped drive must not add the same folder twice (ADR-010).
      var existing = ReadSourceRoots(connection).Select(e => (Root: PathIdentity.Resolve(e.Root), e.Name)).ToList();
      var appData = PathIdentity.Resolve(_areas.AppDataRoot);
      foreach (var raw in paths)
      {
        string path;
        try
        {
          path = Normalize(raw);
        }
        catch (ArgumentException)
        {
          problems.Add($"{raw}：不是有效的資料夾路徑。");
          continue;
        }
        if (!Directory.Exists(path))
        {
          problems.Add($"{path}：找不到這個資料夾。");
          continue;
        }
        var real = PathIdentity.Resolve(path);
        if (LocationSuggester.Same(real, appData) || LocationSuggester.Inside(real, appData))
        {
          problems.Add($"{path}：這是 File Merge Assistant 自己的資料夾，不能加入。");
          continue;
        }
        var clash = existing.FirstOrDefault(e => LocationSuggester.Same(e.Root, real) || LocationSuggester.Inside(real, e.Root) || LocationSuggester.Inside(e.Root, real));
        if (clash.Root is not null)
        {
          var alias = LocationSuggester.Same(real, path) ? string.Empty : $"（實際位置是 {real}）";
          problems.Add(LocationSuggester.Same(clash.Root, real)
            ? $"{path}{alias}：已經是來源「{clash.Name}」。"
            : LocationSuggester.Inside(real, clash.Root)
              ? $"{path}{alias}：已經包含在來源「{clash.Name}」裡。"
              : $"{path}{alias}：包含已加入的來源「{clash.Name}」。同一個檔不能屬於兩個來源，請改加其他資料夾或先移除那個來源。");
          continue;
        }

        var volume = _volumes.Describe(real);
        var id = "s" + Guid.NewGuid().ToString("N")[..10];
        var name = Path.GetFileName(real.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : volume?.Label ?? real;
        var position = IndexDatabase.Scalar<long>(connection, "SELECT COALESCE(MAX(position), 0) + 1 FROM sources");
        IndexDatabase.Execute(connection,
          """
          INSERT INTO sources(id, name, root, is_protected, skip_regenerable, skip_cloud, regenerable_names, exclusions,
            volume_serial, volume_label, volume_kind, added_at, position)
          VALUES ($id, $name, $root, $protected, $regen, $cloud, $names, $exclusions, $serial, $label, $kind, $at, $position)
          """,
          ("$id", id), ("$name", name), ("$root", real),
          ("$protected", options.ProtectedPaths.Any(p => LocationSuggester.Same(p, path) || LocationSuggester.Same(p, real)) ? 1 : 0),
          ("$regen", options.SkipRegenerable ? 1 : 0), ("$cloud", options.SkipCloudOnly ? 1 : 0),
          ("$names", string.Join('\n', RegenerableFolders.Defaults)), ("$exclusions", string.Join('\n', DefaultExclusions)),
          ("$serial", volume?.Serial), ("$label", volume?.Label), ("$kind", volume?.Describe() ?? "磁碟"),
          ("$at", Stamp(Now)), ("$position", position));
        existing.Add((real, name));
        if (startScan)
        {
          QueueJob(connection, id, name, ScanJobKind.FirstScan, "第一次掃描");
        }
      }
    }
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return Task.FromResult<IReadOnlyList<string>>(problems);
  }

  public Task ScanSourceAsync(string sourceId)
  {
    using (var connection = _database.Open())
    {
      var resumable = IndexDatabase.Scalar<string>(connection,
        "SELECT id FROM jobs WHERE source_id = $s AND state IN ('Interrupted', 'Paused', 'WaitingForSource') LIMIT 1", ("$s", sourceId));
      if (resumable is not null)
      {
        return ResumeJobAsync(resumable);
      }
      var queued = IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE source_id = $s AND state = 'Queued'", ("$s", sourceId));
      if (queued == 0)
      {
        var name = IndexDatabase.Scalar<string>(connection, "SELECT name FROM sources WHERE id = $s", ("$s", sourceId)) ?? sourceId;
        QueueJob(connection, sourceId, name, ScanJobKind.Update, "你要求更新");
      }
    }
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public Task PauseJobAsync(string jobId)
  {
    if (_worker.IsRunning(jobId))
    {
      // Only a job that is still running can start pausing; a finished one keeps its state.
      Execute("UPDATE jobs SET state = 'Pausing' WHERE id = $id AND state = 'Running'", ("$id", jobId));
      _worker.Pause(jobId);
    }
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public Task ResumeJobAsync(string jobId)
  {
    Execute(
      """
      UPDATE jobs SET state = 'Queued', kind = 'Resume', reason = '繼續上次的進度', position = (SELECT COALESCE(MIN(position), 0) - 1 FROM jobs)
      WHERE id = $id AND state IN ('Paused', 'Interrupted', 'WaitingForSource')
      """,
      ("$id", jobId));
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public Task CancelJobAsync(string jobId)
  {
    if (_worker.IsRunning(jobId))
    {
      _worker.Cancel(jobId);
    }
    else
    {
      // A job the worker has just claimed is Running in the index; cancel that one through the worker instead.
      using var connection = _database.Open();
      if (IndexDatabase.Execute(connection, "DELETE FROM jobs WHERE id = $id AND state NOT IN ('Running', 'Pausing')", ("$id", jobId)) == 0)
      {
        _worker.Cancel(jobId);
      }
    }
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public Task RestartJobAsync(string jobId)
  {
    Execute("UPDATE jobs SET state = 'Queued', kind = 'FirstScan', reason = '從頭重新掃描', saved = 0 WHERE id = $id AND state NOT IN ('Running', 'Pausing')",
      ("$id", jobId));
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public void MoveJob(string jobId, int offset)
  {
    var queued = _jobs.Where(j => j.State == ScanJobState.Queued).ToList();
    var from = queued.FindIndex(j => j.Id == jobId);
    var to = from + offset;
    if (from < 0 || to < 0 || to >= queued.Count)
    {
      return;
    }
    using (var connection = _database.Open())
    {
      var a = IndexDatabase.Scalar<long>(connection, "SELECT position FROM jobs WHERE id = $id", ("$id", queued[from].Id));
      var b = IndexDatabase.Scalar<long>(connection, "SELECT position FROM jobs WHERE id = $id", ("$id", queued[to].Id));
      if (a == b)
      {
        b = a + Math.Sign(offset);
      }
      IndexDatabase.Execute(connection, "UPDATE jobs SET position = $p WHERE id = $id", ("$p", b), ("$id", queued[from].Id));
      IndexDatabase.Execute(connection, "UPDATE jobs SET position = $p WHERE id = $id", ("$p", a), ("$id", queued[to].Id));
    }
    Apply(Build(detect: false, probe: false));
  }

  public Task RescanFailedAsync(string jobId)
  {
    var job = _jobs.FirstOrDefault(j => j.Id == jobId);
    if (job is null)
    {
      return Task.CompletedTask;
    }
    using (var connection = _database.Open())
    {
      IndexDatabase.Execute(connection, "UPDATE jobs SET errors = 0, error_summary = NULL WHERE id = $id", ("$id", jobId));
      QueueJob(connection, job.SourceId, job.Title, ScanJobKind.Update, $"重新掃描上次無法讀取的 {job.Errors} 個項目");
    }
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public void Decide(StackDecision decision)
  {
    ArgumentNullException.ThrowIfNull(decision);
    var stack = _stacks.FirstOrDefault(s => s.Id == decision.StackId)
      ?? throw new StackChangedException(decision.StackId);
    var review = new StackReview(stack, decision);
    if (review.Block != ReviewBlock.None)
    {
      throw new InvalidOperationException($"Stack '{stack.Id}' is blocked: {review.Block}.");
    }
    lock (_stateGate)
    {
      if (decision.Action == StackAction.Later)
      {
        _decisions.Remove(stack.Id);
        Execute("DELETE FROM decisions WHERE stack_id = $id", ("$id", stack.Id));
      }
      else
      {
        // Remember what the user saw: the copies and each copy's file list, to notice any change before acting.
        var stored = new StoredDecision(decision, decision.Action == StackAction.KeepAll ? KeptAll : Pending, Now,
          stack.Copies.Select(c => c.Id).ToList(),
          stack.Copies.Where(c => c.Digest is not null).ToDictionary(c => c.Id, c => c.Digest!, StringComparer.Ordinal));
        _decisions[stack.Id] = stored;
        Save(stack.Id, stored);
        PlanCheckedAt = null;
      }
    }
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public int DecideSuggested(IReadOnlyList<string> stackIds)
  {
    ArgumentNullException.ThrowIfNull(stackIds);
    var added = 0;
    foreach (var id in stackIds)
    {
      var stack = _stacks.FirstOrDefault(s => s.Id == id);
      if (stack is null || stack.Status != StackStatus.Open || stack.Block != StackBlock.None)
      {
        continue;
      }
      var decision = StackDecision.Suggested(stack) with { FromSuggestion = true };
      if (new StackReview(stack, decision).Block != ReviewBlock.None)
      {
        continue;
      }
      Decide(decision);
      added++;
    }
    return added;
  }

  public void Undecide(string stackId)
  {
    lock (_stateGate)
    {
      _decisions.Remove(stackId);
      PlanCheckedAt = null;
    }
    Execute("DELETE FROM decisions WHERE stack_id = $id", ("$id", stackId));
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public void SetPlanItemEnabled(string itemId, bool enabled) => UpdateToggle(itemId, t => t with { Enabled = enabled });

  public void SetSkipChanged(string itemId, bool skip) => UpdateToggle(itemId, t => t with { Skip = skip });

  public async Task RecheckPlanAsync()
  {
    var plan = _plan.ToList();
    var results = await Task.Run(() =>
    {
      var keepers = new Dictionary<string, string?>(StringComparer.Ordinal);
      return plan.SelectMany(entry => entry.Items.Select(item => (item.Id, Change: CheckItem(entry, item, keepers)))).ToList();
    }).ConfigureAwait(true);
    lock (_stateGate)
    {
      foreach (var (id, change) in results)
      {
        _checks[id] = (change is null ? PlanCheck.Confirmed : PlanCheck.Changed, change);
      }
    }
    PlanCheckedAt = Now;
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public async Task<ExecutionBatch> ExecutePlanAsync(CancellationToken cancellationToken)
  {
    // Check every item against the disk now; the executor checks each one again right before it runs.
    await RecheckPlanAsync().ConfigureAwait(true);
    var plan = _plan.ToList();
    var ranAt = Now;
    var batchId = "b" + ranAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..4];

    // The run is journalled before anything moves, and every item before and after it acts.
    Execute("INSERT INTO batches(id, ran_at) VALUES ($id, $at)", ("$id", batchId), ("$at", Stamp(ranAt)));
    var positions = new Dictionary<string, int>(StringComparer.Ordinal);
    void Record(BatchItem item)
    {
      if (!positions.TryGetValue(item.Id, out var position))
      {
        positions[item.Id] = position = positions.Count;
      }
      SaveItem(batchId, item, position);
    }

    ExecutionBatch batch;
    try
    {
      var keepers = new Dictionary<string, string?>(StringComparer.Ordinal);
      batch = await Task.Run(() => _executor.Execute(batchId, ranAt, plan, (entry, item) => CheckItem(entry, item, keepers), Record, cancellationToken),
        CancellationToken.None).ConfigureAwait(true);
    }
    finally
    {
      LoadHistory();
    }

    lock (_stateGate)
    {
      foreach (var entry in plan)
      {
        var items = batch.Items.Where(i => i.StackId == entry.Stack.Id).ToList();
        if (items.Count == 0 || !_decisions.TryGetValue(entry.Stack.Id, out var stored))
        {
          continue;
        }
        // Converged only when every item finished; anything skipped or failed puts the stack back for another look.
        var next = stored with { Status = items.All(i => i.State == BatchItemState.Done) ? Converged : Reopened };
        _decisions[entry.Stack.Id] = next;
        Save(entry.Stack.Id, next);
      }
      _checks.Clear();
      PlanCheckedAt = null;
    }
    RescanAffected(batch.Items);
    Apply(Build(detect: false, probe: false));
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
    return batch;
  }

  public async Task RetryFailedAsync(string batchId)
  {
    var batch = _history.FirstOrDefault(b => b.Id == batchId);
    if (batch is null || batch.EmptiedAt is not null)
    {
      return;
    }
    var items = await Task.Run(() => batch.Items.Select(i => i.State == BatchItemState.Failed ? PlanExecutor.Retry(i) : i).ToList()).ConfigureAwait(true);
    SaveItems(batchId, items);
    lock (_stateGate)
    {
      foreach (var stackId in items.Select(i => i.StackId).Distinct(StringComparer.Ordinal))
      {
        if (items.Where(i => i.StackId == stackId).All(i => i.State == BatchItemState.Done) && _decisions.TryGetValue(stackId, out var stored))
        {
          var converged = stored with { Status = Converged };
          _decisions[stackId] = converged;
          Save(stackId, converged);
        }
      }
    }
    RescanAffected(items);
    LoadHistory();
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public Task RestoreItemAsync(string batchId, string itemId) => RestoreAsync(batchId, i => i.Id == itemId);

  public Task RestoreBatchAsync(string batchId) => RestoreAsync(batchId, _ => true);

  public async Task ResolveRestoreConflictAsync(string conflictId, RestoreConflictChoice choice, string? folder)
  {
    var conflict = _conflicts.FirstOrDefault(c => c.Id == conflictId);
    var batch = conflict is null ? null : _history.FirstOrDefault(b => b.Id == conflict.BatchId);
    var item = batch?.Items.FirstOrDefault(i => i.Id == conflict!.ItemId);
    if (conflict is null || batch is null || item is null || conflict.Resolution is not null)
    {
      return;
    }
    string resolution;
    string? placed = null;
    if (item.State == BatchItemState.Emptied)
    {
      resolution = "回收區已經清空，這一項不能再還原。";
    }
    else
    {
      var now = Now;
      try
      {
        placed = await Task.Run(() => PlanExecutor.Resolve(conflict, item, choice, folder, now)).ConfigureAwait(true);
        resolution = placed is null ? "已留在回收區，之後仍可還原。" : $"已還原到 {placed}，原位置現在的檔沒有被改動。";
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        resolution = $"還原沒有完成：{e.Message}";
      }
    }
    Execute("UPDATE restore_conflicts SET resolution = $r WHERE id = $id", ("$r", resolution), ("$id", conflictId));
    if (placed is not null)
    {
      SaveItems(batch.Id, batch.Items.Select(i => i.Id == item.Id ? i with { State = BatchItemState.Restored } : i).ToList());
      Reopen(item.StackId);
      RescanAffected([item]);
    }
    LoadHistory();
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public async Task EmptyRecycleAreasAsync()
  {
    var batches = _history.Where(b => b.EmptiedAt is null).ToList();
    var outcomes = await Task.Run(() => batches.Select(b =>
    {
      try
      {
        return (Batch: b, Result: _executor.Empty(b));
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
      {
        return (Batch: b, Result: ((IReadOnlySet<string>)new HashSet<string>(), false));
      }
    }).ToList()).ConfigureAwait(true);
    foreach (var (batch, (emptied, complete)) in outcomes)
    {
      SaveItems(batch.Id, batch.Items.Select(i => emptied.Contains(i.Id)
        ? i.State == BatchItemState.Done ? i with { State = BatchItemState.Emptied } : i with { UndonePath = null }
        : i).ToList());
      if (complete)
      {
        Execute("UPDATE batches SET emptied_at = $at WHERE id = $id", ("$at", Stamp(Now)), ("$id", batch.Id));
      }
    }
    LoadHistory();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  public void RespondToNotice(string noticeId, NoticeResponse response)
  {
  }

  public void Dispose()
  {
    lock (_refreshGate)
    {
      if (_disposed)
      {
        return;
      }
      _disposed = true;
    }
    _worker.Dispose();
  }

  private async Task RestoreAsync(string batchId, Func<BatchItem, bool> which)
  {
    var batch = _history.FirstOrDefault(b => b.Id == batchId);
    if (batch is null || batch.EmptiedAt is not null)
    {
      return;
    }
    var open = _conflicts.Where(c => c.BatchId == batchId && c.Resolution is null).Select(c => c.ItemId).ToHashSet(StringComparer.Ordinal);
    var now = Now;
    var outcomes = await Task.Run(() =>
    {
      var items = batch.Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
      var conflicts = new List<RestoreConflict>();
      // Recycled copies come back first; a file copied into the keeper is only undone once the copy it came from is back.
      foreach (var item in batch.Items.OrderBy(i => i.IsCopyKind() ? 1 : 0))
      {
        if (!which(item) || !item.CanRestore || open.Contains(item.Id))
        {
          continue;
        }
        if (item.IsCopyKind() && SourceStillRecycled(items.Values, item) is { } holder)
        {
          items[item.Id] = item with { FailureReason = $"先還原「{holder}」，再還原這個複製，以免這個檔只剩在回收區裡。" };
          continue;
        }
        try
        {
          var outcome = _executor.Restore(batchId, item, now);
          items[item.Id] = outcome.Item;
          if (outcome.Conflict is not null)
          {
            conflicts.Add(outcome.Conflict);
          }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
          items[item.Id] = item with { FailureReason = $"還原沒有完成：{e.Message}" };
        }
      }
      return (Items: batch.Items.Select(i => items[i.Id]).ToList(), Conflicts: conflicts);
    }).ConfigureAwait(true);
    SaveItems(batchId, outcomes.Items);
    foreach (var conflict in outcomes.Conflicts)
    {
      SaveConflict(conflict);
    }
    foreach (var stackId in outcomes.Items.Where(i => i.State == BatchItemState.Restored).Select(i => i.StackId).Distinct(StringComparer.Ordinal))
    {
      Reopen(stackId);
    }
    RescanAffected(outcomes.Items);
    LoadHistory();
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  /// <summary>The recycled copy that still holds the source of a copied file, if it has not been put back yet.</summary>
  private static string? SourceStillRecycled(IEnumerable<BatchItem> items, BatchItem copy) =>
    copy.SourcePath is not { } source
      ? null
      : items.FirstOrDefault(i => i.Kind == PlanItemKind.Recycle && i.State is BatchItemState.Done or BatchItemState.Failed && i.OriginalPath is { } original
        && (LocationSuggester.Same(original, source) || LocationSuggester.Inside(source, original)))?.OriginalPath;

  /// <summary>A restored stack goes back to the list: its copies are where they were.</summary>
  private void Reopen(string stackId)
  {
    lock (_stateGate)
    {
      if (_decisions.TryGetValue(stackId, out var stored))
      {
        var reopened = stored with { Status = Reopened };
        _decisions[stackId] = reopened;
        Save(stackId, reopened);
      }
    }
  }

  /// <summary>Queues an update of every source a run touched, so the index matches the disk again.</summary>
  private void RescanAffected(IReadOnlyList<BatchItem> items)
  {
    var paths = items.SelectMany(i => new[] { i.OriginalPath, i.CreatedPath, i.SourcePath }).Where(p => p is not null).Select(p => p!).ToList();
    using (var connection = _database.Open())
    {
      foreach (var source in _sources.Where(s => paths.Any(p => LocationSuggester.Same(p, s.RootPath) || LocationSuggester.Inside(p, s.RootPath))))
      {
        var queued = IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE source_id = $s AND state = 'Queued'", ("$s", source.Id));
        if (queued == 0)
        {
          QueueJob(connection, source.Id, source.Name, ScanJobKind.Update, "整理之後更新索引");
        }
      }
    }
    _worker.Wake();
  }

  /// <summary>
  /// A run the app did not finish (it closed or crashed mid-way) leaves items journalled as Running. Each is settled from
  /// what is on disk: finished if the move or copy is complete, otherwise failed with its paths kept for restore.
  /// </summary>
  private void RecoverInterruptedRun()
  {
    using var connection = _database.Open();
    var running = new List<(string Batch, BatchItem Item)>();
    using (var command = IndexDatabase.Command(connection, $"{ItemColumns} WHERE state = 'Running'"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        running.Add((reader.GetString(0), ReadItem(reader)));
      }
    }
    foreach (var (batchId, item) in running)
    {
      var settled = item.Kind switch
      {
        PlanItemKind.Recycle when item.OriginalPath is { } original && item.RecycledPath is { } recycled
          && !File.Exists(original) && !Directory.Exists(original) && (File.Exists(recycled) || Directory.Exists(recycled))
          => item with { State = BatchItemState.Done },
        PlanItemKind.Copy or PlanItemKind.SaveAs when item.CreatedPath is { } created && File.Exists(created) && PlanExecutor.Stamp(created) == item.SourceStamp
          => item with { State = BatchItemState.Done },
        _ => item with { State = BatchItemState.Failed, FailureReason = "程式在處理這一項時結束了。可以還原放回已移動的部分，或重試。" },
      };
      if (settled.State == BatchItemState.Failed && settled.RecycledPath is { } path && !File.Exists(path) && !Directory.Exists(path))
      {
        settled = settled with { RecycledPath = null };
      }
      SaveItem(batchId, settled, null);
    }
  }

  private sealed record Snapshot(
    long Sequence,
    IReadOnlyList<SourceInfo> Sources,
    IReadOnlyList<ScanJobInfo> Jobs,
    IReadOnlyList<Stack>? Detected,
    Dictionary<string, string> OnlineRoots);

  /// <summary>Asks for a background refresh; while one runs, further requests fold into one more pass.</summary>
  private void QueueRefresh(bool detect)
  {
    lock (_refreshGate)
    {
      _detectQueued |= detect;
      if (_disposed)
      {
        return;
      }
      if (_refreshRunning)
      {
        _refreshDirty = true;
        return;
      }
      _refreshRunning = true;
    }
    _ = Task.Run(RefreshLoopAsync);
  }

  private async Task RefreshLoopAsync()
  {
    while (true)
    {
      await Task.Delay(250).ConfigureAwait(false);
      bool detectNow;
      lock (_refreshGate)
      {
        _refreshDirty = false;
        detectNow = _detectQueued;
        _detectQueued = false;
      }
      Snapshot? snapshot = null;
      try
      {
        snapshot = Build(detectNow);
      }
      catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
      {
        // The next progress event tries again.
      }
      if (snapshot is not null)
      {
        if (_context is null)
        {
          Apply(snapshot);
        }
        else
        {
          _context.Post(_ => Apply(snapshot), null);
        }
      }
      lock (_refreshGate)
      {
        if (_disposed || (!_refreshDirty && !_detectQueued))
        {
          _refreshRunning = false;
          return;
        }
      }
    }
  }

  /// <param name="detect">Also run stack detection (expensive).</param>
  /// <param name="probe">
  /// Check every source's drive. Builds triggered from the interface pass false and reuse the last known state for
  /// sources already seen, so a disconnected network drive cannot freeze the window; background refreshes probe.
  /// </param>
  private Snapshot Build(bool detect, bool probe = true)
  {
    var sequence = Interlocked.Increment(ref _buildSequence);
    var known = _sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
    var lastOnline = _onlineRoots;
    using var connection = _database.Open();
    var sources = new List<(SourceInfo Info, string Root, bool Online)>();
    var jobs = ReadJobs(connection);
    var online = new Dictionary<string, string>(StringComparer.Ordinal);
    using (var command = IndexDatabase.Command(connection,
      """
      SELECT id, name, root, is_protected, regenerable_names, exclusions, volume_serial, volume_kind,
        last_scan_at, last_scan_errors, last_seen_at, indexed_files, regenerable_files, cloud_files, skip_regenerable
      FROM sources ORDER BY position
      """))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        var id = reader.GetString(0);
        var root = reader.GetString(2);
        var serial = reader.IsDBNull(6) ? null : reader.GetString(6);
        var isOnline = !probe && known.Contains(id)
          ? lastOnline.ContainsKey(id)
          : Directory.Exists(root) && (serial is null || _volumes.Describe(root)?.Serial is not { } current || current == serial);
        if (isOnline)
        {
          online[id] = root;
        }
        var job = jobs.FirstOrDefault(j => j.SourceId == id && j.State != ScanJobState.Completed);
        var lastScan = ParseStamp(reader, 8);
        var state = !isOnline ? SourceState.Offline
          : job?.State is ScanJobState.Running or ScanJobState.Pausing ? SourceState.Scanning
          : job?.State is ScanJobState.Queued ? SourceState.Queued
          : job?.State is ScanJobState.Interrupted ? SourceState.Interrupted
          : job?.State is ScanJobState.Paused || lastScan is null ? SourceState.NeedsUpdate
          : SourceState.Online;
        var remark = state switch
        {
          SourceState.Interrupted => "上次掃描沒有完成",
          SourceState.NeedsUpdate when lastScan is null => job is null ? "還沒掃描" : "第一次掃描已暫停",
          SourceState.NeedsUpdate => "掃描已暫停",
          SourceState.Queued when lastScan is null => "已排入佇列，等候第一次掃描",
          SourceState.Scanning when job is not null => $"{Clock(job.QueuedAt)} 開始",
          _ => null,
        };
        var info = new SourceInfo(id, reader.GetString(1), root, state, reader.GetInt64(3) != 0, reader.GetInt64(11), reader.GetInt64(12),
          reader.GetInt64(13), lastScan, (int)reader.GetInt64(9), reader.IsDBNull(7) ? "磁碟" : reader.GetString(7), ParseStamp(reader, 10), remark)
        {
          RegenerableNames = ScanWorker.SplitLines(reader.GetString(4)),
          Exclusions = ScanWorker.SplitLines(reader.GetString(5)),
          SkipsRegenerable = reader.GetInt64(14) != 0,
          VolumeSerial = serial,
        };
        sources.Add((info, root, isOnline));
      }
    }

    IReadOnlyList<Stack>? detected = null;
    if (detect)
    {
      _sinceDetect.Restart();
      var scanning = jobs.Where(j => j.State is ScanJobState.Running or ScanJobState.Pausing).Select(j => j.SourceId).ToHashSet(StringComparer.Ordinal);
      detected = StackDetector.Detect(ReadDetectionInput(connection, sources.Select(s => new DetectionSource(
        s.Info.Id, s.Info.Name, s.Root, s.Info.IsProtected, !s.Online, scanning.Contains(s.Info.Id))
      {
        IndexedAt = s.Info.LastScanAt,
      }).ToList()));
    }
    return new Snapshot(sequence, sources.Select(s => s.Info).ToList(), jobs, detected, online);
  }

  /// <summary>Applies a snapshot; parts older than what is already shown are ignored.</summary>
  private void Apply(Snapshot snapshot)
  {
    var progress = false;
    if (snapshot.Sequence > _appliedSequence)
    {
      _appliedSequence = snapshot.Sequence;
      _sources = snapshot.Sources;
      _jobs = snapshot.Jobs;
      _onlineRoots = snapshot.OnlineRoots;
      progress = true;
    }
    if (snapshot.Detected is not null && snapshot.Sequence > _detectedSequence)
    {
      _detectedSequence = snapshot.Sequence;
      _detected = snapshot.Detected;
      ApplyDecisions();
      Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
      return;
    }
    if (progress)
    {
      // Scan progress only: stacks and the plan keep their identity, so open pages are not rebuilt under the user.
      Changed?.Invoke(this, WorkbenchChangedEventArgs.Progress);
    }
  }

  /// <summary>Applies the stored decisions to the detected stacks and rebuilds the pending plan.</summary>
  private void ApplyDecisions()
  {
    lock (_stateGate)
    {
      ApplyDecisionsLocked();
    }
  }

  private void ApplyDecisionsLocked()
  {
    FollowChangedStacks();
    var stacks = new List<Stack>(_detected.Count);
    var plan = new List<PlanEntry>();
    foreach (var detected in _detected)
    {
      var stack = detected;
      var status = StackStatus.Open;
      if (_decisions.TryGetValue(stack.Id, out var stored) && stack.IndexOf(stored.Decision.KeeperCopyId) is var keeper && keeper >= 0)
      {
        var added = stack.Copies.Where(c => !stored.CopyIds.Contains(c.Id, StringComparer.Ordinal)).Select(c => c.Id).ToList();
        var changed = stack.Copies
          .Where(c => c.Digest is not null && stored.Digests.TryGetValue(c.Id, out var digest) && digest != c.Digest)
          .Select(c => c.Id)
          .ToList();
        stack = stack with { NewCopyIds = added, ChangedCopyIds = changed };

        if (stored.Status == Pending && changed.Count > 0)
        {
          // The files changed after the user looked (ADR-006): never run the old decision on new content.
          stored = stored with { Status = Reopened };
          _decisions[stack.Id] = stored;
          Save(stack.Id, stored);
        }

        if (stored.Status == KeptAll)
        {
          status = StackStatus.KeptAll;
        }
        else if (stored.Status == Converged)
        {
          status = StackStatus.Converged;
        }
        else if (stored.Status == Pending && !stack.Copies[keeper].IsOffline)
        {
          try
          {
            var review = new StackReview(stack, stored.Decision);
            if (review.Block == ReviewBlock.None && review.ChangesFiles)
            {
              var entry = PlanBuilder.Build(review, stored.At);
              plan.Add(entry with { Items = entry.Items.Select(WithState).ToList() });
              status = StackStatus.Pending;
            }
          }
          catch (ArgumentException)
          {
            // The decision no longer fits the stack; it stays stored and the stack is shown for another look.
          }
        }
        // Reopened, or a pending decision whose keeper is offline: back in the list, earlier choices preloaded.
      }
      stacks.Add(stack with { Status = status });
    }
    _stacks = stacks;
    _plan = plan.OrderBy(e => e.DecidedAt).ToList();
  }

  /// <summary>Applies the user's toggles and the last re-check to a freshly built plan item.</summary>
  private PlanItem WithState(PlanItem item)
  {
    var result = item;
    if (_toggles.TryGetValue(item.Id, out var toggle))
    {
      result = result with { Enabled = toggle.Enabled, SkipChanged = toggle.Skip };
    }
    if (_checks.TryGetValue(item.Id, out var check))
    {
      result = result with { Check = check.Check, ChangeDetail = check.Detail };
    }
    return result;
  }

  /// <summary>
  /// A stack's id follows its copies, so a new or vanished copy gives it a new id. Decisions follow the keeper copy to
  /// the stack that now holds it (ADR-006): a pending or converged decision reopens the stack for another look, a
  /// keep-all decision stays and the new copies are marked. A decision whose keeper is in no stack waits, unused.
  /// </summary>
  private void FollowChangedStacks()
  {
    var present = _detected.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
    foreach (var (oldId, stored) in _decisions.Where(d => !present.Contains(d.Key)).ToList())
    {
      var holders = _detected.Where(s => s.IndexOf(stored.Decision.KeeperCopyId) >= 0).ToList();
      if (holders.Count != 1 || _decisions.ContainsKey(holders[0].Id))
      {
        continue;
      }
      var stack = holders[0];
      var moved = stored with
      {
        Decision = stored.Decision with { StackId = stack.Id },
        Status = stored.Status == KeptAll ? KeptAll : Reopened,
      };
      _decisions.Remove(oldId);
      _decisions[stack.Id] = moved;
      Execute("DELETE FROM decisions WHERE stack_id = $id", ("$id", oldId));
      Save(stack.Id, moved);
    }
  }

  private void Save(string stackId, StoredDecision stored) =>
    Execute(
      """
      INSERT INTO decisions(stack_id, json, status, decided_at) VALUES ($id, $json, $status, $at)
      ON CONFLICT(stack_id) DO UPDATE SET json = excluded.json, status = excluded.status, decided_at = excluded.decided_at
      """,
      ("$id", stackId), ("$json", Serialize(stored)), ("$status", stored.Status), ("$at", Stamp(stored.At)));

  /// <summary>
  /// Checks one item against the disk; returns why it no longer matches what the user decided on, or null. The keeper is
  /// checked once per stack (cached in <paramref name="keepers"/>), before any of that stack's items has run.
  /// </summary>
  private string? CheckItem(PlanEntry entry, PlanItem item, Dictionary<string, string?> keepers)
  {
    if (!keepers.TryGetValue(entry.Stack.Id, out var keeper))
    {
      keepers[entry.Stack.Id] = keeper = CheckCopyContent(entry, entry.Keeper, "正本");
    }
    if (keeper is not null)
    {
      return keeper;
    }
    if (item.IsCopyLike)
    {
      return CheckCopy(entry, item);
    }
    var copy = entry.Stack.Copies.FirstOrDefault(c => c.Id == item.CopyId);
    return copy is null ? "這份已經不在這疊裡。" : CheckCopyContent(entry, copy, "這份");
  }

  private static string? CheckCopy(PlanEntry entry, PlanItem item)
  {
    var copy = entry.Stack.Copies.FirstOrDefault(c => c.Id == item.CopyId);
    if (copy is null)
    {
      return "來源的那份已經不在這疊裡。";
    }
    var relative = item.SourcePath[(copy.Path.TrimEnd('\\').Length + 1)..].Replace('\\', '/');
    var row = entry.Stack.Rows.FirstOrDefault(r => r.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
    var version = row?.Cells[entry.Stack.IndexOf(copy.Id)];
    var file = new FileInfo(item.SourcePath);
    if (!file.Exists)
    {
      return $"你決定之後，{item.SourcePath} 不見了，所以先停下來。";
    }
    if (version is not null && (file.Length != version.SizeBytes || file.LastWriteTimeUtc.Ticks != version.Modified.UtcTicks))
    {
      return $"你在 {entry.DecidedAt:HH:mm} 決定之後，{item.SourcePath} 在 {file.LastWriteTime:MM/dd HH:mm} 被修改了。它已經和你看到的不一樣，所以先停下來。";
    }
    if (item.Kind != PlanItemKind.Replace && File.Exists(item.DestinationPath))
    {
      return $"正本裡已經有 {item.DestinationPath}，不會覆寫，所以先停下來。";
    }
    return null;
  }

  /// <summary>
  /// Walks a copy on disk with the same rules as a scan and compares its file list with what the user saw when
  /// deciding (the stored digest); also confirms the volume is the one the source was added from.
  /// </summary>
  private string? CheckCopyContent(PlanEntry entry, StackCopy copy, string who)
  {
    var separator = copy.Id.IndexOf(':', StringComparison.Ordinal);
    var sourceId = copy.Id[..separator];
    var relativeRoot = copy.Id[(separator + 1)..];
    var source = _sources.FirstOrDefault(s => s.Id == sourceId);
    if (source is null)
    {
      return $"{who}的來源已經不在清單裡，所以先停下來。";
    }
    if (source.VolumeSerial is { } serial && _volumes.Describe(source.RootPath)?.Serial is { } current && current != serial)
    {
      return $"{who}所在的位置現在是另一顆硬碟（磁碟區序號不同），所以先停下來。";
    }
    string? expected;
    lock (_stateGate)
    {
      expected = _decisions.TryGetValue(entry.Stack.Id, out var stored) && stored.Digests.TryGetValue(copy.Id, out var digest) ? digest : copy.Digest;
    }

    if (entry.Stack.Kind == StackKind.File)
    {
      var file = new FileInfo(copy.Path);
      if (!file.Exists)
      {
        return $"{who}已經不在原位置，所以先停下來。";
      }
      return expected is not null && CopyDigest.Of([(file.Name, file.Length, file.LastWriteTimeUtc.Ticks)]) != expected
        ? $"你在 {entry.DecidedAt:HH:mm} 決定之後，{who}（{copy.Path}）在 {file.LastWriteTime:MM/dd HH:mm} 被修改了，所以先停下來。"
        : null;
    }

    if (!Directory.Exists(copy.Path))
    {
      return $"{who}（{copy.Path}）已經不在原位置，所以先停下來。";
    }
    var filter = new ScanFilter(new ScanSource(source.Id, source.RootPath, source.SkipsRegenerable, true, source.RegenerableNames, source.Exclusions),
      [_areas.AppDataRoot]);
    List<(string Relative, long Size, long ModifiedTicks)> files;
    try
    {
      files = filter.Walk(copy.Path, relativeRoot).ToList();
    }
    catch (Exception e) when (e is UnauthorizedAccessException or IOException)
    {
      return $"無法讀取{who}（{e.Message}），所以先停下來。";
    }
    if (expected is null || CopyDigest.Of(files) == expected)
    {
      return null;
    }
    return $"你在 {entry.DecidedAt:HH:mm} 決定之後，{who}（{copy.Path}）裡的檔有新增、刪除或修改，已經和你看到的不一樣，所以先停下來。"
      + (Difference(sourceId, relativeRoot, files) is { } detail ? $"例如：{detail}" : string.Empty);
  }

  /// <summary>Names one file that differs between the disk and the index, to make a change notice concrete.</summary>
  private string? Difference(string sourceId, string relativeRoot, List<(string Relative, long Size, long ModifiedTicks)> files)
  {
    var indexed = new Dictionary<string, (long Size, long Mtime)>(StringComparer.OrdinalIgnoreCase);
    using (var connection = _database.Open())
    using (var command = IndexDatabase.Command(connection,
      "SELECT rel_path, size, mtime FROM files WHERE source_id = $s AND rel_path LIKE $prefix ESCAPE '|'",
      ("$s", sourceId), ("$prefix", EscapeLike(relativeRoot + "\\") + "%")))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        indexed[reader.GetString(0)[(relativeRoot.Length + 1)..].Replace('\\', '/')] = (reader.GetInt64(1), reader.GetInt64(2));
      }
    }
    foreach (var (relative, size, modified) in files)
    {
      if (!indexed.TryGetValue(relative, out var known))
      {
        return $"多了 {relative}。";
      }
      if (known.Size != size || known.Mtime != modified)
      {
        return $"{relative} 被修改了。";
      }
    }
    var onDisk = files.Select(f => f.Relative).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return indexed.Keys.FirstOrDefault(k => !onDisk.Contains(k)) is { } gone ? $"少了 {gone}。" : null;
  }

  private void UpdateToggle(string itemId, Func<(bool Enabled, bool Skip), (bool Enabled, bool Skip)> change)
  {
    lock (_stateGate)
    {
      var item = _plan.SelectMany(e => e.Items).FirstOrDefault(i => i.Id == itemId);
      if (item is null)
      {
        return;
      }
      _toggles[itemId] = change((item.Enabled, item.SkipChanged));
    }
    ApplyDecisions();
    Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
  }

  private static List<ScanJobInfo> ReadJobs(SqliteConnection connection)
  {
    var jobs = new List<ScanJobInfo>();
    using var command = IndexDatabase.Command(connection,
      """
      SELECT id, source_id, title, kind, state, phase, queued_at, discovered, candidates, hashed, succeeded, skipped, errors,
        error_summary, current_path, current_size, last_checkpoint_at, finished_at, reason, saved
      FROM jobs
      ORDER BY CASE state WHEN 'Running' THEN 0 WHEN 'Pausing' THEN 0 WHEN 'Paused' THEN 1 WHEN 'Queued' THEN 2 ELSE 3 END, position, queued_at
      """);
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      jobs.Add(new ScanJobInfo(reader.GetString(0), reader.GetString(1), reader.GetString(2),
        Enum.Parse<ScanJobKind>(reader.GetString(3)), Enum.Parse<ScanJobState>(reader.GetString(4)), Enum.Parse<ScanPhase>(reader.GetString(5)),
        ParseStamp(reader, 6) ?? DateTimeOffset.Now)
      {
        Discovered = reader.GetInt64(7),
        Candidates = reader.GetInt64(8),
        Hashed = reader.GetInt64(9),
        Succeeded = reader.GetInt64(10),
        Skipped = reader.GetInt64(11),
        SkippedReason = reader.GetInt64(11) > 0 ? "排除規則或掃描中變動" : null,
        Errors = reader.GetInt64(12),
        ErrorSummary = reader.IsDBNull(13) ? null : reader.GetString(13),
        CurrentPath = reader.IsDBNull(14) ? null : reader.GetString(14),
        CurrentSize = reader.GetInt64(15),
        LastCheckpointAt = ParseStamp(reader, 16),
        FinishedAt = ParseStamp(reader, 17),
        Reason = reader.IsDBNull(18) ? null : reader.GetString(18),
        Saved = reader.GetInt64(19),
      });
    }
    // Describe errors of finished jobs from the recorded paths.
    for (var i = 0; i < jobs.Count; i++)
    {
      var job = jobs[i];
      if (job.State == ScanJobState.Completed && job.Errors > 0 && job.ErrorSummary is null)
      {
        var first = IndexDatabase.Scalar<string>(connection, "SELECT path FROM scan_errors WHERE job_id = $id LIMIT 1", ("$id", job.Id));
        jobs[i] = job with
        {
          ErrorSummary = $"無法讀取 {first} 等 {job.Errors} 個項目。它們不在任何一疊裡，也不會被當成已刪除。",
        };
      }
    }
    return jobs;
  }

  private DetectionInput ReadDetectionInput(SqliteConnection connection, List<DetectionSource> sources)
  {
    // One read transaction, so the three tables come from the same moment of the index.
    IndexDatabase.Execute(connection, "BEGIN");
    try
    {
      var roots = sources.ToDictionary(s => s.Id, s => s.Root.TrimEnd('\\'), StringComparer.Ordinal);
      var files = new List<IndexedFile>();
      using (var command = IndexDatabase.Command(connection, "SELECT id, source_id, rel_path, size, mtime, hash, cloud_only FROM files"))
      using (var reader = command.ExecuteReader())
      {
        while (reader.Read())
        {
          var sourceId = reader.GetString(1);
          var relative = reader.GetString(2);
          // Never treat the app's own folders (data, recycle areas) as user files, even if an older scan indexed them.
          if (roots.TryGetValue(sourceId, out var root) && IsAppOwned(root + "\\" + relative))
          {
            continue;
          }
          files.Add(new IndexedFile(reader.GetInt64(0), sourceId, relative, reader.GetInt64(3), reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetInt64(6) != 0));
        }
      }
      var regenerable = ReadFolders(connection, "SELECT source_id, rel_path, name, bytes, files FROM regenerable_dirs");
      var projects = ReadFolders(connection, "SELECT source_id, rel_path, marker, 0, 0 FROM project_dirs");
      return new DetectionInput(sources, files, regenerable, projects);
    }
    finally
    {
      IndexDatabase.Execute(connection, "COMMIT");
    }
  }

  private bool IsAppOwned(string fullPath) =>
    _areas.IsInsideArea(fullPath) || fullPath.EndsWith(ScanFilter.PartialSuffix, StringComparison.OrdinalIgnoreCase)
    || LocationSuggester.Inside(fullPath, _areas.AppDataRoot);

  private static List<IndexedFolder> ReadFolders(SqliteConnection connection, string sql)
  {
    var folders = new List<IndexedFolder>();
    using var command = IndexDatabase.Command(connection, sql);
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      folders.Add(new IndexedFolder(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4)));
    }
    return folders;
  }

  private static List<(string Root, string Name)> ReadSourceRoots(SqliteConnection connection)
  {
    var roots = new List<(string Root, string Name)>();
    using var command = IndexDatabase.Command(connection, "SELECT root, name FROM sources");
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      roots.Add((reader.GetString(0), reader.GetString(1)));
    }
    return roots;
  }

  private static void QueueJob(SqliteConnection connection, string sourceId, string title, ScanJobKind kind, string reason)
  {
    var position = IndexDatabase.Scalar<long>(connection, "SELECT COALESCE(MAX(position), 0) + 1 FROM jobs");
    IndexDatabase.Execute(connection,
      "INSERT INTO jobs(id, source_id, title, kind, state, phase, queued_at, position, reason) VALUES ($id, $s, $t, $k, 'Queued', 'Discover', $at, $p, $r)",
      ("$id", "j" + Guid.NewGuid().ToString("N")[..10]), ("$s", sourceId), ("$t", title), ("$k", kind.ToString()),
      ("$at", Stamp(DateTimeOffset.Now)), ("$p", position), ("$r", reason));
  }

  private const string ItemColumns =
    """
    SELECT batch_id, id, stack_id, kind, target, detail, state, bytes, file_count, failed_files, failure_reason, retried,
      recycle_volume, original_path, recycled_path, created_path, source_path, source_stamp, undone_path, manifest
    FROM batch_items
    """;

  private static BatchItem ReadItem(SqliteDataReader reader)
  {
    string? Text(int column) => reader.IsDBNull(column) ? null : reader.GetString(column);
    return new BatchItem(reader.GetString(1), reader.GetString(2), Enum.Parse<PlanItemKind>(reader.GetString(3)), reader.GetString(4),
      Enum.Parse<BatchItemState>(reader.GetString(6)))
    {
      Detail = reader.GetString(5),
      Bytes = reader.GetInt64(7),
      FileCount = reader.GetInt64(8),
      FailedFiles = reader.GetInt64(9),
      FailureReason = Text(10),
      Retried = reader.GetInt64(11) != 0,
      RecycleVolume = Text(12),
      OriginalPath = Text(13),
      RecycledPath = Text(14),
      CreatedPath = Text(15),
      SourcePath = Text(16),
      SourceStamp = Text(17),
      UndonePath = Text(18),
      Manifest = Text(19),
    };
  }

  /// <summary>Writes one item of a run's journal; <paramref name="position"/> is only used when the item is new.</summary>
  private void SaveItem(string batchId, BatchItem item, int? position)
  {
    using var connection = _database.Open();
    WriteItem(connection, batchId, item, position ?? 0);
  }

  private void SaveItems(string batchId, List<BatchItem> items)
  {
    using var connection = _database.Open();
    IndexDatabase.InTransaction(connection, () =>
    {
      for (var i = 0; i < items.Count; i++)
      {
        WriteItem(connection, batchId, items[i], i);
      }
    });
  }

  private static void WriteItem(SqliteConnection connection, string batchId, BatchItem item, int position) =>
    IndexDatabase.Execute(connection,
      """
      INSERT INTO batch_items(batch_id, id, position, stack_id, kind, target, detail, state, bytes, file_count, failed_files,
        failure_reason, retried, recycle_volume, original_path, recycled_path, created_path, source_path, source_stamp, undone_path, manifest)
      VALUES ($b, $id, $pos, $stack, $kind, $target, $detail, $state, $bytes, $files, $failed, $reason, $retried, $volume,
        $original, $recycled, $created, $source, $stamp, $undone, $manifest)
      ON CONFLICT(batch_id, id) DO UPDATE SET state = excluded.state, failed_files = excluded.failed_files,
        failure_reason = excluded.failure_reason, retried = excluded.retried, recycle_volume = excluded.recycle_volume,
        original_path = excluded.original_path, recycled_path = excluded.recycled_path, created_path = excluded.created_path,
        source_path = excluded.source_path, source_stamp = excluded.source_stamp, undone_path = excluded.undone_path,
        manifest = excluded.manifest
      """,
      ("$b", batchId), ("$id", item.Id), ("$pos", position), ("$stack", item.StackId), ("$kind", item.Kind.ToString()), ("$target", item.Target),
      ("$detail", item.Detail), ("$state", item.State.ToString()), ("$bytes", item.Bytes), ("$files", item.FileCount),
      ("$failed", item.FailedFiles), ("$reason", item.FailureReason), ("$retried", item.Retried ? 1 : 0), ("$volume", item.RecycleVolume),
      ("$original", item.OriginalPath), ("$recycled", item.RecycledPath), ("$created", item.CreatedPath), ("$source", item.SourcePath),
      ("$stamp", item.SourceStamp), ("$undone", item.UndonePath), ("$manifest", item.Manifest));

  private void SaveConflict(RestoreConflict c) =>
    Execute(
      """
      INSERT OR REPLACE INTO restore_conflicts(id, batch_id, item_id, original_path, recycled_at, recycled_bytes, recycled_modified,
        current_created, current_bytes, resolution)
      VALUES ($id, $b, $item, $path, $rat, $rbytes, $rmod, $ccreated, $cbytes, NULL)
      """,
      ("$id", c.Id), ("$b", c.BatchId), ("$item", c.ItemId), ("$path", c.OriginalPath), ("$rat", Stamp(c.RecycledAt)),
      ("$rbytes", c.RecycledBytes), ("$rmod", Stamp(c.RecycledModified)), ("$ccreated", Stamp(c.CurrentCreated)), ("$cbytes", c.CurrentBytes));

  private void LoadHistory()
  {
    using var connection = _database.Open();
    var items = new Dictionary<string, List<BatchItem>>(StringComparer.Ordinal);
    using (var command = IndexDatabase.Command(connection, ItemColumns + " ORDER BY batch_id, position"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        if (!items.TryGetValue(reader.GetString(0), out var list))
        {
          items[reader.GetString(0)] = list = [];
        }
        list.Add(ReadItem(reader));
      }
    }
    var batches = new List<ExecutionBatch>();
    using (var command = IndexDatabase.Command(connection, "SELECT id, ran_at, emptied_at FROM batches ORDER BY ran_at DESC"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        batches.Add(new ExecutionBatch(reader.GetString(0), ParseStamp(reader, 1) ?? DateTimeOffset.Now, items.GetValueOrDefault(reader.GetString(0)) ?? [])
        {
          EmptiedAt = ParseStamp(reader, 2),
        });
      }
    }
    var conflicts = new List<RestoreConflict>();
    using (var command = IndexDatabase.Command(connection,
      "SELECT id, batch_id, item_id, original_path, recycled_at, recycled_bytes, recycled_modified, current_created, current_bytes, resolution FROM restore_conflicts"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        conflicts.Add(new RestoreConflict(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
          ParseStamp(reader, 4) ?? DateTimeOffset.Now, reader.GetInt64(5), ParseStamp(reader, 6) ?? DateTimeOffset.Now,
          ParseStamp(reader, 7) ?? DateTimeOffset.Now, reader.GetInt64(8))
        {
          Resolution = reader.IsDBNull(9) ? null : reader.GetString(9),
        });
      }
    }
    _history = batches;
    _conflicts = conflicts;
  }

  private void LoadDecisions()
  {
    using var connection = _database.Open();
    using var command = IndexDatabase.Command(connection, "SELECT stack_id, json, status, decided_at FROM decisions");
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      var dto = JsonSerializer.Deserialize<DecisionDto>(reader.GetString(1), Json);
      if (dto is not null)
      {
        _decisions[reader.GetString(0)] = new StoredDecision(ToDecision(dto), reader.GetString(2), ParseStamp(reader, 3) ?? DateTimeOffset.Now,
          dto.CopyIds ?? [], dto.Digests ?? new Dictionary<string, string>(StringComparer.Ordinal));
      }
    }
  }

  private void Execute(string sql, params (string Name, object? Value)[] parameters)
  {
    using var connection = _database.Open();
    IndexDatabase.Execute(connection, sql, parameters);
  }

  /// <summary>
  /// A decision as stored: its status (Pending, KeptAll, Reopened or Converged), the copies the stack had then, and each
  /// copy's file-list digest at that moment.
  /// </summary>
  private sealed record StoredDecision(StackDecision Decision, string Status, DateTimeOffset At, IReadOnlyList<string> CopyIds,
    IReadOnlyDictionary<string, string> Digests);

  private sealed record DecisionDto(string StackId, string KeeperCopyId, StackAction Action, string[] AlsoKeep, string[] Removed,
    Dictionary<string, ConflictChoice> Choices, bool FromSuggestion, string[]? CopyIds, Dictionary<string, string>? Digests);

  private static string Serialize(StoredDecision stored)
  {
    var d = stored.Decision;
    return JsonSerializer.Serialize(
      new DecisionDto(d.StackId, d.KeeperCopyId, d.Action, [.. d.AlsoKeep], [.. d.Removed], d.Choices.ToDictionary(), d.FromSuggestion,
        [.. stored.CopyIds], stored.Digests.ToDictionary()),
      Json);
  }

  private static StackDecision ToDecision(DecisionDto dto) =>
    new(dto.StackId, dto.KeeperCopyId, dto.Action)
    {
      AlsoKeep = new HashSet<string>(dto.AlsoKeep, StringComparer.Ordinal),
      Removed = new HashSet<string>(dto.Removed, StringComparer.Ordinal),
      Choices = new Dictionary<string, ConflictChoice>(dto.Choices, StringComparer.Ordinal),
      FromSuggestion = dto.FromSuggestion,
    };

  private static string Normalize(string path)
  {
    var full = Path.GetFullPath(path.Trim());
    var trimmed = full.TrimEnd('\\');
    return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
  }

  private static string EscapeLike(string text) => text.Replace("|", "||", StringComparison.Ordinal)
    .Replace("%", "|%", StringComparison.Ordinal).Replace("_", "|_", StringComparison.Ordinal);

  private static string Stamp(DateTimeOffset at) => at.ToString("o", CultureInfo.InvariantCulture);

  private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

  private static DateTimeOffset? ParseStamp(SqliteDataReader reader, int column) =>
    reader.IsDBNull(column) ? null : DateTimeOffset.Parse(reader.GetString(column), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

internal static class BatchItemKinds
{
  public static bool IsCopyKind(this BatchItem item) => item.Kind is PlanItemKind.Copy or PlanItemKind.SaveAs;
}
