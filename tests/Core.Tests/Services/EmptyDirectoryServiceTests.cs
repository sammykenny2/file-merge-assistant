using FileMergeAssistant.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class EmptyDirectoryServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly EmptyDirectoryService _sut;

  public EmptyDirectoryServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-empty-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
    var logger = Substitute.For<ILogger>();
    _sut = new EmptyDirectoryService(logger);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Cleanup_Removes_Empty_Directory()
  {
    var emptyDir = Path.Combine(_tempDir, "empty");
    Directory.CreateDirectory(emptyDir);

    var removed = _sut.Cleanup(_tempDir);

    Assert.Equal(1, removed);
    Assert.False(Directory.Exists(emptyDir));
  }

  [Fact]
  public void Cleanup_Does_Not_Remove_Non_Empty_Directory()
  {
    var dir = Path.Combine(_tempDir, "notempty");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "file.txt"), "content");

    var removed = _sut.Cleanup(_tempDir);

    Assert.Equal(0, removed);
    Assert.True(Directory.Exists(dir));
  }

  [Fact]
  public void Cleanup_Removes_Nested_Empty_Directories()
  {
    var dir1 = Path.Combine(_tempDir, "level1");
    var dir2 = Path.Combine(dir1, "level2");
    Directory.CreateDirectory(dir2);

    var removed = _sut.Cleanup(_tempDir);

    Assert.Equal(2, removed);
    Assert.False(Directory.Exists(dir1));
  }

  [Fact]
  public void Cleanup_DryRun_Does_Not_Delete()
  {
    var emptyDir = Path.Combine(_tempDir, "empty");
    Directory.CreateDirectory(emptyDir);

    var removed = _sut.Cleanup(_tempDir, dryRun: true);

    Assert.Equal(1, removed);
    Assert.True(Directory.Exists(emptyDir));
  }

  [Fact]
  public void Cleanup_NonExistent_Path_Returns_Zero()
  {
    var removed = _sut.Cleanup("/nonexistent/path");

    Assert.Equal(0, removed);
  }
}
