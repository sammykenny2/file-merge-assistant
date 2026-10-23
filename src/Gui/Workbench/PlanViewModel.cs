using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Workbench;

public sealed partial class PlanItemViewModel : ObservableObject
{
  private readonly IWorkbenchStore _store;

  public PlanItemViewModel(IWorkbenchStore store, PlanEntry entry, PlanItem item)
  {
    _store = store;
    Id = item.Id;
    Verb = item.Kind switch
    {
      PlanItemKind.Copy => "複製",
      PlanItemKind.SaveAs => "另存",
      PlanItemKind.Replace => "取代",
      _ => "移入回收區",
    };
    IsCopy = item.IsCopyLike;
    Target = item.Target;
    Detail = item.Kind switch
    {
      PlanItemKind.Copy => $"從 {SourceRoot(entry, item)} 複製到正本" + (item.LooksLikeSecret ? "；看起來是金鑰或密碼設定檔" : string.Empty),
      PlanItemKind.SaveAs => $"{item.CopyName}那份的版本；正本原本的檔不變",
      PlanItemKind.Replace => $"用{item.CopyName}那份的版本取代；正本原本的版本移入回收區",
      _ => item.FileCount > 1
        ? $"{Fmt.Count(item.FileCount)} 個檔，{Fmt.Size(item.Bytes)}" + (item.IncludesRegenerable is null ? string.Empty : $"，含 {item.IncludesRegenerable}")
        : Fmt.Size(item.Bytes),
    };
    _isEnabled = item.Enabled;
    IsChanged = item.Check == PlanCheck.Changed && item.Enabled;
    ChangeDetail = item.ChangeDetail;
    IsSkipped = item.SkipChanged;
    Status = !item.Enabled
      ? new ChipInfo("不執行", Tone.Skipped)
      : item.Check switch
      {
        PlanCheck.Changed when item.SkipChanged => new ChipInfo("這次略過", Tone.Skipped),
        PlanCheck.Changed => new ChipInfo("有變動，先停下", Tone.Warn),
        PlanCheck.Confirmed => new ChipInfo("已重新確認", Tone.Done),
        _ => new ChipInfo("待確認", Tone.Wait),
      };
    DependencyWarning = PlanBuilder.HasDependencyWarning(entry, item)
      ? "沒有複製的話，這個檔只存在於即將移入回收區的那份，會跟著一起移入。"
      : null;
    ToggleName = (item.Enabled ? "不執行：" : "執行：") + Verb + " " + Target;
    SkipLabel = item.SkipChanged ? "改回先停下" : "這次略過這項";
  }

  public string Id { get; }
  public string Verb { get; }
  public bool IsCopy { get; }
  public string Target { get; }
  public string Detail { get; }
  public bool IsChanged { get; }
  public string? ChangeDetail { get; }
  public bool IsSkipped { get; }
  public ChipInfo Status { get; }
  public string? DependencyWarning { get; }
  public string ToggleName { get; }
  public string SkipLabel { get; }

  [ObservableProperty]
  private bool _isEnabled;

  partial void OnIsEnabledChanged(bool value) => _store.SetPlanItemEnabled(Id, value);

  [RelayCommand]
  private void ToggleSkip() => _store.SetSkipChanged(Id, !IsSkipped);

  private static string SourceRoot(PlanEntry entry, PlanItem item) =>
    entry.Stack.Copies.FirstOrDefault(c => c.Id == item.CopyId)?.Path ?? item.CopyName;
}

public sealed record PlanStackViewModel(
  string Title,
  string Decision,
  string KeeperPrefix,
  string KeeperTail,
  string? KeeperCheck,
  IReadOnlyList<PlanItemViewModel> Items,
  string? Foot,
  IRelayCommand Edit,
  IRelayCommand Remove);

public sealed record SpaceFigure(string Label, string Value, string Note, bool IsAccent);

/// <summary>Screen 5, 「待執行：執行前確認」.</summary>
public sealed partial class PlanViewModel : PageViewModel
{
  [ObservableProperty]
  private string _title = string.Empty;

  [ObservableProperty]
  private string _checkLine = string.Empty;

  [ObservableProperty]
  private bool _isChecking;

  [ObservableProperty]
  private bool _isRunning;

  [ObservableProperty]
  private string _summary = string.Empty;

  [ObservableProperty]
  private string _executeLabel = string.Empty;

  [ObservableProperty]
  private bool _canExecute;

  [ObservableProperty]
  private bool _hasStopped;

  [ObservableProperty]
  private bool _isEmpty;

