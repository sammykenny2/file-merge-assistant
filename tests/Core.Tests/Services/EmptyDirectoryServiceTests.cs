using FileWorkbench.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class EmptyDirectoryServiceTests : IDisposable
{
  private readonly string _tempDir;
  private readonly EmptyDirectoryService _sut;

  public EmptyDirectoryServiceTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-empty-test-{Guid.NewGuid():N}");
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
  public void Cleanup_DryRun_Counts_Nested_Empty_Directories_Without_Deleting()
  {
    var dir1 = Path.Combine(_tempDir, "level1");
    var dir2 = Path.Combine(dir1, "level2");
    Directory.CreateDirectory(dir2);

    var removed = _sut.Cleanup(_tempDir, dryRun: true);

    Assert.Equal(2, removed);
    Assert.True(Directory.Exists(dir2));
  }

  [Fact]
  public void Cleanup_DryRun_Count_Matches_Real_Cleanup()
  {
    Directory.CreateDirectory(Path.Combine(_tempDir, "a", "b", "c"));
    Directory.CreateDirectory(Path.Combine(_tempDir, "keep", "empty"));
    File.WriteAllText(Path.Combine(_tempDir, "keep", "file.txt"), "content");

    var dryRunCount = _sut.Cleanup(_tempDir, dryRun: true);
    var realCount = _sut.Cleanup(_tempDir);

    Assert.Equal(4, dryRunCount);
    Assert.Equal(dryRunCount, realCount);
    Assert.False(Directory.Exists(Path.Combine(_tempDir, "a")));
    Assert.False(Directory.Exists(Path.Combine(_tempDir, "keep", "empty")));
    Assert.True(File.Exists(Path.Combine(_tempDir, "keep", "file.txt")));
  }

  [Fact]
  public void Cleanup_Does_Not_Remove_Empty_Root()
  {
    var removed = _sut.Cleanup(_tempDir);

    Assert.Equal(0, removed);
    Assert.True(Directory.Exists(_tempDir));
  }

  [Fact]
  public void Cleanup_Does_Not_Follow_Junction_Outside_Root()
  {
    var root = Path.Combine(_tempDir, "root");
    var outside = Path.Combine(_tempDir, "outside");
    var outsideEmpty = Path.Combine(outside, "emptyChild");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(outsideEmpty);
    var junction = Path.Combine(root, "link");
    CreateJunction(junction, outside);

    try
    {
      var dryRunCount = _sut.Cleanup(root, dryRun: true);
      var realCount = _sut.Cleanup(root);

      Assert.Equal(0, dryRunCount);
      Assert.Equal(0, realCount);
      Assert.True(Directory.Exists(outsideEmpty));
      Assert.True(Directory.Exists(junction));
    }
    finally
    {
      // Remove the link itself so Dispose does not have to delete through it.
      if (Directory.Exists(junction))
        Directory.Delete(junction);
    }
  }

  private static void CreateJunction(string junctionPath, string targetPath)
  {
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
      FileName = "cmd.exe",
      ArgumentList = { "/c", "mklink", "/J", junctionPath, targetPath },
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true
    })!;
    process.WaitForExit();
    Assert.True(process.ExitCode == 0 && Directory.Exists(junctionPath), "Could not create a test junction with mklink /J.");
  }

  [Fact]
  public void Cleanup_NonExistent_Path_Returns_Zero()
  {
    var removed = _sut.Cleanup("/nonexistent/path");

    Assert.Equal(0, removed);
  }
}
