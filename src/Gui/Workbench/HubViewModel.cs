using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Workbench;

public sealed record PlaceChip(string Name, bool IsOffline);

/// <summary>The suggested next stack at the top of the workbench.</summary>
public sealed record FeaturedStackViewModel(
  string StackId,
  string Title,
  string Kind,
  string Reason,
  string Explanation,
  int CopyCount,
  IReadOnlyList<PlaceChip> Places);

public sealed record HubItemViewModel(
  string StackId,
  string Title,
  string Kind,
  string Where,
  string Detail,
  string Size,
  int CopyCount,
  string? BlockedText,
  bool IsOfflineBlock,
  IRelayCommand Open)
{
  public bool IsReady => BlockedText is null;
  public string OpenLabel => AppText.Get(IsReady ? "hub.item.review" : "hub.item.look");
  public string OpenName => AppText.Get(IsReady ? "hub.item.reviewName" : "hub.item.lookName", ("title", Title));
}

public sealed partial class HubGroupViewModel : ObservableObject
{
  [ObservableProperty]
  private bool _isBulkOpen;

  public HubGroupViewModel(StackGroup group, IReadOnlyList<HubItemViewModel> items, bool offersBulk, IRelayCommand bulk)
  {
    Title = StackText.GroupTitle(group);
    Description = StackText.GroupDescription(group);
    Items = items;
    var ready = items.Count(i => i.IsReady);
    var blocked = items.Count - ready;
    HasBulk = offersBulk && ready > 0;
    BulkLabel = AppText.Plural("hub.bulk.label", ready, ("count", ready));
    BulkGo = AppText.Plural("hub.bulk.go", ready, ("count", ready));
    BulkText = AppText.Plural(blocked > 0 ? "hub.bulk.textOffline" : "hub.bulk.text", ready, ("count", ready), ("offline", blocked));
    Bulk = bulk;
  }

  public string Title { get; }
  public string Description { get; }
  public IReadOnlyList<HubItemViewModel> Items { get; }
  public bool HasBulk { get; }
  public string BulkLabel { get; }
  public string BulkGo { get; }
  public string BulkText { get; }
  public IRelayCommand Bulk { get; }

  [RelayCommand]
  private void ToggleBulk() => IsBulkOpen = !IsBulkOpen;
}

public sealed partial class PairFilterViewModel : ObservableObject
{
  [ObservableProperty]
  private bool _isSelected;

  public PairFilterViewModel(string key, string label, int count, double share, bool selected, Action<PairFilterViewModel> picked)
  {
    Key = key;
    Label = label;
    Count = count;
    BarWidth = Math.Max(4, share * 64);
    _isSelected = selected;
    Pick = new RelayCommand(() => picked(this));
  }

  public string Key { get; }
  public string Label { get; }
  public int Count { get; }
  public double BarWidth { get; }
  public IRelayCommand Pick { get; }
}

public enum KindFilter
{
  All,
  Folder,
  File,
}

/// <summary>One choice of the sort list; its label follows the display language.</summary>
public sealed partial class SortOption(StackSort sort, string labelKey) : ObservableObject
{
  public StackSort Sort { get; } = sort;

  public string LabelKey { get; } = labelKey;

  [ObservableProperty]
  private string _label = AppText.Get(labelKey);
}

public sealed record TrayItem(string Title, string Detail);

/// <summary>Screen 3, 「工作台：挑下一疊」.</summary>
public sealed partial class HubViewModel : PageViewModel
{
  private const int PageSize = 20;
  private int _skip;
  private int _shown = PageSize;
  private string? _pair;

  [ObservableProperty]
  private string _summary = string.Empty;

  [ObservableProperty]
  private FeaturedStackViewModel? _featured;

  [ObservableProperty]
  private string _listNote = string.Empty;

  [ObservableProperty]
  private string? _pairNote;

  [ObservableProperty]
  private bool _hasMore;

  [ObservableProperty]
  private KindFilter _kind = KindFilter.All;

  [ObservableProperty]
  private SortOption _sort;

  [ObservableProperty]
  private string _pairsNote = string.Empty;

  [ObservableProperty]
  private string? _sinceTitle;

  [ObservableProperty]
  private string? _sinceNote;

  [ObservableProperty]
  private string _trayActionLabel = string.Empty;

  [ObservableProperty]
  private string _loadMoreLabel = string.Empty;

