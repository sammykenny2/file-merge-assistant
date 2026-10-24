using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface IMetadataExtractor
{
  Task<ExifMetadata?> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
