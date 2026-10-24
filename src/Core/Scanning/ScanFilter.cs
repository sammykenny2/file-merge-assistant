using System.IO.Enumeration;
using FileWorkbench.Sources;

namespace FileWorkbench.Scanning;

/// <summary>What a folder entry means to a scan.</summary>
public enum EntryKind
{
  /// <summary>Indexed one by one.</summary>
  Index,

  /// <summary>Not indexed at all (links, exclusions, the app's own folders).</summary>
  Skip,

  /// <summary>A regenerable folder: measured, not indexed file by file.</summary>
  Regenerable,

  /// <summary>A <c>.git</c> folder: marks its parent as a code project, not indexed.</summary>
  GitFolder,
}

/// <summary>
/// The one set of rules for which entries a source's index holds. The scanner and every later comparison against the
/// disk (re-check before running the plan) use it, so they always agree on what "the same folder" means.
/// </summary>
public sealed class ScanFilter
{
  /// <summary>Name of the per-volume recycle area folder; never indexed.</summary>
  public const string RecycleFolderName = ".fma-recycle";

  /// <summary>Suffix of files the executor is still writing; never indexed.</summary>
  public const string PartialSuffix = ".fma-partial";

  private readonly ScanSource _source;
  private readonly IReadOnlyList<string> _excludedRoots;

  /// <param name="source">The source and its rules.</param>
  /// <param name="excludedRoots">Absolute folders never indexed, such as the app's data folder with its recycle area.</param>
  public ScanFilter(ScanSource source, IReadOnlyList<string> excludedRoots)
  {
    _source = source;
    _excludedRoots = excludedRoots.Select(r => Path.GetFullPath(r).TrimEnd('\\')).ToList();
  }

  /// <summary>
  /// Junctions and symbolic links are never followed: they would index the same files twice. Other reparse points (cloud
  /// files, deduplicated files) are ordinary files to the user and are indexed.
  /// </summary>
  public static bool IsLink(FileSystemInfo entry)
  {
    ArgumentNullException.ThrowIfNull(entry);
    if ((entry.Attributes & FileAttributes.ReparsePoint) == 0)
    {
      return false;
    }
    try
    {
      return entry.LinkTarget is not null;
    }
    catch (IOException)
    {
      return true;
    }
    catch (UnauthorizedAccessException)
    {
      return true;
    }
  }

  /// <param name="entry">The entry.</param>
  /// <param name="sourceRelative">Its path relative to the source root.</param>
  public EntryKind Classify(FileSystemInfo entry, string sourceRelative)
  {
    ArgumentNullException.ThrowIfNull(entry);
    if (IsLink(entry))
    {
      return EntryKind.Skip;
    }
    if (entry is DirectoryInfo directory)
    {
      if (directory.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
      {
        return EntryKind.GitFolder;
      }
      if (directory.Name.Equals(RecycleFolderName, StringComparison.OrdinalIgnoreCase) || IsExcludedRoot(directory.FullName))
      {
        return EntryKind.Skip;
      }
      if (_source.SkipRegenerable && RegenerableFolders.IsRegenerable(directory.Name, _source.RegenerableNames)
        && (!RegenerableFolders.NeedsProject(directory.Name) || NextToProjectMarker(directory)))
      {
        return EntryKind.Regenerable;
      }
      return SourceScanner.IsExcluded(sourceRelative, directory.Name, _source.Exclusions) ? EntryKind.Skip : EntryKind.Index;
    }
    if (entry.Name.EndsWith(PartialSuffix, StringComparison.OrdinalIgnoreCase) || entry.Name.StartsWith(PathIdentity.ProbePrefix, StringComparison.Ordinal))
    {
      return EntryKind.Skip;
    }
    return SourceScanner.IsExcluded(sourceRelative, entry.Name, _source.Exclusions) ? EntryKind.Skip : EntryKind.Index;
  }

  /// <summary>
  /// Lists the indexable files under <paramref name="folder"/> (a folder inside the source), with paths relative to that
  /// folder using forward slashes, exactly as a scan would record them. Throws when a folder cannot be read.
  /// </summary>
  public IEnumerable<(string Relative, long Size, long ModifiedTicks)> Walk(string folder, string folderSourceRelative)
  {
    var pending = new Stack<string>();
    pending.Push(string.Empty);
    while (pending.Count > 0)
    {
      var relative = pending.Pop();
      var full = relative.Length == 0 ? folder : Path.Combine(folder, relative);
      foreach (var entry in new DirectoryInfo(full).EnumerateFileSystemInfos().ToList())
      {
        PathText.RequireReadable(entry.FullName);
        var child = relative.Length == 0 ? entry.Name : relative + "\\" + entry.Name;
        var kind = Classify(entry, folderSourceRelative.Length == 0 ? child : folderSourceRelative + "\\" + child);
        if (kind != EntryKind.Index)
        {
          continue;
        }
        if (entry is DirectoryInfo)
        {
          pending.Push(child);
          continue;
        }
        var file = (FileInfo)entry;
        yield return (child.Replace('\\', '/'), file.Length, file.LastWriteTimeUtc.Ticks);
      }
    }
  }

  private bool IsExcludedRoot(string fullPath)
  {
    var path = fullPath.TrimEnd('\\');
    return _excludedRoots.Any(r => path.Equals(r, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>True when a project marker file (package.json, *.csproj, ...) names this file.</summary>
  /// <summary>True when the folder's parent holds a project marker file (package.json, *.csproj, Cargo.toml, ...).</summary>
  private static bool NextToProjectMarker(DirectoryInfo directory)
  {
    try
    {
      return directory.Parent?.EnumerateFiles().Any(f => ProjectMarker(f.Name) is not null) == true;
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      // Unknown: index it, which is the safe side.
      return false;
    }
  }

  public static string? ProjectMarker(string fileName) =>
    SourceScanner.ProjectMarkers.FirstOrDefault(m => FileSystemName.MatchesSimpleExpression(m, fileName, ignoreCase: true));
}
