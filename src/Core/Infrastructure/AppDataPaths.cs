namespace FileMergeAssistant.Infrastructure;

/// <summary>
/// Fixed per-user locations for the data the application writes, independent of the working directory (FR-010, ADR-012).
/// Set <c>FMA_DATA_DIR</c> to use another root, for example in tests or for a portable copy.
/// </summary>
public static class AppDataPaths
{
  public const string DataDirectoryVariable = "FMA_DATA_DIR";
  public const string ApplicationFolderName = "FileMergeAssistant";

  public static string Root => ResolveRoot(
    Environment.GetEnvironmentVariable(DataDirectoryVariable),
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

  public static string CacheDirectory => Path.Combine(Root, "cache");
  public static string RecycleBinDirectory => Path.Combine(Root, "recycle-bin");
  public static string LogDirectory => Path.Combine(Root, "logs");
  public static string ResultsDirectory => Path.Combine(Root, "results");
  public static string UpdatesDirectory => Path.Combine(Root, "updates");

  /// <summary>The local index of sources, files, fingerprints and decisions (ADR-008).</summary>
  public static string IndexPath => Path.Combine(Root, "index.db");

  public static string ResolveRoot(string? overrideRoot, string localApplicationData)
  {
    if (!string.IsNullOrWhiteSpace(overrideRoot))
    {
      PathText.RequireSupported(overrideRoot, nameof(overrideRoot));
      return Path.GetFullPath(overrideRoot);
    }
    if (string.IsNullOrWhiteSpace(localApplicationData))
      throw new InvalidOperationException($"The local application data folder is not available; set {DataDirectoryVariable}.");
    PathText.RequireSupported(localApplicationData, nameof(localApplicationData));
    return Path.Combine(localApplicationData, ApplicationFolderName);
  }
}
