using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class PreviewStatisticsServiceTests
{
  private readonly PreviewStatisticsService _sut = new(new SpaceSavingsAnalyzer());

  [Fact]
  public void Generate_Returns_Correct_File_Counts()
  {
    var scanResult = CreateScanResult(totalFiles: 100, totalSize: 50000, groups: [
      CreateGroup(".jpg", "hash1", 3, 1000),
      CreateGroup(".png", "hash2", 2, 2000)
    ]);

    var stats = _sut.Generate(scanResult);

    Assert.Equal(100, stats.TotalFilesScanned);
    Assert.Equal(3, stats.DuplicateFileCount);  // (3-1) + (2-1)
    Assert.Equal(97, stats.UniqueFilesToKeep);   // 100 - 3
    Assert.Equal(3, stats.FilesToRemove);
    Assert.Equal(2, stats.DuplicateGroupCount);
  }

  [Fact]
  public void Generate_Calculates_Correct_Savings()
  {
    var scanResult = CreateScanResult(totalFiles: 50, totalSize: 10000, groups: [
      CreateGroup(".jpg", "hash1", 3, 1000),  // wasted: 2 * 1000 = 2000
      CreateGroup(".png", "hash2", 2, 500)     // wasted: 1 * 500 = 500
    ]);

    var stats = _sut.Generate(scanResult);

    Assert.Equal(2500, stats.WastedBytes);
    Assert.Equal(7500, stats.SizeAfterCleanup);
    Assert.Equal(25.0, stats.WastedPercentage);
  }

  [Fact]
  public void Generate_Empty_ScanResult_Returns_Zero_Stats()
  {
    var scanResult = CreateScanResult(totalFiles: 0, totalSize: 0, groups: []);

    var stats = _sut.Generate(scanResult);

    Assert.Equal(0, stats.DuplicateGroupCount);
    Assert.Equal(0, stats.DuplicateFileCount);
    Assert.Equal(0, stats.FilesToRemove);
    Assert.Equal(0, stats.WastedBytes);
    Assert.Equal(0, stats.WastedPercentage);
    Assert.Empty(stats.ByExtension);
    Assert.Empty(stats.ByDirectory);
    Assert.Empty(stats.TopGroups);
  }

  [Fact]
  public void Generate_Includes_Extension_Breakdown()
  {
    var scanResult = CreateScanResult(totalFiles: 20, totalSize: 10000, groups: [
      CreateGroup(".jpg", "hash1", 2, 1000),
      CreateGroup(".jpg", "hash2", 2, 500),
      CreateGroup(".png", "hash3", 3, 2000)
    ]);

    var stats = _sut.Generate(scanResult);

    Assert.True(stats.ByExtension.Count >= 2);
    Assert.Contains(stats.ByExtension, e => e.Extension == ".jpg");
    Assert.Contains(stats.ByExtension, e => e.Extension == ".png");
  }

  [Fact]
  public void Generate_Includes_Directory_Breakdown()
  {
    var scanResult = CreateScanResult(totalFiles: 10, totalSize: 5000, groups: [
      CreateGroup(".txt", "hash1", 3, 500),
      CreateGroup(".txt", "hash2", 2, 1000)
    ]);

    var stats = _sut.Generate(scanResult);

    Assert.NotEmpty(stats.ByDirectory);
    Assert.All(stats.ByDirectory, d => Assert.True(d.DuplicateFileCount > 0));
  }

  [Fact]
  public void Generate_Respects_TopGroupsCount()
  {
    var groups = Enumerable.Range(0, 20)
      .Select(i => CreateGroup(".txt", $"hash{i}", 2, (i + 1) * 100))
      .ToList();

    var scanResult = CreateScanResult(totalFiles: 200, totalSize: 100000, groups: groups);

    var stats = _sut.Generate(scanResult, topGroupsCount: 5);

    Assert.Equal(5, stats.TopGroups.Count);
    // Top groups should be ordered by wasted bytes descending
    for (var i = 1; i < stats.TopGroups.Count; i++)
    {
      Assert.True(stats.TopGroups[i - 1].WastedBytes >= stats.TopGroups[i].WastedBytes);
    }
  }

  [Fact]
  public void Generate_CleanupPercentage_Correct_For_No_Duplicates()
  {
    var scanResult = CreateScanResult(totalFiles: 10, totalSize: 5000, groups: []);

    var stats = _sut.Generate(scanResult);

    Assert.Equal(100, stats.CleanupPercentage);
  }

  [Fact]
  public void Generate_SizeAfterCleanup_Never_Negative()
  {
    var scanResult = CreateScanResult(totalFiles: 5, totalSize: 1000, groups: [
      CreateGroup(".bin", "hash1", 5, 200)  // wasted: 4 * 200 = 800
    ]);

    var stats = _sut.Generate(scanResult);

    Assert.True(stats.SizeAfterCleanup >= 0);
    Assert.Equal(200, stats.SizeAfterCleanup);  // 1000 - 800
  }

  private static ScanResult CreateScanResult(int totalFiles, long totalSize, List<DuplicateGroup> groups)
  {
    return new ScanResult
    {
      ScannedPath = "/test",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = totalFiles,
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
