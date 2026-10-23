using System.Globalization;
using System.IO.Hashing;
using System.Text;
using FileMergeAssistant.Scanning;

namespace FileMergeAssistant.Stacks;

/// <summary>
/// Everything inside a copy on disk, including what scans skip (<c>.git</c>, regenerable folders, excluded files): how
/// many entries there are and a fingerprint of every file's relative path, size and time and every link's name. Taken
/// when the user decides and again right before the copy is recycled; any difference means the copy changed in a way
/// the comparison could not show, so it is not recycled.
/// </summary>
public static class CopySnapshot
{
  /// <param name="path">The copy: a folder or a single file.</param>
  /// <param name="skip">Paths that are not the user's (the app's own folders and partial files).</param>
  /// <exception cref="IOException">A folder could not be read.</exception>
  /// <exception cref="UnauthorizedAccessException">A folder could not be read.</exception>
  public static string Of(string path, Func<string, bool> skip)
  {
    ArgumentNullException.ThrowIfNull(path);
    ArgumentNullException.ThrowIfNull(skip);
    var lines = new List<string>();
    var file = new FileInfo(path);
    if (file.Exists)
    {
      lines.Add(Line(file.Name, file));
    }
    else if (Directory.Exists(path))
    {
      var pending = new Stack<(string Relative, DirectoryInfo Folder)>();
      pending.Push((string.Empty, new DirectoryInfo(path)));
      while (pending.Count > 0)
      {
        var (relative, folder) = pending.Pop();
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
          var child = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
          if (skip(entry.FullName))
          {
            continue;
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
