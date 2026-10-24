using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface IScanResultStore
{
  Task SaveAsync(ScanResult result, string filePath, CancellationToken cancellationToken = default);
  Task<ScanResult> LoadAsync(string filePath, CancellationToken cancellationToken = default);
}
