using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Localization;
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
  private readonly AnalysisEngineRegistry _engines;
  private AnalysisEngineSnapshot _newJobEngine;
  private readonly PlanExecutor _executor;
  private readonly RecycleAreaLocator _areas;
  private readonly object _refreshGate = new();

  // Guards decisions, plan item states and the derived stacks and plan. On the UI thread it is uncontended; it matters
  // when no synchronization context is given and refreshes are applied on a worker thread.
  private readonly object _stateGate = new();
  private readonly Stopwatch _sinceDetect = Stopwatch.StartNew();
  private readonly Dictionary<string, StoredDecision> _decisions = new(StringComparer.Ordinal);
  private readonly Dictionary<string, (bool Enabled, bool Skip)> _toggles = new(StringComparer.Ordinal);
  private readonly Dictionary<string, (PlanCheck Check, Hold? Detail)> _checks = new(StringComparer.Ordinal);
  private readonly List<Task> _snapshotting = [];

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

  private IndexedWorkbenchStore(IndexDatabase database, IVolumeInfoProvider volumes, SynchronizationContext? context, bool detectNow,
    AnalysisEngineRegistry? engines)
  {
    _database = database;
    _volumes = volumes;
    _context = context;
    _engines = engines ?? AnalysisEngineRegistry.ManagedOnly();
    _newJobEngine = _engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    _areas = new RecycleAreaLocator(Path.GetDirectoryName(Path.GetFullPath(database.Path))!);
    _executor = new PlanExecutor(_areas);
    ScanWorker.RecoverInterrupted(database);
    _worker = new ScanWorker(database, volumes, [_areas.AppDataRoot], _engines);
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
    AppText.Changed += OnLanguageChanged;
  }

  public event EventHandler<WorkbenchChangedEventArgs>? Changed;

  /// <summary>Opens the index and starts the scan worker.</summary>
  /// <param name="path">Index file, normally <c>AppDataPaths.Root\index.db</c>.</param>
  /// <param name="volumes">Volume facts; the Win32 provider by default.</param>
  /// <param name="context">Where <see cref="Changed"/> is raised; the caller's context by default.</param>
  /// <param name="startWorker">False keeps queued jobs waiting (tests start it themselves).</param>
  /// <param name="detectNow">True detects stacks before returning; false detects them in the background (the app).</param>
  /// <param name="engines">The available analysis implementations; managed analysis is required.</param>
  public static IndexedWorkbenchStore Open(string path, IVolumeInfoProvider? volumes = null, SynchronizationContext? context = null,
    bool startWorker = true, bool detectNow = true, AnalysisEngineRegistry? engines = null)
  {
    var store = new IndexedWorkbenchStore(IndexDatabase.OpenOrCreate(path), volumes ?? new Win32VolumeInfoProvider(),
      context ?? SynchronizationContext.Current, detectNow, engines);
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

  public AnalysisEngineRegistry Engines => _engines;
  public AnalysisEngineSnapshot NewJobEngine => _newJobEngine;

  /// <summary>Changes new jobs only. Existing queued, paused and interrupted snapshots are immutable.</summary>
  public void SelectNewJobEngine(AnalysisEngineSnapshot snapshot)
  {
    _engines.Resolve(snapshot);
    if (snapshot.Capability != AnalysisCapability.ExactDuplicates) { throw new ArgumentException("The scan queue currently accepts exact-content analysis only.", nameof(snapshot)); }
    _newJobEngine = snapshot with { ScopeJson = "{}" };
  }
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
        var busy = IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM jobs WHERE state IN ('Queued', 'Running', 'Pausing', 'Cancelling')");
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

  public async Task<IReadOnlyList<string>> AddSourcesAsync(IReadOnlyList<string> paths, SourceOptions options, bool startScan)
  {
    ArgumentNullException.ThrowIfNull(paths);
    ArgumentNullException.ThrowIfNull(options);
    // Resolving real locations can wait on a slow or disconnected network drive; never on the interface thread.
    // Only sources known to be online are asked for their identity; an unplugged network drive would stall every check.
    var online = _onlineRoots.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var problems = await Task.Run(() => AddSources(paths, options, startScan, online)).ConfigureAwait(true);
    _worker.Wake();
    Apply(Build(detect: false, probe: false));
    return problems;
  }

  private List<string> AddSources(IReadOnlyList<string> paths, SourceOptions options, bool startScan, HashSet<string> online)
  {
    var problems = new List<string>();
    var added = new List<(string Id, string Name)>();
    using (var connection = _database.Open())
    {
      // Compare real locations: a junction, subst drive or mapped drive must not add the same folder twice (ADR-010).
      var existing = ReadSourceRoots(connection);
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
          problems.Add(AppText.Get("store.add.invalid", ("path", raw)));
          continue;
        }
        if (!Directory.Exists(path))
        {
          problems.Add(AppText.Get("store.add.missing", ("path", path)));
          continue;
        }
        var real = PathIdentity.Resolve(path);
        if (LocationSuggester.Same(real, appData) || LocationSuggester.Inside(real, appData) || PathIdentity.SameOrInside(path, _areas.AppDataRoot) == true)
        {
          problems.Add(AppText.Get("store.add.appFolder", ("path", path)));
          continue;
        }
        // Names first (they also work for sources that are offline), then identity: a UNC path, junction, subst or mapped
        // drive can name a folder that is already a source.
        var self = PathIdentity.Identify(path);
        string? clash = null;
        foreach (var e in existing)
        {
          var known = online.Contains(e.Root);
          var same = LocationSuggester.Same(e.Root, real)
            || (known && self is { } mine && PathIdentity.Identify(e.Root) is { } theirs && theirs.SameAs(mine));
          var inside = !same && (LocationSuggester.Inside(real, e.Root) || (known && PathIdentity.SameOrInside(path, e.Root) == true));
          var around = !same && !inside && (LocationSuggester.Inside(e.Root, real) || (known && PathIdentity.SameOrInside(e.Root, path) == true));
          var key = same ? "store.add.same" : inside ? "store.add.inside" : around ? "store.add.around" : null;
          if (key is not null)
          {
            clash = AppText.Get(key, ("path", path), ("name", e.Name), ("root", e.Root));
            break;
          }
        }
        if (clash is not null)
        {
          problems.Add(clash);
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
          // The drive's description is stored in the language of the moment, like every text written into the index.
          ("$serial", volume?.Serial), ("$label", volume?.Label), ("$kind", volume?.Describe() ?? AppText.Get("scan.volume.other")),
          ("$at", Stamp(Now)), ("$position", position));
        existing.Add((real, name));
        online.Add(real);
        added.Add((id, name));
      }
      if (startScan)
      {
        foreach (var source in added)
        {
          QueueJob(connection, source.Id, source.Name, ScanJobKind.FirstScan, AppText.Get("scan.job.firstScan"));
        }
      }
    }
    return problems;
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
        QueueJob(connection, sourceId, name, ScanJobKind.Update, AppText.Get("scan.job.requested"));
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
      UPDATE jobs SET state = 'Queued', kind = 'Resume', reason = $reason, position = (SELECT COALESCE(MIN(position), 0) - 1 FROM jobs)
      WHERE id = $id AND state IN ('Paused', 'Interrupted', 'WaitingForSource', 'WaitingForEngine', 'Failed') AND analysis_snapshot IS NOT NULL
      """,
      ("$id", jobId), ("$reason", AppText.Get("scan.job.resume")));
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
      if (IndexDatabase.Execute(connection, "UPDATE jobs SET state = 'Canceled', finished_at = $at WHERE id = $id AND state NOT IN ('Running', 'Pausing', 'Cancelling')",
        ("$id", jobId), ("$at", Stamp(Now))) == 0)
      {
        _worker.Cancel(jobId);
      }
    }
    Apply(Build(detect: false, probe: false));
    return Task.CompletedTask;
  }

  public Task RestartJobAsync(string jobId)
  {
    using (var connection = _database.Open())
    {
      // New settings require a new identity. The old job remains as historical provenance.
      IndexDatabase.InTransaction(connection, () =>
      {
        using var command = IndexDatabase.Command(connection, "SELECT source_id, title FROM jobs WHERE id = $id AND state NOT IN ('Running', 'Pausing', 'Cancelling')", ("$id", jobId));
        string sourceId, title;
        using (var reader = command.ExecuteReader())
        {
          if (!reader.Read()) { return; }
          sourceId = reader.GetString(0);
          title = reader.GetString(1);
        }
        IndexDatabase.Execute(connection, "UPDATE jobs SET state = 'Canceled', finished_at = $at WHERE id = $id", ("$id", jobId), ("$at", Stamp(Now)));
        QueueJob(connection, sourceId, title, ScanJobKind.FirstScan, AppText.Get("scan.job.restart"));
      });
    }
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
      QueueJob(connection, job.SourceId, job.Title, ScanJobKind.Update,
        AppText.Plural("scan.job.rescanFailed", job.Errors, ("count", job.Errors.ToString(CultureInfo.InvariantCulture))));
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
          stack.Copies.Where(c => c.Digest is not null).ToDictionary(c => c.Id, c => c.Digest!, StringComparer.Ordinal),
          stack.Copies.Where(c => c.EvidenceDigest is not null).ToDictionary(c => c.Id, c => c.EvidenceDigest!, StringComparer.Ordinal),
          new Dictionary<string, string>(StringComparer.Ordinal));
        _decisions[stack.Id] = stored;
        Save(stack.Id, stored);
        PlanCheckedAt = null;
        if (stored.Status == Pending)
        {
          TakeSnapshots(stack, stored);
        }
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
    await SnapshotsTaken().ConfigureAwait(true);
    var plan = _plan.ToList();
    var results = await Task.Run(() =>
    {
      var keepers = new Dictionary<string, Hold?>(StringComparer.Ordinal);
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
    using var changing = FileChanges.Begin();
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
      // What the executor journals is fixed text in the language of the moment.
      var keepers = new Dictionary<string, Hold?>(StringComparer.Ordinal);
      batch = await Task.Run(() => _executor.Execute(batchId, ranAt, plan, (entry, item) => CheckItem(entry, item, keepers)?.Text, Record, cancellationToken),
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
    using var changing = FileChanges.Begin();
    var batch = _history.FirstOrDefault(b => b.Id == batchId);
    if (batch is null || batch.EmptiedAt is not null)
    {
      return;
    }
    var items = await Task.Run(() => batch.Items
      .Select(i => i.State != BatchItemState.Failed ? i : RetryRefused(i) is { } reason ? i with { FailureReason = reason } : _executor.Retry(batchId, i))
      .ToList()).ConfigureAwait(true);
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
    using var changing = FileChanges.Begin();
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
      resolution = AppText.Get("store.restore.emptied");
    }
    else
    {
      var now = Now;
      try
      {
        placed = await Task.Run(() => PlanExecutor.Resolve(conflict, item, choice, folder, now)).ConfigureAwait(true);
        resolution = placed is null ? AppText.Get("store.restore.kept") : AppText.Get("store.restore.placed", ("path", placed));
      }
      catch (Exception e) when (e is IOException or UnauthorizedAccessException)
      {
        resolution = AppText.Get("store.restore.failed", ("error", e.Message));
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
    using var changing = FileChanges.Begin();
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
    AppText.Changed -= OnLanguageChanged;
    _worker.Dispose();
  }

  /// <summary>
  /// Source remarks, job summaries and the stacks' evidence and keeper reasons are made when the index is read, so a new
  /// display language reads it again (detection included); <see cref="Changed"/> follows when that is done. Text already
  /// written into the index, such as job reasons and run results, keeps its language.
  /// </summary>
  private void OnLanguageChanged(object? sender, EventArgs e) => QueueRefresh(detect: true);

  private async Task RestoreAsync(string batchId, Func<BatchItem, bool> which)
  {
    using var changing = FileChanges.Begin();
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
      // Recycled copies come back first: a copied or replacing file is only undone while the file it came from is in place.
      foreach (var item in batch.Items.OrderBy(i => i.Kind == PlanItemKind.Recycle ? 0 : 1))
      {
        if (!which(item) || !item.CanRestore || open.Contains(item.Id))
        {
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
          items[item.Id] = item with { FailureReason = AppText.Get("store.restore.failed", ("error", e.Message)) };
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

  /// <summary>
  /// Why a failed recycle must not be retried now: its keeper is gone, or the copy has since become a keeper itself.
  /// Copies are always safe to retry (they never remove anything).
  /// </summary>
  private string? RetryRefused(BatchItem item)
  {
    if (item.Kind != PlanItemKind.Recycle || item.OriginalPath is not { } original)
    {
      return null;
    }
    if (item.KeeperPath is not { } keeper)
    {
      return AppText.Get("store.retry.noKeeper");
    }
    if (!File.Exists(keeper) && !(Directory.Exists(keeper) && Directory.EnumerateFileSystemEntries(keeper).Any()))
    {
      return AppText.Get("store.retry.keeperGone", ("path", keeper));
    }
    List<StoredDecision> decisions;
    lock (_stateGate)
    {
      decisions = [.. _decisions.Values];
    }
    foreach (var d in decisions)
    {
      var keeperPath = CopyPath(d.Decision.KeeperCopyId);
      if (d.Status is Pending or Reopened && keeperPath is not null
        && (LocationSuggester.Same(keeperPath, original) || LocationSuggester.Inside(keeperPath, original) || LocationSuggester.Inside(original, keeperPath)))
      {
        return AppText.Get("store.retry.nowKeeper");
      }
      var copyId = d.CopyIds.FirstOrDefault(id => CopyPath(id) is { } p && LocationSuggester.Same(p, original));
      if (copyId is null)
      {
        continue;
      }
      if (d.Status == KeptAll)
      {
        return AppText.Get("store.retry.keptAll");
      }
      if (d.Decision.AlsoKeep.Contains(copyId))
      {
        return AppText.Get("store.retry.keptCopy");
      }
    }
    return null;
  }

  /// <summary>The full path of a copy id (<c>sourceId:relative path</c>), or null when its source is gone.</summary>
  private string? CopyPath(string copyId)
  {
    var cut = copyId.IndexOf(':', StringComparison.Ordinal);
    var source = cut < 0 ? null : _sources.FirstOrDefault(s => s.Id == copyId[..cut]);
    return source is null ? null : Path.Combine(source.RootPath, copyId[(cut + 1)..]);
  }

  /// <summary>
  /// Records, in the background, everything inside each copy that may be recycled (<see cref="CopySnapshot"/>), as it is
  /// now that the user decided. A run recycles a copy only if it still matches.
  /// </summary>
  private void TakeSnapshots(Stack stack, StoredDecision stored)
  {
    var copies = stack.Copies.Where(c => c.Id != stored.Decision.KeeperCopyId && !c.IsOffline).ToList();
    var task = Task.Run(() =>
    {
      var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
      foreach (var copy in copies)
      {
        try
        {
          snapshots[copy.Id] = CopySnapshot.Of(copy.Path, IsAppOwned, stored.At.UtcDateTime);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
          // No snapshot: that copy is not recycled until the stack is decided again.
        }
      }
      lock (_stateGate)
      {
        if (_decisions.TryGetValue(stack.Id, out var current) && current.At == stored.At)
        {
          var updated = current with { Snapshots = snapshots };
          _decisions[stack.Id] = updated;
          Save(stack.Id, updated);
        }
      }
    });
    lock (_snapshotting)
    {
      _snapshotting.RemoveAll(t => t.IsCompleted);
      _snapshotting.Add(task);
    }
  }

  private Task SnapshotsTaken()
  {
    lock (_snapshotting)
    {
      return Task.WhenAll(_snapshotting.ToArray());
    }
  }

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
          QueueJob(connection, source.Id, source.Name, ScanJobKind.Update, AppText.Get("scan.job.afterRun"));
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
      // A drive that is not here cannot tell what happened: the item stays interrupted until a start when it is back.
      if (new[] { item.OriginalPath, item.RecycledPath, item.CreatedPath }.Any(p => p is not null && !Directory.Exists(RecycleAreaLocator.Volume(p))))
      {
        continue;
      }
      var settled = item.Kind switch
      {
        PlanItemKind.Recycle when item.OriginalPath is { } original && item.RecycledPath is { } recycled
          && !File.Exists(original) && !Directory.Exists(original) && (File.Exists(recycled) || Directory.Exists(recycled))
          => item with { State = BatchItemState.Done },
        PlanItemKind.Copy or PlanItemKind.SaveAs when item.CreatedPath is { } created && File.Exists(created) && PlanExecutor.Stamp(created) == item.SourceStamp
          => item with { State = BatchItemState.Done },
        _ => item with { State = BatchItemState.Failed, FailureReason = AppText.Get("store.run.interrupted") },
      };
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
        var job = jobs.FirstOrDefault(j => j.SourceId == id && j.State is not (ScanJobState.Completed or ScanJobState.Canceled));
        var lastScan = ParseStamp(reader, 8);
        var state = !isOnline ? SourceState.Offline
          : job?.State is ScanJobState.Running or ScanJobState.Pausing or ScanJobState.Cancelling ? SourceState.Scanning
          : job?.State is ScanJobState.Queued ? SourceState.Queued
          : job?.State is ScanJobState.Interrupted or ScanJobState.Failed or ScanJobState.NeedsNewJob or ScanJobState.WaitingForEngine ? SourceState.Interrupted
          : job?.State is ScanJobState.Paused || lastScan is null ? SourceState.NeedsUpdate
          : SourceState.Online;
        var remark = state switch
        {
          SourceState.Interrupted => AppText.Get("scan.source.interrupted"),
          SourceState.NeedsUpdate when lastScan is null => AppText.Get(job is null ? "scan.source.notScanned" : "scan.source.firstScanPaused"),
          SourceState.NeedsUpdate => AppText.Get("scan.source.scanPaused"),
          SourceState.Queued when lastScan is null => AppText.Get("scan.source.queuedFirst"),
          SourceState.Scanning when job is not null => AppText.Get("scan.source.startedAt", ("time", Clock(job.QueuedAt))),
          _ => null,
        };
        var info = new SourceInfo(id, reader.GetString(1), root, state, reader.GetInt64(3) != 0, reader.GetInt64(11), reader.GetInt64(12),
          reader.GetInt64(13), lastScan, (int)reader.GetInt64(9), reader.IsDBNull(7) ? AppText.Get("scan.volume.other") : reader.GetString(7),
          ParseStamp(reader, 10), remark)
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
        var evidenceChanged = stack.Copies
          .Where(c => c.EvidenceDigest is not null && (!stored.EvidenceDigests.TryGetValue(c.Id, out var evidence) || evidence != c.EvidenceDigest))
          .Select(c => c.Id).ToList();
        stack = stack with { NewCopyIds = added, ChangedCopyIds = changed, EvidenceChangedCopyIds = evidenceChanged };

        if (stored.Status == Pending && (changed.Count > 0 || evidenceChanged.Count > 0))
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
      result = result with { Check = check.Check, ChangeDetail = check.Detail?.Text };
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
  private Hold? CheckItem(PlanEntry entry, PlanItem item, Dictionary<string, Hold?> keepers)
  {
    if (!keepers.TryGetValue(entry.Stack.Id, out var keeper))
    {
      keepers[entry.Stack.Id] = keeper = CheckCopyContent(entry, entry.Keeper, isKeeper: true);
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
    return copy is null ? new Hold("store.check.copyMissing") : CheckCopyContent(entry, copy, isKeeper: false);
  }

  private static Hold? CheckCopy(PlanEntry entry, PlanItem item)
  {
    var copy = entry.Stack.Copies.FirstOrDefault(c => c.Id == item.CopyId);
    if (copy is null)
    {
      return new Hold("store.check.sourceCopyMissing");
    }
    var relative = item.SourcePath[(copy.Path.TrimEnd('\\').Length + 1)..].Replace('\\', '/');
    var row = entry.Stack.Rows.FirstOrDefault(r => r.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase));
    var version = row?.Cells[entry.Stack.IndexOf(copy.Id)];
    var file = new FileInfo(item.SourcePath);
    if (!file.Exists)
    {
      return new Hold("store.check.fileGone", ("path", item.SourcePath));
    }
    if (version is not null && (file.Length != version.SizeBytes || file.LastWriteTimeUtc.Ticks != version.Modified.UtcTicks))
    {
      return new Hold("store.check.fileModified", ("decided", DecidedAt(entry)), ("path", item.SourcePath), ("modified", WrittenAt(file)));
    }
    if (item.Kind != PlanItemKind.Replace && File.Exists(item.DestinationPath))
    {
      return new Hold("store.check.destinationExists", ("path", item.DestinationPath));
    }
    return null;
  }

  /// <summary>
  /// Walks a copy on disk with the same rules as a scan and compares its file list with what the user saw when
  /// deciding (the stored digest); also confirms the volume is the one the source was added from.
  /// </summary>
  /// <param name="entry">The plan entry the copy belongs to.</param>
  /// <param name="copy">The keeper or the copy an item recycles.</param>
  /// <param name="isKeeper">Which of the two, for the wording: the sentences name 「正本」 or 「這份」.</param>
  private Hold? CheckCopyContent(PlanEntry entry, StackCopy copy, bool isKeeper)
  {
    var who = isKeeper ? ".keeper" : ".copy";
    var separator = copy.Id.IndexOf(':', StringComparison.Ordinal);
    var sourceId = copy.Id[..separator];
    var relativeRoot = copy.Id[(separator + 1)..];
    var source = _sources.FirstOrDefault(s => s.Id == sourceId);
    if (source is null)
    {
      return new Hold("store.check.sourceRemoved" + who);
    }
    if (source.VolumeSerial is { } serial && _volumes.Describe(source.RootPath)?.Serial is { } current && current != serial)
    {
      return new Hold("store.check.otherDrive" + who);
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
        return new Hold("store.check.gone" + who);
      }
      return expected is not null && CopyDigest.Of([(file.Name, file.Length, file.LastWriteTimeUtc.Ticks)]) != expected
        ? new Hold("store.check.modified" + who, ("decided", DecidedAt(entry)), ("path", copy.Path), ("modified", WrittenAt(file)))
        : SnapshotChanged(entry, copy);
    }

    if (!Directory.Exists(copy.Path))
    {
      return new Hold("store.check.folderGone" + who, ("path", copy.Path));
    }
    // The same rules as a scan. Cloud placeholders are listed like any file (never read), as they are in the index.
    var filter = new ScanFilter(new ScanSource(source.Id, source.RootPath, source.SkipsRegenerable, true, source.RegenerableNames, source.Exclusions),
      [_areas.AppDataRoot]);
    List<(string Relative, long Size, long ModifiedTicks)> files;
    try
    {
      files = filter.Walk(copy.Path, relativeRoot).ToList();
    }
    catch (Exception e) when (e is UnauthorizedAccessException or IOException)
    {
      return new Hold("store.check.unreadable" + who, ("error", e.Message));
    }
    if (expected is null || CopyDigest.Of(files) == expected)
    {
      return SnapshotChanged(entry, copy);
    }
    // One sentence per kind of example, so each language keeps its own order.
    return Difference(sourceId, relativeRoot, files) is { } detail
      ? new Hold(detail.Kind + who, ("decided", DecidedAt(entry)), ("path", copy.Path), ("file", detail.File))
      : new Hold("store.check.contentChanged" + who, ("decided", DecidedAt(entry)), ("path", copy.Path));
  }

  /// <summary>
  /// For a copy that will be recycled: compares everything inside it, including what scans skip (<c>.git</c>, regenerable
  /// folders, excluded files), with the snapshot taken when the user decided. The keeper is not compared this way; work
  /// in it (new commits, rebuilt folders) never puts the other copies at risk, so every sentence here is about 「這份」.
  /// </summary>
  private Hold? SnapshotChanged(PlanEntry entry, StackCopy copy)
  {
    if (copy.Id == entry.Keeper.Id)
    {
      return null;
    }
    string? snapshot;
    lock (_stateGate)
    {
      snapshot = _decisions.TryGetValue(entry.Stack.Id, out var stored) && stored.Snapshots.TryGetValue(copy.Id, out var taken) ? taken : null;
    }
    if (snapshot is null)
    {
      return new Hold("store.check.noSnapshot", ("path", copy.Path));
    }
    if (snapshot.StartsWith(CopySnapshot.ChangedPrefix, StringComparison.Ordinal))
    {
      return new Hold("store.check.changingAtDecision", ("path", copy.Path), ("file", snapshot[CopySnapshot.ChangedPrefix.Length..]));
    }
    string now;
    try
    {
      now = CopySnapshot.Of(copy.Path, IsAppOwned);
    }
    catch (Exception e) when (e is UnauthorizedAccessException or IOException)
    {
      return new Hold("store.check.unreadable.copy", ("error", e.Message));
    }
    return now == snapshot ? null : new Hold("store.check.snapshotChanged", ("decided", DecidedAt(entry)), ("path", copy.Path));
  }

  /// <summary>When the user decided, as shown in a check result.</summary>
  private static string DecidedAt(PlanEntry entry) => entry.DecidedAt.ToString("HH:mm", AppText.Culture);

  /// <summary>When a file was last written, as shown in a check result.</summary>
  private static string WrittenAt(FileInfo file) => file.LastWriteTime.ToString("MM/dd HH:mm", AppText.Culture);

  /// <summary>
  /// Why an item is held back, as a catalog key and its values, so the plan shows it in the current display language. It
  /// becomes fixed text only when a run writes it into the journal.
  /// </summary>
  private sealed record Hold(string Key, params (string Name, object? Value)[] Values)
  {
    public string Text => AppText.Get(Key, Values);
  }

  /// <summary>
  /// Names one file that differs between the disk and the index, to make a change notice concrete: the key of the
  /// sentence (added, modified or missing, without the .keeper/.copy suffix) and the file.
  /// </summary>
  private (string Kind, string File)? Difference(string sourceId, string relativeRoot, List<(string Relative, long Size, long ModifiedTicks)> files)
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
        return ("store.check.contentAdded", relative);
      }
      if (known.Size != size || known.Mtime != modified)
      {
        return ("store.check.contentModified", relative);
      }
    }
    var onDisk = files.Select(f => f.Relative).ToHashSet(StringComparer.OrdinalIgnoreCase);
    return indexed.Keys.FirstOrDefault(k => !onDisk.Contains(k)) is { } gone ? ("store.check.contentRemoved", gone) : null;
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
        error_summary, current_path, current_size, last_checkpoint_at, finished_at, reason, saved,
        analysis_snapshot, analysis_generation, analysis_complete
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
        SkippedReason = reader.GetInt64(11) > 0 ? AppText.Get("scan.job.skipped") : null,
        Errors = reader.GetInt64(12),
        ErrorSummary = reader.IsDBNull(13) ? null : reader.GetString(13),
        CurrentPath = reader.IsDBNull(14) ? null : reader.GetString(14),
        CurrentSize = reader.GetInt64(15),
        LastCheckpointAt = ParseStamp(reader, 16),
        FinishedAt = ParseStamp(reader, 17),
        Reason = reader.IsDBNull(18) ? null : reader.GetString(18),
        Saved = reader.GetInt64(19),
        Snapshot = ReadJobSnapshot(reader.IsDBNull(20) ? null : reader.GetString(20)),
        AnalysisGeneration = reader.GetInt64(21),
        AnalysisComplete = reader.GetInt64(22) != 0,
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
          ErrorSummary = AppText.Plural("scan.job.errors", job.Errors,
            ("path", first ?? string.Empty), ("count", job.Errors.ToString(CultureInfo.InvariantCulture))),
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
    || Path.GetFileName(fullPath).StartsWith(PathIdentity.ProbePrefix, StringComparison.Ordinal)
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

  private static ScanJobSnapshot? ReadJobSnapshot(string? json)
  {
    try { return json is null ? null : ScanJobSnapshot.Deserialize(json); }
    catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException) { return null; }
  }

  private void QueueJob(SqliteConnection connection, string sourceId, string title, ScanJobKind kind, string reason)
  {
    var position = IndexDatabase.Scalar<long>(connection, "SELECT COALESCE(MAX(position), 0) + 1 FROM jobs");
    var snapshot = ScanJobSnapshot.Capture(connection, sourceId, _newJobEngine, [_areas.AppDataRoot]);
    IndexDatabase.Execute(connection,
      "INSERT INTO jobs(id, source_id, title, kind, state, phase, queued_at, position, reason, analysis_snapshot) VALUES ($id, $s, $t, $k, 'Queued', 'Discover', $at, $p, $r, $snapshot)",
      ("$id", "j" + Guid.NewGuid().ToString("N")[..10]), ("$s", sourceId), ("$t", title), ("$k", kind.ToString()),
      ("$at", Stamp(DateTimeOffset.Now)), ("$p", position), ("$r", reason), ("$snapshot", snapshot.Serialize()));
  }

  private const string ItemColumns =
    """
    SELECT batch_id, id, stack_id, kind, target, detail, state, bytes, file_count, failed_files, failure_reason, retried,
      recycle_volume, original_path, recycled_path, created_path, source_path, source_stamp, undone_path, manifest, keeper_path
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
      KeeperPath = Text(20),
    };
  }

  /// <summary>Writes one item of a run's journal; <paramref name="position"/> is only used when the item is new.</summary>
  private void SaveItem(string batchId, BatchItem item, int? position) =>
    Journal(connection => WriteItem(connection, batchId, item, position ?? 0));

  private void SaveItems(string batchId, List<BatchItem> items) =>
    Journal(connection => IndexDatabase.InTransaction(connection, () =>
    {
      for (var i = 0; i < items.Count; i++)
      {
        WriteItem(connection, batchId, items[i], i);
      }
    }));

  /// <summary>
  /// Writes to the journal with a full flush to disk: an entry must survive a power cut as surely as the move it
  /// describes, or a recycled folder could be left in a recycle area with no record of it.
  /// </summary>
  private void Journal(Action<SqliteConnection> write)
  {
    using var connection = _database.Open();
    IndexDatabase.Execute(connection, "PRAGMA synchronous = FULL");
    try
    {
      write(connection);
    }
    finally
    {
      IndexDatabase.Execute(connection, "PRAGMA synchronous = NORMAL");
    }
  }

  private static void WriteItem(SqliteConnection connection, string batchId, BatchItem item, int position) =>
    IndexDatabase.Execute(connection,
      """
      INSERT INTO batch_items(batch_id, id, position, stack_id, kind, target, detail, state, bytes, file_count, failed_files,
        failure_reason, retried, recycle_volume, original_path, recycled_path, created_path, source_path, source_stamp, undone_path, manifest,
        keeper_path)
      VALUES ($b, $id, $pos, $stack, $kind, $target, $detail, $state, $bytes, $files, $failed, $reason, $retried, $volume,
        $original, $recycled, $created, $source, $stamp, $undone, $manifest, $keeper)
      ON CONFLICT(batch_id, id) DO UPDATE SET state = excluded.state, failed_files = excluded.failed_files,
        failure_reason = excluded.failure_reason, retried = excluded.retried, recycle_volume = excluded.recycle_volume,
        original_path = excluded.original_path, recycled_path = excluded.recycled_path, created_path = excluded.created_path,
        source_path = excluded.source_path, source_stamp = excluded.source_stamp, undone_path = excluded.undone_path,
        manifest = excluded.manifest, keeper_path = excluded.keeper_path
      """,
      ("$b", batchId), ("$id", item.Id), ("$pos", position), ("$stack", item.StackId), ("$kind", item.Kind.ToString()), ("$target", item.Target),
      ("$detail", item.Detail), ("$state", item.State.ToString()), ("$bytes", item.Bytes), ("$files", item.FileCount),
      ("$failed", item.FailedFiles), ("$reason", item.FailureReason), ("$retried", item.Retried ? 1 : 0), ("$volume", item.RecycleVolume),
      ("$original", item.OriginalPath), ("$recycled", item.RecycledPath), ("$created", item.CreatedPath), ("$source", item.SourcePath),
      ("$stamp", item.SourceStamp), ("$undone", item.UndonePath), ("$manifest", item.Manifest), ("$keeper", item.KeeperPath));

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
          dto.CopyIds ?? [], dto.Digests ?? new Dictionary<string, string>(StringComparer.Ordinal),
          dto.EvidenceDigests ?? new Dictionary<string, string>(StringComparer.Ordinal),
          dto.Snapshots ?? new Dictionary<string, string>(StringComparer.Ordinal));
      }
    }
  }

  private void Execute(string sql, params (string Name, object? Value)[] parameters)
  {
    using var connection = _database.Open();
    IndexDatabase.Execute(connection, sql, parameters);
  }

  /// <summary>
  /// A decision as stored: its status (Pending, KeptAll, Reopened or Converged), the copies the stack had then, each
  /// copy's indexed file-list digest at that moment, and the full snapshot of each copy that may be recycled.
  /// </summary>
  private sealed record StoredDecision(StackDecision Decision, string Status, DateTimeOffset At, IReadOnlyList<string> CopyIds,
    IReadOnlyDictionary<string, string> Digests, IReadOnlyDictionary<string, string> EvidenceDigests, Dictionary<string, string> Snapshots);

  private sealed record DecisionDto(string StackId, string KeeperCopyId, StackAction Action, string[] AlsoKeep, string[] Removed,
    Dictionary<string, ConflictChoice> Choices, bool FromSuggestion, string[]? CopyIds, Dictionary<string, string>? Digests,
    Dictionary<string, string>? Snapshots, Dictionary<string, string>? EvidenceDigests);

  private static string Serialize(StoredDecision stored)
  {
    var d = stored.Decision;
    return JsonSerializer.Serialize(
      new DecisionDto(d.StackId, d.KeeperCopyId, d.Action, [.. d.AlsoKeep], [.. d.Removed], d.Choices.ToDictionary(), d.FromSuggestion,
        [.. stored.CopyIds], stored.Digests.ToDictionary(), stored.Snapshots.ToDictionary(), stored.EvidenceDigests.ToDictionary()),
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
