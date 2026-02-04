using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class JsonScanResultStoreTests : IDisposable
{
  private readonly string _tempDir;
  private readonly JsonScanResultStore _sut = new();

  public JsonScanResultStoreTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SaveAsync_Creates_File()
  {
    var result = CreateScanResult();
    var filePath = Path.Combine(_tempDir, "result.json");

    await _sut.SaveAsync(result, filePath);

    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task SaveAsync_Creates_Directory_If_Missing()
  {
    var result = CreateScanResult();
    var filePath = Path.Combine(_tempDir, "subdir", "result.json");

    await _sut.SaveAsync(result, filePath);

    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task Roundtrip_Preserves_Data()
  {
    var original = CreateScanResult();
    var filePath = Path.Combine(_tempDir, "result.json");

    await _sut.SaveAsync(original, filePath);
    var loaded = await _sut.LoadAsync(filePath);

    Assert.Equal(original.ScannedPath, loaded.ScannedPath);
    Assert.Equal(original.HashAlgorithm, loaded.HashAlgorithm);
    Assert.Equal(original.TotalFilesScanned, loaded.TotalFilesScanned);
    Assert.Equal(original.TotalSizeBytes, loaded.TotalSizeBytes);
    Assert.Equal(original.DuplicateGroups.Count, loaded.DuplicateGroups.Count);
  }

  [Fact]
  public async Task Roundtrip_Preserves_DuplicateGroup_Data()
  {
    var now = DateTime.UtcNow;
    var original = new ScanResult
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
              FilePath = @"C:\test\a.txt",
              SizeBytes = 1000,
              CreatedUtc = now,
              ModifiedUtc = now,
              Hash = "abc123",
              HashAlgorithm = "sha256"
            },
            new ScannedFile
            {
              FilePath = @"C:\test\b.txt",
              SizeBytes = 1000,
              CreatedUtc = now,
              ModifiedUtc = now,
              Hash = "abc123",
              HashAlgorithm = "sha256"
            }
          ]
        }
      ]
    };
    var filePath = Path.Combine(_tempDir, "result.json");

    await _sut.SaveAsync(original, filePath);
    var loaded = await _sut.LoadAsync(filePath);

    Assert.Single(loaded.DuplicateGroups);
    Assert.Equal("abc123", loaded.DuplicateGroups[0].Hash);
    Assert.Equal(2, loaded.DuplicateGroups[0].Files.Count);
    Assert.Equal(@"C:\test\a.txt", loaded.DuplicateGroups[0].Files[0].FilePath);
  }

  [Fact]
  public async Task LoadAsync_Throws_For_Missing_File()
  {
    var filePath = Path.Combine(_tempDir, "nonexistent.json");

    await Assert.ThrowsAsync<FileNotFoundException>(
      () => _sut.LoadAsync(filePath));
  }

  private static ScanResult CreateScanResult()
  {
    return new ScanResult
    {
      ScannedPath = @"C:\test",
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      TotalFilesScanned = 10,
      TotalSizeBytes = 10000,
      DuplicateGroups = []
    };
  }
}
