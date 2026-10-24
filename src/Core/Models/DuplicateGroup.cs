namespace FileWorkbench.Models;

public record DuplicateGroup
{
  public required string Hash { get; init; }
  public required IReadOnlyList<ScannedFile> Files { get; init; }
  public long WastedBytes => Files.Count > 1
    ? (Files.Count - 1) * Files[0].SizeBytes
    : 0;
}
