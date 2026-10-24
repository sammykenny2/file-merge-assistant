using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface IRecycleBinService
{
  Task<string> RecycleAsync(string filePath, CancellationToken cancellationToken = default);
  Task RestoreAsync(string originalPath, CancellationToken cancellationToken = default);
  Task RestoreAllAsync(CancellationToken cancellationToken = default);
  Task<IReadOnlyList<RecycleBinEntry>> ListEntriesAsync(CancellationToken cancellationToken = default);
  Task PurgeAsync(CancellationToken cancellationToken = default);
  Task PurgeOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default);
}
