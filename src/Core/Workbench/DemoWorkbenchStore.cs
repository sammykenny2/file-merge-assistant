using System.Diagnostics;
using System.Globalization;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Workbench;

/// <summary>
/// An in-memory store with the GUI flow mock's simulated data. Decisions, the pending plan, runs and restores behave
/// like the real workflow, but nothing on disk is read or changed. The clock starts at 10/23 14:32 and runs forward.
/// </summary>
public sealed class DemoWorkbenchStore : IWorkbenchStore
{
  private const string ChangedCopyPath = @"C:\Users\demo\Desktop\invoice-tool - 複製";
  private const string FailingCopyPath = @"C:\Users\demo\Desktop\京都挑選";

  private readonly Stopwatch _clock = Stopwatch.StartNew();
  private readonly List<SourceInfo> _sources = DemoData.Sources();
  private readonly List<ScanJobInfo> _jobs = DemoData.Jobs();
  private readonly List<Stack> _stacks = DemoData.Stacks();
  private readonly Dictionary<string, StackDecision> _decisions = new(StringComparer.Ordinal);
  private readonly List<PlanEntry> _plan = [];
  private readonly List<ExecutionBatch> _history = DemoData.History();
  private readonly List<RestoreConflict> _conflicts = DemoData.RestoreConflicts();
  private readonly List<RecycleArea> _recycleAreas = DemoData.RecycleAreas();
  private readonly SinceLastVisit _since = DemoData.Since();
  private readonly HashSet<string> _failedOnce = new(StringComparer.Ordinal);
  private List<ChangeNotice>? _sinceNotices;
  private int _batchNumber;

  public event EventHandler<WorkbenchChangedEventArgs>? Changed;

  /// <summary>Delay used to make pausing, checking and running visible; tests set it to zero.</summary>
  public TimeSpan StepDelay { get; init; } = TimeSpan.FromMilliseconds(1200);

  public bool IsDemo => true;

  public DateTimeOffset Now => DemoData.Start + _clock.Elapsed;

  public IReadOnlyList<SourceInfo> Sources => _sources;
  public IReadOnlyList<ScanJobInfo> Jobs => _jobs;
  public IReadOnlyList<Stack> Stacks => _stacks;
  public IReadOnlyList<PlanEntry> Plan => _plan;
  public DateTimeOffset? PlanCheckedAt { get; private set; }
  public IReadOnlyList<ExecutionBatch> History => _history;
  public IReadOnlyList<RestoreConflict> RestoreConflicts => _conflicts;
  public IReadOnlyList<RecycleArea> RecycleAreas => _recycleAreas;
  public SinceLastVisit? SinceLastVisit => _sinceNotices is null ? _since : _since with { Notices = _sinceNotices };

  public StackDecision? DecisionFor(string stackId) => _decisions.GetValueOrDefault(stackId);

  public IReadOnlyList<SuggestedLocation> SuggestLocations() => DemoData.Locations();

  public Task<Stack> WithDetailsAsync(Stack stack) => Task.FromResult(stack);

  public bool CanExecute => true;

  public string? ExecuteUnavailableReason => null;

  public Task<IReadOnlyList<string>> AddSourcesAsync(IReadOnlyList<string> paths, SourceOptions options, bool startScan)
  {
    ArgumentNullException.ThrowIfNull(paths);
    ArgumentNullException.ThrowIfNull(options);
    foreach (var path in paths)
    {
      if (_sources.Any(s => string.Equals(s.RootPath, path, StringComparison.OrdinalIgnoreCase)))
      {
        continue;
      }
      var id = $"src{_sources.Count + 1}";
      var name = Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : path;
      var isProtected = options.ProtectedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
      _sources.Add(new SourceInfo(id, name, path, startScan ? SourceState.Queued : SourceState.NeedsUpdate, isProtected, 0, 0, 0, null, 0, "內建 SSD",
        Remark: startScan ? "已排入佇列，等候第一次掃描" : "還沒掃描")
      {
        RegenerableNames = options.SkipRegenerable ? RegenerableFolders.Defaults : [],
      });
      if (startScan)
      {
        _jobs.Insert(QueueEnd(), new ScanJobInfo($"job-{id}", id, name, ScanJobKind.FirstScan, ScanJobState.Queued, ScanPhase.Discover, Now)
        {
          Reason = "第一次掃描",
        });
      }
    }
    OnChanged();
    return Task.FromResult<IReadOnlyList<string>>([]);
  }

