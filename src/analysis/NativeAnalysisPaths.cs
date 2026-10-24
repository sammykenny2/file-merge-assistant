using System.Runtime.InteropServices;

namespace FileMergeAssistant.Analysis;

/// <summary>Only executable-relative, package-owned locations; never searches source folders, CWD or PATH.</summary>
public static class NativeAnalysisPaths
{
  public const string PackageMarker = ".installed-by-file-merge-assistant-setup";
  public static string LibraryName => OperatingSystem.IsWindows() ? "fma_czkawka_ffi.dll"
    : OperatingSystem.IsMacOS() ? "libfma_czkawka_ffi.dylib" : "libfma_czkawka_ffi.so";

  public static string Resolve(string executableDirectory)
  {
    if (!Path.IsPathFullyQualified(executableDirectory)) { throw new ArgumentException("The executable directory must be absolute.", nameof(executableDirectory)); }
    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(executableDirectory));
    var local = Path.Combine(root, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    // A present local module is authoritative, even if invalid. There is no fallback after a load failure.
    if (File.Exists(Path.Combine(local, LibraryName))) { return local; }
    if (string.Equals(Path.GetFileName(root), "cli", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
      && File.Exists(Path.Combine(root, PackageMarker)))
    {
      var app = Path.Combine(Path.GetDirectoryName(root)!, "app");
      if (File.Exists(Path.Combine(app, PackageMarker)))
      {
        return Path.Combine(app, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
      }
    }
    return local;
  }
}
