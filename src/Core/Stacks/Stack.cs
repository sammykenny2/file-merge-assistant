namespace FileWorkbench.Stacks;

/// <summary>Whether the copies in a stack are whole folders or single files (ADR-001).</summary>
public enum StackKind
{
  Folder,
  File,
}

/// <summary>Why a stack cannot be consolidated yet.</summary>
public enum StackBlock
{
  None,

  /// <summary>A copy lives on an offline volume; its current state cannot be confirmed.</summary>
  SourceOffline,

  /// <summary>One of the sources is still being scanned, so the membership may change.</summary>
  StillScanning,
}

/// <summary>Where a stack stands in the user's work.</summary>
public enum StackStatus
{
  /// <summary>Waiting to be reviewed.</summary>
  Open,

  /// <summary>Decided and waiting in the pending plan.</summary>
  Pending,

  /// <summary>Executed: only the keeper (and anything the user kept) remains.</summary>
  Converged,

  /// <summary>The user decided the copies are deliberate versions.</summary>
  KeptAll,

  /// <summary>Only one copy remains, so there is nothing to review.</summary>
  Dissolved,
}

/// <summary>Read-only git facts about a copy (ADR-005). The app never runs a git command that changes a repository.</summary>
/// <param name="Branch">Checked-out branch.</param>
/// <param name="Head">Commit id of HEAD.</param>
/// <param name="UncommittedChanges">Number of changed or untracked files.</param>
/// <param name="BehindKeeper">Commits the keeper has that this copy lacks, when known.</param>
/// <param name="AheadOfKeeper">Commits this copy has that the keeper lacks, when known.</param>
public sealed record GitStatus(string Branch, string? Head, int UncommittedChanges, int? BehindKeeper = null, int? AheadOfKeeper = null);

/// <summary>One copy in a stack: a folder or a single file at one location.</summary>
/// <param name="Id">Identity of this copy inside the stack.</param>
/// <param name="SourceId">Source the copy belongs to.</param>
/// <param name="SourceName">Short source name shown as the column title.</param>
/// <param name="Path">Full path of the folder or file.</param>
/// <param name="Tag">Short ASCII tag used when a version is saved alongside, for example <c>receipt.from-desktop.html</c>.</param>
/// <param name="IsProtected">The copy is inside a protected source.</param>
/// <param name="IsOffline">The copy is on an offline volume.</param>
/// <param name="SizeBytes">Size without regenerable folders.</param>
/// <param name="RegenerableBytes">Size of regenerable folders inside the copy.</param>
/// <param name="FileCount">Files without regenerable folders.</param>
/// <param name="LastModified">Latest modification inside the copy.</param>
public sealed record StackCopy(
  string Id,
  string SourceId,
  string SourceName,
  string Path,
  string Tag,
  bool IsProtected,
  bool IsOffline,
  long SizeBytes,
  long RegenerableBytes,
  long FileCount,
  DateTimeOffset? LastModified)
{
  public GitStatus? Git { get; init; }

  /// <summary>Extra facts for the column header, for example 「檔名改過，例如 清水寺-01.jpg」.</summary>
  public IReadOnlyList<string> Notes { get; init; } = [];

  /// <summary>For offline copies: when the index was taken.</summary>
  public DateTimeOffset? IndexedAt { get; init; }

  /// <summary>Why this copy is the suggested keeper; only set on the suggested keeper.</summary>
  public string? SuggestReason { get; init; }

  /// <summary>Fingerprint of the copy's file list as indexed (<see cref="CopyDigest"/>); compared with the decision's.</summary>
  public string? Digest { get; init; }

  /// <summary>Versioned content evidence; changes require a pending decision to be reviewed again.</summary>
  public string? EvidenceDigest { get; init; }

  public long TotalBytes => SizeBytes + RegenerableBytes;
}

/// <summary>One version of a file inside a copy.</summary>
/// <param name="ContentId">Content fingerprint (or another value that is equal only for equal content).</param>
/// <param name="Modified">Last write time.</param>
/// <param name="SizeBytes">File size.</param>
public sealed record FileVersion(string ContentId, DateTimeOffset Modified, long SizeBytes = 0);

