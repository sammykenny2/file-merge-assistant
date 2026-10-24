namespace FileWorkbench.Models;

public record RecycleBinEntry
{
  public required string OriginalPath { get; init; }
  public required string RecycleBinPath { get; init; }
  public required DateTime DeletedUtc { get; init; }
  public required long SizeBytes { get; init; }
  public string? Hash { get; init; }
}

public record RecycleBinManifest
{
  public List<RecycleBinEntry> Entries { get; init; } = [];
  public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
}
