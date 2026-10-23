using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Localization;
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
    Role = review.RoleOf(copy);
    IsKeeper = Role == CopyRole.Keeper;
    Tone = IsKeeper ? ColumnTone.Keeper : Role == CopyRole.Offline ? ColumnTone.Offline : ColumnTone.Normal;
    Pill = Role switch
    {
      CopyRole.Keeper => AppText.Get("stack.pill.keeper"),
      CopyRole.Recycle => AppText.Get("stack.pill.recycle"),
      CopyRole.AlsoKeep => AppText.Get("stack.pill.alsoKeep"),
      CopyRole.Protected => AppText.Get("stack.pill.protected"),
      CopyRole.Offline => AppText.Get("stack.pill.offline"),
      _ => AppText.Get("stack.pill.unchanged"),
    };
    Source = copy.SourceName;
    (Prefix, Tail) = Fmt.SplitPath(copy.Path);
    IsProtected = copy.IsProtected;
    Metas = Meta(copy, keeper, now);
    KeeperNote = !IsKeeper
      ? null
      : copy.Id == review.Stack.SuggestedKeeperCopy.Id && copy.SuggestReason is not null
        ? AppText.Get("stack.column.suggestedKeeper", ("reason", copy.SuggestReason))
        : AppText.Get("stack.column.chosenKeeper");
    CanSetKeeper = !IsKeeper && StackReview.CanBeKeeper(copy);
    OfflineNoKeeper = !IsKeeper && !StackReview.CanBeKeeper(copy);
    ShowAlsoKeep = review.ChangesFiles && Role is CopyRole.Recycle or CopyRole.AlsoKeep;
    _isAlsoKept = Role == CopyRole.AlsoKeep;
    CanRemove = review.CanRemove(copy);
    SetKeeper = setKeeper;
    Remove = remove;
    SetKeeperName = AppText.Get("stack.column.setKeeperName", ("source", copy.SourceName));
    RemoveName = AppText.Get("stack.column.removeName", ("source", copy.SourceName));
  }

  /// <summary>What happens to this copy; the view colours the pill by it, never by the pill's text.</summary>
  public CopyRole Role { get; }
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
      lines.Add(AppText.Get("stack.column.lastSeen", ("day", Fmt.Day(indexed, now))));
    }
    else if (copy.LastModified is { } modified)
    {
      lines.Add(AppText.Get("stack.column.modified", ("day", Fmt.Day(modified, now))));
    }
    lines.AddRange(copy.Notes);
    if (copy.Git is { } git)
    {
      lines.Add(GitLine(copy, keeper, git));
    }
    return lines;
  }

  private static string GitLine(StackCopy copy, StackCopy keeper, GitStatus git)
  {
    var dirty = git.UncommittedChanges;
    if (copy.Id != keeper.Id && keeper.Git is { } keeperGit && git.Head is not null && git.Head == keeperGit.Head)
    {
      return dirty > 0
        ? AppText.Plural("stack.git.sameDirty", dirty, ("count", dirty))
        : AppText.Get("stack.git.same");
    }
    if (copy.Id != keeper.Id && git.BehindKeeper is { } behind && behind > 0)
    {
      if (git.AheadOfKeeper is { } ahead && ahead > 0)
      {
        return dirty > 0
          ? AppText.Plural("stack.git.behindAheadDirty", dirty, ("branch", git.Branch), ("behind", behind), ("ahead", ahead), ("count", dirty))
          : AppText.Get("stack.git.behindAhead", ("branch", git.Branch), ("behind", behind), ("ahead", ahead));
      }
      return dirty > 0
        ? AppText.Plural("stack.git.behindDirty", dirty, ("branch", git.Branch), ("behind", behind), ("count", dirty))
        : AppText.Get("stack.git.behind", ("branch", git.Branch), ("behind", behind));
    }
    return dirty > 0
      ? AppText.Plural("stack.git.branchDirty", dirty, ("branch", git.Branch), ("count", dirty))
      : AppText.Get("stack.git.branch", ("branch", git.Branch));
  }
}

public sealed partial class ActionOptionViewModel : ObservableObject
{
  private readonly Action<StackAction> _pick;

