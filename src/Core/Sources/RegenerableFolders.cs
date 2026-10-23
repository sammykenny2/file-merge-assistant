namespace FileMergeAssistant.Sources;

/// <summary>
/// Folders that build tools recreate. They are measured (size and file count) but never compared file by file,
/// and they follow the copy that contains them (ADR-005).
/// </summary>
public static class RegenerableFolders
{
  public static IReadOnlyList<string> Defaults { get; } =
  [
    "node_modules",
    "bin",
    "obj",
    ".vs",
    "dist",
    "__pycache__",
    ".gradle",
    ".venv",
    "target",
    ".next",
    "Library",
  ];

  /// <summary>
  /// Names that are common outside code projects too (a music <c>Library</c>, a <c>bin</c> of old files). They count as
  /// regenerable only next to a project marker; elsewhere their files are indexed and compared like any others.
  /// </summary>
  public static bool NeedsProject(string folderName) =>
    GenericNames.Contains(folderName, StringComparer.OrdinalIgnoreCase);

  private static readonly string[] GenericNames = ["bin", "obj", "dist", "target", "Library"];

  public static bool IsRegenerable(string folderName, IReadOnlyList<string>? names = null)
  {
    foreach (var name in names ?? Defaults)
    {
      if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }
    return false;
  }
}
