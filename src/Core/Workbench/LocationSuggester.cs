using System.Globalization;
using FileMergeAssistant.Localization;
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
        Add(path, projects > 0 ? GitProjects(projects) : AppText.Get("scan.suggest.codeFolder"), true);
      }
    }
    Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), AppText.Get("scan.suggest.documents"), true);
    Add(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), AppText.Get("scan.suggest.pictures"), false);
    Add(Path.Combine(userProfile, "Downloads"), AppText.Get("scan.suggest.downloads"), false);
    Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppText.Get("scan.suggest.desktop"), false);
    Add(oneDrive, AppText.Get("scan.suggest.oneDrive"), false);

    var system = Path.GetPathRoot(Environment.SystemDirectory);
    foreach (var drive in DriveInfo.GetDrives())
    {
      if (drive.Name.Equals(system, StringComparison.OrdinalIgnoreCase) || drive.DriveType is not (DriveType.Fixed or DriveType.Removable) || !drive.IsReady)
      {
        continue;
      }
      var info = volumes.Describe(drive.Name);
      var label = info?.Label;
      string description;
      if (drive.DriveType == DriveType.Removable)
      {
        description = label is null ? AppText.Get("scan.suggest.removable") : AppText.Get("scan.suggest.removableLabel", ("label", label));
      }
      else
      {
        var kind = info?.Describe() ?? AppText.Get("scan.volume.fixed");
        description = label is null ? kind : AppText.Get("scan.suggest.labelled", ("label", label), ("drive", kind));
      }
      Add(drive.Name, description, false);
    }
    return result;
  }

  /// <summary>
  /// 「偵測到 23 個 git 專案」. Both languages keep the word "git": the first-run screen protects the folder whose
  /// description names git.
  /// </summary>
  internal static string GitProjects(int count) =>
    AppText.Plural("scan.suggest.gitProjects", count, ("count", count.ToString(CultureInfo.InvariantCulture)));

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

  internal static bool Same(string a, string b) => WindowsSourcePathService.Instance.Same(a, b);

  /// <summary>True when <paramref name="child"/> is strictly inside <paramref name="parent"/>.</summary>
  internal static bool Inside(string child, string parent)
  {
    return WindowsSourcePathService.Instance.Inside(child, parent);
  }
}