  [ObservableProperty]
  private string _label;

  [ObservableProperty]
  private string _description = string.Empty;

  [ObservableProperty]
  private bool _isSuggested;

  [ObservableProperty]
  private bool _isChosen;

  public ActionOptionViewModel(StackAction action, string label, Action<StackAction> pick)
  {
    Action = action;
    _label = label;
    _pick = pick;
  }

  public StackAction Action { get; }

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

  /// <summary>Set by the stack screen each time it redraws, so it follows the display language.</summary>
  [ObservableProperty]
  private string _label = string.Empty;

  [ObservableProperty]
  private string? _why;

  public ConflictOptionViewModel(ConflictChoice choice, bool isEnabled, Action<ConflictChoice> pick)
  {
    Choice = choice;
    IsEnabled = isEnabled;
    _pick = pick;
  }

  public ConflictChoice Choice { get; }
  public bool IsEnabled { get; }

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

public sealed partial class ConflictViewModel : ObservableObject
{
  /// <summary>Set by the stack screen each time it redraws, so it follows the display language.</summary>
  [ObservableProperty]
  private string _compare = string.Empty;

  public ConflictViewModel(string path, IReadOnlyList<ConflictOptionViewModel> options)
  {
    Path = path;
    Options = options;
    GroupName = "conflict:" + path;
  }

  public string Path { get; }
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

  /// <summary>The decisions of this session, kept so the log can be redrawn in another display language.</summary>
  private readonly List<(string Title, StackAction Action)> _session = [];

  private Stack _stack = null!;
  private StackDecision _decision = null!;
  private bool _keeperUnavailable;
  private StackReview _review = null!;

  /// <summary>The catalog key and stack title of the toast, so it can be redrawn in another display language.</summary>
  private (string Key, string StackId, string Title)? _toastSource;

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

  [ObservableProperty]
  private string? _alert;

  [ObservableProperty]
  private string? _hiddenCopiesText;

  [ObservableProperty]
  private string? _reopenedText;

  public StackViewModel(IWorkbenchStore store, INavigator navigator, string stackId)
    : base(store, navigator)
  {
    Options =
    [
      new ActionOptionViewModel(StackAction.Merge, ActionLabel(StackAction.Merge), PickAction),
      new ActionOptionViewModel(StackAction.KeepKeeperOnly, ActionLabel(StackAction.KeepKeeperOnly), PickAction),
      new ActionOptionViewModel(StackAction.KeepAll, ActionLabel(StackAction.KeepAll), PickAction),
      new ActionOptionViewModel(StackAction.Later, ActionLabel(StackAction.Later), PickAction),
    ];
    Load(stackId);
  }

  /// <summary>The comparison lists at most this many differing paths; the decision still covers all of them.</summary>
  public const int MaxRows = 200;

  /// <summary>The comparison shows at most this many copies as columns.</summary>
  public const int MaxColumns = 12;

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

  /// <summary>A stack's title as the store has it now, in the current language; the remembered one if it is gone.</summary>
  private string TitleOf(string stackId, string remembered) =>
    Store.Stacks.FirstOrDefault(s => s.Id == stackId)?.Title ?? remembered;

  /// <summary>
  /// Redraws from the store and in the current display language. The decision being edited (keeper, action, conflict
  /// choices, copies kept or removed) lives in <c>_decision</c> and is kept.
  /// </summary>