  public Task ScanSourceAsync(string sourceId)
  {
    var interrupted = _jobs.FirstOrDefault(j => j.SourceId == sourceId && j.State == ScanJobState.Interrupted);
    if (interrupted is not null)
    {
      return ResumeJobAsync(interrupted.Id);
    }
    if (_jobs.Any(j => j.SourceId == sourceId && j.State is ScanJobState.Queued or ScanJobState.Running or ScanJobState.Pausing))
    {
      return Task.CompletedTask;
    }
    var source = _sources.First(s => s.Id == sourceId);
    _jobs.Insert(QueueEnd(), new ScanJobInfo($"job-{sourceId}-{_jobs.Count}", sourceId, source.Name, ScanJobKind.Update, ScanJobState.Queued, ScanPhase.Discover, Now)
    {
      Reason = "你要求更新",
    });
    SetSourceState(sourceId, SourceState.Queued);
    OnChanged();
    return Task.CompletedTask;
  }

  public async Task PauseJobAsync(string jobId)
  {
    var job = Job(jobId);
    if (job.State != ScanJobState.Running)
    {
      return;
    }
    Replace(job with { State = ScanJobState.Pausing });
    await Task.Delay(StepDelay);
    Replace(Job(jobId) with { State = ScanJobState.Paused, Saved = job.Hashed, LastCheckpointAt = Now });
  }

  public Task ResumeJobAsync(string jobId)
  {
    var job = Job(jobId);
    switch (job.State)
    {
      case ScanJobState.Paused when job.Id == "job-usb":
        Replace(job with { State = ScanJobState.Running });
        break;
      case ScanJobState.Paused or ScanJobState.Interrupted:
        _jobs.Remove(job);
        _jobs.Insert(QueueEnd(), job with { State = ScanJobState.Queued, Reason = "繼續上次的進度" });
        SetSourceState(job.SourceId, SourceState.Queued);
        OnChanged();
        break;
      case ScanJobState.WaitingForSource:
        Replace(job with { Reason = "還沒有偵測到這顆硬碟。接上後會先確認是同一顆，再自動開始。" });
        break;
    }
    return Task.CompletedTask;
  }

  public Task CancelJobAsync(string jobId)
  {
    var job = Job(jobId);
    _jobs.Remove(job);
    if (job.State is ScanJobState.Running or ScanJobState.Pausing or ScanJobState.Paused or ScanJobState.Queued or ScanJobState.Interrupted)
    {
      SetSourceState(job.SourceId, SourceState.Online);
    }
    OnChanged();
    return Task.CompletedTask;
  }

  public Task RestartJobAsync(string jobId)
  {
    var job = Job(jobId);
    _jobs.Remove(job);
    _jobs.Insert(QueueEnd(), job with { State = ScanJobState.Queued, Kind = ScanJobKind.FirstScan, Saved = 0, Reason = "從頭重新掃描" });
    SetSourceState(job.SourceId, SourceState.Queued);
    OnChanged();
    return Task.CompletedTask;
  }

  public void MoveJob(string jobId, int offset)
  {
    var queued = _jobs.Where(j => j.State == ScanJobState.Queued).ToList();
    var job = Job(jobId);
    var from = queued.IndexOf(job);
    var to = from + offset;
    if (from < 0 || to < 0 || to >= queued.Count)
    {
      return;
    }
    var a = _jobs.IndexOf(queued[from]);
    var b = _jobs.IndexOf(queued[to]);
    (_jobs[a], _jobs[b]) = (_jobs[b], _jobs[a]);
    OnChanged();
  }

  public Task RescanFailedAsync(string jobId)
  {
    var job = Job(jobId);
    var source = _sources.First(s => s.Id == job.SourceId);
    _jobs.Insert(QueueEnd(), new ScanJobInfo($"{job.Id}-retry", job.SourceId, source.Name, ScanJobKind.Update, ScanJobState.Queued, ScanPhase.Discover, Now)
    {
      Reason = string.Create(CultureInfo.InvariantCulture, $"重新掃描上次失敗的 {job.Errors} 個檔"),
    });
    Replace(job with { Errors = 0, ErrorSummary = null, Reason = "失敗的項目已重新排入佇列" });
    return Task.CompletedTask;
  }

