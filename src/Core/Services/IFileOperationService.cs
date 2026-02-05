namespace FileMergeAssistant.Services;

public interface IFileOperationService
{
  Task DeleteAsync(string filePath, bool dryRun, CancellationToken cancellationToken = default);
  Task MoveAsync(string sourcePath, string destinationPath, bool dryRun, CancellationToken cancellationToken = default);
}