  public override void Refresh()
  {
    // Text made when something happened (the toast, the alert, the session log) is made again in the current language.
    Toast = _toastSource is { } toast ? AppText.Get(toast.Key, ("title", TitleOf(toast.StackId, toast.Title))) : null;
    if (Alert is not null)
    {
      Alert = AppText.Get("stack.alert.changed");
    }
    for (var i = 0; i < _session.Count && i < Log.Count; i++)
    {
      var item = new SessionLogItem(_session[i].Title, LogText(_session[i].Action));
      if (Log[i] != item)
      {
        Log[i] = item;
      }
    }

    if (IsDone)
    {
      UpdateDone();
      return;
    }
    // A stack that is gone from the store stays on screen as it was last seen.
    var current = Store.Stacks.FirstOrDefault(s => s.Id == _stack.Id);
    if (current is not null)
    {
      _stack = KeepDetails(current, _stack);
    }
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
    try
    {
      Store.Decide(_decision);
    }
    catch (StackChangedException)
    {
      Alert = AppText.Get("stack.alert.changed");
      return;
    }
    Alert = null;
    var changesFiles = _review.ChangesFiles;
    _toastSource = (changesFiles ? "stack.toast.added"
      : _decision.Action == StackAction.KeepAll ? "stack.toast.keepAll" : "stack.toast.later", _stack.Id, _stack.Title);
    Toast = AppText.Get(_toastSource.Value.Key, ("title", _stack.Title));
    _session.Add((_stack.Title, _decision.Action));
    Log.Add(new SessionLogItem(_stack.Title, LogText(_decision.Action)));
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
    var stored = Store.DecisionFor(stackId);
    _keeperUnavailable = stored is not null && (_stack.IndexOf(stored.KeeperCopyId) is not (>= 0 and var keeper) || !StackReview.CanBeKeeper(_stack.Copies[keeper]));
    // A stored decision whose keeper is offline or gone keeps its other choices; the keeper falls back to the suggestion.
    _decision = stored is null ? StackDecision.Suggested(_stack)
      : _keeperUnavailable ? stored.WithKeeper(StackDecision.Suggested(_stack).KeeperCopyId)
      : stored;
    if (!keepToast)
    {
      _toastSource = null;
      Toast = null;
    }
    Conflicts.Clear();
    Rebuild();
    LoadDetails();
  }

