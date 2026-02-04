using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public class DuplicateDetector : IDuplicateDetector
{
  public IReadOnlyList<DuplicateGroup> FindDuplicates(IReadOnlyList<ScannedFile> files)
  {
    return files
      .Where(f => f.Hash != null)
      .GroupBy(f => f.Hash!)
      .Where(g => g.Count() >= 2)
      .Select(g => new DuplicateGroup
      {
        Hash = g.Key,
        Files = g.OrderBy(f => f.CreatedUtc).ToList()
      })
      .OrderByDescending(g => g.WastedBytes)
      .ToList();
  }
}
