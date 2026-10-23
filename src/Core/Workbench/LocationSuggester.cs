using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Workbench;

/// <summary>
/// Folders worth offering on the first-run screen: code folders, the user's document folders, OneDrive and other
/// drives. Only checks that folders exist and counts git projects one or two levels down; nothing is opened or changed.
/// </summary>
public static class LocationSuggester
{
  private static readonly string[] CodeFolders = [@"source\repos", "source", "repos", "Projects", "projects", "code", "dev", @"Documents\GitHub"];

  public static IReadOnlyList<SuggestedLocation> Suggest(IVolumeInfoProvider volumes, string? userProfile = null, string? oneDrive = null)
  {
    ArgumentNullException.ThrowIfNull(volumes);
    userProfile ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    oneDrive ??= Environment.GetEnvironmentVariable("OneDrive");
    var result = new List<SuggestedLocation>();

    void Add(string? path, string description, bool selected)
    {
      if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
      {
        return;
      }
      var full = Path.GetFullPath(path).TrimEnd('\\');
      if (full.Length == 2 && full[1] == ':')
      {
        full += "\\";
      }
      if (result.Any(r => Same(r.Path, full) || Inside(full, r.Path) || Inside(r.Path, full)))
      {
        return;
      }
      result.Add(new SuggestedLocation(full, description, selected));
    }

    foreach (var folder in CodeFolders)
    {
      var path = Path.Combine(userProfile, folder);
      if (Directory.Exists(path))
      {
        var projects = CountGitProjects(path);
        Add(path, projects > 0 ? $"偵測到 {projects} 個 git 專案" : "程式碼資料夾", true);
      }
    }
    Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "文件", true);
    Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "圖片", false);
    Add(Path.Combine(userProfile, "Downloads"), "下載", false);
    Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面", false);
    Add(oneDrive, "OneDrive；只在雲端的檔不會被下載", false);

    var system = Path.GetPathRoot(Environment.SystemDirectory);
    foreach (var drive in DriveInfo.GetDrives())
    {
      if (drive.Name.Equals(system, StringComparison.OrdinalIgnoreCase) || drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
      {
        continue;
      }
      var info = volumes.Describe(drive.Name);
      var label = info?.Label is { } name ? $"{name}，" : string.Empty;
      var description = drive.DriveType == DriveType.Removable
        ? $"{label}外接硬碟；拔除後仍會保留索引"
        : $"{label}{info?.Describe() ?? "內建硬碟"}";
      Add(drive.Name, description, false);
    }
    return result;
  }

  private static int CountGitProjects(string path)
  {
    var count = 0;
    try
    {
      foreach (var child in Directory.EnumerateDirectories(path))
      {
        if (Directory.Exists(Path.Combine(child, ".git")))
        {
          count++;
          continue;
        }
        foreach (var grandchild in Directory.EnumerateDirectories(child).Take(50))
        {
          if (Directory.Exists(Path.Combine(grandchild, ".git")))
          {
            count++;
          }
        }
      }
    }
    catch (Exception e) when (e is UnauthorizedAccessException or IOException)
    {
      // A partial count is fine for a description.
    }
    return count;
  }

  internal static bool Same(string a, string b) => string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

  /// <summary>True when <paramref name="child"/> is strictly inside <paramref name="parent"/>.</summary>
  internal static bool Inside(string child, string parent)
  {
    var p = parent.TrimEnd('\\') + "\\";
    return child.Length > p.Length - 1 && !Same(child, parent) && child.StartsWith(p, StringComparison.OrdinalIgnoreCase);
  }
}
