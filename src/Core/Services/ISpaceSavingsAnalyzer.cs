using FileWorkbench.Models;

namespace FileWorkbench.Services;

public interface ISpaceSavingsAnalyzer
{
  SpaceSavingsReport Analyze(ScanResult scanResult, int topGroupsCount = 10);
}
