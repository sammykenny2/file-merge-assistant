using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>How a cell of the comparison table is coloured; the text always says the same thing.</summary>
public enum CellTone
{
  Keeper,
  KeeperMissing,
  Missing,
  Same,
  Older,
  Highlight,
}

public enum ColumnTone
{
  Normal,
  Keeper,
  Offline,
}

public sealed record CellViewModel(string Main, string? Sub, CellTone Tone, ColumnTone Column, bool Highlighted);

public sealed record StackRowViewModel(
  string Path,
  string? Description,
  string? Warning,
  string? Hint,
  bool HintIsWarning,
  IReadOnlyList<CellViewModel> Cells);

public sealed record SimpleCellViewModel(string Text, ColumnTone Column);

public sealed partial class StackColumnViewModel : ObservableObject
{
  private readonly Action<bool> _setAlsoKeep;

  public StackColumnViewModel(StackReview review, StackCopy copy, StackCopy keeper, DateTimeOffset now,
    IRelayCommand setKeeper, IRelayCommand remove, Action<bool> setAlsoKeep)
  {
    _setAlsoKeep = setAlsoKeep;
    var role = review.RoleOf(copy);
    IsKeeper = role == CopyRole.Keeper;
    Tone = IsKeeper ? ColumnTone.Keeper : role == CopyRole.Offline ? ColumnTone.Offline : ColumnTone.Normal;
    Pill = role switch
    {
      CopyRole.Keeper => "正本",
      CopyRole.Recycle => "移入回收區",
      CopyRole.AlsoKeep => "也保留",
      CopyRole.Protected => "受保護，保留",
      CopyRole.Offline => "離線，先不動",
      _ => "不變動",
    };
    Source = copy.SourceName;
    (Prefix, Tail) = Fmt.SplitPath(copy.Path);
    IsProtected = copy.IsProtected;
    Metas = Meta(copy, keeper, now);
    KeeperNote = !IsKeeper
      ? null
      : copy.Id == review.Stack.SuggestedKeeperCopy.Id && copy.SuggestReason is not null
        ? $"建議當正本：{copy.SuggestReason}"
        : "你選的正本。比較結果已經以這份為準重新計算。";
    CanSetKeeper = !IsKeeper && StackReview.CanBeKeeper(copy);
    OfflineNoKeeper = !IsKeeper && !StackReview.CanBeKeeper(copy);
    ShowAlsoKeep = review.ChangesFiles && role is CopyRole.Recycle or CopyRole.AlsoKeep;
    _isAlsoKept = role == CopyRole.AlsoKeep;
    CanRemove = review.CanRemove(copy);
    SetKeeper = setKeeper;
    Remove = remove;
    SetKeeperName = $"把「{copy.SourceName}」這份設為正本";
    RemoveName = $"把「{copy.SourceName}」這份移出這疊";
  }

  public bool IsKeeper { get; }
  public ColumnTone Tone { get; }
  public string Pill { get; }
  public string Source { get; }
  public string Prefix { get; }
  public string Tail { get; }
  public bool IsProtected { get; }
  public IReadOnlyList<string> Metas { get; }
  public string? KeeperNote { get; }
  public bool CanSetKeeper { get; }
  public bool OfflineNoKeeper { get; }
  public bool ShowAlsoKeep { get; }
  public bool CanRemove { get; }
  public IRelayCommand SetKeeper { get; }
  public IRelayCommand Remove { get; }
  public string SetKeeperName { get; }
  public string RemoveName { get; }

  [ObservableProperty]
  private bool _isAlsoKept;

  partial void OnIsAlsoKeptChanged(bool value) => _setAlsoKeep(value);

