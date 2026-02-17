namespace FileMergeAssistant.Models;

public record ScanOptions
{
  public required string Path { get; init; }
  public IReadOnlyList<string> AdditionalPaths { get; init; } = [];
  public bool Recursive { get; init; } = true;
  public string Algorithm { get; init; } = "sha256";
  public string MatchMode { get; init; } = "content";
  public string OutputDirectory { get; init; } = "./fma-results/";
  public long MinSizeBytes { get; init; }
  public long MaxSizeBytes { get; init; }
  public IReadOnlyList<string> ExcludePatterns { get; init; } = [];
  public IReadOnlyList<string> ExcludeExtensions { get; init; } = [];
  public IReadOnlyList<string> ProtectedPaths { get; init; } = [];
  public bool IncrementalScan { get; init; }
  public bool SkipHardlinks { get; init; }
  public bool ByteByByteVerification { get; init; }
  public bool CleanupEmptyDirectories { get; init; }

  public IEnumerable<string> AllPaths
  {
    get
    {
      yield return Path;
      foreach (var p in AdditionalPaths)
        yield return p;
    }
  }
}