/// <summary>
/// A relative path whose content is not identical in every copy. <see cref="Cells"/> has one entry per copy, in the
/// order of <see cref="Stack.Copies"/>; <c>null</c> means the copy does not have the path.
/// </summary>
/// <param name="RelativePath">Path relative to the copy root, with forward slashes; for a file stack, the file name.</param>
/// <param name="Cells">The version in each copy.</param>
/// <param name="Count">Number of files the row stands for (1 unless the row summarises a set, such as renamed photos).</param>
/// <param name="Description">Optional line under the path, for example 「內容相同、檔名不同」.</param>
/// <param name="Warning">Optional warning, for example when the file looks like a secrets file.</param>
public sealed record StackRow(
  string RelativePath,
  IReadOnlyList<FileVersion?> Cells,
  int Count = 1,
  string? Description = null,
  string? Warning = null);

/// <summary>Regenerable folders inside the copies, shown as one row (ADR-005).</summary>
/// <param name="Names">Folder names, for example 「node_modules、dist」.</param>
/// <param name="BytesPerCopy">Size in each copy, in copy order; 0 means none.</param>
public sealed record RegenerableSummary(string Names, IReadOnlyList<long> BytesPerCopy);

/// <summary>A file outside the stack that looks related but has different content (shown as a note, never merged in).</summary>
public sealed record RelatedOutside(string Path, DateTimeOffset Modified);

/// <summary>
/// A set of two or more copies of the same folder or file at different locations; the unit the user reviews and
/// decides on (ADR-001).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
  Justification = "「疊」 (stack) is the product's review unit, not a collection type.")]
public sealed record Stack(
  string Id,
  string Title,
  StackKind Kind,
  IReadOnlyList<StackCopy> Copies,
  IReadOnlyList<StackRow> Rows,
  int SuggestedKeeper,
  StackAction SuggestedAction)
{
  /// <summary>Files whose content is identical in every copy.</summary>
  public long SameFileCount { get; init; }

  /// <summary>Overrides the label of the identical-files row, for example 「檔案內容」 for a single-file stack.</summary>
  public string? SameLabel { get; init; }

  public RegenerableSummary? Regenerable { get; init; }

  public StackBlock Block { get; init; } = StackBlock.None;

  public StackStatus Status { get; init; } = StackStatus.Open;

  /// <summary>How the copies were confirmed to be the same, shown under 「判定依據」.</summary>
  public string Evidence { get; init; } = string.Empty;

  /// <summary>Optional note shown under the comparison, for example a likely curated copy.</summary>
  public string? Note { get; init; }

  /// <summary>Similar-looking files that are not part of the stack because their content differs.</summary>
  public IReadOnlyList<RelatedOutside> RelatedOutside { get; init; } = [];

  /// <summary>When the stack was found or last changed.</summary>
  public DateTimeOffset UpdatedAt { get; init; }

  /// <summary>Copy ids that appeared after the stack was last decided (ADR-006).</summary>
  public IReadOnlyList<string> NewCopyIds { get; init; } = [];

  /// <summary>Copy ids whose files changed after the stack was last decided (ADR-006).</summary>
  public IReadOnlyList<string> ChangedCopyIds { get; init; } = [];

  /// <summary>Copies whose indexed content evidence changed, even when their metadata did not.</summary>
  public IReadOnlyList<string> EvidenceChangedCopyIds { get; init; } = [];

  public StackCopy SuggestedKeeperCopy => Copies[SuggestedKeeper];

  public int IndexOf(string copyId)
  {
    for (var i = 0; i < Copies.Count; i++)
    {
      if (Copies[i].Id == copyId)
      {
        return i;
      }
    }
    return -1;
  }

  public IEnumerable<string> SourceIds => Copies.Select(c => c.SourceId).Distinct(StringComparer.Ordinal);
}
