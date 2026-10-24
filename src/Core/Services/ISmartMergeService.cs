using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface ISmartMergeService
{
  IReadOnlyList<FileNameGroup> FindSameNameFiles(IReadOnlyList<ScannedFile> files);
}
