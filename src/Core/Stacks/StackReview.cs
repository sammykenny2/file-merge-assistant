namespace FileWorkbench.Stacks;

/// <summary>What happens to a copy under the current decision.</summary>
public enum CopyRole
{
  Keeper,

  /// <summary>Moved to the recycle area when the plan runs.</summary>
  Recycle,

  /// <summary>The user ticked 「也保留這份」.</summary>
  AlsoKeep,

  /// <summary>In a protected source, so never recycled.</summary>
  Protected,

  /// <summary>On an offline volume; left alone this time.</summary>
  Offline,

  /// <summary>The action changes no files (keep all, or later).</summary>
  Unchanged,
}

/// <summary>How one differing path compares against the keeper.</summary>
public enum RowKind
{
  /// <summary>No copy that is still in the stack has the path.</summary>
  None,

  /// <summary>Every copy that will be recycled has the keeper's content.</summary>
  Same,

  /// <summary>The keeper lacks the path and a copy that will be recycled has it.</summary>
  OnlyElsewhere,

  /// <summary>A copy that will be recycled has a newer, different version.</summary>
  Conflict,

  /// <summary>Copies that will be recycled only have older versions.</summary>
  Older,

  /// <summary>The keeper lacks the path, but a copy that stays (protected or also kept) has it.</summary>
  KeptElsewhere,

  /// <summary>Only an offline copy has the path; nothing happens to it this time.</summary>
  OnlyOffline,
}

/// <summary>What a cell of the comparison table shows.</summary>
public enum CellKind
{
  /// <summary>The keeper's own version.</summary>
  KeeperHas,

  /// <summary>The keeper does not have the path.</summary>
  KeeperMissing,

  /// <summary>This copy does not have the path.</summary>
  Missing,

  /// <summary>This copy has the path and the keeper does not.</summary>
  KeeperLacks,

  SameAsKeeper,
  Newer,
  Older,

  /// <summary>Different content with the same modification time.</summary>
  Different,
}

/// <summary>Why a decision cannot be added to the pending plan yet.</summary>
public enum ReviewBlock
{
  None,

  /// <summary>Conflicting paths still need a choice.</summary>
  UnresolvedConflicts,

  /// <summary>No copy would be recycled; 「都保留」 says the same thing honestly.</summary>
  NothingToRecycle,

  /// <summary>A source is still being scanned, so the stack may still change.</summary>
  StillScanning,
}

/// <summary>A path that differs between copies, analysed against the keeper.</summary>
/// <param name="Row">The row.</param>
/// <param name="Kind">How it compares.</param>
/// <param name="KeeperVersion">The keeper's version, if any.</param>
/// <param name="Source">For <see cref="RowKind.OnlyElsewhere"/> and <see cref="RowKind.Conflict"/>: the copy whose version would be copied (the newest one).</param>
/// <param name="SourceVersion">That copy's version.</param>
/// <param name="Choice">The user's choice for a conflict.</param>
public sealed record RowReview(
  StackRow Row,
  RowKind Kind,
  FileVersion? KeeperVersion,
  StackCopy? Source,
  FileVersion? SourceVersion,
  ConflictChoice Choice);

public enum OutcomeKind
{
  /// <summary>Keep all: nothing changes; reminded only when a copy changes.</summary>
  KeepAllNote,

  /// <summary>Later: nothing changes; the stack goes back to the list.</summary>
  LaterNote,

  CopyToKeeper,
  ConflictUnresolved,
  SaveAs,
  ReplaceKeeperVersion,
  KeepKeeperVersion,

  /// <summary>Keep-keeper-only would recycle files the keeper lacks or has older.</summary>
  RecyclesUniqueFiles,

  Recycle,
  KeepCopy,
  OfflineUntouched,
}

/// <summary>One line of 「加入後，執行時會做的事」.</summary>
public sealed record ReviewOutcome(OutcomeKind Kind)
{
  public IReadOnlyList<string> Paths { get; init; } = [];
  public int FileCount { get; init; }
  public StackCopy? Copy { get; init; }
  public long Bytes { get; init; }

  /// <summary>For copies into a git working tree: the files become untracked.</summary>
  public bool KeeperIsGit { get; init; }

  /// <summary>For <see cref="OutcomeKind.SaveAs"/>: the new relative path.</summary>
  public string? NewPath { get; init; }
}

