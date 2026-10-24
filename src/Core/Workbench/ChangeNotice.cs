namespace FileWorkbench.Workbench;

public enum NoticeKind
{
  /// <summary>An offline source came back, possibly under another drive letter (ADR-010).</summary>
  DriveReconnected,

  /// <summary>A converged stack has a new copy (ADR-006).</summary>
  NewCopy,

  /// <summary>A kept-all stack changed; only a reminder (ADR-006).</summary>
  KeptAllChanged,

  /// <summary>A stack that was still being compared can now be consolidated.</summary>
  StackReady,

  /// <summary>A stack lost copies outside the app and dissolved.</summary>
  CopyLost,

  /// <summary>A source gained files.</summary>
  SourceGrew,

  /// <summary>Recycled files still take space.</summary>
  RecycleReminder,
}

/// <summary>Something that changed since the user last worked in the app.</summary>
/// <param name="Id">Notice id.</param>
/// <param name="Kind">What changed.</param>
/// <param name="NeedsAttention">Listed under 「需要你看一下」 rather than 「只是讓你知道」.</param>
/// <param name="Title">Headline.</param>
/// <param name="Description">Details.</param>
public sealed record ChangeNotice(string Id, NoticeKind Kind, bool NeedsAttention, string Title, string Description)
{
  public string? StackId { get; init; }
  public string? SourceId { get; init; }

  /// <summary>What the user chose, once handled.</summary>
  public string? HandledText { get; init; }
}

/// <summary>Responses offered on a notice.</summary>
public enum NoticeResponse
{
  /// <summary>DriveReconnected: it is the same drive; update the index.</summary>
  SameDrive,

  /// <summary>DriveReconnected: treat it as a new source.</summary>
  NewSource,

  /// <summary>NewCopy: the new copy does not belong to the stack.</summary>
  NotInStack,

  /// <summary>KeptAllChanged: keep all again.</summary>
  KeepAllAgain,
}

/// <summary>How the number of open stacks changed since the last visit.</summary>
public sealed record OpenStackDelta(string Label, int Delta);

/// <summary>The 「一週後回來」 summary.</summary>
public sealed record SinceLastVisit(DateTimeOffset LastVisit, DateTimeOffset Now, int OpenAtLastVisit, IReadOnlyList<OpenStackDelta> Deltas, IReadOnlyList<ChangeNotice> Notices)
{
  public int OpenNow => OpenAtLastVisit + Deltas.Sum(d => d.Delta);
}
