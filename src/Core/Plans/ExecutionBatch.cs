namespace FileMergeAssistant.Plans;

public enum BatchItemState
{
  Done,

  /// <summary>Some or all of the item failed; only the failed part is retried.</summary>
  Failed,

  /// <summary>Not run: it changed after the decision, or the user skipped it.</summary>
  Skipped,

  /// <summary>Put back by a restore.</summary>
  Restored,
}

/// <summary>What happened to one plan item when the plan ran.</summary>
public sealed record BatchItem(string Id, string StackId, PlanItemKind Kind, string Target, BatchItemState State)
{
  /// <summary>Where the copy went, or which recycle area holds the item.</summary>
  public string Detail { get; init; } = string.Empty;

  public long Bytes { get; init; }
  public long FileCount { get; init; } = 1;

  /// <summary>For partly failed folders: how many files did not move.</summary>
  public long FailedFiles { get; init; }

  public string? FailureReason { get; init; }

  /// <summary>The item failed at first and succeeded on retry.</summary>
  public bool Retried { get; init; }

  /// <summary>Volume of the recycle area that holds the item, for example <c>D:\</c>.</summary>
  public string? RecycleVolume { get; init; }

  public bool CanRestore => State == BatchItemState.Done;
}

/// <summary>One run of the pending plan, kept in the history.</summary>
public sealed record ExecutionBatch(string Id, DateTimeOffset RanAt, IReadOnlyList<BatchItem> Items)
{
  /// <summary>When the recycle area holding this batch was emptied; the batch can no longer be restored.</summary>
  public DateTimeOffset? EmptiedAt { get; init; }

  public int Done => Items.Count(i => i.State == BatchItemState.Done);
  public int Failed => Items.Count(i => i.State == BatchItemState.Failed);
  public int Skipped => Items.Count(i => i.State == BatchItemState.Skipped);
  public int Restored => Items.Count(i => i.State == BatchItemState.Restored);
  public long RecycledBytes => Items.Where(i => i.Kind == PlanItemKind.Recycle && i.State == BatchItemState.Done).Sum(i => i.Bytes);
}

/// <summary>A restore that found a file at the original location. The app never overwrites it.</summary>
/// <param name="Id">Conflict id.</param>
/// <param name="BatchId">Batch the item belongs to.</param>
/// <param name="ItemId">The batch item.</param>
/// <param name="OriginalPath">Where the item was before it was recycled.</param>
/// <param name="RecycledAt">When it was moved to the recycle area.</param>
/// <param name="RecycledBytes">Size of the recycled version.</param>
/// <param name="RecycledModified">Last write time of the recycled version.</param>
/// <param name="CurrentCreated">When the file now at the original location was created.</param>
/// <param name="CurrentBytes">Its size.</param>
public sealed record RestoreConflict(
  string Id,
  string BatchId,
  string ItemId,
  string OriginalPath,
  DateTimeOffset RecycledAt,
  long RecycledBytes,
  DateTimeOffset RecycledModified,
  DateTimeOffset CurrentCreated,
  long CurrentBytes)
{
  /// <summary>Set once the user resolved the conflict.</summary>
  public string? Resolution { get; init; }
}

public enum RestoreConflictChoice
{
  /// <summary>Restore next to the existing file with a 「(還原 MM-dd)」 suffix.</summary>
  RenameAtOriginal,

  /// <summary>Restore into a folder the user picks.</summary>
  OtherFolder,

  /// <summary>Leave it in the recycle area.</summary>
  KeepInRecycleArea,
}

/// <summary>Space a volume's recycle area currently holds (ADR-007).</summary>
public sealed record RecycleArea(string Volume, long Bytes);