  private static List<string> Meta(StackCopy copy, StackCopy keeper, DateTimeOffset now)
  {
    var lines = new List<string>();
    if (copy.IsOffline && copy.IndexedAt is { } indexed)
    {
      lines.Add($"最後看到 {Fmt.Day(indexed, now)}，資料是當時的索引");
    }
    else if (copy.LastModified is { } modified)
    {
      lines.Add($"最後修改 {Fmt.Day(modified, now)}");
    }
    lines.AddRange(copy.Notes);
    if (copy.Git is { } git)
    {
      var uncommitted = git.UncommittedChanges > 0 ? $"，{git.UncommittedChanges} 個未提交的變更" : string.Empty;
      if (copy.Id != keeper.Id && keeper.Git is { } keeperGit && git.Head is not null && git.Head == keeperGit.Head)
      {
        lines.Add($"git：和正本同一個 commit{uncommitted}");
      }
      else if (copy.Id != keeper.Id && git.BehindKeeper is > 0)
      {
        var ahead = git.AheadOfKeeper is > 0 ? $"；有 {git.AheadOfKeeper} 個正本沒有的 commit" : "；沒有正本以外的 commit";
        lines.Add($"git：{git.Branch}，落後正本 {git.BehindKeeper} 個 commit{ahead}{uncommitted}");
      }
      else
      {
        lines.Add($"git：{git.Branch}{uncommitted}");
      }
    }
    return lines;
  }
}

public sealed partial class ActionOptionViewModel : ObservableObject
{
  private readonly Action<StackAction> _pick;

  [ObservableProperty]
  private string _description = string.Empty;

  [ObservableProperty]
  private bool _isSuggested;

  [ObservableProperty]
  private bool _isChosen;

  public ActionOptionViewModel(StackAction action, string label, Action<StackAction> pick)
  {
    Action = action;
    Label = label;
    _pick = pick;
  }

  public StackAction Action { get; }
  public string Label { get; }

  /// <summary>Two-way for the radio button; picking an option changes the decision.</summary>
  public bool IsSelected
  {
    get => IsChosen;
    set
    {
      if (value && !IsChosen)
      {
        _pick(Action);
      }
    }
  }

  partial void OnIsChosenChanged(bool value) => OnPropertyChanged(nameof(IsSelected));
}

public sealed partial class ConflictOptionViewModel : ObservableObject
{
  private readonly Action<ConflictChoice> _pick;

  [ObservableProperty]
  private bool _isChosen;

  public ConflictOptionViewModel(ConflictChoice choice, string label, bool isEnabled, string? why, Action<ConflictChoice> pick)
  {
    Choice = choice;
    Label = label;
    IsEnabled = isEnabled;
    Why = why;
    _pick = pick;
  }

  public ConflictChoice Choice { get; }
  public string Label { get; }
  public bool IsEnabled { get; }
  public string? Why { get; }

  public bool IsSelected
  {
    get => IsChosen && IsEnabled;
    set
    {
      if (value && !IsChosen && IsEnabled)
      {
        _pick(Choice);
      }
    }
  }

  partial void OnIsChosenChanged(bool value) => OnPropertyChanged(nameof(IsSelected));
}

public sealed class ConflictViewModel
{
  public ConflictViewModel(string path, string compare, IReadOnlyList<ConflictOptionViewModel> options)
  {
    Path = path;
    Compare = compare;
    Options = options;
    GroupName = "conflict:" + path;
  }

  public string Path { get; }
  public string Compare { get; }
  public string GroupName { get; }
  public IReadOnlyList<ConflictOptionViewModel> Options { get; }
}

public enum OutcomeIcon
{
  Copy,
  Recycle,
  Keep,
  Offline,
  Warn,
  None,
}

public sealed record OutcomeViewModel(string Text, OutcomeIcon Icon);

public sealed record SessionLogItem(string Title, string What);

/// <summary>Screen 4, 「一疊：比對與決定」. Deciding only records the decision; files change when the plan runs.</summary>
public sealed partial class StackViewModel : PageViewModel
{
  private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
  private Stack _stack = null!;
  private StackDecision _decision = null!;
  private StackReview _review = null!;

  [ObservableProperty]
  private string _title = string.Empty;

  [ObservableProperty]
  private string _group = string.Empty;

  [ObservableProperty]
  private string _kindLine = string.Empty;

  [ObservableProperty]
  private string _why = string.Empty;

  [ObservableProperty]
  private string _evidence = string.Empty;

  [ObservableProperty]
  private string _position = string.Empty;

