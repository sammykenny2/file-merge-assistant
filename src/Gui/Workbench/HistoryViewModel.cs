using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Workbench;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.Workbench;

public sealed record BatchItemViewModel(ChipInfo Status, string Verb, string Target, string Detail, bool CanRestore, IRelayCommand Restore)
{
  public string RestoreName => AppText.Get("history.item.restoreName", ("target", Target));
}

public sealed record FailureViewModel(string Verb, string Target, string Reason);

public sealed record SkippedViewModel(string Text, IRelayCommand Open);

public sealed record BatchSummaryViewModel(string When, string State, string Counts, string Restore, bool IsSelected, bool IsLocked, bool HasProblem, IRelayCommand Select);

/// <summary>One recycle area in the side panel: 「D:\ 的回收區」 and how much it holds.</summary>
public sealed record RecycleAreaRow(string Name, long Bytes);

public sealed partial class RestoreChoiceViewModel : ObservableObject
{
  private readonly Action<RestoreChoiceViewModel> _picked;

  [ObservableProperty]
  private bool _isChosen;

  public RestoreChoiceViewModel(RestoreConflictChoice choice, string label, Action<RestoreChoiceViewModel> picked)
  {
    Choice = choice;
    Label = label;
    _picked = picked;
  }

  public RestoreConflictChoice Choice { get; }
  public string Label { get; }

  public bool IsSelected
  {
    get => IsChosen;
    set
    {
      if (value && !IsChosen)
      {
        _picked(this);
      }
    }
  }

  partial void OnIsChosenChanged(bool value) => OnPropertyChanged(nameof(IsSelected));
}

public sealed partial class RestoreConflictViewModel : ObservableObject
{
  private readonly IWorkbenchStore _store;
  private readonly RestoreConflict _conflict;

  [ObservableProperty]
  private string _actionLabel = AppText.Get("history.conflict.restore");

  [ObservableProperty]
  private bool _canResolve;

  public RestoreConflictViewModel(IWorkbenchStore store, RestoreConflict conflict, DateTimeOffset batchTime)
  {
    _store = store;
    _conflict = conflict;
    var now = store.Now;
    Kicker = BatchKicker(batchTime, now);
    Path = conflict.OriginalPath;
    Recycled = AppText.Get("history.conflict.recycled", ("day", Fmt.Day(conflict.RecycledAt, now)),
      ("size", Fmt.Size(conflict.RecycledBytes)), ("modified", Fmt.Day(conflict.RecycledModified, now)));
    Current = AppText.Get("history.conflict.current", ("day", Fmt.Day(conflict.CurrentCreated, now)), ("size", Fmt.Size(conflict.CurrentBytes)));
    Resolution = conflict.Resolution;
    // The suffix PlanExecutor gives the restored file, in the display language it is restored in.
    var renamed = $"{System.IO.Path.GetFileName(conflict.OriginalPath)} {PlanExecutor.RestoredSuffix(now)}";
    Choices =
    [
      new RestoreChoiceViewModel(RestoreConflictChoice.RenameAtOriginal, AppText.Get("history.conflict.renameAtOriginal", ("name", renamed)), Pick),
      new RestoreChoiceViewModel(RestoreConflictChoice.OtherFolder, AppText.Get("history.conflict.otherFolder"), Pick),
      new RestoreChoiceViewModel(RestoreConflictChoice.KeepInRecycleArea, AppText.Get("history.conflict.keep"), Pick),
    ];
  }

  /// <summary>「10/22 09:12 的整理」: which run a batch or a conflict belongs to.</summary>
  internal static string BatchKicker(DateTimeOffset ranAt, DateTimeOffset now) =>
    AppText.Get("history.kicker", ("day", Fmt.Day(ranAt, now)), ("time", Fmt.Time(ranAt)));

  public string Kicker { get; }
  public string Path { get; }
  public string Recycled { get; }
  public string Current { get; }
  public string? Resolution { get; }
  public bool IsResolved => Resolution is not null;
  public IReadOnlyList<RestoreChoiceViewModel> Choices { get; }
  public string GroupName => "restore:" + _conflict.Id;

