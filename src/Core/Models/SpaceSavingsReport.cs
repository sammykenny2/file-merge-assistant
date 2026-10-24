namespace FileWorkbench.Models;

public record SpaceSavingsReport
{
  public required long TotalScannedBytes { get; init; }
  public required long TotalWastedBytes { get; init; }
  public required int TotalDuplicateFiles { get; init; }
  public required int TotalDuplicateGroups { get; init; }
  public required IReadOnlyList<ExtensionBreakdown> ByExtension { get; init; }
  public required IReadOnlyList<DuplicateGroup> TopGroups { get; init; }
  public double WastedPercentage => TotalScannedBytes > 0
    ? (double)TotalWastedBytes / TotalScannedBytes * 100
    : 0;
}

public record ExtensionBreakdown
{
  public required string Extension { get; init; }
  public required int FileCount { get; init; }
  public required long TotalBytes { get; init; }
  public required long WastedBytes { get; init; }
  public required int DuplicateGroupCount { get; init; }
}
