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
