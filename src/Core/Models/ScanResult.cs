namespace FileMergeAssistant.Models;

public record ScanResult
{
  public required string ScannedPath { get; init; }
  public required DateTime ScanDateUtc { get; init; }
  public required string HashAlgorithm { get; init; }
  public required int TotalFilesScanned { get; init; }
  public required long TotalSizeBytes { get; init; }
  public required IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; }
  public int DuplicateFileCount => DuplicateGroups.Sum(g => g.Files.Count - 1);
  public long WastedSpaceBytes => DuplicateGroups.Sum(g => g.WastedBytes);
}
