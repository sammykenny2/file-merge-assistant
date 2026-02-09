using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class ScanCacheServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ScanCacheService _sut;

  public ScanCacheServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-test-cache-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    var cacheDir = Path.Combine(_tempDir, ".cache");
    var logger = Substitute.For<ILogger>();
    _sut = new ScanCacheService(logger, cacheDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task SaveAndLoadCache_RoundTrips()
  {
    var cache = new ScanCache
    {
      ScannedPath = "/test/path",
      CacheDateUtc = DateTime.UtcNow,
      HashAlgorithm = "sha256",
      Entries = new Dictionary<string, CachedFileEntry>
      {
        ["/test/file.txt"] = new CachedFileEntry
        {
          Hash = "abc123",
          SizeBytes = 1000,
          ModifiedUtc = DateTime.UtcNow
        }
      }
    };

    await _sut.SaveCacheAsync(cache);

    var loaded = await _sut.LoadCacheAsync("/test/path", "sha256");

    Assert.NotNull(loaded);
    Assert.Single(loaded.Entries);
    Assert.Equal("abc123", loaded.Entries["/test/file.txt"].Hash);
  }

  [Fact]
  public async Task LoadCacheAsync_Returns_Null_When_Not_Found()
  {
    var loaded = await _sut.LoadCacheAsync("/nonexistent/path", "sha256");
    Assert.Null(loaded);
  }

  [Fact]
  public void IsEntryValid_Returns_True_For_Valid_Entry()
  {
    var filePath = Path.Combine(_tempDir, "test.txt");
    File.WriteAllText(filePath, "content");
    var fileInfo = new FileInfo(filePath);

    var entry = new CachedFileEntry
    {
      Hash = "abc",
      SizeBytes = fileInfo.Length,
      ModifiedUtc = fileInfo.LastWriteTimeUtc
    };

    Assert.True(_sut.IsEntryValid(entry, filePath));
  }

  [Fact]
  public void IsEntryValid_Returns_False_When_Size_Changed()
  {
    var filePath = Path.Combine(_tempDir, "test.txt");
    File.WriteAllText(filePath, "content");

    var entry = new CachedFileEntry
    {
      Hash = "abc",
      SizeBytes = 99999,
      ModifiedUtc = new FileInfo(filePath).LastWriteTimeUtc
    };

    Assert.False(_sut.IsEntryValid(entry, filePath));
  }

  [Fact]
  public void IsEntryValid_Returns_False_When_File_Missing()
  {
    var entry = new CachedFileEntry
    {
      Hash = "abc",
      SizeBytes = 100,
      ModifiedUtc = DateTime.UtcNow
    };

    Assert.False(_sut.IsEntryValid(entry, Path.Combine(_tempDir, "nonexistent.txt")));
  }
}
