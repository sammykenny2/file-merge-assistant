namespace FileMergeAssistant.Stacks;

/// <summary>Sections of the 「待整理」 list on the workbench.</summary>
public enum StackGroup
{
  /// <summary>One copy already contains everything the others have.</summary>
  ReadyToConverge,

  /// <summary>Some copies have files the others lack.</summary>
  NeedsMerge,

  /// <summary>Same paths with different content.</summary>
  VersionDifferences,

  /// <summary>A source is still being scanned; the result may change.</summary>
  StillComparing,
}

public enum StackSort
{
  /// <summary>Most space for the fewest decisions first.</summary>
  Suggested,

  MostSpace,
  RecentlyChanged,
  MostCopies,
}

/// <summary>What the workbench needs to know about a stack without opening it.</summary>
/// <param name="Stack">The stack.</param>
/// <param name="Group">List section.</param>
/// <param name="ReclaimableBytes">What the suggested decision would move to the recycle area.</param>
/// <param name="OnlyElsewhereFiles">Files only some copies have.</param>
/// <param name="ConflictFiles">Paths where a non-keeper copy is newer; each needs a choice.</param>
/// <param name="DifferingFiles">Paths whose content differs in some copy.</param>
/// <param name="LooksDeliberate">Some copy was edited apart from the keeper (each side is newer somewhere): probably separate versions.</param>
public sealed record StackSummary(
  Stack Stack,
  StackGroup Group,
  long ReclaimableBytes,
  int OnlyElsewhereFiles,
  int ConflictFiles,
  int DifferingFiles,
  bool LooksDeliberate)
{
  /// <summary>Everything that is not the suggested keeper, including offline copies.</summary>
  public long DuplicateBytes { get; init; }

  public bool IsBlocked => Stack.Block != StackBlock.None;

  /// <summary>Ordering weight for <see cref="StackSort.Suggested"/>: bytes per decision, deliberate versions last.</summary>
  public double SuggestedScore =>
    IsBlocked ? -1 : (LooksDeliberate ? 0 : ReclaimableBytes / (1.0 + ConflictFiles));
}

/// <summary>Sorts stacks into the workbench sections and suggests what to do next.</summary>
public static class StackTriage
{
  /// <summary>
  /// Compares every copy (including offline and protected ones) against the suggested keeper. What the suggested
  /// decision would actually recycle is measured separately, because offline and protected copies stay.
  /// </summary>
  public static StackSummary Summarize(Stack stack)
  {
    ArgumentNullException.ThrowIfNull(stack);
    var keeperIndex = stack.SuggestedKeeper;
    int onlyElsewhere = 0, conflicts = 0, differing = 0;
    foreach (var row in stack.Rows)
    {
      var keeper = row.Cells[keeperIndex];
      var others = row.Cells.Where((v, i) => i != keeperIndex && v is not null).Select(v => v!).ToList();
      if (keeper is null)
      {
        onlyElsewhere += others.Count > 0 ? row.Count : 0;
        continue;
      }
      var different = others.Where(v => v.ContentId != keeper.ContentId).ToList();
      if (different.Count > 0)
      {
        differing += row.Count;
        conflicts += different.Any(v => v.Modified > keeper.Modified) ? row.Count : 0;
      }
    }

    var group = stack.Block == StackBlock.StillScanning
      ? StackGroup.StillComparing
      : onlyElsewhere > 0
        ? StackGroup.NeedsMerge
        : differing > 0 ? StackGroup.VersionDifferences : StackGroup.ReadyToConverge;

    var suggested = new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));
    return new StackSummary(stack, group, suggested.RecycleBytes, onlyElsewhere, conflicts, differing, DivergedCopies(stack, keeperIndex) > 0)
    {
      DuplicateBytes = suggested.DuplicateBytes,
    };
  }

  public static IReadOnlyList<StackSummary> Order(IEnumerable<StackSummary> summaries, StackSort sort)
  {
    return sort switch
    {
      StackSort.MostSpace => summaries.OrderByDescending(s => s.ReclaimableBytes).ToList(),
      StackSort.RecentlyChanged => summaries.OrderByDescending(s => s.Stack.UpdatedAt).ToList(),
      StackSort.MostCopies => summaries.OrderByDescending(s => s.Stack.Copies.Count).ThenByDescending(s => s.ReclaimableBytes).ToList(),
      _ => summaries.OrderByDescending(s => s.SuggestedScore).ToList(),
    };
  }

  /// <summary>
  /// Non-keeper copies that are newer than the keeper on some path and older on another: both sides were edited
  /// after they split, which usually means deliberate versions rather than a stale backup.
  /// </summary>
  private static int DivergedCopies(Stack stack, int keeperIndex)
  {
    var count = 0;
    for (var i = 0; i < stack.Copies.Count; i++)
    {
      if (i == keeperIndex)
      {
        continue;
      }
      var newer = false;
      var older = false;
      foreach (var row in stack.Rows)
      {
        var mine = row.Cells[i];
        var keeper = row.Cells[keeperIndex];
        if (mine is null || keeper is null || mine.ContentId == keeper.ContentId)
        {
          continue;
        }
        newer |= mine.Modified > keeper.Modified;
        older |= mine.Modified < keeper.Modified;
      }
      if (newer && older)
      {
        count++;
      }
    }
    return count;
  }
}
