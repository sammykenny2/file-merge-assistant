using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public interface ISmartMergeService
{
  IReadOnlyList<FileNameGroup> FindSameNameFiles(IReadOnlyList<ScannedFile> files);
}