  public void Decide(StackDecision decision)
  {
    ArgumentNullException.ThrowIfNull(decision);
    var stack = StackById(decision.StackId);
    var review = new StackReview(stack, decision);
    if (review.Block != ReviewBlock.None)
    {
      throw new InvalidOperationException($"Stack '{stack.Id}' is blocked: {review.Block}.");
    }

    _plan.RemoveAll(e => e.Stack.Id == stack.Id);
    switch (decision.Action)
    {
      case StackAction.Merge or StackAction.KeepKeeperOnly:
        _plan.Add(PlanBuilder.Build(review, Now));
        _decisions[stack.Id] = decision;
        SetStackStatus(stack.Id, StackStatus.Pending);
        PlanCheckedAt = null;
        break;
      case StackAction.KeepAll:
        _decisions[stack.Id] = decision;
        SetStackStatus(stack.Id, StackStatus.KeptAll);
        break;
      default:
        _decisions.Remove(stack.Id);
        SetStackStatus(stack.Id, StackStatus.Open);
        break;
    }
    OnChanged();
  }

  public int DecideSuggested(IReadOnlyList<string> stackIds)
  {
    ArgumentNullException.ThrowIfNull(stackIds);
    var added = 0;
    foreach (var id in stackIds)
    {
      var stack = StackById(id);
      if (stack.Status != StackStatus.Open || stack.Block != StackBlock.None)
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
    _plan.RemoveAll(e => e.Stack.Id == stackId);
    _decisions.Remove(stackId);
    SetStackStatus(stackId, StackStatus.Open);
    OnChanged();
  }

  public void SetPlanItemEnabled(string itemId, bool enabled) => UpdateItem(itemId, i => i with { Enabled = enabled });

  public void SetSkipChanged(string itemId, bool skip) => UpdateItem(itemId, i => i with { SkipChanged = skip });

  public async Task RecheckPlanAsync()
  {
    await Task.Delay(StepDelay / 2);
    for (var e = 0; e < _plan.Count; e++)
    {
      var entry = _plan[e];
      var items = entry.Items.Select(item =>
      {
        if (item.Kind == PlanItemKind.Recycle && item.SourcePath == ChangedCopyPath)
        {
          var changedAt = entry.DecidedAt + TimeSpan.FromSeconds(Math.Max(1, (Now - entry.DecidedAt).TotalSeconds / 2));
          return item with
          {
            Check = PlanCheck.Changed,
            ChangeDetail = string.Create(CultureInfo.InvariantCulture,
              $"你在 {entry.DecidedAt:HH:mm} 決定之後，這份裡的 src/app.ts 在 {changedAt:HH:mm} 被修改了。它已經和你看到的不一樣，所以先停下來。"),
          };
        }
        return item with { Check = PlanCheck.Confirmed, ChangeDetail = null };
      }).ToList();
      _plan[e] = entry with { Items = items, KeeperCheck = KeeperCheckText(entry) };
    }
    PlanCheckedAt = Now;
    OnChanged();
  }

  public async Task<ExecutionBatch> ExecutePlanAsync(CancellationToken cancellationToken)
  {
    if (PlanCheckedAt is null)
    {
      await RecheckPlanAsync();
    }
    await Task.Delay(StepDelay, cancellationToken);

    var batchId = string.Create(CultureInfo.InvariantCulture, $"batch-run{++_batchNumber}");
    var items = new List<BatchItem>();
    foreach (var entry in _plan)
    {
      foreach (var item in entry.Items.Where(i => i.Enabled))
      {
        var state = item.Check == PlanCheck.Changed ? BatchItemState.Skipped : BatchItemState.Done;
        var result = new BatchItem(item.Id, entry.Stack.Id, item.Kind, item.Target, state)
        {
          Detail = item.Kind == PlanItemKind.Recycle ? string.Empty : entry.Keeper.Path,
          Bytes = item.Bytes,
          FileCount = item.FileCount,
          RecycleVolume = item.Kind == PlanItemKind.Recycle ? Path.GetPathRoot(item.SourcePath) : null,
          FailureReason = state == BatchItemState.Skipped ? "決定之後有變動" : null,
        };
        if (state == BatchItemState.Done && item.SourcePath == FailingCopyPath && _failedOnce.Add(item.Id))
        {
          result = result with
          {
            State = BatchItemState.Failed,
            FailedFiles = 1,
            FailureReason = "清水寺-07.jpg 正在被「相片」使用，所以還在原處。關閉它之後再重試。",
          };
        }
        items.Add(result);
      }

      var skipped = items.Any(i => i.StackId == entry.Stack.Id && i.State == BatchItemState.Skipped);
      if (skipped)
      {
        _decisions.Remove(entry.Stack.Id);
      }
      SetStackStatus(entry.Stack.Id, skipped ? StackStatus.Open : StackStatus.Converged, raise: false);
    }

    var batch = new ExecutionBatch(batchId, Now, items);
    _history.Insert(0, batch);
    foreach (var item in items.Where(i => i.Kind == PlanItemKind.Recycle && i.State is BatchItemState.Done or BatchItemState.Failed))
    {
      var moved = item.State == BatchItemState.Done ? item.Bytes : item.Bytes - (item.Bytes / Math.Max(1, item.FileCount));
      AddRecycled(item.RecycleVolume!, moved);
    }
    _plan.Clear();
    PlanCheckedAt = null;
    OnChanged();
    return batch;
  }

  public async Task RetryFailedAsync(string batchId)
  {
    await Task.Delay(StepDelay);
    var batch = Batch(batchId);
    var items = batch.Items.Select(i =>
    {
      if (i.State != BatchItemState.Failed)
      {
        return i;
      }
      AddRecycled(i.RecycleVolume ?? @"C:\", i.Bytes / Math.Max(1, i.FileCount));
      return i with { State = BatchItemState.Done, FailedFiles = 0, FailureReason = null, Retried = true };
    }).ToList();
    ReplaceBatch(batch with { Items = items });
  }

  public Task RestoreItemAsync(string batchId, string itemId)
  {
    var batch = Batch(batchId);
    ReplaceBatch(batch with { Items = batch.Items.Select(i => i.Id == itemId ? Restore(i) : i).ToList() });
    return Task.CompletedTask;
  }

  public Task RestoreBatchAsync(string batchId)
  {
    var batch = Batch(batchId);
    var conflicted = _conflicts.Where(c => c.BatchId == batchId && c.Resolution is null).Select(c => c.ItemId).ToHashSet(StringComparer.Ordinal);
    ReplaceBatch(batch with { Items = batch.Items.Select(i => conflicted.Contains(i.Id) ? i : Restore(i)).ToList() });
    return Task.CompletedTask;
  }

  public Task ResolveRestoreConflictAsync(string conflictId, RestoreConflictChoice choice, string? folder)
  {
    var index = _conflicts.FindIndex(c => c.Id == conflictId);
    var conflict = _conflicts[index];
    var name = Path.GetFileName(conflict.OriginalPath);
    var resolution = choice switch
    {
      RestoreConflictChoice.RenameAtOriginal => string.Create(CultureInfo.InvariantCulture, $"已還原為 {name} (還原 {Now:MM-dd})，原位置的新檔沒有被改動。"),
      RestoreConflictChoice.OtherFolder => $"已還原到 {folder ?? "你指定的資料夾"}。",
      _ => "已留在回收區，之後仍可還原。",
    };
    _conflicts[index] = conflict with { Resolution = resolution };
    if (choice != RestoreConflictChoice.KeepInRecycleArea)
    {
      var batch = Batch(conflict.BatchId);
      ReplaceBatch(batch with { Items = batch.Items.Select(i => i.Id == conflict.ItemId ? Restore(i) : i).ToList() });
    }
    else
    {
      OnChanged();
    }
    return Task.CompletedTask;
  }

  public async Task EmptyRecycleAreasAsync()
  {
    await Task.Delay(StepDelay);
    for (var i = 0; i < _recycleAreas.Count; i++)
    {
      _recycleAreas[i] = _recycleAreas[i] with { Bytes = 0 };
    }
    for (var i = 0; i < _history.Count; i++)
    {
      if (_history[i].EmptiedAt is null && _history[i].Items.Any(item => item.Kind == PlanItemKind.Recycle && item.State == BatchItemState.Done))
      {
        _history[i] = _history[i] with { EmptiedAt = Now };
      }
    }
    OnChanged();
  }

  public void RespondToNotice(string noticeId, NoticeResponse response)
  {
    var notices = (_sinceNotices ?? _since.Notices).ToList();
    var index = notices.FindIndex(n => n.Id == noticeId);
    if (index < 0)
    {
      return;
    }
    var text = response switch
    {
      NoticeResponse.SameDrive => "已確認是同一顆。更新工作已排入佇列，目前正在探索。",
      NoticeResponse.NewSource => @"已當成新的來源加入，不會沿用 E:\ 的索引。",
      NoticeResponse.NotInStack => "已移出這疊。之後不會再把它和這疊放在一起。",
      _ => "維持都保留。下次有變動時才會再提醒。",
    };
    notices[index] = notices[index] with { HandledText = text };
    _sinceNotices = notices;
    OnChanged();
  }

  private BatchItem Restore(BatchItem item)
  {
    if (item.State != BatchItemState.Done)
    {
      return item;
    }
    if (item.Kind == PlanItemKind.Recycle && item.RecycleVolume is not null)
    {
      AddRecycled(item.RecycleVolume, -item.Bytes);
    }
    if (_stacks.FirstOrDefault(s => s.Id == item.StackId) is { Status: StackStatus.Converged } stack)
    {
      _decisions.Remove(stack.Id);
      SetStackStatus(stack.Id, StackStatus.Open, raise: false);
    }
    return item with { State = BatchItemState.Restored };
  }

  private static string KeeperCheckText(PlanEntry entry)
  {
    if (entry.Keeper.SourceId == "od")
    {
      return "正本已下載到本機並確認內容相同";
    }
    return entry.Keeper.IsProtected ? "正本還在，內容和你決定時相同；受保護，只會新增檔案" : "正本還在，內容和你決定時相同";
  }

  private void AddRecycled(string volume, long bytes)
  {
    var index = _recycleAreas.FindIndex(a => string.Equals(a.Volume, volume, StringComparison.OrdinalIgnoreCase));
    if (index < 0)
    {
      _recycleAreas.Add(new RecycleArea(volume, Math.Max(0, bytes)));
      return;
    }
    _recycleAreas[index] = _recycleAreas[index] with { Bytes = Math.Max(0, _recycleAreas[index].Bytes + bytes) };
  }

  private void UpdateItem(string itemId, Func<PlanItem, PlanItem> change)
  {
    for (var e = 0; e < _plan.Count; e++)
    {
      var entry = _plan[e];
      if (entry.Items.Any(i => i.Id == itemId))
      {
        _plan[e] = entry with { Items = entry.Items.Select(i => i.Id == itemId ? change(i) : i).ToList() };
        OnChanged();
        return;
      }
    }
  }

  private int QueueEnd()
  {
    var last = _jobs.FindLastIndex(j => j.State is ScanJobState.Running or ScanJobState.Pausing or ScanJobState.Queued);
    return last + 1;
  }

  private ScanJobInfo Job(string jobId) => _jobs.First(j => j.Id == jobId);

  private void Replace(ScanJobInfo job)
  {
    var index = _jobs.FindIndex(j => j.Id == job.Id);
    _jobs[index] = job;
    OnChanged();
  }

  private void SetSourceState(string sourceId, SourceState state)
  {
    var index = _sources.FindIndex(s => s.Id == sourceId);
    if (index >= 0 && _sources[index].State != SourceState.Scanning)
    {
      _sources[index] = _sources[index] with { State = state };
    }
  }

  private Stack StackById(string stackId) => _stacks.First(s => s.Id == stackId);

  private void SetStackStatus(string stackId, StackStatus status, bool raise = false)
  {
    var index = _stacks.FindIndex(s => s.Id == stackId);
    _stacks[index] = _stacks[index] with { Status = status };
    if (raise)
    {
      OnChanged();
    }
  }

  private ExecutionBatch Batch(string batchId) => _history.First(b => b.Id == batchId);

  private void ReplaceBatch(ExecutionBatch batch)
  {
    var index = _history.FindIndex(b => b.Id == batch.Id);
    _history[index] = batch;
    OnChanged();
  }

  private void OnChanged() => Changed?.Invoke(this, WorkbenchChangedEventArgs.Everything);
}
