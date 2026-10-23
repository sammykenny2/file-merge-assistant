using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Localization;
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
    Verb = VerbText(item.Kind);
    IsCopy = item.IsCopyLike;
    Target = item.Target;
    Detail = item.Kind switch
    {
      PlanItemKind.Copy => AppText.Get(item.LooksLikeSecret ? "plan.item.copyDetailSecret" : "plan.item.copyDetail", ("source", SourceRoot(entry, item))),
      PlanItemKind.SaveAs => AppText.Get("plan.item.saveAsDetail", ("copy", item.CopyName)),
      PlanItemKind.Replace => AppText.Get("plan.item.replaceDetail", ("copy", item.CopyName)),
      _ => item.FileCount > 1 ? FilesText(item.FileCount, item.Bytes, item.IncludesRegenerable) : Fmt.Size(item.Bytes),
    };
    _isEnabled = item.Enabled;
    IsChanged = item.Check == PlanCheck.Changed && item.Enabled;
    ChangeDetail = item.ChangeDetail;
    IsSkipped = item.SkipChanged;
    Status = !item.Enabled
      ? new ChipInfo(AppText.Get("plan.status.off"), Tone.Skipped)
      : item.Check switch
      {
        PlanCheck.Changed when item.SkipChanged => new ChipInfo(AppText.Get("plan.status.skipped"), Tone.Skipped),
        PlanCheck.Changed => new ChipInfo(AppText.Get("plan.status.changed"), Tone.Warn),
        PlanCheck.Confirmed => new ChipInfo(AppText.Get("plan.status.confirmed"), Tone.Done),
        _ => new ChipInfo(AppText.Get("plan.status.waiting"), Tone.Wait),
      };
    DependencyWarning = PlanBuilder.HasDependencyWarning(entry, item)
      ? AppText.Get("plan.item.dependency")
      : null;
    ToggleName = AppText.Get(item.Enabled ? "plan.item.toggleOff" : "plan.item.toggleOn", ("verb", Verb), ("target", Target));
    SkipLabel = AppText.Get(item.SkipChanged ? "plan.item.unskip" : "plan.item.skip");
  }

  /// <summary>What a plan item does, as the plan and the history show it.</summary>
  internal static string VerbText(PlanItemKind kind) => kind switch
  {
    PlanItemKind.Copy => AppText.Get("plan.verb.copy"),
    PlanItemKind.SaveAs => AppText.Get("plan.verb.saveAs"),
    PlanItemKind.Replace => AppText.Get("plan.verb.replace"),
    _ => AppText.Get("plan.verb.recycle"),
  };

  /// <summary>「12 個檔，38 MB」, with the regenerable folders it includes when there are any.</summary>
  internal static string FilesText(long count, long bytes, string? regenerable = null) => regenerable is null
    ? AppText.Plural("plan.item.files", count, ("count", Fmt.Count(count)), ("size", Fmt.Size(bytes)))
    : AppText.Plural("plan.item.filesRegenerable", count, ("count", Fmt.Count(count)), ("size", Fmt.Size(bytes)), ("regenerable", regenerable));

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

  [ObservableProperty]
  private string? _unavailableReason;

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
    Title = AppText.Plural("plan.title", plan.Count, ("count", Fmt.Count(plan.Count)));

    Stacks.Clear();
    foreach (var entry in plan)
    {
      var (prefix, tail) = Fmt.SplitPath(entry.Keeper.Path);
      var stackId = entry.Stack.Id;
      var decision = AppText.Get((entry.Decision.Action == StackAction.Merge, entry.Decision.FromSuggestion) switch
      {
        (true, true) => "plan.decision.mergeSuggested",
        (true, false) => "plan.decision.merge",
        (false, true) => "plan.decision.keepOnlySuggested",
        _ => "plan.decision.keepOnly",
      });
      var foot = entry.OfflineCopies.Count == 0
        ? null
        : string.Join(AppText.Get("plan.join.semicolon"), entry.OfflineCopies.Select(c => AppText.Get("plan.stack.offline", ("path", c.Path))));
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
      ? AppText.Get("plan.check.checking")
      : Store.PlanCheckedAt is { } at
        ? AppText.Plural(changed > 0 ? "plan.check.changed" : "plan.check.confirmed", items.Count,
            ("when", Fmt.When(at, Store.Now)), ("count", Fmt.Count(items.Count)),
            ("ready", Fmt.Count(items.Count - changed)), ("changed", Fmt.Count(changed)))
        : AppText.Get("plan.check.never");
    Summary = AppText.Plural("plan.summary", running.Count, ("count", Fmt.Count(running.Count)), ("copies", Fmt.Count(copies)),
      ("recycles", Fmt.Count(recycles)), ("size", Fmt.Size(recycleBytes)));
    ExecuteLabel = AppText.Plural("plan.run", running.Count, ("count", Fmt.Count(running.Count)));
    UnavailableReason = Store.CanExecute ? null : Store.ExecuteUnavailableReason;
    CanExecute = Store.CanExecute && running.Count > 0 && Store.PlanCheckedAt is not null && !IsChecking && !IsRunning;

    var duplicate = plan.Sum(e => e.Stack.Copies.Where(c => c.Id != e.Keeper.Id && !e.Decision.Removed.Contains(c.Id)).Sum(c => c.TotalBytes));
    var offlineNote = plan.SelectMany(e => e.OfflineCopies).Select(c => System.IO.Path.GetPathRoot(c.Path)).Distinct().ToList();
    SpaceFigures.Clear();
    SpaceFigures.Add(new SpaceFigure(AppText.Plural("plan.space.copies", plan.Count, ("count", Fmt.Count(plan.Count))), Fmt.Size(duplicate),
      offlineNote.Count > 0
        ? AppText.Get("plan.space.copiesNoteOffline", ("drives", string.Join(AppText.Get("plan.join.enumeration"), offlineNote)))
        : AppText.Get("plan.space.copiesNote"), false));
    SpaceFigures.Add(new SpaceFigure(AppText.Get("plan.space.thisRun"), Fmt.Size(recycleBytes), AppText.Get("plan.space.thisRunNote"), true));
    var areas = Store.RecycleAreas.Where(a => a.Bytes > 0).ToList();
    SpaceFigures.Add(new SpaceFigure(AppText.Get("plan.space.inUse"), Fmt.Size(areas.Sum(a => a.Bytes)),
      areas.Count == 0
        ? AppText.Get("plan.space.inUseEmpty")
        : AppText.Get("plan.space.inUseNote", ("areas", string.Join(AppText.Get("plan.join.comma"), areas.Select(a => $"{a.Volume} {Fmt.Size(a.Bytes)}")))), false));
    SpaceFigures.Add(new SpaceFigure(AppText.Get("plan.space.freed"), "0 B", AppText.Get("plan.space.freedNote"), false));
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
