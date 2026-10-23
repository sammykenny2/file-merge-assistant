using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
  public string OpenLabel => IsReady ? "整理" : "先看看";
  public string OpenName => $"{OpenLabel} {Title}";
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
    BulkLabel = $"依建議加入 {ready} 疊…";
    BulkGo = $"加入這 {ready} 疊";
    BulkText = $"會把這一區目前列出、可以整理的 {ready} 疊，用建議的正本與做法加入待執行"
      + (blocked > 0 ? $"；離線而不能整理的 {blocked} 疊不包含在內" : string.Empty)
      + "。還沒載入的疊也不包含。它們會標記為「依建議加入」，執行前可以逐項檢查或移除。";
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

public sealed record SortOption(StackSort Sort, string Label);

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

  public HubViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    _sort = SortOptions[0];
    Refresh();
  }

  public IReadOnlyList<SortOption> SortOptions { get; } =
  [
    new(StackSort.Suggested, "建議順序：省空間又少決定的先"),
    new(StackSort.MostSpace, "可省空間最多"),
    new(StackSort.RecentlyChanged, "最近有變動"),
    new(StackSort.MostCopies, "複本份數最多"),
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
    Summary = $"找到 {Fmt.Count(stacks.Count(s => s.Status is not (StackStatus.Dissolved or StackStatus.Converged)) + converged)} 疊。已收斂 {Fmt.Count(converged)} 疊、都保留 {Fmt.Count(keptAll)} 疊"
      + (pending > 0 ? $"、待執行 {Fmt.Count(pending)} 疊" : string.Empty)
      + $"，還有 {Fmt.Count(open.Count)} 疊待整理。"
      + (scanning.Count > 0 ? $"{string.Join("、", scanning)} 還在掃描，之後可能再多幾疊。" : string.Empty);

    BuildFeatured(open);
    BuildPairs(open);
    BuildGroups(open);
    BuildTray();

    if (Store.SinceLastVisit is { } since && since.Notices.Any(n => n.NeedsAttention && n.HandledText is null))
    {
      var count = since.Notices.Count(n => n.NeedsAttention && n.HandledText is null);
      SinceTitle = $"自 {Fmt.Day(since.LastVisit, since.Now)} 以來，有 {count} 件事需要你看一下";
      SinceNote = Store.IsDemo
        ? $"示範：模擬 {Fmt.Day(since.Now, since.Now)} 回來時，程式會先告訴你這段時間的變化。"
        : "檔案在這段時間有增有減。先看看變了什麼，再繼續下一疊。";
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
      stack.Copies.Count, StackText.Places(stack).Select(p => new PlaceChip(p.Name + (p.Offline ? "（離線）" : string.Empty), p.Offline)).ToList());
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
          ? $"{names.GetValueOrDefault(parts[0], parts[0])}內部"
          : $"{names.GetValueOrDefault(parts[0], parts[0])} 與 {names.GetValueOrDefault(parts[1], parts[1])}";
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
    PairsNote = $"一疊可能橫跨好幾個位置，所以加總會超過 {Fmt.Count(open.Count)}。";
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

    ListNote = $"目前列出 {Fmt.Count(shown.Count)} 疊，共 {Fmt.Count(open.Count)} 疊";
    var pair = Pairs.FirstOrDefault(p => p.Key == _pair);
    PairNote = pair is null ? null : $"只看「{pair.Label}」：共 {Fmt.Count(pair.Count)} 疊，目前列出 {Fmt.Count(shown.Count)} 疊";
  }

  private void BuildTray()
  {
    Tray.Clear();
    foreach (var entry in Store.Plan)
    {
      var what = entry.Decision.Action == StackAction.Merge ? "合併到正本" : "只保留正本";
      Tray.Add(new TrayItem(entry.Stack.Title, entry.Decision.FromSuggestion ? $"{what}（依建議加入）" : what));
    }
    TrayActionLabel = $"檢查並執行 {Fmt.Count(Store.Plan.Count)} 疊";
    OnPropertyChanged(nameof(HasTray));
  }

  private HubItemViewModel ToItem(StackSummary summary)
  {
    var stack = summary.Stack;
    var blocked = StackText.Blocked(stack);
    var size = summary.Group == StackGroup.StillComparing ? $"至少 {Fmt.Size(summary.DuplicateBytes)}" : Fmt.Size(summary.DuplicateBytes);
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
