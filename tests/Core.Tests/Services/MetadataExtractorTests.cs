using FileWorkbench.Services;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class MetadataExtractorTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ExifMetadataExtractor _sut;

  public MetadataExtractorTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);

    var logger = Substitute.For<ILogger>();
    _sut = new ExifMetadataExtractor(logger);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task ExtractAsync_Returns_Null_For_NonImage_File()
  {
    var filePath = Path.Combine(_tempDir, "test.txt");
    File.WriteAllText(filePath, "not an image");

    var result = await _sut.ExtractAsync(filePath);

    Assert.Null(result);
  }

  [Fact]
  public async Task ExtractAsync_Returns_Null_For_Unsupported_Extension()
  {
    var filePath = Path.Combine(_tempDir, "test.xyz");
    File.WriteAllText(filePath, "data");

    var result = await _sut.ExtractAsync(filePath);

    Assert.Null(result);
  }

  [Theory]
  [InlineData(".jpg")]
  [InlineData(".jpeg")]
  [InlineData(".png")]
  [InlineData(".gif")]
  [InlineData(".bmp")]
  [InlineData(".tiff")]
  public async Task ExtractAsync_Does_Not_Throw_For_Supported_Extensions(string extension)
  {
    // Create a minimal file with supported extension - even if metadata can't be parsed,
    // it should not throw
    var filePath = Path.Combine(_tempDir, $"test{extension}");
    File.WriteAllBytes(filePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

    var result = await _sut.ExtractAsync(filePath);

    // May or may not return metadata, but should not throw
    // null is acceptable for files without valid metadata
  }
}
