using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public class SmartMergeService : ISmartMergeService
{
  public IReadOnlyList<FileNameGroup> FindSameNameFiles(IReadOnlyList<ScannedFile> files)
  {
    return files
      .GroupBy(f => Path.GetFileName(f.FilePath), StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() >= 2)
      .Where(g => g.Select(f => f.Hash).Distinct().Count() > 1)
      .Select(g => new FileNameGroup
      {
        FileName = g.Key,
        Files = g.OrderBy(f => f.CreatedUtc).ToList()
      })
      .OrderByDescending(g => g.TotalSizeBytes)
      .ToList();
  }
}