/// <summary>
/// Evaluates a decision for one stack: the role of each copy, how each differing path compares with the keeper, what
/// running the plan would do, and whether the decision can be added to the pending plan. Pure logic, no file access.
/// </summary>
public sealed class StackReview
{
  private readonly Dictionary<string, CopyRole> _fileActionRoles = new(StringComparer.Ordinal);

  public StackReview(Stack stack, StackDecision decision)
  {
    ArgumentNullException.ThrowIfNull(stack);
    ArgumentNullException.ThrowIfNull(decision);

    KeeperIndex = stack.IndexOf(decision.KeeperCopyId);
    if (KeeperIndex < 0)
    {
      throw new ArgumentException($"Copy '{decision.KeeperCopyId}' is not part of stack '{stack.Id}'.", nameof(decision));
    }
    if (stack.Copies[KeeperIndex].IsOffline)
    {
      throw new ArgumentException("An offline copy cannot be the keeper.", nameof(decision));
    }

    Stack = stack;
    Decision = decision;
    Keeper = stack.Copies[KeeperIndex];
    ActiveCopies = stack.Copies.Where(c => c.Id == Keeper.Id || !decision.Removed.Contains(c.Id)).ToList();
    RemovedCopies = stack.Copies.Where(c => c.Id != Keeper.Id && decision.Removed.Contains(c.Id)).ToList();

    foreach (var copy in ActiveCopies)
    {
      _fileActionRoles[copy.Id] = FileActionRole(copy);
    }

    Recycled = ActiveCopies.Where(c => _fileActionRoles[c.Id] == CopyRole.Recycle).ToList();
    KeptOthers = ActiveCopies.Where(c => _fileActionRoles[c.Id] is CopyRole.Protected or CopyRole.AlsoKeep).ToList();
    OfflineCopies = ActiveCopies.Where(c => _fileActionRoles[c.Id] == CopyRole.Offline).ToList();

    Rows = stack.Rows.Select(Analyse).ToList();
    OnlyElsewhere = Rows.Where(r => r.Kind == RowKind.OnlyElsewhere).ToList();
    Conflicts = Rows.Where(r => r.Kind == RowKind.Conflict).ToList();
  }

  public Stack Stack { get; }
  public StackDecision Decision { get; }
  public StackAction Action => Decision.Action;
  public int KeeperIndex { get; }
  public StackCopy Keeper { get; }

  /// <summary>Copies still in the stack, in stack order.</summary>
  public IReadOnlyList<StackCopy> ActiveCopies { get; }

  /// <summary>Copies the user moved out with 「不屬於這疊」.</summary>
  public IReadOnlyList<StackCopy> RemovedCopies { get; }

  /// <summary>Copies a file action would recycle (regardless of the chosen action).</summary>
  public IReadOnlyList<StackCopy> Recycled { get; }

  /// <summary>Non-keeper copies that stay: protected or also kept.</summary>
  public IReadOnlyList<StackCopy> KeptOthers { get; }

  public IReadOnlyList<StackCopy> OfflineCopies { get; }

  public IReadOnlyList<RowReview> Rows { get; }
  public IReadOnlyList<RowReview> OnlyElsewhere { get; }
  public IReadOnlyList<RowReview> Conflicts { get; }

  /// <summary>True for the two actions that change files.</summary>
  public bool ChangesFiles => Action is StackAction.Merge or StackAction.KeepKeeperOnly;

  public bool KeeperIsProtected => Keeper.IsProtected;

  public int OnlyElsewhereFileCount => OnlyElsewhere.Sum(r => r.Row.Count);

  public int ConflictCount => Conflicts.Count;

  /// <summary>Files keep-keeper-only would recycle although the keeper lacks them or has an older version.</summary>
  public int LossFileCount => OnlyElsewhereFileCount + Conflicts.Sum(r => r.Row.Count);

  public int UnresolvedCount => Action == StackAction.Merge ? Conflicts.Count(IsUnresolved) : 0;

  /// <summary>Everything that is not the keeper, including offline copies and regenerable folders.</summary>
  public long DuplicateBytes => ActiveCopies.Where(c => c.Id != Keeper.Id).Sum(c => c.TotalBytes);

