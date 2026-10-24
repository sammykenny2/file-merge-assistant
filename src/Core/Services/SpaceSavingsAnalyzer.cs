using FileWorkbench.Models;

namespace FileWorkbench.Services;

public class SpaceSavingsAnalyzer : ISpaceSavingsAnalyzer
{
  public SpaceSavingsReport Analyze(ScanResult scanResult, int topGroupsCount = 10)
  {
    var byExtension = scanResult.DuplicateGroups
      .SelectMany(g => g.Files.Select(f => new { File = f, Group = g }))
      .GroupBy(x => Path.GetExtension(x.File.FilePath).ToLowerInvariant())
      .Select(g =>
      {
        var groupHashes = g.Select(x => x.Group.Hash).Distinct().ToList();
        var groups = scanResult.DuplicateGroups.Where(dg => groupHashes.Contains(dg.Hash)).ToList();
        return new ExtensionBreakdown
        {
          Extension = string.IsNullOrEmpty(g.Key) ? "(no extension)" : g.Key,
          FileCount = g.Count(),
          TotalBytes = g.Sum(x => x.File.SizeBytes),
          WastedBytes = groups.Sum(dg => dg.WastedBytes),
          DuplicateGroupCount = groups.Count
        };
      })
      .OrderByDescending(e => e.WastedBytes)
      .ToList();

    var topGroups = scanResult.DuplicateGroups
      .OrderByDescending(g => g.WastedBytes)
      .Take(topGroupsCount)
      .ToList();

    return new SpaceSavingsReport
    {
      TotalScannedBytes = scanResult.TotalSizeBytes,
      TotalWastedBytes = scanResult.WastedSpaceBytes,
      TotalDuplicateFiles = scanResult.DuplicateFileCount,
      TotalDuplicateGroups = scanResult.DuplicateGroups.Count,
      ByExtension = byExtension,
      TopGroups = topGroups
    };
  }
}