  /// <summary>Loads slower facts (git status) after the stack is shown, then redraws if the user is still on it.</summary>
  private void LoadDetails()
  {
    var shown = _stack;
    var scheduler = SynchronizationContext.Current is null ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();
    Store.WithDetailsAsync(shown).ContinueWith(task =>
    {
      if (task.IsCompletedSuccessfully && !ReferenceEquals(task.Result, shown) && _stack.Id == shown.Id && !IsDone)
      {
        _stack = KeepDetails(_stack, task.Result);
        Rebuild();
      }
    }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
  }

  /// <summary>Carries git facts from <paramref name="detailed"/> over to a newer copy of the same stack.</summary>
  private static Stack KeepDetails(Stack current, Stack detailed)
  {
    if (detailed.Copies.All(c => c.Git is null))
    {
      return current;
    }
    var git = detailed.Copies.Where(c => c.Git is not null).ToDictionary(c => c.Id, c => c.Git, StringComparer.Ordinal);
    return current with { Copies = current.Copies.Select(c => c.Git is null && git.TryGetValue(c.Id, out var g) ? c with { Git = g } : c).ToList() };
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
    // A very large file stack would make the table unreadable; the decision still covers every copy.
    var shown = active.Take(MaxColumns).ToList();
    HiddenCopiesText = active.Count > MaxColumns
      ? AppText.Plural("stack.hiddenCopies", active.Count - MaxColumns,
        ("count", Fmt.Count(active.Count - MaxColumns)), ("total", Fmt.Count(active.Count)))
      : null;
    var reasons = new List<string>();
    if (_stack.ChangedCopyIds.Count > 0)
    {
      reasons.Add(AppText.Get("stack.reopened.changed",
        ("paths", StackText.JoinList(_stack.Copies.Where(c => _stack.ChangedCopyIds.Contains(c.Id)).Select(c => c.Path)))));
    }
    if (_stack.NewCopyIds.Count > 0)
    {
      reasons.Add(AppText.Get("stack.reopened.newCopies",
        ("paths", StackText.JoinList(_stack.Copies.Where(c => _stack.NewCopyIds.Contains(c.Id)).Select(c => c.Path)))));
    }
    if (_stack.EvidenceChangedCopyIds.Count > 0 && _stack.ChangedCopyIds.Count == 0)
    {
      reasons.Add(AppText.Get("stack.reopened.analysisChanged"));
    }
    if (_keeperUnavailable)
    {
      reasons.Add(AppText.Get("stack.reopened.keeperUnavailable"));
    }
    ReopenedText = reasons.Count == 0 ? null : StackText.JoinSentences([.. reasons, AppText.Get("stack.reopened.confirm")]);

    Title = _stack.Title;
    Group = StackText.GroupTitle(summary.Group);
    KindLine = StackText.Kind(_stack.Kind, active.Count);
    Why = WhyText(_stack, active.Count);
    Evidence = _stack.Evidence;
    var open = Store.Stacks.Count(s => s.Status == StackStatus.Open && s.Id != _stack.Id);
    Position = AppText.Plural("stack.position", open, ("count", Fmt.Count(open)));
    BlockedStackText = StackText.Blocked(_stack);
    RemovedText = review.RemovedCopies.Count == 0
      ? null
      : AppText.Plural("stack.removed", review.RemovedCopies.Count,
        ("copies", StackText.JoinList(review.RemovedCopies.Select(c => $"{c.SourceName} {c.Path}"))));

    Columns.Clear();
    foreach (var copy in shown)
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
    TableWidth = RowHeaderWidth + CellWidth * shown.Count;

    ColumnTone ToneOf(StackCopy copy) => review.RoleOf(copy) switch
    {
      CopyRole.Keeper => ColumnTone.Keeper,
      CopyRole.Offline => ColumnTone.Offline,
      _ => ColumnTone.Normal,
    };

    HasSame = _stack.SameFileCount > 0 || _stack.SameLabel is not null;
    SameLabel = _stack.SameLabel ?? StackText.Files(_stack.SameFileCount);
    SameSub = AppText.Plural("stack.same.sub", active.Count, ("count", active.Count));
    SameCells.Clear();
    var sameText = AppText.Get("stack.cell.same");
    foreach (var copy in shown)
    {
      SameCells.Add(new SimpleCellViewModel(sameText, ToneOf(copy)));
    }

    Rows.Clear();
    foreach (var row in review.Rows.Take(MaxRows))
    {
      var (hint, hintWarn) = Hint(review, row);
      var cells = shown.Select(copy => Cell(review, row, copy, ToneOf(copy), now)).ToList();
      Rows.Add(new StackRowViewModel(row.Row.RelativePath, row.Row.Description, row.Row.Warning, hint, hintWarn, cells));
    }
    NoDiff = Rows.Count == 0;
    DiffNote = NoDiff
      ? AppText.Get("stack.diff.none")
      : review.Rows.Count > MaxRows
        ? AppText.Get("stack.diff.truncated", ("count", Fmt.Count(review.Rows.Count)), ("max", MaxRows))
        : AppText.Get("stack.diff.note");

    HasRegenerable = _stack.Regenerable is not null;
    RegenerableLabel = _stack.Regenerable?.Names ?? string.Empty;
    RegenerableSub = AppText.Get("stack.regenerable.sub");
    RegenerableCells.Clear();
    if (_stack.Regenerable is { } regenerable)
    {
      foreach (var copy in shown)
      {
        var bytes = regenerable.BytesPerCopy[_stack.IndexOf(copy.Id)];
        RegenerableCells.Add(new SimpleCellViewModel(bytes > 0 ? Fmt.Size(bytes) : AppText.Get("stack.regenerable.none"), ToneOf(copy)));
      }
    }

    var notes = new List<string>();
    if (_stack.Note is not null)
    {
      notes.Add(_stack.Note);
    }
    notes.AddRange(_stack.RelatedOutside.Select(r =>
      AppText.Get("stack.note.related", ("path", r.Path), ("day", Fmt.Day(r.Modified, now)))));
    Note = notes.Count == 0 ? null : string.Join(Environment.NewLine, notes);

    UpdateOptions(review);
    UpdateConflicts(review);
    UpdateOutcome(review);

    BlockReason = review.Block switch
    {
      ReviewBlock.UnresolvedConflicts => AppText.Plural("stack.block.unresolved", review.UnresolvedCount, ("count", review.UnresolvedCount)),
      ReviewBlock.NothingToRecycle => BlockedStackText is not null
        ? AppText.Get("stack.block.keepAllOrLater", ("reason", BlockedStackText))
        : AppText.Get("stack.block.nothingToRecycle"),
      ReviewBlock.StillScanning => AppText.Get("stack.block.later", ("reason", BlockedStackText ?? string.Empty)),
      _ => null,
    };
    PrimaryLabel = _decision.Action switch
    {
      StackAction.KeepAll => AppText.Get("stack.primary.keepAll"),
      StackAction.Later => AppText.Get("stack.primary.later"),
      _ => AppText.Get("stack.primary.add"),
    };
    PrimaryNote = review.ChangesFiles
      ? AppText.Get("stack.primary.noteChanges")
      : AppText.Get("stack.primary.noteNoChanges");
  }

  private void UpdateOptions(StackReview review)
  {
    var unique = review.OnlyElsewhereFileCount;
    var conflicts = review.ConflictCount;
    var loss = review.LossFileCount;
    foreach (var option in Options)
    {
      option.Label = ActionLabel(option.Action);
      option.IsChosen = option.Action == _decision.Action;
      option.IsSuggested = option.Action == _stack.SuggestedAction;
      option.Description = option.Action switch
      {
        StackAction.Merge => conflicts > 0
          ? AppText.Plural("stack.option.mergeConflicts", conflicts, ("files", StackText.Files(unique)), ("count", conflicts))
          : unique > 0
            ? AppText.Plural("stack.option.merge", unique, ("count", Fmt.Count(unique)))
            : AppText.Get("stack.option.mergeSame"),
        StackAction.KeepKeeperOnly => loss > 0
          ? AppText.Plural("stack.option.keepKeeperOnlyLoss", loss, ("count", Fmt.Count(loss)))
          : AppText.Get("stack.option.keepKeeperOnly"),
        StackAction.KeepAll => AppText.Get("stack.option.keepAll"),
        _ => AppText.Get("stack.option.later"),
      };
    }
  }

  private void UpdateConflicts(StackReview review)
  {
    var visible = _decision.Action == StackAction.Merge ? review.Conflicts : [];
    HasConflicts = visible.Count > 0;
    var theirsAllowed = review.IsChoiceAllowed(ConflictChoice.Theirs);
    // The same conflicts keep their view models, so the radio button being clicked is not replaced under the pointer.
    var same = Conflicts.Count == visible.Count
      && Conflicts.Zip(visible).All(p => p.First.Path == p.Second.Row.RelativePath && p.First.Options[1].IsEnabled == theirsAllowed);
    if (!same)
    {
      Conflicts.Clear();
      foreach (var row in visible)
      {
        var path = row.Row.RelativePath;
        void Pick(ConflictChoice choice)
        {
          _decision = _decision.WithChoice(path, choice);
          Rebuild();
        }
        Conflicts.Add(new ConflictViewModel(path,
        [
          new ConflictOptionViewModel(ConflictChoice.Keeper, true, Pick),
          new ConflictOptionViewModel(ConflictChoice.Theirs, theirsAllowed, Pick),
          new ConflictOptionViewModel(ConflictChoice.Both, true, Pick),
        ]));
      }
    }

    // The text is set on every redraw, kept view models included, so it follows the display language.
    var now = Store.Now;
    foreach (var (vm, row) in Conflicts.Zip(visible))
    {
      var source = row.Source!;
      var keeperDay = Fmt.Day(row.KeeperVersion!.Modified, now);
      var theirDay = Fmt.Day(row.SourceVersion!.Modified, now);
      vm.Compare = AppText.Get("stack.conflict.compare", ("keeperDay", keeperDay), ("source", source.SourceName), ("theirDay", theirDay));
      foreach (var option in vm.Options)
      {
        option.Label = option.Choice switch
        {
          ConflictChoice.Keeper => AppText.Get("stack.conflict.useKeeper", ("day", keeperDay)),
          ConflictChoice.Theirs => AppText.Get("stack.conflict.useTheirs", ("source", source.SourceName), ("day", theirDay)),
          _ => AppText.Get("stack.conflict.keepBoth", ("source", source.SourceName),
            ("name", System.IO.Path.GetFileName(StackReview.SaveAsPath(vm.Path, source.Tag)))),
        };
        option.Why = option.IsEnabled ? null : AppText.Get("stack.conflict.protectedKeeper");
        option.IsChosen = option.Choice == row.Choice;
      }
    }
  }

  private void UpdateOutcome(StackReview review)
  {
    Outcome.Clear();
    foreach (var item in review.Outcome)
    {
      Outcome.Add(item.Kind switch
      {
        OutcomeKind.KeepAllNote => new OutcomeViewModel(AppText.Get("stack.outcome.keepAll"), OutcomeIcon.Keep),
        OutcomeKind.LaterNote => new OutcomeViewModel(AppText.Get("stack.outcome.later"), OutcomeIcon.None),
        OutcomeKind.CopyToKeeper => new OutcomeViewModel(
          AppText.Plural(item.KeeperIsGit ? "stack.outcome.copyGit" : "stack.outcome.copy", item.FileCount,
            ("count", Fmt.Count(item.FileCount)), ("paths", StackText.JoinList(item.Paths))), OutcomeIcon.Copy),
        OutcomeKind.ConflictUnresolved => new OutcomeViewModel(AppText.Get("stack.outcome.unresolved", ("path", item.Paths[0])), OutcomeIcon.Warn),
        OutcomeKind.SaveAs => new OutcomeViewModel(AppText.Get("stack.outcome.saveAs", ("path", item.NewPath ?? string.Empty)), OutcomeIcon.Copy),
        OutcomeKind.ReplaceKeeperVersion => new OutcomeViewModel(
          AppText.Get("stack.outcome.replace", ("source", item.Copy!.SourceName), ("path", item.Paths[0])), OutcomeIcon.Warn),
        OutcomeKind.KeepKeeperVersion => new OutcomeViewModel(AppText.Get("stack.outcome.keepKeeperVersion", ("path", item.Paths[0])), OutcomeIcon.Keep),
        OutcomeKind.RecyclesUniqueFiles => new OutcomeViewModel(
          AppText.Plural("stack.outcome.recyclesUnique", item.FileCount,
            ("count", Fmt.Count(item.FileCount)), ("paths", StackText.JoinList(item.Paths))), OutcomeIcon.Warn),
        OutcomeKind.Recycle => new OutcomeViewModel(
          AppText.Get("stack.outcome.recycle", ("path", item.Copy!.Path), ("size", Fmt.Size(item.Bytes))), OutcomeIcon.Recycle),
        OutcomeKind.KeepCopy => new OutcomeViewModel(
          AppText.Get(item.Copy!.IsProtected ? "stack.outcome.keepProtected" : "stack.outcome.keepChosen", ("path", item.Copy.Path)), OutcomeIcon.Keep),
        _ => new OutcomeViewModel(AppText.Get("stack.outcome.offline", ("path", item.Copy!.Path)), OutcomeIcon.Offline),
      });
    }

    Space.Clear();
    Space.Add(AppText.Get("stack.space.duplicates", ("size", Fmt.Size(review.DuplicateBytes))));
    Space.Add(!review.ChangesFiles
      ? AppText.Get("stack.space.none")
      : review.RecycleRegenerableBytes > 0
        ? AppText.Get("stack.space.recycleRegenerable", ("size", Fmt.Size(review.RecycleBytes)), ("regenerable", Fmt.Size(review.RecycleRegenerableBytes)))
        : AppText.Get("stack.space.recycle", ("size", Fmt.Size(review.RecycleBytes))));
    Space.Add(AppText.Get("stack.space.release"));
  }

  private void UpdateDone()
  {
    var pending = Store.Plan.Count;
    HasPending = pending > 0;
    PendingLabel = AppText.Plural("stack.done.run", pending, ("count", Fmt.Count(pending)));
    DoneText = pending > 0
      ? AppText.Plural("stack.done.pending", pending, ("count", Fmt.Count(pending)))
      : AppText.Get("stack.done.none");
  }

  private static string ActionLabel(StackAction action) => action switch
  {
    StackAction.Merge => AppText.Get("stack.action.merge"),
    StackAction.KeepKeeperOnly => AppText.Get("stack.action.keepKeeperOnly"),
    StackAction.KeepAll => AppText.Get("stack.action.keepAll"),
    _ => AppText.Get("stack.action.later"),
  };

  /// <summary>What a decision did, for the session log at the end.</summary>
  private static string LogText(StackAction action) => action switch
  {
    StackAction.Merge => AppText.Get("stack.log.merge"),
    StackAction.KeepKeeperOnly => AppText.Get("stack.log.keepKeeperOnly"),
    StackAction.KeepAll => AppText.Get("stack.log.keepAll"),
    _ => AppText.Get("stack.log.later"),
  };

  private static string WhyText(Stack stack, int copies)
  {
    if (stack.Kind == StackKind.File)
    {
      return AppText.Plural("stack.why.files", copies, ("count", copies), ("where", StackText.Where(stack)));
    }
    if (stack.Rows.Count == 0)
    {
      return stack.SameFileCount > 0
        ? AppText.Plural("stack.why.sameFiles", stack.SameFileCount, ("count", Fmt.Count(stack.SameFileCount)))
        : AppText.Plural("stack.why.identicalCopies", copies, ("count", copies));
    }
    return stack.SameFileCount > 0
      ? AppText.Plural("stack.why.sameAndDiffs", stack.SameFileCount, ("count", Fmt.Count(stack.SameFileCount)))
      : AppText.Get("stack.why.diffs");
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
          ? (AppText.Get("stack.hint.copyFrom", ("source", row.Source!.SourceName)), false)
          : (AppText.Get("stack.hint.notInKeeper"), true);
      case RowKind.Conflict:
        if (review.Action == StackAction.KeepKeeperOnly)
        {
          return (AppText.Get("stack.hint.newerRecycled", ("source", row.Source!.SourceName)), true);
        }
        if (review.IsUnresolved(row))
        {
          return (AppText.Get("stack.hint.chooseVersion"), true);
        }
        return row.Choice switch
        {
          ConflictChoice.Keeper => (AppText.Get("stack.hint.useKeeper"), false),
          ConflictChoice.Both => (AppText.Get("stack.hint.keepBoth",
            ("name", System.IO.Path.GetFileName(StackReview.SaveAsPath(row.Row.RelativePath, row.Source!.Tag)))), false),
          _ => (AppText.Get("stack.hint.useTheirs", ("source", row.Source!.SourceName)), false),
        };
      case RowKind.Older:
        return (AppText.Get("stack.hint.older"), false);
      case RowKind.KeptElsewhere:
        return (AppText.Get("stack.hint.keptElsewhere"), false);
      case RowKind.OnlyOffline:
        return (AppText.Get("stack.hint.onlyOffline"), false);
      default:
        return (null, false);
    }
  }