  [RelayCommand]
  private async Task ResolveAsync()
  {
    var choice = Choices.FirstOrDefault(c => c.IsChosen);
    if (choice is null || IsResolved)
    {
      return;
    }
    string? folder = null;
    if (choice.Choice == RestoreConflictChoice.OtherFolder)
    {
      var dialog = new OpenFolderDialog { Title = AppText.Get("history.conflict.folderDialog") };
      if (dialog.ShowDialog() != true)
      {
        return;
      }
      folder = dialog.FolderName;
    }
    await _store.ResolveRestoreConflictAsync(_conflict.Id, choice.Choice, folder);
  }

  private void Pick(RestoreChoiceViewModel picked)
  {
    foreach (var choice in Choices)
    {
      choice.IsChosen = choice == picked;
    }
    ActionLabel = AppText.Get(picked.Choice == RestoreConflictChoice.KeepInRecycleArea ? "history.conflict.keepAction" : "history.conflict.restore");
    CanResolve = !IsResolved;
  }
}

/// <summary>Screen 6, 「執行結果、歷史與還原」.</summary>
public sealed partial class HistoryViewModel : PageViewModel
{
  private string? _batchId;

  [ObservableProperty]
  private bool _hasBatch;

  [ObservableProperty]
  private string _kicker = string.Empty;

  [ObservableProperty]
  private string _headline = string.Empty;

  [ObservableProperty]
  private string _summary = string.Empty;

  [ObservableProperty]
  private bool _isRetrying;

  [ObservableProperty]
  private string _retryLabel = string.Empty;

  [ObservableProperty]
  private bool _canRestoreAll;

  [ObservableProperty]
  private bool _isConfirmingRestoreAll;

  [ObservableProperty]
  private bool _isConfirmingEmpty;

  [ObservableProperty]
  private bool _isEmptying;

  [ObservableProperty]
  private bool _canEmpty;

  [ObservableProperty]
  private string _recycleTotal = string.Empty;

  [ObservableProperty]
  private string _emptyWarningBefore = string.Empty;

  [ObservableProperty]
  private string _emptyWarningAfter = string.Empty;

  public HistoryViewModel(IWorkbenchStore store, INavigator navigator, string? batchId)
    : base(store, navigator)
  {
    _batchId = batchId;
    Refresh();
  }

  public ObservableCollection<SpaceFigure> SpaceFigures { get; } = [];
  public ObservableCollection<FailureViewModel> Failures { get; } = [];
  public ObservableCollection<SkippedViewModel> Skipped { get; } = [];
  public ObservableCollection<BatchItemViewModel> Items { get; } = [];
  public ObservableCollection<RestoreConflictViewModel> Conflicts { get; } = [];
  public ObservableCollection<BatchSummaryViewModel> Batches { get; } = [];
  public ObservableCollection<RecycleAreaRow> Areas { get; } = [];

  public bool HasFailures => Failures.Count > 0;
  public bool HasSkipped => Skipped.Count > 0;
  public bool HasConflicts => Conflicts.Count > 0;

