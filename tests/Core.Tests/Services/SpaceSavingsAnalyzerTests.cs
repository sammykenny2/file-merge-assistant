using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class SpaceSavingsAnalyzerTests
{
  private readonly SpaceSavingsAnalyzer _sut = new();

  [Fact]
  public void Analyze_Calculates_Wasted_Percentage()
  {
    var scanResult = CreateScanResult(totalSize: 10000, groups: [
      CreateGroup(".jpg", "hash1", 2, 1000),
      CreateGroup(".png", "hash2", 3, 500)
    ]);

    var report = _sut.Analyze(scanResult);

    Assert.True(report.WastedPercentage > 0);
    Assert.Equal(10000, report.TotalScannedBytes);
  }

  [Fact]
  public void Analyze_Groups_By_Extension()
  {
    var scanResult = CreateScanResult(totalSize: 5000, groups: [
      CreateGroup(".jpg", "hash1", 2, 1000),
      CreateGroup(".jpg", "hash2", 2, 500),
      CreateGroup(".png", "hash3", 2, 2000)
    ]);

    var report = _sut.Analyze(scanResult);

    Assert.True(report.ByExtension.Count >= 2);
  }

  [Fact]
  public void Analyze_Returns_TopGroups()
  {
    var groups = Enumerable.Range(0, 20)
      .Select(i => CreateGroup(".txt", $"hash{i}", 2, i * 100))
      .ToList();

    var scanResult = CreateScanResult(totalSize: 100000, groups: groups);

    var report = _sut.Analyze(scanResult, topGroupsCount: 5);

    Assert.Equal(5, report.TopGroups.Count);
  }

  [Fact]
  public void Analyze_Empty_ScanResult_Returns_Zero_Percentage()
  {
    var scanResult = CreateScanResult(totalSize: 0, groups: []);

    var report = _sut.Analyze(scanResult);

    Assert.Equal(0, report.WastedPercentage);
    Assert.Empty(report.ByExtension);
  }

  private static ScanResult CreateScanResult(long totalSize, List<DuplicateGroup> groups)
  {
    return new ScanResult
    {
      ScannedPath = "/test",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 100,
      TotalSizeBytes = totalSize,
      DuplicateGroups = groups
    };
  }

  private static DuplicateGroup CreateGroup(string ext, string hash, int fileCount, long fileSize)
  {
    var files = Enumerable.Range(0, fileCount)
      .Select(i => new ScannedFile
      {
        FilePath = $"/folder{i}/file{i}{ext}",
        SizeBytes = fileSize,
        Hash = hash,
        HashAlgorithm = "sha256",
        CreatedUtc = DateTime.UtcNow.AddDays(-i),
        ModifiedUtc = DateTime.UtcNow
      })
      .ToList();

    return new DuplicateGroup { Hash = hash, Files = files };
  }
}
