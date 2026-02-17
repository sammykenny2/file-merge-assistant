using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public class PreviewStatisticsService : IPreviewStatisticsService
{
  private readonly ISpaceSavingsAnalyzer _analyzer;

  public PreviewStatisticsService(ISpaceSavingsAnalyzer analyzer)
  {
    _analyzer = analyzer;
  }

  public PreviewStatistics Generate(ScanResult scanResult, int topGroupsCount = 10, int topDirectoriesCount = 10)
  {
    var savingsReport = _analyzer.Analyze(scanResult, topGroupsCount);

    var duplicateFileCount = scanResult.DuplicateFileCount;
    var filesToRemove = duplicateFileCount;
    var uniqueFilesToKeep = scanResult.TotalFilesScanned - filesToRemove;

    var byDirectory = BuildDirectoryBreakdown(scanResult, topDirectoriesCount);

    return new PreviewStatistics
    {
      TotalFilesScanned = scanResult.TotalFilesScanned,
      TotalSizeBytes = scanResult.TotalSizeBytes,
      DuplicateGroupCount = scanResult.DuplicateGroups.Count,
      DuplicateFileCount = duplicateFileCount,
      UniqueFilesToKeep = uniqueFilesToKeep,
      FilesToRemove = filesToRemove,
      WastedBytes = scanResult.WastedSpaceBytes,
      SizeAfterCleanup = scanResult.TotalSizeBytes - scanResult.WastedSpaceBytes,
      ByExtension = savingsReport.ByExtension,
      ByDirectory = byDirectory,
      TopGroups = savingsReport.TopGroups
    };
  }

  private static IReadOnlyList<DirectoryBreakdown> BuildDirectoryBreakdown(
    ScanResult scanResult, int topCount)
  {
    return scanResult.DuplicateGroups
      .SelectMany(g => g.Files.Skip(1).Select(f => new { File = f, Group = g }))
      .GroupBy(x => Path.GetDirectoryName(x.File.FilePath) ?? "(root)")
      .Select(g =>
      {
        var groupHashes = g.Select(x => x.Group.Hash).Distinct().Count();
        return new DirectoryBreakdown
        {
          Directory = g.Key,
          DuplicateFileCount = g.Count(),
          WastedBytes = g.Sum(x => x.File.SizeBytes),
          DuplicateGroupCount = groupHashes
        };
      })
      .OrderByDescending(d => d.WastedBytes)
      .Take(topCount)
      .ToList();
  }
}
