using System.Text;
using FileMergeAssistant.Models;

namespace FileMergeAssistant.Reporting;

public class CsvReportGenerator : IReportGenerator
{
  public ReportFormat Format => ReportFormat.Csv;

  public async Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default)
  {
    var filePath = outputPath != null
      ? Path.Combine(outputPath, "scan-report.csv")
      : "scan-report.csv";

    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    var sb = new StringBuilder();
    sb.AppendLine("Group,FilePath,Size,Hash,CreatedUtc,ModifiedUtc");

    var groupIndex = 1;
    foreach (var group in result.DuplicateGroups)
    {
      foreach (var file in group.Files)
      {
        sb.Append(groupIndex).Append(',');
        sb.Append(EscapeCsv(file.FilePath)).Append(',');
        sb.Append(file.SizeBytes).Append(',');
        sb.Append(EscapeCsv(file.Hash ?? "")).Append(',');
        sb.Append(file.CreatedUtc.ToString("o")).Append(',');
        sb.AppendLine(file.ModifiedUtc.ToString("o"));
      }
      groupIndex++;
    }

    await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
  }

  private static string EscapeCsv(string value)
  {
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
      return $"\"{value.Replace("\"", "\"\"")}\"";
    return value;
  }
}