  [ObservableProperty]
  private string? _toast;

  [ObservableProperty]
  private string? _removedText;

  [ObservableProperty]
  private string _sameLabel = string.Empty;

  [ObservableProperty]
  private string _sameSub = string.Empty;

  [ObservableProperty]
  private bool _hasSame;

  [ObservableProperty]
  private bool _hasRegenerable;

  [ObservableProperty]
  private string _regenerableLabel = string.Empty;

  [ObservableProperty]
  private string _regenerableSub = string.Empty;

  [ObservableProperty]
  private string _diffNote = string.Empty;

  [ObservableProperty]
  private bool _noDiff;

  [ObservableProperty]
  private string? _note;

  [ObservableProperty]
  private bool _hasConflicts;

  [ObservableProperty]
  private string? _blockReason;

  [ObservableProperty]
  private string _primaryLabel = string.Empty;

  [ObservableProperty]
  private string _primaryNote = string.Empty;

  [ObservableProperty]
  private double _tableWidth;

  [ObservableProperty]
  private bool _isDone;

  [ObservableProperty]
  private string _doneText = string.Empty;

  [ObservableProperty]
  private string _pendingLabel = string.Empty;

  [ObservableProperty]
  private bool _hasPending;

  [ObservableProperty]
  private string? _blockedStackText;

  public StackViewModel(IWorkbenchStore store, INavigator navigator, string stackId)
    : base(store, navigator)
  {
    Options =
    [
      new ActionOptionViewModel(StackAction.Merge, "合併到正本", PickAction),
      new ActionOptionViewModel(StackAction.KeepKeeperOnly, "只保留正本", PickAction),
      new ActionOptionViewModel(StackAction.KeepAll, "都保留", PickAction),
      new ActionOptionViewModel(StackAction.Later, "稍後再看", PickAction),
    ];
    Load(stackId);
  }

  public const double RowHeaderWidth = 230;
  public const double CellWidth = 180;

  public IReadOnlyList<ActionOptionViewModel> Options { get; }
  public ObservableCollection<StackColumnViewModel> Columns { get; } = [];
  public ObservableCollection<StackRowViewModel> Rows { get; } = [];
  public ObservableCollection<SimpleCellViewModel> SameCells { get; } = [];
  public ObservableCollection<SimpleCellViewModel> RegenerableCells { get; } = [];
  public ObservableCollection<ConflictViewModel> Conflicts { get; } = [];
  public ObservableCollection<OutcomeViewModel> Outcome { get; } = [];
  public ObservableCollection<string> Space { get; } = [];
  public ObservableCollection<SessionLogItem> Log { get; } = [];

  public bool IsBlocked => BlockReason is not null;

  public bool CanSubmit => !IsBlocked;

  public override void Refresh()
  {
    if (IsDone)
    {
      UpdateDone();
      return;
    }
    var current = Store.Stacks.FirstOrDefault(s => s.Id == _stack.Id);
    if (current is null)
    {
      return;
    }
    _stack = current;
    Rebuild();
  }

  partial void OnBlockReasonChanged(string? value)
  {
    OnPropertyChanged(nameof(IsBlocked));
    OnPropertyChanged(nameof(CanSubmit));
  }

  [RelayCommand]
  private void Submit()
  {
    if (_review.Block != ReviewBlock.None)
    {
      return;
    }
    Store.Decide(_decision);
    var changesFiles = _review.ChangesFiles;
    Toast = changesFiles
      ? $"已加入待執行：{_stack.Title}"
      : _decision.Action == StackAction.KeepAll ? $"已標記為都保留：{_stack.Title}" : $"已放回待整理：{_stack.Title}";
    var what = _decision.Action switch
    {
      StackAction.Merge => "合併到正本，在待執行",
      StackAction.KeepKeeperOnly => "只保留正本，在待執行",
      StackAction.KeepAll => "都保留",
      _ => "稍後再看",
    };
    Log.Add(new SessionLogItem(_stack.Title, what));
    _visited.Add(_stack.Id);

    var next = StackTriage.Order(
        Store.Stacks.Where(s => s.Status == StackStatus.Open && s.Block == StackBlock.None && !_visited.Contains(s.Id)).Select(StackTriage.Summarize),
        StackSort.Suggested)
      ;
    var nextStack = next.Count > 0 ? next[0] : null;
    if (nextStack is null || Log.Count >= 10)
    {
      IsDone = true;
      UpdateDone();
      return;
    }
    Load(nextStack.Stack.Id, keepToast: true);
  }