  public override void Refresh()
  {
    var now = Store.Now;
    var history = Store.History;
    var batch = history.FirstOrDefault(b => b.Id == _batchId) ?? (history.Count > 0 ? history[0] : null);
    _batchId = batch?.Id;
    HasBatch = batch is not null;

    Failures.Clear();
    Skipped.Clear();
    Items.Clear();
    SpaceFigures.Clear();
    if (batch is not null)
    {
      BuildBatch(batch, now);
    }

    Conflicts.Clear();
    foreach (var conflict in Store.RestoreConflicts)
    {
      var time = history.FirstOrDefault(b => b.Id == conflict.BatchId)?.RanAt ?? conflict.RecycledAt;
      Conflicts.Add(new RestoreConflictViewModel(Store, conflict, time));
    }

    Batches.Clear();
    foreach (var item in history)
    {
      var id = item.Id;
      Batches.Add(new BatchSummaryViewModel(
        $"{Fmt.Day(item.RanAt, now)} {Fmt.Time(item.RanAt)}",
        StateText(item),
        CountsText(item),
        RestoreText(item, now),
        item.Id == _batchId,
        item.EmptiedAt is not null,
        item.Failed > 0,
        new RelayCommand(() =>
        {
          _batchId = id;
          IsConfirmingRestoreAll = false;
          Refresh();
        })));
    }

    Areas.Clear();
    foreach (var area in Store.RecycleAreas)
    {
      Areas.Add(new RecycleAreaRow(AppText.Get("history.recycle.area", ("volume", area.Volume)), area.Bytes));
    }
    RecycleTotal = Fmt.Size(Store.RecycleAreas.Sum(a => a.Bytes));
    CanEmpty = Store.RecycleAreas.Any(a => a.Bytes > 0) && !IsEmptying;
    // One sentence in the catalog; the screen shows the total in bold where the placeholder sits.
    var warning = AppText.Get("history.recycle.emptyWarning");
    var at = warning.IndexOf(TotalPlaceholder, StringComparison.Ordinal);
    EmptyWarningBefore = at < 0 ? warning : warning[..at];
    EmptyWarningAfter = at < 0 ? string.Empty : warning[(at + TotalPlaceholder.Length)..];

    OnPropertyChanged(nameof(HasFailures));
    OnPropertyChanged(nameof(HasSkipped));
    OnPropertyChanged(nameof(HasConflicts));
  }

  [RelayCommand]
  private async Task RetryAsync()
  {
    if (_batchId is null || IsRetrying)
    {
      return;
    }
    IsRetrying = true;
    RetryLabel = AppText.Get("history.retry.running");
    try
    {
      await Store.RetryFailedAsync(_batchId);
    }
    finally
    {
      IsRetrying = false;
      Refresh();
    }
  }

  [RelayCommand]
  private void AskRestoreAll() => IsConfirmingRestoreAll = true;

  [RelayCommand]
  private void CancelRestoreAll() => IsConfirmingRestoreAll = false;

  [RelayCommand]
  private async Task RestoreAllAsync()
  {
    IsConfirmingRestoreAll = false;
    if (_batchId is not null)
    {
      await Store.RestoreBatchAsync(_batchId);
    }
  }

  [RelayCommand]
  private void AskEmpty() => IsConfirmingEmpty = true;

  [RelayCommand]
  private void CancelEmpty() => IsConfirmingEmpty = false;

  [RelayCommand]
  private async Task EmptyAsync()
  {
    IsConfirmingEmpty = false;
    IsEmptying = true;
    try
    {
      await Store.EmptyRecycleAreasAsync();
    }
    finally
    {
      IsEmptying = false;
      Refresh();
    }
  }

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();

