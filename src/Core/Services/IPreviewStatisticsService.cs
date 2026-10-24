using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface IPreviewStatisticsService
{
  PreviewStatistics Generate(ScanResult scanResult, int topGroupsCount = 10, int topDirectoriesCount = 10);
}
