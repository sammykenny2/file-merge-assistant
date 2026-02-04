using FileMergeAssistant.Models;

namespace FileMergeAssistant.Reporting;

public interface IReportGenerator
{
  ReportFormat Format { get; }
  Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default);
}
