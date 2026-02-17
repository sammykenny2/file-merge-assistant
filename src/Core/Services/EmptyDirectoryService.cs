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

    var removed = 0;
    var changed = true;

    while (changed)
    {
      changed = false;
      foreach (var dir in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
      {
        try
        {
          if (!Directory.EnumerateFileSystemEntries(dir).Any())
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
            removed++;
            changed = true;
          }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
          _logger.Warning(ex, "Could not process directory: {Directory}", dir);
        }
      }
    }

    return removed;
  }
}
