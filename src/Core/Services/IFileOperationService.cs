namespace FileWorkbench.Services;

public interface IFileOperationService
{
  Task DeleteAsync(string filePath, bool dryRun, CancellationToken cancellationToken = default);
  Task MoveAsync(string sourcePath, string destinationPath, bool dryRun, CancellationToken cancellationToken = default);
  Task CreateHardlinkAsync(string sourcePath, string linkPath, bool dryRun, CancellationToken cancellationToken = default);
  Task CreateSymlinkAsync(string targetPath, string linkPath, bool dryRun, CancellationToken cancellationToken = default);
}
