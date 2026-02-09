using FileMergeAssistant.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class RecycleBinServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly string _recycleBinDir;
  private readonly RecycleBinService _sut;

  public RecycleBinServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    _recycleBinDir = Path.Combine(_tempDir, ".recycle-bin");
    var logger = Substitute.For<ILogger>();
    _sut = new RecycleBinService(logger, _recycleBinDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task RecycleAsync_Moves_File_To_RecycleBin()
  {
    var filePath = CreateFile("test.txt", "content");

    await _sut.RecycleAsync(filePath);

    Assert.False(File.Exists(filePath));
    var entries = await _sut.ListEntriesAsync();
    Assert.Single(entries);
  }

  [Fact]
  public async Task RestoreAsync_Restores_File_To_Original_Location()
  {
    var filePath = CreateFile("test.txt", "content");
    var fullPath = Path.GetFullPath(filePath);

    await _sut.RecycleAsync(filePath);
    Assert.False(File.Exists(filePath));

    await _sut.RestoreAsync(fullPath);
    Assert.True(File.Exists(fullPath));
    Assert.Equal("content", await File.ReadAllTextAsync(fullPath));
  }

  [Fact]
  public async Task RestoreAllAsync_Restores_All_Files()
  {
    var file1 = CreateFile("a.txt", "content a");
    var file2 = CreateFile("b.txt", "content b");
    var fullPath1 = Path.GetFullPath(file1);
    var fullPath2 = Path.GetFullPath(file2);

    await _sut.RecycleAsync(file1);
    await _sut.RecycleAsync(file2);

    Assert.False(File.Exists(file1));
    Assert.False(File.Exists(file2));

    await _sut.RestoreAllAsync();

    Assert.True(File.Exists(fullPath1));
    Assert.True(File.Exists(fullPath2));
  }

  [Fact]
  public async Task PurgeAsync_Permanently_Deletes_Recycled_Files()
  {
    var filePath = CreateFile("test.txt", "content");
    await _sut.RecycleAsync(filePath);

    var entriesBefore = await _sut.ListEntriesAsync();
    Assert.Single(entriesBefore);

    await _sut.PurgeAsync();

    var entriesAfter = await _sut.ListEntriesAsync();
    Assert.Empty(entriesAfter);
  }

  [Fact]
  public async Task ListEntriesAsync_Returns_Empty_When_No_Recycled_Files()
  {
    var entries = await _sut.ListEntriesAsync();
    Assert.Empty(entries);
  }

  private string CreateFile(string name, string content)
  {
    var filePath = Path.Combine(_tempDir, name);
    File.WriteAllText(filePath, content);
    return filePath;
  }
}