  /// <summary>What this decision would move to the recycle area.</summary>
  public long RecycleBytes => ChangesFiles ? Recycled.Sum(c => c.TotalBytes) : 0;

  public long RecycleRegenerableBytes => ChangesFiles ? Recycled.Sum(c => c.RegenerableBytes) : 0;

  public CopyRole RoleOf(StackCopy copy)
  {
    var role = _fileActionRoles.TryGetValue(copy.Id, out var r) ? r : CopyRole.Unchanged;
    return !ChangesFiles && role is CopyRole.Recycle or CopyRole.AlsoKeep or CopyRole.Protected ? CopyRole.Unchanged : role;
  }

  public bool IsUnresolved(RowReview row) =>
    row.Choice == ConflictChoice.None || (row.Choice == ConflictChoice.Theirs && KeeperIsProtected);

  /// <summary>Whether a conflict choice can be picked; replacing a protected keeper's file is not allowed (ADR-003).</summary>
  public bool IsChoiceAllowed(ConflictChoice choice) => choice != ConflictChoice.Theirs || !KeeperIsProtected;

  /// <summary>Whether a copy may become the keeper.</summary>
  public static bool CanBeKeeper(StackCopy copy) => !copy.IsOffline;

  /// <summary>Whether 「不屬於這疊」 is offered for a copy: never for the keeper, and a stack keeps at least two copies.</summary>
  public bool CanRemove(StackCopy copy) => copy.Id != Keeper.Id && ActiveCopies.Count > 2;

  public ReviewBlock Block
  {
    get
    {
      if (!ChangesFiles)
      {
        return ReviewBlock.None;
      }
      if (Stack.Block == StackBlock.StillScanning)
      {
        return ReviewBlock.StillScanning;
      }
      if (UnresolvedCount > 0)
      {
        return ReviewBlock.UnresolvedConflicts;
      }
      var mergeAddsFiles = Action == StackAction.Merge && (OnlyElsewhereFileCount > 0 || ConflictCount > 0);
      return Recycled.Count == 0 && !mergeAddsFiles ? ReviewBlock.NothingToRecycle : ReviewBlock.None;
    }
  }

  public CellKind CellOf(RowReview row, StackCopy copy)
  {
    var index = Stack.IndexOf(copy.Id);
    var cell = row.Row.Cells[index];
    var keeper = row.KeeperVersion;
    if (copy.Id == Keeper.Id)
    {
      return cell is null ? CellKind.KeeperMissing : CellKind.KeeperHas;
    }
    if (cell is null)
    {
      return CellKind.Missing;
    }
    if (keeper is null)
    {
      return CellKind.KeeperLacks;
    }
    if (cell.ContentId == keeper.ContentId)
    {
      return CellKind.SameAsKeeper;
    }
    if (cell.Modified > keeper.Modified)
    {
      return CellKind.Newer;
    }
    return cell.Modified < keeper.Modified ? CellKind.Older : CellKind.Different;
  }

  public IReadOnlyList<ReviewOutcome> Outcome
  {
    get
    {
      var outcome = new List<ReviewOutcome>();
      if (Action == StackAction.KeepAll)
      {
        outcome.Add(new ReviewOutcome(OutcomeKind.KeepAllNote));
        return outcome;
      }
      if (Action == StackAction.Later)
      {
        outcome.Add(new ReviewOutcome(OutcomeKind.LaterNote));
        return outcome;
      }

      if (Action == StackAction.Merge)
      {
        if (OnlyElsewhereFileCount > 0)
        {
          outcome.Add(new ReviewOutcome(OutcomeKind.CopyToKeeper)
          {
            FileCount = OnlyElsewhereFileCount,
            Paths = OnlyElsewhere.Select(r => r.Row.RelativePath).ToList(),
            KeeperIsGit = Keeper.Git is not null,
          });
        }
        foreach (var conflict in Conflicts)
        {
          var path = conflict.Row.RelativePath;
          if (IsUnresolved(conflict))
          {
            outcome.Add(new ReviewOutcome(OutcomeKind.ConflictUnresolved) { Paths = [path] });
          }
          else if (conflict.Choice == ConflictChoice.Both)
          {
            outcome.Add(new ReviewOutcome(OutcomeKind.SaveAs)
            {
              Paths = [path],
              NewPath = SaveAsPath(path, conflict.Source!.Tag),
              Copy = conflict.Source,
            });
          }
          else if (conflict.Choice == ConflictChoice.Theirs)
          {
            outcome.Add(new ReviewOutcome(OutcomeKind.ReplaceKeeperVersion) { Paths = [path], Copy = conflict.Source });
          }
          else
          {
            outcome.Add(new ReviewOutcome(OutcomeKind.KeepKeeperVersion) { Paths = [path] });
          }
        }
      }

      if (Action == StackAction.KeepKeeperOnly && LossFileCount > 0)
      {
        outcome.Add(new ReviewOutcome(OutcomeKind.RecyclesUniqueFiles)
        {
          FileCount = LossFileCount,
          Paths = OnlyElsewhere.Concat(Conflicts).Select(r => r.Row.RelativePath).ToList(),
        });
      }

      outcome.AddRange(Recycled.Select(c => new ReviewOutcome(OutcomeKind.Recycle) { Copy = c, Bytes = c.TotalBytes }));
      outcome.AddRange(KeptOthers.Select(c => new ReviewOutcome(OutcomeKind.KeepCopy) { Copy = c }));
      outcome.AddRange(OfflineCopies.Select(c => new ReviewOutcome(OutcomeKind.OfflineUntouched) { Copy = c }));
      return outcome;
    }
  }

