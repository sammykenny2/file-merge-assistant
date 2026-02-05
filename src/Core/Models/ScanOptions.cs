namespace FileMergeAssistant.Models;

public record ScanOptions
{
  public required string Path { get; init; }
  public bool Recursive { get; init; } = true;
  public string Algorithm { get; init; } = "sha256";
  public string OutputDirectory { get; init; } = "./fma-results/";
  public long MinSizeBytes { get; init; }
}
