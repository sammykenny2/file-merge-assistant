namespace FileMergeAssistant.Models;

public record FileNameGroup
{
  public required string FileName { get; init; }
  public required IReadOnlyList<ScannedFile> Files { get; init; }
  public long TotalSizeBytes => Files.Sum(f => f.SizeBytes);
}
