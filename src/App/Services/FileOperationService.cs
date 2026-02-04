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

    _logger.Information("Deleting: {FilePath}", filePath);
    File.Delete(filePath);
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

    _logger.Information("Moving: {Source} -> {Destination}", sourcePath, destinationPath);
    File.Move(sourcePath, destinationPath, overwrite: false);
    return Task.CompletedTask;
  }
}