  [RelayCommand]
  private void RestoreRemoved()
  {
    _decision = _decision.WithoutRemoved();
    Rebuild();
  }

  [RelayCommand]
  private void GoToHub() => Navigator.ShowHub();

  [RelayCommand]
  private void GoToPlan() => Navigator.ShowPlan();

  private void Load(string stackId, bool keepToast = false)
  {
    _stack = Store.Stacks.First(s => s.Id == stackId);
    _decision = Store.DecisionFor(stackId) ?? StackDecision.Suggested(_stack);
    if (!keepToast)
    {
      Toast = null;
    }
    Conflicts.Clear();
    Rebuild();
  }

  private void PickAction(StackAction action)
  {
    _decision = _decision.WithAction(action);
    Rebuild();
  }

  private void Rebuild()
  {
    var now = Store.Now;
    _review = new StackReview(_stack, _decision);
    var review = _review;
    var summary = StackTriage.Summarize(_stack);
    var active = review.ActiveCopies;

    Title = _stack.Title;
    Group = StackText.GroupTitle(summary.Group);
    KindLine = $"{(_stack.Kind == StackKind.Folder ? "資料夾複本" : "單檔複本")}，{active.Count} 份";
    Why = WhyText(_stack, active.Count);
    Evidence = _stack.Evidence;
    var open = Store.Stacks.Count(s => s.Status == StackStatus.Open && s.Id != _stack.Id);
    Position = $"還有 {Fmt.Count(open)} 疊待整理";
    BlockedStackText = StackText.Blocked(_stack);
    RemovedText = review.RemovedCopies.Count == 0
      ? null
      : $"已移出這疊：{string.Join("、", review.RemovedCopies.Select(c => $"{c.SourceName} {c.Path}"))}。之後不會再把它和這疊放在一起。";

    Columns.Clear();
    foreach (var copy in active)
    {
      var id = copy.Id;
      Columns.Add(new StackColumnViewModel(review, copy, review.Keeper, now,
        new RelayCommand(() =>
        {
          _decision = _decision.WithKeeper(id);
          Rebuild();
        }),
        new RelayCommand(() =>
        {
          _decision = _decision.WithRemoved(id);
          Rebuild();
        }),
        keep =>
        {
          _decision = _decision.WithAlsoKeep(id, keep);
          Rebuild();
        }));
    }
    TableWidth = RowHeaderWidth + CellWidth * active.Count;

    ColumnTone ToneOf(StackCopy copy) => review.RoleOf(copy) switch
    {
      CopyRole.Keeper => ColumnTone.Keeper,
      CopyRole.Offline => ColumnTone.Offline,
      _ => ColumnTone.Normal,
    };

    HasSame = _stack.SameFileCount > 0 || _stack.SameLabel is not null;
    SameLabel = _stack.SameLabel ?? $"{Fmt.Count(_stack.SameFileCount)} 個檔";
    SameSub = $"{active.Count} 份內容都相同";
    SameCells.Clear();
    foreach (var copy in active)
    {
      SameCells.Add(new SimpleCellViewModel("相同", ToneOf(copy)));
    }

    Rows.Clear();
    foreach (var row in review.Rows)
    {
      var (hint, hintWarn) = Hint(review, row);
      var cells = active.Select(copy => Cell(review, row, copy, ToneOf(copy), now)).ToList();
      Rows.Add(new StackRowViewModel(row.Row.RelativePath, row.Row.Description, row.Row.Warning, hint, hintWarn, cells));
    }
    NoDiff = Rows.Count == 0;
    DiffNote = NoDiff ? "沒有不同的地方" : "只列出有差異或需要說明的檔";

    HasRegenerable = _stack.Regenerable is not null;
    RegenerableLabel = _stack.Regenerable?.Names ?? string.Empty;
    RegenerableSub = "可重建，不逐檔比對；跟著所在的那份一起處理";
    RegenerableCells.Clear();
    if (_stack.Regenerable is { } regenerable)
    {
      foreach (var copy in active)
      {
        var bytes = regenerable.BytesPerCopy[_stack.IndexOf(copy.Id)];
        RegenerableCells.Add(new SimpleCellViewModel(bytes > 0 ? Fmt.Size(bytes) : "沒有", ToneOf(copy)));
      }
    }

    var notes = new List<string>();
    if (_stack.Note is not null)
    {
      notes.Add(_stack.Note);
    }
    notes.AddRange(_stack.RelatedOutside.Select(r =>
      $"同名但內容不同，不在這疊：{r.Path}（{Fmt.Day(r.Modified, now)} 修改）。檔名相近不代表內容相同，所以不會一起處理。"));
    Note = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);

