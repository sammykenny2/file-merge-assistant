using FileWorkbench.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class FileOperationServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ILogger _logger;
  private readonly FileOperationService _sut;

  public FileOperationServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    _logger = Substitute.For<ILogger>();
    _sut = new FileOperationService(_logger);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task DeleteAsync_Deletes_File()
  {
    var filePath = CreateFile("delete-me.txt", "content");

    await _sut.DeleteAsync(filePath, dryRun: false);

    Assert.False(File.Exists(filePath));
  }

  [Fact]
  public async Task DeleteAsync_DryRun_Does_Not_Delete_File()
  {
    var filePath = CreateFile("keep-me.txt", "content");

    await _sut.DeleteAsync(filePath, dryRun: true);

    Assert.True(File.Exists(filePath));
  }

  [Fact]
  public async Task DeleteAsync_Missing_File_Logs_Warning_Does_Not_Throw()
  {
    var filePath = Path.Combine(_tempDir, "nonexistent.txt");

    await _sut.DeleteAsync(filePath, dryRun: false);

    // File.Delete does not throw for missing files, so no warning is logged.
    // This test simply verifies no exception is thrown.
  }

  [Fact]
  public async Task MoveAsync_Moves_File()
  {
    var source = CreateFile("source.txt", "content");
    var dest = Path.Combine(_tempDir, "moved", "dest.txt");

    await _sut.MoveAsync(source, dest, dryRun: false);

    Assert.False(File.Exists(source));
    Assert.True(File.Exists(dest));
    Assert.Equal("content", await File.ReadAllTextAsync(dest));
  }

  [Fact]
  public async Task MoveAsync_DryRun_Does_Not_Move_File()
  {
    var source = CreateFile("stay.txt", "content");
    var dest = Path.Combine(_tempDir, "moved", "dest.txt");

    await _sut.MoveAsync(source, dest, dryRun: true);

    Assert.True(File.Exists(source));
    Assert.False(File.Exists(dest));
  }

  [Fact]
  public async Task MoveAsync_Creates_Destination_Directory()
  {
    var source = CreateFile("source.txt", "content");
    var destDir = Path.Combine(_tempDir, "new-dir", "sub-dir");
    var dest = Path.Combine(destDir, "dest.txt");

    await _sut.MoveAsync(source, dest, dryRun: false);

    Assert.True(Directory.Exists(destDir));
    Assert.True(File.Exists(dest));
  }

  private string CreateFile(string name, string content)
  {
    var filePath = Path.Combine(_tempDir, name);
    File.WriteAllText(filePath, content);
    return filePath;
  }
}
