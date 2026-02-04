using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public interface IMetadataExtractor
{
  Task<ExifMetadata?> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}
