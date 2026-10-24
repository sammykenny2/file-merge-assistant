using System.Text.Json.Serialization;

namespace FileWorkbench.Models;

public record ScanCache
{
  public required string ScannedPath { get; init; }
  public required DateTime CacheDateUtc { get; init; }
  public required string HashAlgorithm { get; init; }
  public required Dictionary<string, CachedFileEntry> Entries { get; init; }
}

public record CachedFileEntry
{
  public required string Hash { get; init; }
  public required long SizeBytes { get; init; }
  public required DateTime ModifiedUtc { get; init; }

  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ExifMetadata? Metadata { get; init; }
}
