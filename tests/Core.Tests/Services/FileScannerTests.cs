using FileWorkbench.Models;
using FileWorkbench.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class FileScannerTests : IDisposable
{
  private readonly string _tempDir;
  private readonly FileScanner _sut;
  private readonly IMetadataExtractor _metadataExtractor;

  public FileScannerTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);

    _metadataExtractor = Substitute.For<IMetadataExtractor>();
    _metadataExtractor.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<ExifMetadata?>(null));

    var logger = Substitute.For<ILogger>();
    _sut = new FileScanner(_metadataExtractor, logger);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void CountFiles_Returns_Correct_Count()
  {
    CreateFile("a.txt", "content a");
    CreateFile("b.txt", "content b");

    var count = _sut.CountFiles(_tempDir, recursive: false);

    Assert.Equal(2, count);
  }

  [Fact]
  public void CountFiles_Recursive_Includes_Subdirectories()
  {
    CreateFile("a.txt", "content a");
    var subDir = Path.Combine(_tempDir, "sub");
    Directory.CreateDirectory(subDir);
    File.WriteAllText(Path.Combine(subDir, "b.txt"), "content b");

    var count = _sut.CountFiles(_tempDir, recursive: true);

    Assert.Equal(2, count);
  }

  [Fact]
  public void CountFiles_NonRecursive_Excludes_Subdirectories()
  {
    CreateFile("a.txt", "content a");
    var subDir = Path.Combine(_tempDir, "sub");
    Directory.CreateDirectory(subDir);
    File.WriteAllText(Path.Combine(subDir, "b.txt"), "content b");

    var count = _sut.CountFiles(_tempDir, recursive: false);

    Assert.Equal(1, count);
  }

  [Fact]
  public void CountFiles_MinSize_Filters_Small_Files()
  {
    CreateFile("small.txt", "x");
    CreateFile("large.txt", new string('x', 1000));

    var count = _sut.CountFiles(_tempDir, recursive: false, minSizeBytes: 100);

    Assert.Equal(1, count);
  }

  [Fact]
  public async Task ScanAsync_Returns_Files_With_Hashes()
  {
    CreateFile("a.txt", "content a");
    CreateFile("b.txt", "content b");

    var options = new ScanOptions { Path = _tempDir, Algorithm = "sha256" };
    var files = new List<ScannedFile>();

    await foreach (var file in _sut.ScanAsync(options))
    {
      files.Add(file);
    }

    Assert.Equal(2, files.Count);
    Assert.All(files, f =>
    {
      Assert.NotNull(f.Hash);
      Assert.Equal("sha256", f.HashAlgorithm);
      Assert.True(f.SizeBytes > 0);
    });
  }

  [Fact]
  public async Task ScanAsync_Respects_MinSize()
  {
    CreateFile("small.txt", "x");
    CreateFile("large.txt", new string('x', 1000));

    var options = new ScanOptions { Path = _tempDir, Algorithm = "sha256", MinSizeBytes = 100 };
    var files = new List<ScannedFile>();

    await foreach (var file in _sut.ScanAsync(options))
    {
      files.Add(file);
    }

    Assert.Single(files);
  }

  private void CreateFile(string name, string content)
  {
    File.WriteAllText(Path.Combine(_tempDir, name), content);
  }
}
