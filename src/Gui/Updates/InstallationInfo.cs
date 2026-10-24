using System.IO;
using Microsoft.Win32;

namespace FileWorkbench.Gui.Updates;

/// <summary>Whether this process runs from the copy the setup program installed (installer/FileWorkbench.nsi).</summary>
internal static class InstallationInfo
{
  internal const string RegistryKey = @"Software\FileMergeAssistant";

  /// <summary>
  /// True when HKCU\Software\FileMergeAssistant\InstallDir names the folder whose app\ subfolder holds this process.
  /// Only such a copy can update itself by running a newer setup program; builds run from source cannot.
  /// </summary>
  public static bool IsInstalledCopy()
  {
    using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
    if (key?.GetValue("InstallDir") is not string installDir || string.IsNullOrWhiteSpace(installDir))
      return false;
    try
    {
      var expected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(installDir, "app")));
      var actual = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
      return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
    {
      return false;
    }
  }
}