  /// <summary>
  /// The relative path used to keep both versions: <c>name.from-tag.ext</c>. A leading dot does not count as the
  /// extension separator, so <c>.gitconfig</c> becomes <c>.gitconfig.from-tag</c>.
  /// </summary>
  public static string SaveAsPath(string relativePath, string tag)
  {
    ArgumentNullException.ThrowIfNull(relativePath);
    var slash = relativePath.LastIndexOf('/');
    var dot = relativePath.LastIndexOf('.');
    return dot > slash + 1
      ? string.Concat(relativePath.AsSpan(0, dot), ".from-", tag, relativePath.AsSpan(dot))
      : relativePath + ".from-" + tag;
  }

  private CopyRole FileActionRole(StackCopy copy)
  {
    if (copy.Id == Keeper.Id)
    {
      return CopyRole.Keeper;
    }
    if (copy.IsOffline)
    {
      return CopyRole.Offline;
    }
    if (copy.IsProtected)
    {
      return CopyRole.Protected;
    }
    return Decision.AlsoKeep.Contains(copy.Id) ? CopyRole.AlsoKeep : CopyRole.Recycle;
  }

  private RowReview Analyse(StackRow row)
  {
    var keeperVersion = row.Cells[KeeperIndex];
    var choice = Decision.ChoiceFor(row.RelativePath);
    var recycled = Recycled
      .Select(c => (Copy: c, Version: row.Cells[Stack.IndexOf(c.Id)]))
      .Where(x => x.Version is not null)
      .Select(x => (x.Copy, Version: x.Version!))
      .ToList();

    if (keeperVersion is null)
    {
      if (recycled.Count > 0)
      {
        var newest = recycled.OrderByDescending(x => x.Version.Modified).First();
        return new RowReview(row, RowKind.OnlyElsewhere, null, newest.Copy, newest.Version, choice);
      }
      if (KeptOthers.Any(c => row.Cells[Stack.IndexOf(c.Id)] is not null))
      {
        return new RowReview(row, RowKind.KeptElsewhere, null, null, null, choice);
      }
      return OfflineCopies.Any(c => row.Cells[Stack.IndexOf(c.Id)] is not null)
        ? new RowReview(row, RowKind.OnlyOffline, null, null, null, choice)
        : new RowReview(row, RowKind.None, null, null, null, choice);
    }

    var different = recycled.Where(x => x.Version.ContentId != keeperVersion.ContentId).ToList();
    var newer = different.Where(x => x.Version.Modified > keeperVersion.Modified).OrderByDescending(x => x.Version.Modified).ToList();
    if (newer.Count > 0)
    {
      return new RowReview(row, RowKind.Conflict, keeperVersion, newer[0].Copy, newer[0].Version, choice);
    }
    return different.Count > 0
      ? new RowReview(row, RowKind.Older, keeperVersion, null, null, choice)
      : new RowReview(row, RowKind.Same, keeperVersion, null, null, choice);
  }
}
