using FileMergeAssistant.Models;
using FileMergeAssistant.Reporting;
using Xunit;

namespace FileMergeAssistant.Tests.Reporting;

public class PowerShellScriptGeneratorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly PowerShellScriptGenerator _sut = new();

  public PowerShellScriptGeneratorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-test-ps-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Format_Returns_PowerShell()
  {
    Assert.Equal(ReportFormat.PowerShell, _sut.Format);
  }

  [Fact]
  public async Task GenerateAsync_Creates_PS1_File()
  {
    var scanResult = CreateScanResult();

    await _sut.GenerateAsync(scanResult, _tempDir);

    var filePath = Path.Combine(_tempDir, "resolve-duplicates.ps1");
    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task GenerateAsync_Contains_Script_Header()
  {
    var scanResult = CreateScanResult();

    await _sut.GenerateAsync(scanResult, _tempDir);

    var content = await File.ReadAllTextAsync(Path.Combine(_tempDir, "resolve-duplicates.ps1"));
    Assert.Contains("#Requires -Version 5.1", content);
    Assert.Contains("File Merge Assistant", content);
    Assert.Contains("SupportsShouldProcess", content);
  }

  [Fact]
  public async Task GenerateAsync_Contains_File_Operations()
  {
    var scanResult = CreateScanResult();

    await _sut.GenerateAsync(scanResult, _tempDir);

    var content = await File.ReadAllTextAsync(Path.Combine(_tempDir, "resolve-duplicates.ps1"));
    Assert.Contains("Test-Path", content);
    Assert.Contains("KEEP:", content);
    Assert.Contains("deletedCount", content);
    Assert.Contains("Summary", content);
  }

  private static ScanResult CreateScanResult()
  {
    return new ScanResult
    {
      ScannedPath = @"C:\Photos",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 100,
      TotalSizeBytes = 1024 * 1024 * 50,
      DuplicateGroups =
      [
        new DuplicateGroup
        {
          Hash = "abcdef1234567890",
          Files =
          [
            new ScannedFile
            {
              FilePath = @"C:\Photos\original\photo.jpg",
              SizeBytes = 1024 * 1024,
              Hash = "abcdef1234567890",
              HashAlgorithm = "sha256",
              CreatedUtc = DateTime.UtcNow.AddDays(-10),
              ModifiedUtc = DateTime.UtcNow
            },
            new ScannedFile
            {
              FilePath = @"C:\Photos\backup\photo.jpg",
              SizeBytes = 1024 * 1024,
              Hash = "abcdef1234567890",
              HashAlgorithm = "sha256",
              CreatedUtc = DateTime.UtcNow.AddDays(-5),
              ModifiedUtc = DateTime.UtcNow
            }
          ]
        }
      ]
    };
  }
}
