namespace FileMergeAssistant.Models;

public record PreviewStatistics
{
  public required int TotalFilesScanned { get; init; }
  public required long TotalSizeBytes { get; init; }
  public required int DuplicateGroupCount { get; init; }
  public required int DuplicateFileCount { get; init; }
  public required int UniqueFilesToKeep { get; init; }
  public required int FilesToRemove { get; init; }
  public required long WastedBytes { get; init; }
  public required long SizeAfterCleanup { get; init; }
  public required IReadOnlyList<ExtensionBreakdown> ByExtension { get; init; }
  public required IReadOnlyList<DirectoryBreakdown> ByDirectory { get; init; }
  public required IReadOnlyList<DuplicateGroup> TopGroups { get; init; }

  public double WastedPercentage => TotalSizeBytes > 0
    ? (double)WastedBytes / TotalSizeBytes * 100
    : 0;

  public double CleanupPercentage => TotalSizeBytes > 0
    ? (double)SizeAfterCleanup / TotalSizeBytes * 100
    : 100;
}

public record DirectoryBreakdown
{
  public required string Directory { get; init; }
  public required int DuplicateFileCount { get; init; }
  public required long WastedBytes { get; init; }
  public required int DuplicateGroupCount { get; init; }
}
