using System.Globalization;
using System.IO.Hashing;
using System.Text;
using FileWorkbench.Scanning;

namespace FileWorkbench.Stacks;

/// <summary>
/// Everything inside a copy on disk, including what scans skip (<c>.git</c>, regenerable folders, excluded files): how
/// many entries there are and a fingerprint of every file's relative path, size and time and every link's name. Taken
/// when the user decides and again right before the copy is recycled; any difference means the copy changed in a way
/// the comparison could not show, so it is not recycled.
/// </summary>
public static class CopySnapshot
{
  /// <summary>Prefix of a snapshot that found something changed at or after the decision.</summary>
  public const string ChangedPrefix = "changed:";

  /// <param name="path">The copy: a folder or a single file.</param>
  /// <param name="skip">Paths that are not the user's (the app's own folders and partial files).</param>
  /// <param name="changedSince">
  /// When the user decided. A file or folder written between it and now changed while the snapshot was being taken, so
  /// the snapshot is <see cref="ChangedPrefix"/> and its relative path instead of a baseline (a folder's time moves when
  /// entries are added, removed or renamed in it). A time later than now (a camera with a wrong clock) is not a change:
  /// it was there before, and would otherwise block the copy for good.
  /// </param>
  /// <exception cref="IOException">A folder could not be read.</exception>
  /// <exception cref="UnauthorizedAccessException">A folder could not be read.</exception>
  public static string Of(string path, Func<string, bool> skip, DateTime? changedSince = null)
  {
    ArgumentNullException.ThrowIfNull(path);
    ArgumentNullException.ThrowIfNull(skip);
    PathText.RequireReadable(path);
    bool Late(FileSystemInfo entry) =>
      changedSince is { } since && entry.LastWriteTimeUtc >= since && entry.LastWriteTimeUtc <= DateTime.UtcNow.AddSeconds(5);
    var lines = new List<string>();
    var file = new FileInfo(path);
    if (file.Exists)
    {
      if (Late(file))
      {
        return ChangedPrefix + file.Name;
      }
      lines.Add(Line(file.Name, file));
    }
    else if (Directory.Exists(path))
    {
      var top = new DirectoryInfo(path);
      if (Late(top))
      {
        return ChangedPrefix + top.Name;
      }
      var pending = new Stack<(string Relative, DirectoryInfo Folder)>();
      pending.Push((string.Empty, top));
      while (pending.Count > 0)
      {
        var (relative, folder) = pending.Pop();
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
          PathText.RequireReadable(entry.FullName);
          var child = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
          if (skip(entry.FullName))
          {
            continue;
          }
          if (Late(entry))
          {
            return ChangedPrefix + child;
          }
          if (ScanFilter.IsLink(entry))
          {
            lines.Add(child + "|link");
          }
          else if (entry is DirectoryInfo directory)
          {
            lines.Add(child + "/");
            pending.Push((child, directory));
          }
          else
          {
            lines.Add(Line(child, (FileInfo)entry));
          }
        }
      }
    }
    else
    {
      return "missing";
    }

    lines.Sort(StringComparer.Ordinal);
    var hasher = new XxHash64();
    foreach (var line in lines)
    {
      hasher.Append(Encoding.UTF8.GetBytes(line + "\n"));
    }
    return string.Create(CultureInfo.InvariantCulture, $"{lines.Count}:{Convert.ToHexStringLower(hasher.GetCurrentHash())}");
  }

  private static string Line(string relative, FileInfo file) =>
    string.Create(CultureInfo.InvariantCulture, $"{relative}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
}