  public PlanViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    Refresh();
    if (Store.Plan.Count > 0 && Store.PlanCheckedAt is null)
    {
      _ = RecheckAsync();
    }
  }

  public ObservableCollection<PlanStackViewModel> Stacks { get; } = [];
  public ObservableCollection<SpaceFigure> SpaceFigures { get; } = [];

  public override void Refresh()
  {
    var plan = Store.Plan;
    IsEmpty = plan.Count == 0;
    Title = $"待執行：{Fmt.Count(plan.Count)} 疊";

    Stacks.Clear();
    foreach (var entry in plan)
    {
      var (prefix, tail) = Fmt.SplitPath(entry.Keeper.Path);
      var stackId = entry.Stack.Id;
      var decision = entry.Decision.Action == StackAction.Merge ? "合併到正本，再把其他份移入回收區" : "只保留正本";
      if (entry.Decision.FromSuggestion)
      {
        decision += "（依建議加入）";
      }
      var foot = entry.OfflineCopies.Count == 0
        ? null
        : string.Join("；", entry.OfflineCopies.Select(c => $"先不動：{c.Path}（離線，接上後會再提醒）"));
      Stacks.Add(new PlanStackViewModel(entry.Stack.Title, decision, prefix, tail, entry.KeeperCheck,
        entry.Items.Select(i => new PlanItemViewModel(Store, entry, i)).ToList(), foot,
        new RelayCommand(() => Navigator.ShowStack(stackId)),
        new RelayCommand(() => Store.Undecide(stackId))));
    }

    var items = plan.SelectMany(e => e.Items).ToList();
    var running = items.Where(i => i.WillRun).ToList();
    var copies = running.Count(i => i.IsCopyLike);
    var recycles = running.Count(i => i.Kind == PlanItemKind.Recycle);
    var recycleBytes = running.Where(i => i.Kind == PlanItemKind.Recycle).Sum(i => i.Bytes);
    HasStopped = items.Any(i => i.Enabled && i.Check == PlanCheck.Changed && !i.SkipChanged);
    var changed = items.Count(i => i.Enabled && i.Check == PlanCheck.Changed);

    CheckLine = IsChecking
      ? "正在重新檢查每個檔的現況…"
      : Store.PlanCheckedAt is { } at
        ? $"{Fmt.When(at, Store.Now)} 重新檢查了 {Fmt.Count(items.Count)} 項：" + (changed > 0 ? $"{Fmt.Count(items.Count - changed)} 項可以執行，{Fmt.Count(changed)} 項有變動。" : "可以執行的都已確認。")
        : "還沒有重新檢查。執行前會先檢查一次。";
    Summary = $"會執行 {Fmt.Count(running.Count)} 項：複製或另存 {Fmt.Count(copies)} 個檔到正本，移入回收區 {Fmt.Count(recycles)} 項，共 {Fmt.Size(recycleBytes)}。";
    ExecuteLabel = $"執行 {Fmt.Count(running.Count)} 項";
    CanExecute = running.Count > 0 && Store.PlanCheckedAt is not null && !IsChecking && !IsRunning;

    var duplicate = plan.Sum(e => e.Stack.Copies.Where(c => c.Id != e.Keeper.Id && !e.Decision.Removed.Contains(c.Id)).Sum(c => c.TotalBytes));
    var offlineNote = plan.SelectMany(e => e.OfflineCopies).Select(c => System.IO.Path.GetPathRoot(c.Path)).Distinct().ToList();
    SpaceFigures.Clear();
    SpaceFigures.Add(new SpaceFigure($"這 {Fmt.Count(plan.Count)} 疊的複本共佔", Fmt.Size(duplicate),
      "所有不是正本的那幾份加起來" + (offlineNote.Count > 0 ? $"，包含離線的 {string.Join("、", offlineNote)}。" : "。"), false));
    SpaceFigures.Add(new SpaceFigure("這次會移入回收區", Fmt.Size(recycleBytes), "只算勾選而且可以執行的項目。", true));
    var areas = Store.RecycleAreas.Where(a => a.Bytes > 0).ToList();
    SpaceFigures.Add(new SpaceFigure("回收區目前已佔用", Fmt.Size(areas.Sum(a => a.Bytes)),
      areas.Count == 0 ? "目前是空的。" : string.Join("，", areas.Select(a => $"{a.Volume} {Fmt.Size(a.Bytes)}")) + "，來自之前的整理。", false));
    SpaceFigures.Add(new SpaceFigure("實際釋放", "0 B", "移入回收區不等於釋放空間；清空回收區後才會釋放。", false));
  }

  partial void OnIsCheckingChanged(bool value) => Refresh();

  [RelayCommand]
  private async Task RecheckAsync()
  {
    IsChecking = true;
    try
    {
      await Store.RecheckPlanAsync();
    }
    finally
    {
      IsChecking = false;
    }
  }

  [RelayCommand]
  private async Task ExecuteAsync()
  {
    if (!CanExecute)
    {
      return;
    }
    IsRunning = true;
    CanExecute = false;
    try
    {
      var batch = await Store.ExecutePlanAsync(CancellationToken.None);
      Navigator.ShowHistory(batch.Id);
    }
    finally
    {
      IsRunning = false;
    }
  }

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();
}