    UpdateOptions(review);
    UpdateConflicts(review);
    UpdateOutcome(review);

    BlockReason = review.Block switch
    {
      ReviewBlock.UnresolvedConflicts => $"還有 {review.UnresolvedCount} 個檔要選版本，才能加入待執行。",
      ReviewBlock.NothingToRecycle => BlockedStackText is not null
        ? $"{BlockedStackText}。現在可以選「都保留」或「稍後再看」。"
        : "沒有要移入回收區的複本。如果每份都要留，請選「都保留」。",
      ReviewBlock.StillScanning => $"{BlockedStackText}。現在可以選「稍後再看」。",
      _ => null,
    };
    PrimaryLabel = _decision.Action switch
    {
      StackAction.KeepAll => "標記為都保留",
      StackAction.Later => "放回待整理",
      _ => "加入待執行",
    };
    PrimaryNote = review.ChangesFiles
      ? "加入待執行不會改動任何檔案；執行前會再檢查一次每個檔的現況。"
      : "這個選擇不會改動任何檔案。";
  }

  private void UpdateOptions(StackReview review)
  {
    var unique = review.OnlyElsewhereFileCount;
    var conflicts = review.ConflictCount;
    var loss = review.LossFileCount;
    foreach (var option in Options)
    {
      option.IsChosen = option.Action == _decision.Action;
      option.IsSuggested = option.Action == _stack.SuggestedAction;
      option.Description = option.Action switch
      {
        StackAction.Merge => unique > 0 || conflicts > 0
          ? $"把只在其他份的 {Fmt.Count(unique)} 個檔複製進正本{(conflicts > 0 ? $"，{conflicts} 個檔要選版本" : string.Empty)}，再把其他份移入回收區。"
          : "其他份沒有正本缺少或較新的內容，效果和「只保留正本」相同。",
        StackAction.KeepKeeperOnly => loss > 0
          ? $"其他份移入回收區。其中 {Fmt.Count(loss)} 個檔正本沒有或比較新，也會一起移入。"
          : "其他份的內容正本都有，直接移入回收區。",
        StackAction.KeepAll => "它們是刻意分開的版本。不改動檔案；之後其中一份有變動才會再提醒。",
        _ => "放回待整理，不做任何改動。",
      };
    }
  }

  private void UpdateConflicts(StackReview review)
  {
    var visible = _decision.Action == StackAction.Merge ? review.Conflicts : [];
    HasConflicts = visible.Count > 0;
    var same = Conflicts.Count == visible.Count
      && Conflicts.Zip(visible).All(p => p.First.Path == p.Second.Row.RelativePath && p.First.Options[1].IsEnabled == review.IsChoiceAllowed(ConflictChoice.Theirs));
    if (same)
    {
      foreach (var (vm, row) in Conflicts.Zip(visible))
      {
        foreach (var option in vm.Options)
        {
          option.IsChosen = option.Choice == row.Choice;
        }
      }
      return;
    }

    Conflicts.Clear();
    var now = Store.Now;
    foreach (var row in visible)
    {
      var path = row.Row.RelativePath;
      var source = row.Source!;
      var keeperDay = Fmt.Day(row.KeeperVersion!.Modified, now);
      var theirDay = Fmt.Day(row.SourceVersion!.Modified, now);
      void Pick(ConflictChoice choice)
      {
        _decision = _decision.WithChoice(path, choice);
        Rebuild();
      }
      var theirsAllowed = review.IsChoiceAllowed(ConflictChoice.Theirs);
      var options = new List<ConflictOptionViewModel>
      {
        new(ConflictChoice.Keeper, $"用正本的版本（{keeperDay}）", true, null, Pick),
        new(ConflictChoice.Theirs, $"用「{source.SourceName}」的版本（{theirDay}）", theirsAllowed, theirsAllowed ? null : "正本受保護，不能覆寫", Pick),
        new(ConflictChoice.Both, $"兩份都留：「{source.SourceName}」那版另存為 {System.IO.Path.GetFileName(StackReview.SaveAsPath(path, source.Tag))}", true, null, Pick),
      };
      foreach (var option in options)
      {
        option.IsChosen = option.Choice == row.Choice;
      }
      Conflicts.Add(new ConflictViewModel(path, $"正本是 {keeperDay} 的版本；「{source.SourceName}」那份是 {theirDay}，比較新。", options));
    }
  }

  private void UpdateOutcome(StackReview review)
  {
    Outcome.Clear();
    foreach (var item in review.Outcome)
    {
      Outcome.Add(item.Kind switch
      {
        OutcomeKind.KeepAllNote => new OutcomeViewModel("不改動任何檔案。這疊會標記為「都保留」，之後其中一份有變動才會再提醒。", OutcomeIcon.Keep),
        OutcomeKind.LaterNote => new OutcomeViewModel("不改動任何檔案。這疊會放回待整理。", OutcomeIcon.None),
        OutcomeKind.CopyToKeeper => new OutcomeViewModel(
          $"複製 {Fmt.Count(item.FileCount)} 個檔到正本：{string.Join("、", item.Paths)}{(item.KeeperIsGit ? "。在 git 裡會顯示為未追蹤的檔案。" : string.Empty)}", OutcomeIcon.Copy),
        OutcomeKind.ConflictUnresolved => new OutcomeViewModel($"{item.Paths[0]}：還沒選版本", OutcomeIcon.Warn),
        OutcomeKind.SaveAs => new OutcomeViewModel($"另存 {item.NewPath}", OutcomeIcon.Copy),
        OutcomeKind.ReplaceKeeperVersion => new OutcomeViewModel(
          $"用「{item.Copy!.SourceName}」的版本取代正本的 {item.Paths[0]}；正本原本的版本移入回收區", OutcomeIcon.Warn),
        OutcomeKind.KeepKeeperVersion => new OutcomeViewModel($"{item.Paths[0]}：保留正本的版本", OutcomeIcon.Keep),
        OutcomeKind.RecyclesUniqueFiles => new OutcomeViewModel(
          $"{Fmt.Count(item.FileCount)} 個正本沒有或比較新的檔會一起移入回收區：{string.Join("、", item.Paths)}", OutcomeIcon.Warn),
        OutcomeKind.Recycle => new OutcomeViewModel($"移入回收區：{item.Copy!.Path}（{Fmt.Size(item.Bytes)}）", OutcomeIcon.Recycle),
        OutcomeKind.KeepCopy => new OutcomeViewModel(
          $"保留：{item.Copy!.Path}{(item.Copy.IsProtected ? "（受保護）" : "（你選擇也保留）")}", OutcomeIcon.Keep),
        _ => new OutcomeViewModel($"先不動：{item.Copy!.Path}（離線，接上後會再提醒）", OutcomeIcon.Offline),
      });
    }

    Space.Clear();
    Space.Add($"這疊的其他複本共佔 {Fmt.Size(review.DuplicateBytes)}。");
    Space.Add(review.ChangesFiles
      ? $"這次會移入回收區 {Fmt.Size(review.RecycleBytes)}{(review.RecycleRegenerableBytes > 0 ? $"，其中 {Fmt.Size(review.RecycleRegenerableBytes)} 是可重建的資料夾" : string.Empty)}。"
      : "這次不會移入任何檔案。");
    Space.Add("移入回收區不會立刻釋放空間；清空回收區後才會釋放。");
  }

  private void UpdateDone()
  {
    var pending = Store.Plan.Count;
    HasPending = pending > 0;
    PendingLabel = $"檢查並執行 {Fmt.Count(pending)} 疊";
    DoneText = pending > 0
      ? $"已有 {Fmt.Count(pending)} 疊在待執行，還沒有改動任何檔案。可以現在檢查並執行，或回工作台繼續整理下一疊。"
      : "這一段沒有加入待執行的疊，也沒有改動任何檔案。";
  }

  private static string WhyText(Stack stack, int copies)
  {
    if (stack.Kind == StackKind.File)
    {
      return $"{copies} 個檔內容完全相同，散在{StackText.Where(stack)}。";
    }
    if (stack.Rows.Count == 0)
    {
      return stack.SameFileCount > 0 ? $"{Fmt.Count(stack.SameFileCount)} 個檔在每一份裡內容都相同。" : $"{copies} 份的內容完全相同。";
    }
    return stack.SameFileCount > 0
      ? $"{Fmt.Count(stack.SameFileCount)} 個檔在每一份裡內容都相同，資料夾結構一致。差異只在下面這幾個檔。"
      : "下面逐份列出各份之間的差異。";
  }

  private static (string? Text, bool Warn) Hint(StackReview review, RowReview row)
  {
    if (!review.ChangesFiles)
    {
      return (null, false);
    }
    switch (row.Kind)
    {
      case RowKind.OnlyElsewhere:
        return review.Action == StackAction.Merge
          ? ($"會從「{row.Source!.SourceName}」複製到正本", false)
          : ("正本沒有，會隨複本移入回收區", true);
      case RowKind.Conflict:
        if (review.Action == StackAction.KeepKeeperOnly)
        {
          return ($"「{row.Source!.SourceName}」的較新版會隨複本移入回收區", true);
        }
        if (review.IsUnresolved(row))
        {
          return ("需要你決定用哪一版", true);
        }
        return row.Choice switch
        {
          ConflictChoice.Keeper => ("用正本的版本；較新的那版隨複本回收", false),
          ConflictChoice.Both => ($"兩份都留：另存為 {System.IO.Path.GetFileName(StackReview.SaveAsPath(row.Row.RelativePath, row.Source!.Tag))}", false),
          _ => ($"用「{row.Source!.SourceName}」的版本取代正本的", false),
        };
      case RowKind.Older:
        return ("其他份是舊版本，隨複本回收", false);
      case RowKind.KeptElsewhere:
        return ("保留在其他份裡", false);
      case RowKind.OnlyOffline:
        return ("只在離線的那份，這次不處理", false);
      default:
        return (null, false);
    }
  }

  private CellViewModel Cell(StackReview review, RowReview row, StackCopy copy, ColumnTone column, DateTimeOffset now)
  {
    var index = _stack.IndexOf(copy.Id);
    var version = row.Row.Cells[index];
    var label = version is null ? null : row.Row.Count > 1 ? "有" : Fmt.Day(version.Modified, now);
    var offline = column == ColumnTone.Offline;
    return review.CellOf(row, copy) switch
    {
      CellKind.KeeperHas => new CellViewModel(label!, null, CellTone.Keeper, column, false),
      CellKind.KeeperMissing => new CellViewModel("沒有", null, CellTone.KeeperMissing, column, false),
      CellKind.Missing => new CellViewModel("沒有", null, CellTone.Missing, column, false),
      CellKind.KeeperLacks => new CellViewModel("正本沒有", label, CellTone.Highlight, column, !offline),
      CellKind.SameAsKeeper => new CellViewModel("同正本", null, CellTone.Same, column, false),
      CellKind.Newer => new CellViewModel("較新", label, CellTone.Highlight, column, !offline),
      CellKind.Older => new CellViewModel("較舊", label, CellTone.Older, column, false),
      _ => new CellViewModel("內容不同", null, CellTone.Highlight, column, false),
    };
  }
}
