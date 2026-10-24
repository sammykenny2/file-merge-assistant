using FileWorkbench.Models;
using FileWorkbench.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class FileScannerExclusionTests : IDisposable
{
  private readonly string _tempDir;
  private readonly FileScanner _sut;

  public FileScannerExclusionTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-excl-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);

    var metadataExtractor = Substitute.For<IMetadataExtractor>();
    metadataExtractor.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
      .Returns(Task.FromResult<ExifMetadata?>(null));

    var logger = Substitute.For<ILogger>();
    _sut = new FileScanner(metadataExtractor, logger);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ScanAsync_Excludes_By_Extension()
  {
    CreateFile("keep.txt", "content");
    CreateFile("exclude.tmp", "content");
    CreateFile("exclude.log", "content");

    var options = new ScanOptions
    {
      Path = _tempDir,
      Algorithm = "sha256",
      ExcludeExtensions = [".tmp", ".log"]
    };

    var files = new List<ScannedFile>();
    await foreach (var file in _sut.ScanAsync(options))
    {
      files.Add(file);
    }

    Assert.Single(files);
    Assert.Contains("keep.txt", files[0].FilePath);
  }

  [Fact]
  public async Task ScanAsync_Excludes_By_Pattern()
  {
    CreateFile("keep.txt", "content");

    var subDir = Path.Combine(_tempDir, "node_modules");
    Directory.CreateDirectory(subDir);
    File.WriteAllText(Path.Combine(subDir, "pkg.json"), "content");

    var options = new ScanOptions
    {
      Path = _tempDir,
      Recursive = true,
      Algorithm = "sha256",
      ExcludePatterns = ["node_modules"]
    };

    var files = new List<ScannedFile>();
    await foreach (var file in _sut.ScanAsync(options))
    {
      files.Add(file);
    }

    Assert.Single(files);
    Assert.Contains("keep.txt", files[0].FilePath);
  }

  [Fact]
  public async Task ScanAsync_No_Exclusions_Returns_All()
  {
    CreateFile("a.txt", "content a");
    CreateFile("b.tmp", "content b");

    var options = new ScanOptions
    {
      Path = _tempDir,
      Algorithm = "sha256"
    };

    var files = new List<ScannedFile>();
    await foreach (var file in _sut.ScanAsync(options))
    {
      files.Add(file);
    }

    Assert.Equal(2, files.Count);
  }

  private void CreateFile(string name, string content)
  {
    File.WriteAllText(Path.Combine(_tempDir, name), content);
  }
}
