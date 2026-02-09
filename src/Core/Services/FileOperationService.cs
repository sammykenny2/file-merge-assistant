using Serilog;

namespace FileMergeAssistant.Services;

public class FileOperationService : IFileOperationService
{
  private readonly ILogger _logger;

  public FileOperationService(ILogger logger)
  {
    _logger = logger;
  }

  public Task DeleteAsync(string filePath, bool dryRun, CancellationToken cancellationToken = default)
  {
    if (dryRun)
    {
      _logger.Information("[DRY RUN] Would delete: {FilePath}", filePath);
      return Task.CompletedTask;
    }

    try
    {
      _logger.Information("Deleting: {FilePath}", filePath);
      File.Delete(filePath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.Warning(ex, "Failed to delete file: {FilePath}", filePath);
    }
    return Task.CompletedTask;
  }

  public Task MoveAsync(string sourcePath, string destinationPath, bool dryRun, CancellationToken cancellationToken = default)
  {
    if (dryRun)
    {
      _logger.Information("[DRY RUN] Would move: {Source} -> {Destination}", sourcePath, destinationPath);
      return Task.CompletedTask;
    }

    var destDir = Path.GetDirectoryName(destinationPath);
    if (!string.IsNullOrEmpty(destDir))
      Directory.CreateDirectory(destDir);

    try
    {
      _logger.Information("Moving: {Source} -> {Destination}", sourcePath, destinationPath);
      File.Move(sourcePath, destinationPath, overwrite: false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.Warning(ex, "Failed to move file: {Source} -> {Destination}", sourcePath, destinationPath);
    }
    return Task.CompletedTask;
  }

  public Task CreateHardlinkAsync(string sourcePath, string linkPath, bool dryRun, CancellationToken cancellationToken = default)
  {
    if (dryRun)
    {
      _logger.Information("[DRY RUN] Would create hardlink: {Link} -> {Source}", linkPath, sourcePath);
      return Task.CompletedTask;
    }

    var destDir = Path.GetDirectoryName(linkPath);
    if (!string.IsNullOrEmpty(destDir))
      Directory.CreateDirectory(destDir);

    try
    {
      _logger.Information("Creating hardlink: {Link} -> {Source}", linkPath, sourcePath);
      if (File.Exists(linkPath))
        File.Delete(linkPath);
      File.CreateSymbolicLink(linkPath, sourcePath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.Warning(ex, "Failed to create hardlink: {Link} -> {Source}", linkPath, sourcePath);
    }
    return Task.CompletedTask;
  }

  public Task CreateSymlinkAsync(string targetPath, string linkPath, bool dryRun, CancellationToken cancellationToken = default)
  {
    if (dryRun)
    {
      _logger.Information("[DRY RUN] Would create symlink: {Link} -> {Target}", linkPath, targetPath);
      return Task.CompletedTask;
    }

    var destDir = Path.GetDirectoryName(linkPath);
    if (!string.IsNullOrEmpty(destDir))
      Directory.CreateDirectory(destDir);

    try
    {
      _logger.Information("Creating symlink: {Link} -> {Target}", linkPath, targetPath);
      if (File.Exists(linkPath))
        File.Delete(linkPath);
      File.CreateSymbolicLink(linkPath, targetPath);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.Warning(ex, "Failed to create symlink: {Link} -> {Target}", linkPath, targetPath);
    }
    return Task.CompletedTask;
  }
}
