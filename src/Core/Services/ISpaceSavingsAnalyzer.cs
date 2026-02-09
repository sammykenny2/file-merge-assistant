using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public interface ISpaceSavingsAnalyzer
{
  SpaceSavingsReport Analyze(ScanResult scanResult, int topGroupsCount = 10);
}
