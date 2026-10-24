using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface IScanCacheService
{
  Task<ScanCache?> LoadCacheAsync(string scannedPath, string algorithm, CancellationToken cancellationToken = default);
  Task SaveCacheAsync(ScanCache cache, CancellationToken cancellationToken = default);
  bool IsEntryValid(CachedFileEntry entry, string filePath);
}
