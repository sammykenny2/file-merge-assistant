namespace FileWorkbench.Sources;

/// <summary>What the index knows about a source right now.</summary>
public enum SourceState
{
  /// <summary>Reachable and indexed; nothing queued.</summary>
  Online,

  /// <summary>A scan job for this source is running.</summary>
  Scanning,

  /// <summary>Waiting in the job queue for an update.</summary>
  Queued,

  /// <summary>Files changed since the last scan; an update is queued or recommended.</summary>
  NeedsUpdate,

  /// <summary>The last scan stopped without finishing (for example the app was closed).</summary>
  Interrupted,

  /// <summary>The volume is not connected. The last index stays readable.</summary>
  Offline,
}

/// <summary>A folder the user asked the app to index.</summary>
/// <param name="Id">Stable identity, independent of the drive letter.</param>
/// <param name="Name">Short name shown in the interface, for example 「開發中」.</param>
/// <param name="RootPath">Where the folder was last seen.</param>
/// <param name="State">Current state.</param>
/// <param name="IsProtected">Protected sources are never recycled, moved or overwritten; merges may add files.</param>
/// <param name="IndexedFiles">Files recorded one by one.</param>
/// <param name="RegenerableFiles">Files inside regenerable folders, recorded only as a total.</param>
/// <param name="CloudOnlyFiles">Cloud placeholders that were not downloaded for comparison.</param>
/// <param name="LastScanAt">When the last scan finished.</param>
/// <param name="LastScanErrors">Files the last scan could not read.</param>
/// <param name="VolumeDescription">Plain description of the disk, for example 「外接硬碟，磁碟區序號 7C1E-09AB」.</param>
/// <param name="LastSeenAt">For offline sources, when the volume was last connected.</param>
/// <param name="Remark">Optional extra line, for example 「上次掃描後多了 46 個檔」.</param>
public sealed record SourceInfo(
  string Id,
  string Name,
  string RootPath,
  SourceState State,
  bool IsProtected,
  long IndexedFiles,
  long RegenerableFiles,
  long CloudOnlyFiles,
  DateTimeOffset? LastScanAt,
  int LastScanErrors,
  string VolumeDescription,
  DateTimeOffset? LastSeenAt = null,
  string? Remark = null)
{
  /// <summary>Folder names that are only measured, never compared file by file.</summary>
  public IReadOnlyList<string> RegenerableNames { get; init; } = RegenerableFolders.Defaults;

  /// <summary>Exclusion rules for this source.</summary>
  public IReadOnlyList<string> Exclusions { get; init; } = [];

  /// <summary>Regenerable folders are measured instead of indexed file by file.</summary>
  public bool SkipsRegenerable { get; init; } = true;

  /// <summary>Serial number of the volume the source was added from, when known (ADR-010).</summary>
  public string? VolumeSerial { get; init; }

  public bool IsOffline => State == SourceState.Offline;
}

/// <summary>A folder the first-run screen suggests adding.</summary>
public sealed record SuggestedLocation(string Path, string Description, bool SelectedByDefault);
