using Serilog;

namespace FileMergeAssistant.Services;

public interface IEmptyDirectoryService
{
  int Cleanup(string rootPath, bool dryRun = false);
}

public class EmptyDirectoryService : IEmptyDirectoryService
{
  private readonly ILogger _logger;

  public EmptyDirectoryService(ILogger logger)
  {
    _logger = logger;
  }

  public int Cleanup(string rootPath, bool dryRun = false)
  {
    if (!Directory.Exists(rootPath))
      return 0;

    // Skip reparse points so junctions and directory symlinks are neither followed outside
    // rootPath nor deleted; a parent that contains one is not empty and stays.
    var options = new EnumerationOptions
    {
      RecurseSubdirectories = true,
      AttributesToSkip = FileAttributes.ReparsePoint,
      IgnoreInaccessible = true
    };

    // A child path is always longer than its parent, so this visits children first.
    // Tracking removals lets a dry run treat parents of removable directories as empty too.
    var directories = Directory.EnumerateDirectories(rootPath, "*", options)
      .OrderByDescending(d => d.Length)
      .ToList();
    var removedDirs = new HashSet<string>(StringComparer.Ordinal);

    foreach (var dir in directories)
    {
      try
      {
        if (Directory.EnumerateFileSystemEntries(dir).All(removedDirs.Contains))
        {
          if (dryRun)
          {
            _logger.Information("[DRY RUN] Would remove empty directory: {Directory}", dir);
          }
          else
          {
            _logger.Information("Removing empty directory: {Directory}", dir);
            Directory.Delete(dir);
          }
          removedDirs.Add(dir);
        }
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
      {
        _logger.Warning(ex, "Could not process directory: {Directory}", dir);
      }
    }

    return removedDirs.Count;
  }
}
