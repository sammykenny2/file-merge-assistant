using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public interface IDuplicateDetector
{
  IReadOnlyList<DuplicateGroup> FindDuplicates(IReadOnlyList<ScannedFile> files);
}
