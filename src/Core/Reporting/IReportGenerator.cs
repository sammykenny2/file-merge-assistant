using FileWorkbench.Models;

namespace FileWorkbench.Reporting;

public interface IReportGenerator
{
  ReportFormat Format { get; }
  Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default);
}
