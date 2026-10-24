using FileWorkbench.Analysis;

namespace FileWorkbench.AnalysisVerifier;

internal static class VerificationRoots
{
  public static (string Work, string? SecondWork) Validate(string work, string? secondWork)
  {
    work = RequireNew(work);
    if (secondWork is null) { return (work, null); }
    secondWork = RequireNew(secondWork);
    var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    if (string.Equals(work, secondWork, comparison) || work.StartsWith(secondWork + Path.DirectorySeparatorChar, comparison)
      || secondWork.StartsWith(work + Path.DirectorySeparatorChar, comparison))
    { throw new ArgumentException("Choose distinct, non-overlapping verification directories."); }
    return (work, secondWork);
  }

  public static string RequireNew(string path)
  {
    path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    PathText.RequireSupported(path);
    if (Directory.Exists(path) || File.Exists(path)) { throw new ArgumentException("Verification directories must be new; existing content is never overwritten."); }
    if (Path.GetDirectoryName(path) is not { } parent || !Directory.Exists(parent))
    { throw new ArgumentException("The verification parent directory must already exist."); }
    return path;
  }
}
