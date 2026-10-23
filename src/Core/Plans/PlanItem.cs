using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Plans;

public enum PlanItemKind
{
  /// <summary>Copy a file only another copy has into the keeper, then verify it.</summary>
  Copy,

  /// <summary>Keep both versions: copy the other version next to the keeper's as <c>name.from-tag.ext</c>.</summary>
  SaveAs,

  /// <summary>Recycle the keeper's version and copy the other version in its place.</summary>
  Replace,

  /// <summary>Move a whole copy (folder or file) to the recycle area of its volume.</summary>
  Recycle,
}

/// <summary>Result of re-checking an item against the disk just before running the plan.</summary>
public enum PlanCheck
{
  NotChecked,

  /// <summary>Everything the decision relied on is unchanged.</summary>
  Confirmed,

  /// <summary>Something changed after the decision; the item stops instead of running the old plan.</summary>
  Changed,
}

/// <summary>One action in the pending plan.</summary>
/// <param name="Id">Item id.</param>
/// <param name="StackId">The stack the item came from.</param>
/// <param name="Kind">Action.</param>
/// <param name="Target">What the user sees as the subject: a relative path for copies, a full path for recycling.</param>
/// <param name="SourcePath">For copies, the file to copy; for recycling, the folder or file to move.</param>
/// <param name="DestinationPath">For copies, where the file goes; empty for recycling.</param>
public sealed record PlanItem(
  string Id,
  string StackId,
  PlanItemKind Kind,
  string Target,
  string SourcePath,
  string DestinationPath)
{
  /// <summary>For copies: the copy the file comes from. For recycling: the copy being recycled.</summary>
  public string CopyId { get; init; } = string.Empty;

  /// <summary>Source name of <see cref="CopyId"/>, for display.</summary>
  public string CopyName { get; init; } = string.Empty;

  public long Bytes { get; init; }
  public long FileCount { get; init; } = 1;

  /// <summary>Recycled folders that include regenerable folders, for display (「含 node_modules」).</summary>
  public string? IncludesRegenerable { get; init; }

  /// <summary>The file looks like a secrets file (for example <c>.env.local</c>).</summary>
  public bool LooksLikeSecret { get; init; }

  public bool Enabled { get; init; } = true;
  public PlanCheck Check { get; init; } = PlanCheck.NotChecked;

  /// <summary>What changed, when <see cref="Check"/> is <see cref="PlanCheck.Changed"/>.</summary>
  public string? ChangeDetail { get; init; }

  /// <summary>The user chose to skip a changed item this time instead of stopping.</summary>
  public bool SkipChanged { get; init; }

  public bool IsCopyLike => Kind is PlanItemKind.Copy or PlanItemKind.SaveAs or PlanItemKind.Replace;

  /// <summary>Whether running the plan would execute this item.</summary>
  public bool WillRun => Enabled && Check != PlanCheck.Changed;
}

/// <summary>A decided stack in the pending plan, with the items its decision produced.</summary>
public sealed record PlanEntry(
  Stack Stack,
  StackDecision Decision,
  StackCopy Keeper,
  IReadOnlyList<PlanItem> Items,
  DateTimeOffset DecidedAt)
{
  /// <summary>Offline copies left alone this time.</summary>
  public IReadOnlyList<StackCopy> OfflineCopies { get; init; } = [];

  /// <summary>Protected or also-kept copies.</summary>
  public IReadOnlyList<StackCopy> KeptCopies { get; init; } = [];

  /// <summary>Result of re-checking the keeper, for display.</summary>
  public string? KeeperCheck { get; init; }
}
