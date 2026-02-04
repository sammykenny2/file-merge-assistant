using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public record ScanProgress(int FilesScanned, int TotalFiles, string CurrentFile);

public interface IFileScanner
{
  IAsyncEnumerable<ScannedFile> ScanAsync(
    ScanOptions options,
    IProgress<ScanProgress>? progress = null,
    CancellationToken cancellationToken = default);

  int CountFiles(string path, bool recursive, long minSizeBytes = 0);
}
