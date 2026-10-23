using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Workbench;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.Workbench;

public sealed record BatchItemViewModel(ChipInfo Status, string Verb, string Target, string Detail, bool CanRestore, IRelayCommand Restore)
{
  public string RestoreName => $"還原：{Target}";
}

public sealed record FailureViewModel(string Verb, string Target, string Reason);

public sealed record SkippedViewModel(string Text, IRelayCommand Open);

public sealed record BatchSummaryViewModel(string When, string State, string Counts, string Restore, bool IsSelected, bool IsLocked, bool HasProblem, IRelayCommand Select);

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
  private string _actionLabel = "還原這項";

  [ObservableProperty]
  private bool _canResolve;

  public RestoreConflictViewModel(IWorkbenchStore store, RestoreConflict conflict, DateTimeOffset batchTime)
  {
    _store = store;
    _conflict = conflict;
    var now = store.Now;
    Kicker = $"{Fmt.Day(batchTime, now)} {Fmt.Time(batchTime)} 的整理";
    Path = conflict.OriginalPath;
    Recycled = $"{Fmt.Day(conflict.RecycledAt, now)} 移入，{Fmt.Size(conflict.RecycledBytes)}，最後修改 {Fmt.Day(conflict.RecycledModified, now)}";
    Current = $"{Fmt.Day(conflict.CurrentCreated, now)} 建立，{Fmt.Size(conflict.CurrentBytes)}，內容不同";
    Resolution = conflict.Resolution;
    var name = System.IO.Path.GetFileName(conflict.OriginalPath);
    Choices =
    [
      new RestoreChoiceViewModel(RestoreConflictChoice.RenameAtOriginal, $"還原到原位置，改名為 {name} (還原 {now:MM-dd})", Pick),
      new RestoreChoiceViewModel(RestoreConflictChoice.OtherFolder, "還原到我指定的資料夾…", Pick),
      new RestoreChoiceViewModel(RestoreConflictChoice.KeepInRecycleArea, "這項先不還原，留在回收區", Pick),
    ];
  }

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
      var dialog = new OpenFolderDialog { Title = "選擇要還原到哪個資料夾" };
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
    ActionLabel = picked.Choice == RestoreConflictChoice.KeepInRecycleArea ? "留在回收區" : "還原這項";
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
  public ObservableCollection<RecycleArea> Areas { get; } = [];

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
      Areas.Add(area);
    }
    RecycleTotal = Fmt.Size(Store.RecycleAreas.Sum(a => a.Bytes));
    CanEmpty = Store.RecycleAreas.Any(a => a.Bytes > 0) && !IsEmptying;

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
    RetryLabel = "重試中…";
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
    Kicker = $"{Fmt.Day(batch.RanAt, now)} {Fmt.Time(batch.RanAt)} 的整理";
    Headline = StateText(batch);
    var ran = batch.Items.Count(i => i.State != BatchItemState.Skipped);
    Summary = $"執行了 {Fmt.Count(ran)} 項：{Fmt.Count(batch.Done + batch.Restored)} 項完成"
      + (batch.Failed > 0 ? $"，{Fmt.Count(batch.Failed)} 項失敗" : string.Empty)
      + (batch.Skipped > 0 ? $"；另有 {Fmt.Count(batch.Skipped)} 項這次略過" : string.Empty)
      + (batch.Restored > 0 ? $"；{Fmt.Count(batch.Restored)} 項已還原" : string.Empty)
      + "。";

    var recycled = batch.Items.Where(i => i.Kind == PlanItemKind.Recycle).ToList();
    var failedRecycles = recycled.Where(i => i.State == BatchItemState.Failed).ToList();
    var areas = Store.RecycleAreas.Where(a => a.Bytes > 0).ToList();
    SpaceFigures.Add(new SpaceFigure("這次移入回收區", Fmt.Size(batch.RecycledBytes),
      failedRecycles.Count > 0 ? "失敗的部分還在原處" : $"{Fmt.Count(recycled.Count(i => i.State == BatchItemState.Done))} 項，全部完成", true));
    SpaceFigures.Add(new SpaceFigure("回收區現在佔用", Fmt.Size(areas.Sum(a => a.Bytes)),
      areas.Count == 0 ? "目前是空的" : string.Join("，", areas.Select(a => $"{a.Volume} {Fmt.Size(a.Bytes)}")), false));
    SpaceFigures.Add(new SpaceFigure("實際釋放", batch.EmptiedAt is null ? "0 B" : Fmt.Size(batch.RecycledBytes),
      batch.EmptiedAt is null ? "清空回收區後才會釋放" : $"回收區在 {Fmt.Day(batch.EmptiedAt.Value, now)} 清空", false));

    foreach (var item in batch.Items.Where(i => i.State == BatchItemState.Failed))
    {
      Failures.Add(new FailureViewModel(Verb(item), item.Target,
        (item.FileCount > 1 ? $"其中 {Fmt.Count(item.FileCount - item.FailedFiles)} 個已完成。" : string.Empty) + item.FailureReason));
    }
    RetryLabel = $"重試失敗的 {Fmt.Count(batch.Failed)} 項";

    foreach (var group in batch.Items.Where(i => i.State == BatchItemState.Skipped).GroupBy(i => i.StackId, StringComparer.Ordinal))
    {
      var stack = Store.Stacks.FirstOrDefault(s => s.Id == group.Key);
      var title = stack?.Title ?? "這疊";
      var stackId = group.Key;
      Skipped.Add(new SkippedViewModel(
        $"{string.Join("、", group.Select(i => i.Target))}：你決定之後有變動。{title} 這疊已放回待整理，其他項目照常完成。",
        new RelayCommand(() => Navigator.ShowStack(stackId))));
    }

    foreach (var item in batch.Items)
    {
      var batchId = batch.Id;
      var itemId = item.Id;
      var canRestore = item.CanRestore && batch.EmptiedAt is null && !Store.RestoreConflicts.Any(c => c.ItemId == item.Id && c.Resolution is null);
      Items.Add(new BatchItemViewModel(ItemStatus(batch, item), Verb(item), item.Target, ItemDetail(item), canRestore,
        new AsyncRelayCommand(() => Store.RestoreItemAsync(batchId, itemId))));
    }
    CanRestoreAll = batch.EmptiedAt is null && batch.Items.Any(i => i.CanRestore);
  }

  private static string Verb(BatchItem item) => item.Kind switch
  {
    PlanItemKind.Copy => "複製",
    PlanItemKind.SaveAs => "另存",
    PlanItemKind.Replace => "取代",
    _ => "移入回收區",
  };

  private static ChipInfo ItemStatus(ExecutionBatch batch, BatchItem item) => item.State switch
  {
    BatchItemState.Failed => new ChipInfo("失敗", Tone.Warn),
    BatchItemState.Skipped => new ChipInfo("略過", Tone.Skipped),
    BatchItemState.Restored => new ChipInfo("已還原", Tone.Plain),
    _ when batch.EmptiedAt is not null && item.Kind == PlanItemKind.Recycle => new ChipInfo("已清空", Tone.Plain),
    _ => item.Kind switch
    {
      PlanItemKind.Copy => new ChipInfo("已複製", Tone.Done),
      PlanItemKind.SaveAs => new ChipInfo("已另存", Tone.Done),
      PlanItemKind.Replace => new ChipInfo("已取代", Tone.Done),
      _ => new ChipInfo("可以還原", Tone.Done),
    },
  };

  private static string ItemDetail(BatchItem item)
  {
    if (item.Kind != PlanItemKind.Recycle)
    {
      return item.Detail.Length > 0 ? $"到 {item.Detail}" : string.Empty;
    }
    var size = item.FileCount > 1 ? $"{Fmt.Count(item.FileCount)} 個檔，{Fmt.Size(item.Bytes)}" : Fmt.Size(item.Bytes);
    return item.State switch
    {
      BatchItemState.Failed => $"{Fmt.Count(item.FileCount - item.FailedFiles)} / {Fmt.Count(item.FileCount)} 個檔完成；{item.FailureReason}",
      BatchItemState.Skipped => item.FailureReason ?? "這次略過",
      BatchItemState.Restored => $"{size}，已放回原處",
      _ => $"{size}，放在 {item.RecycleVolume} 的回收區" + (item.Retried ? "；重試後完成" : string.Empty),
    };
  }

  private static string StateText(ExecutionBatch batch)
  {
    if (batch.EmptiedAt is not null)
    {
      return "回收區已清空";
    }
    if (batch.Failed > 0)
    {
      return "部分完成";
    }
    return batch.Skipped > 0 ? $"完成，{Fmt.Count(batch.Skipped)} 項略過" : "完成";
  }

  private static string CountsText(ExecutionBatch batch) =>
    $"{Fmt.Count(batch.Done + batch.Restored)} 項完成"
    + (batch.Failed > 0 ? $"，{Fmt.Count(batch.Failed)} 項失敗" : string.Empty)
    + (batch.Skipped > 0 ? $"，{Fmt.Count(batch.Skipped)} 項略過" : string.Empty);

  private string RestoreText(ExecutionBatch batch, DateTimeOffset now)
  {
    if (batch.EmptiedAt is { } emptied)
    {
      return $"已經不能還原：回收區在 {Fmt.Day(emptied, now)} 清空";
    }
    var conflicts = Store.RestoreConflicts.Count(c => c.BatchId == batch.Id && c.Resolution is null);
    return $"可以還原 {Fmt.Count(batch.Done - conflicts)} 項"
      + (batch.Restored > 0 ? $"，{Fmt.Count(batch.Restored)} 項已還原" : string.Empty)
      + (conflicts > 0 ? $"，{Fmt.Count(conflicts)} 項還原時有衝突" : string.Empty);
  }
}