  public HubViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    _sort = SortOptions[0];
    Refresh();
  }

  public IReadOnlyList<SortOption> SortOptions { get; } =
  [
    new(StackSort.Suggested, "hub.sort.suggested"),
    new(StackSort.MostSpace, "hub.sort.mostSpace"),
    new(StackSort.RecentlyChanged, "hub.sort.recent"),
    new(StackSort.MostCopies, "hub.sort.mostCopies"),
  ];

  public ObservableCollection<HubGroupViewModel> Groups { get; } = [];
  public ObservableCollection<PairFilterViewModel> Pairs { get; } = [];
  public ObservableCollection<TrayItem> Tray { get; } = [];

  public bool HasTray => Tray.Count > 0;

  public bool IsAllKinds
  {
    get => Kind == KindFilter.All;
    set { if (value) Kind = KindFilter.All; }
  }

  public bool IsFolderKind
  {
    get => Kind == KindFilter.Folder;
    set { if (value) Kind = KindFilter.Folder; }
  }

  public bool IsFileKind
  {
    get => Kind == KindFilter.File;
    set { if (value) Kind = KindFilter.File; }
  }

  public bool HasFeatured => Featured is not null;

  public bool HasSince => SinceTitle is not null;

  public override void Refresh()
  {
    var stacks = Store.Stacks;
    var open = stacks.Where(s => s.Status == StackStatus.Open).Select(StackTriage.Summarize).ToList();
    var converged = Store.ConvergedCount;
    var keptAll = stacks.Count(s => s.Status == StackStatus.KeptAll);
    var pending = stacks.Count(s => s.Status == StackStatus.Pending);
    var scanning = Store.Sources.Where(s => s.State == SourceState.Scanning).Select(s => s.RootPath).ToList();
    var found = stacks.Count(s => s.Status is not (StackStatus.Dissolved or StackStatus.Converged)) + converged;
    (string, object?)[] progress =
    [
      ("settled", Fmt.Count(converged)), ("keptAll", Fmt.Count(keptAll)), ("pending", Fmt.Count(pending)), ("open", Fmt.Count(open.Count)),
    ];
    Summary = Fmt.Sentences(
      AppText.Plural("hub.summary.found", found, ("count", Fmt.Count(found))),
      AppText.Get(pending > 0 ? "hub.summary.progressPending" : "hub.summary.progress", progress),
      scanning.Count > 0 ? AppText.Get("hub.summary.scanning", ("sources", Fmt.List(scanning))) : null);

    foreach (var option in SortOptions)
    {
      option.Label = AppText.Get(option.LabelKey);
    }
    LoadMoreLabel = AppText.Get("hub.list.loadMore", ("count", PageSize));

    BuildFeatured(open);
    BuildPairs(open);
    BuildGroups(open);
    BuildTray();

    if (Store.SinceLastVisit is { } since && since.Notices.Any(n => n.NeedsAttention && n.HandledText is null))
    {
      var count = since.Notices.Count(n => n.NeedsAttention && n.HandledText is null);
      SinceTitle = AppText.Plural("hub.since.title", count, ("day", Fmt.Day(since.LastVisit, since.Now)), ("count", count));
      SinceNote = Store.IsDemo
        ? AppText.Get("hub.since.demoNote", ("day", Fmt.Day(since.Now, since.Now)))
        : AppText.Get("hub.since.note");
    }
    else
    {
      SinceTitle = null;
      SinceNote = null;
    }
    OnPropertyChanged(nameof(HasSince));
  }

  partial void OnKindChanged(KindFilter value)
  {
    OnPropertyChanged(nameof(IsAllKinds));
    OnPropertyChanged(nameof(IsFolderKind));
    OnPropertyChanged(nameof(IsFileKind));
    _shown = PageSize;
    Refresh();
  }

  partial void OnSortChanged(SortOption value)
  {
    _shown = PageSize;
    Refresh();
  }

  partial void OnFeaturedChanged(FeaturedStackViewModel? value) => OnPropertyChanged(nameof(HasFeatured));

  [RelayCommand]
  private void OpenFeatured()
  {
    if (Featured is not null)
    {
      Navigator.ShowStack(Featured.StackId);
    }
  }

  [RelayCommand]
  private void SkipFeatured()
  {
    _skip++;
    Refresh();
  }

  [RelayCommand]
  private void ClearPair()
  {
    _pair = null;
    _shown = PageSize;
    Refresh();
  }

  [RelayCommand]
  private void LoadMore()
  {
    _shown += PageSize;
    Refresh();
  }

  [RelayCommand]
  private void OpenPlan() => Navigator.ShowPlan();

  [RelayCommand]
  private void OpenSince() => Navigator.ShowSince();

  private void BuildFeatured(List<StackSummary> open)
  {
    var candidates = StackTriage.Order(open.Where(s => !s.IsBlocked), StackSort.Suggested).Take(4).ToList();
    if (candidates.Count == 0)
    {
      Featured = null;
      return;
    }
    var pick = candidates[_skip % candidates.Count];
    var stack = pick.Stack;
    Featured = new FeaturedStackViewModel(stack.Id, stack.Title, StackText.Kind(stack), StackText.Reason(pick), StackText.Explanation(pick),
      stack.Copies.Count, StackText.Places(stack).Select(p => new PlaceChip(p.Offline ? AppText.Get("hub.place.offline", ("name", p.Name)) : p.Name, p.Offline)).ToList());
  }

  private void BuildPairs(List<StackSummary> open)
  {
    var names = Store.Sources.ToDictionary(s => s.Id, s => s.Name, StringComparer.Ordinal);
    var order = Store.Sources.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
    var counts = new Dictionary<string, (string Label, int Count)>(StringComparer.Ordinal);
    foreach (var summary in open)
    {
      var ids = summary.Stack.SourceIds.OrderBy(id => order.GetValueOrDefault(id, int.MaxValue)).ToList();
      foreach (var key in PairKeys(ids))
      {
        var parts = key.Split('+');
        var label = parts.Length == 1
          ? AppText.Get("hub.pairs.within", ("name", names.GetValueOrDefault(parts[0], parts[0])))
          : AppText.Get("hub.pairs.between", ("first", names.GetValueOrDefault(parts[0], parts[0])), ("second", names.GetValueOrDefault(parts[1], parts[1])));
        counts[key] = (label, counts.GetValueOrDefault(key).Count + 1);
      }
    }
    var top = counts.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value.Label, StringComparer.Ordinal).Take(8).ToList();
    var max = top.Count == 0 ? 1 : top.Max(kv => kv.Value.Count);
    Pairs.Clear();
    foreach (var (key, value) in top)
    {
      Pairs.Add(new PairFilterViewModel(key, value.Label, value.Count, (double)value.Count / max, key == _pair, PickPair));
    }
    PairsNote = AppText.Get("hub.pairs.note", ("count", Fmt.Count(open.Count)));
  }

  private void BuildGroups(List<StackSummary> open)
  {
    var filtered = open.Where(MatchesPair).Where(MatchesKind).ToList();
    var ordered = StackTriage.Order(filtered, Sort.Sort);
    var shown = ordered.Take(_shown).ToList();
    HasMore = ordered.Count > shown.Count;

    Groups.Clear();
    foreach (var group in Enum.GetValues<StackGroup>())
    {
      var items = shown.Where(s => s.Group == group).Select(ToItem).ToList();
      if (items.Count == 0)
      {
        continue;
      }
      var ids = items.Where(i => i.IsReady).Select(i => i.StackId).ToList();
      Groups.Add(new HubGroupViewModel(group, items, group == StackGroup.ReadyToConverge, new RelayCommand(() =>
      {
        Store.DecideSuggested(ids);
        Navigator.ShowPlan();
      })));
    }

    ListNote = AppText.Plural("hub.list.note", open.Count, ("shown", Fmt.Count(shown.Count)), ("total", Fmt.Count(open.Count)));
    var pair = Pairs.FirstOrDefault(p => p.Key == _pair);
    PairNote = pair is null
      ? null
      : AppText.Plural("hub.list.pairNote", pair.Count, ("pair", pair.Label), ("total", Fmt.Count(pair.Count)), ("shown", Fmt.Count(shown.Count)));
  }

  private void BuildTray()
  {
    Tray.Clear();
    foreach (var entry in Store.Plan)
    {
      var merge = entry.Decision.Action == StackAction.Merge;
      var what = entry.Decision.FromSuggestion
        ? (merge ? "hub.tray.mergeSuggested" : "hub.tray.keeperOnlySuggested")
        : (merge ? "hub.tray.merge" : "hub.tray.keeperOnly");
      Tray.Add(new TrayItem(entry.Stack.Title, AppText.Get(what)));
    }
    TrayActionLabel = AppText.Plural("hub.tray.run", Store.Plan.Count, ("count", Fmt.Count(Store.Plan.Count)));
    OnPropertyChanged(nameof(HasTray));
  }

  private HubItemViewModel ToItem(StackSummary summary)
  {
    var stack = summary.Stack;
    var blocked = StackText.Blocked(stack);
    var size = summary.Group == StackGroup.StillComparing
      ? AppText.Get("hub.item.atLeast", ("size", Fmt.Size(summary.DuplicateBytes)))
      : Fmt.Size(summary.DuplicateBytes);
    return new HubItemViewModel(stack.Id, stack.Title, StackText.Kind(stack), StackText.Where(stack), StackText.Detail(summary), size,
      stack.Copies.Count, blocked, stack.Block == StackBlock.SourceOffline, new RelayCommand(() => Navigator.ShowStack(stack.Id)));
  }

  private bool MatchesPair(StackSummary summary)
  {
    if (_pair is null)
    {
      return true;
    }
    var ids = summary.Stack.SourceIds.ToList();
    var parts = _pair.Split('+');
    return parts.Length == 1 ? ids.Count == 1 && ids[0] == parts[0] : ids.Contains(parts[0]) && ids.Contains(parts[1]);
  }

  private bool MatchesKind(StackSummary summary) => Kind switch
  {
    KindFilter.Folder => summary.Stack.Kind == StackKind.Folder,
    KindFilter.File => summary.Stack.Kind == StackKind.File,
    _ => true,
  };

  private void PickPair(PairFilterViewModel pair)
  {
    _pair = _pair == pair.Key ? null : pair.Key;
    _shown = PageSize;
    Refresh();
  }

  private static IEnumerable<string> PairKeys(List<string> ids)
  {
    if (ids.Count == 1)
    {
      yield return ids[0];
      yield break;
    }
    for (var i = 0; i < ids.Count; i++)
    {
      for (var j = i + 1; j < ids.Count; j++)
      {
        yield return string.Create(CultureInfo.InvariantCulture, $"{ids[i]}+{ids[j]}");
      }
    }
  }
}
