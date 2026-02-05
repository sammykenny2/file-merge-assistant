using System.Text.Json;
using System.Text.Json.Serialization;
using FileMergeAssistant.Models;

namespace FileMergeAssistant.Reporting;

public class JsonReportGenerator : IReportGenerator
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
  };

  public ReportFormat Format => ReportFormat.Json;

  public async Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default)
  {
    var filePath = outputPath != null
      ? Path.Combine(outputPath, "scan-report.json")
      : "scan-report.json";

    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    await using var stream = File.Create(filePath);
    await JsonSerializer.SerializeAsync(stream, result, Options, cancellationToken);
  }
}