  private CellViewModel Cell(StackReview review, RowReview row, StackCopy copy, ColumnTone column, DateTimeOffset now)
  {
    var index = _stack.IndexOf(copy.Id);
    var version = row.Row.Cells[index];
    var label = version is null ? null : row.Row.Count > 1 ? AppText.Get("stack.cell.has") : Fmt.Day(version.Modified, now);
    var offline = column == ColumnTone.Offline;
    return review.CellOf(row, copy) switch
    {
      CellKind.KeeperHas => new CellViewModel(label!, null, CellTone.Keeper, column, false),
      CellKind.KeeperMissing => new CellViewModel(AppText.Get("stack.cell.missing"), null, CellTone.KeeperMissing, column, false),
      CellKind.Missing => new CellViewModel(AppText.Get("stack.cell.missing"), null, CellTone.Missing, column, false),
      CellKind.KeeperLacks => new CellViewModel(AppText.Get("stack.cell.keeperLacks"), label, CellTone.Highlight, column, !offline),
      CellKind.SameAsKeeper => new CellViewModel(AppText.Get("stack.cell.sameAsKeeper"), null, CellTone.Same, column, false),
      CellKind.Newer => new CellViewModel(AppText.Get("stack.cell.newer"), label, CellTone.Highlight, column, !offline),
      CellKind.Older => new CellViewModel(AppText.Get("stack.cell.older"), label, CellTone.Older, column, false),
      _ => new CellViewModel(AppText.Get("stack.cell.different"), null, CellTone.Highlight, column, false),
    };
  }
}