  private void BuildBatch(ExecutionBatch batch, DateTimeOffset now)
  {
    Kicker = RestoreConflictViewModel.BatchKicker(batch.RanAt, now);
    Headline = StateText(batch);
    var ran = batch.Items.Count(i => i.State != BatchItemState.Skipped);
    // The clauses carry their own punctuation and always come in this order, as a list.
    var details = Clause("history.summary.done", batch.Done + batch.Restored)
      + (batch.Failed > 0 ? Clause("history.summary.failed", batch.Failed) : string.Empty)
      + (batch.Skipped > 0 ? Clause("history.summary.skipped", batch.Skipped) : string.Empty)
      + (batch.Restored > 0 ? Clause("history.summary.restored", batch.Restored) : string.Empty);
    Summary = AppText.Plural("history.summary", ran, ("count", Fmt.Count(ran)), ("details", details));

    var recycled = batch.Items.Where(i => i.Kind == PlanItemKind.Recycle).ToList();
    var failedRecycles = recycled.Where(i => i.State == BatchItemState.Failed).ToList();
    var doneRecycles = recycled.Count(i => i.State == BatchItemState.Done);
    var areas = Store.RecycleAreas.Where(a => a.Bytes > 0).ToList();
    SpaceFigures.Add(new SpaceFigure(AppText.Get("history.space.recycled"), Fmt.Size(batch.RecycledBytes),
      failedRecycles.Count > 0
        ? AppText.Get("history.space.recycledFailed")
        : AppText.Plural("history.space.recycledDone", doneRecycles, ("count", Fmt.Count(doneRecycles))), true));
    SpaceFigures.Add(new SpaceFigure(AppText.Get("history.space.inUse"), Fmt.Size(areas.Sum(a => a.Bytes)),
      areas.Count == 0
        ? AppText.Get("history.space.inUseEmpty")
        : string.Join(AppText.Get("plan.join.comma"), areas.Select(a => $"{a.Volume} {Fmt.Size(a.Bytes)}")), false));
    SpaceFigures.Add(new SpaceFigure(AppText.Get("history.space.freed"), batch.EmptiedAt is null ? "0 B" : Fmt.Size(batch.RecycledBytes),
      batch.EmptiedAt is { } emptiedAt
        ? AppText.Get("history.space.freedOn", ("day", Fmt.Day(emptiedAt, now)))
        : AppText.Get("history.space.freedLater"), false));

    foreach (var item in batch.Items.Where(i => i.State == BatchItemState.Failed))
    {
      Failures.Add(new FailureViewModel(PlanItemViewModel.VerbText(item.Kind), item.Target,
        item.FileCount > 1
          ? AppText.Get("history.failures.partial", ("count", Fmt.Count(item.FileCount - item.FailedFiles)), ("reason", item.FailureReason ?? string.Empty))
          : item.FailureReason ?? string.Empty));
    }
    RetryLabel = IsRetrying
      ? AppText.Get("history.retry.running")
      : AppText.Plural("history.retry", batch.Failed, ("count", Fmt.Count(batch.Failed)));

    foreach (var group in batch.Items.Where(i => i.State == BatchItemState.Skipped).GroupBy(i => i.StackId, StringComparer.Ordinal))
    {
      var stack = Store.Stacks.FirstOrDefault(s => s.Id == group.Key);
      var title = stack?.Title ?? AppText.Get("history.skipped.unknownStack");
      var stackId = group.Key;
      var reasons = group.Select(i => i.FailureReason ?? AppText.Get("history.skipped.reason")).Distinct(StringComparer.Ordinal);
      Skipped.Add(new SkippedViewModel(
        AppText.Get("history.skipped.text", ("targets", string.Join(AppText.Get("plan.join.enumeration"), group.Select(i => i.Target))),
          ("reasons", JoinSentences(reasons)), ("title", title)),
        new RelayCommand(() => Navigator.ShowStack(stackId))));
    }

    foreach (var item in batch.Items)
    {
      var batchId = batch.Id;
      var itemId = item.Id;
      var canRestore = item.CanRestore && batch.EmptiedAt is null && !Store.RestoreConflicts.Any(c => c.ItemId == item.Id && c.Resolution is null);
      Items.Add(new BatchItemViewModel(ItemStatus(batch, item), PlanItemViewModel.VerbText(item.Kind), item.Target, ItemDetail(item), canRestore,
        new AsyncRelayCommand(() => Store.RestoreItemAsync(batchId, itemId))));
    }
    CanRestoreAll = batch.EmptiedAt is null && batch.Items.Any(i => i.CanRestore);
  }

  private const string TotalPlaceholder = "{total}";

  private static string Clause(string key, long count) => AppText.Get(key, ("count", Fmt.Count(count)));

  /// <summary>
  /// Recorded reasons keep the language they were written in. Chinese sentences run on as before; a sentence that
  /// ends with a Latin full stop gets a space before the next one.
  /// </summary>
  private static string JoinSentences(IEnumerable<string> sentences)
  {
    var text = new StringBuilder();
    foreach (var sentence in sentences)
    {
      if (text.Length > 0 && text[^1] == '.')
      {
        text.Append(' ');
      }
      text.Append(sentence);
    }
    return text.ToString();
  }

