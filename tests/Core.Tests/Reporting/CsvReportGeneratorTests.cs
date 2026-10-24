using FileWorkbench.Models;
using FileWorkbench.Reporting;
using Xunit;

namespace FileWorkbench.Tests.Reporting;

public class CsvReportGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly CsvReportGenerator _sut = new();

  public CsvReportGeneratorTests()
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
  public void Format_Returns_Csv()
  {
    Assert.Equal(ReportFormat.Csv, _sut.Format);
  }

  [Fact]
  public async Task GenerateAsync_Creates_Csv_File()
  {
    var result = CreateScanResult();

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.csv");
    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task GenerateAsync_Writes_Header_Row()
  {
    var result = CreateScanResult();

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.csv");
    var lines = await File.ReadAllLinesAsync(filePath);

    Assert.True(lines.Length >= 1);
    Assert.Equal("Group,FilePath,Size,Hash,CreatedUtc,ModifiedUtc", lines[0]);
  }

  [Fact]
  public async Task GenerateAsync_Writes_Data_Rows()
  {
    var now = DateTime.UtcNow;
    var result = new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = now,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 2,
      TotalSizeBytes = 2000,
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

    var filePath = Path.Combine(_tempDir, "scan-report.csv");
    var lines = await File.ReadAllLinesAsync(filePath);

    // Header + 2 data rows
    Assert.Equal(3, lines.Length);
    Assert.StartsWith("1,", lines[1]);
    Assert.StartsWith("1,", lines[2]);
  }

  [Fact]
  public async Task GenerateAsync_No_Duplicates_Only_Header()
  {
    var result = CreateScanResult();

    await _sut.GenerateAsync(result, _tempDir);

    var filePath = Path.Combine(_tempDir, "scan-report.csv");
    var lines = await File.ReadAllLinesAsync(filePath);

    Assert.Single(lines);
  }

  [Fact]
  public async Task GenerateAsync_Escapes_Commas_In_Paths()
  {
    var now = DateTime.UtcNow;
    var result = new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = now,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 2,
      TotalSizeBytes = 2000,
      DuplicateGroups =
      [
        new DuplicateGroup
        {
          Hash = "abc123",
          Files =
          [
            new ScannedFile
            {
              FilePath = @"C:\path,with,commas\a.txt", SizeBytes = 1000,
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

    var filePath = Path.Combine(_tempDir, "scan-report.csv");
    var content = await File.ReadAllTextAsync(filePath);

    Assert.Contains("\"C:\\path,with,commas\\a.txt\"", content);
  }

  private static ScanResult CreateScanResult()
  {
    return new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 0,
      TotalSizeBytes = 0,
      DuplicateGroups = []
    };
  }
}
