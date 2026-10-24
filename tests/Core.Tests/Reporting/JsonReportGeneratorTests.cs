using System.Text.Json;
using FileWorkbench.Models;
using FileWorkbench.Reporting;
using Xunit;

namespace FileWorkbench.Tests.Reporting;

public class JsonReportGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly JsonReportGenerator _sut = new();

  public JsonReportGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Format_Returns_Json()
  {
    Assert.Equal(ReportFormat.Json, _sut.Format);
  }

  [Fact]
  public async Task GenerateAsync_Creates_Json_File()
  {
    var result = CreateScanResult();

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.json");
    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task GenerateAsync_Writes_Valid_Json()
  {
    var result = CreateScanResult();

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.json");
    var json = await File.ReadAllTextAsync(filePath);
    var doc = JsonDocument.Parse(json);

    Assert.Equal(@"C:\test", doc.RootElement.GetProperty("scannedPath").GetString());
    Assert.Equal("sha256", doc.RootElement.GetProperty("hashAlgorithm").GetString());
    Assert.Equal(5, doc.RootElement.GetProperty("totalFilesScanned").GetInt32());
  }

  [Fact]
  public async Task GenerateAsync_Includes_Duplicate_Groups()
  {
    var now = DateTime.UtcNow;
    var result = new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = now,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 3,
      TotalSizeBytes = 3000,
      DuplicateGroups =
      [
        new DuplicateGroup
        {
          Hash = "abc123",
          Files =
          [
            new ScannedFile
            {
              FilePath = @"C:\test\a.txt", SizeBytes = 1000,
              CreatedUtc = now, ModifiedUtc = now, Hash = "abc123"
            },
            new ScannedFile
            {
              FilePath = @"C:\test\b.txt", SizeBytes = 1000,
              CreatedUtc = now, ModifiedUtc = now, Hash = "abc123"
            }
          ]
        }
      ]
    };

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.json");
    var json = await File.ReadAllTextAsync(filePath);
    var doc = JsonDocument.Parse(json);

    var groups = doc.RootElement.GetProperty("duplicateGroups");
    Assert.Equal(1, groups.GetArrayLength());
  }

  private static ScanResult CreateScanResult()
  {
    return new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 5,
      TotalSizeBytes = 5000,
      DuplicateGroups = []
    };
  }
}