  private static ChipInfo ItemStatus(ExecutionBatch batch, BatchItem item) => item.State switch
  {
    BatchItemState.Running => new ChipInfo(AppText.Get("history.status.interrupted"), Tone.Warn),
    BatchItemState.Emptied => new ChipInfo(AppText.Get("history.status.emptied"), Tone.Plain),
    BatchItemState.Failed => new ChipInfo(AppText.Get("history.status.failed"), Tone.Warn),
    BatchItemState.Skipped => new ChipInfo(AppText.Get("history.status.skipped"), Tone.Skipped),
    BatchItemState.Restored => new ChipInfo(AppText.Get("history.status.restored"), Tone.Plain),
    _ when batch.EmptiedAt is not null && item.Kind == PlanItemKind.Recycle => new ChipInfo(AppText.Get("history.status.emptied"), Tone.Plain),
    _ => item.Kind switch
    {
      PlanItemKind.Copy => new ChipInfo(AppText.Get("history.status.copied"), Tone.Done),
      PlanItemKind.SaveAs => new ChipInfo(AppText.Get("history.status.savedAs"), Tone.Done),
      PlanItemKind.Replace => new ChipInfo(AppText.Get("history.status.replaced"), Tone.Done),
      _ => new ChipInfo(AppText.Get("history.status.restorable"), Tone.Done),
    },
  };

  private static string ItemDetail(BatchItem item)
  {
    // A finished item can carry a note from a restore that did not go ahead (for example, the file was edited since).
    var note = item.State == BatchItemState.Done && item.FailureReason is { } reason
      ? AppText.Get("history.item.note", ("reason", reason))
      : string.Empty;
    if (item.Kind != PlanItemKind.Recycle)
    {
      return item.State switch
      {
        BatchItemState.Failed or BatchItemState.Skipped or BatchItemState.Running => item.FailureReason ?? string.Empty,
        _ => (item.Detail.Length > 0 ? AppText.Get("history.item.to", ("detail", item.Detail)) : string.Empty) + note,
      };
    }
    var size = item.FileCount > 1 ? PlanItemViewModel.FilesText(item.FileCount, item.Bytes) : Fmt.Size(item.Bytes);
    return item.State switch
    {
      BatchItemState.Running => item.FailureReason ?? AppText.Get("history.item.interrupted"),
      BatchItemState.Failed => AppText.Plural("history.item.failedFiles", item.FileCount, ("done", Fmt.Count(item.FileCount - item.FailedFiles)),
        ("count", Fmt.Count(item.FileCount)), ("reason", item.FailureReason ?? string.Empty)),
      BatchItemState.Skipped => item.FailureReason ?? AppText.Get("history.item.skipped"),
      BatchItemState.Restored => AppText.Get("history.item.restored", ("size", size)),
      BatchItemState.Emptied => AppText.Get("history.item.emptied", ("size", size)),
      _ => AppText.Get(item.Retried ? "history.item.inRecycleRetried" : "history.item.inRecycle", ("size", size),
        ("volume", item.RecycleVolume ?? string.Empty)) + note,
    };
  }

  private static string StateText(ExecutionBatch batch)
  {
    if (batch.EmptiedAt is not null)
    {
      return AppText.Get("history.state.emptied");
    }
    if (batch.Failed > 0)
    {
      return AppText.Get("history.state.partial");
    }
    return batch.Skipped > 0 ? Clause("history.state.doneSkipped", batch.Skipped) : AppText.Get("history.state.done");
  }

  private static string CountsText(ExecutionBatch batch) =>
    Clause("history.counts.done", batch.Done + batch.Restored)
    + (batch.Failed > 0 ? Clause("history.counts.failed", batch.Failed) : string.Empty)
    + (batch.Skipped > 0 ? Clause("history.counts.skipped", batch.Skipped) : string.Empty);

  private string RestoreText(ExecutionBatch batch, DateTimeOffset now)
  {
    if (batch.EmptiedAt is { } emptied)
    {
      return AppText.Get("history.restore.emptied", ("day", Fmt.Day(emptied, now)));
    }
    var conflicts = Store.RestoreConflicts.Count(c => c.BatchId == batch.Id && c.Resolution is null);
    var restorable = batch.Done - conflicts;
    return AppText.Plural("history.restore.available", restorable, ("count", Fmt.Count(restorable)))
      + (batch.Restored > 0 ? Clause("history.restore.restored", batch.Restored) : string.Empty)
      + (conflicts > 0 ? Clause("history.restore.conflicts", conflicts) : string.Empty);
  }
}
