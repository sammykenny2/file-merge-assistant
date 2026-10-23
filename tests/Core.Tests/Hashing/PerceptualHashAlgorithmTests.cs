using FileMergeAssistant.Hashing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace FileMergeAssistant.Tests.Hashing;

public class PerceptualHashAlgorithmTests : IDisposable
{
  private readonly string _tempDir;
  private readonly PerceptualHashAlgorithm _sut = new();

  public PerceptualHashAlgorithmTests()
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
  public void Name_Returns_Perceptual()
  {
    Assert.Equal("perceptual", _sut.Name);
  }

  [Fact]
  public async Task ComputeHashAsync_Throws_For_Non_Image_File()
  {
    var filePath = Path.Combine(_tempDir, "test.txt");
    File.WriteAllText(filePath, "not an image");

    await Assert.ThrowsAsync<NotSupportedException>(
      () => _sut.ComputeHashAsync(filePath));
  }

  [Fact]
  public async Task ComputeHashAsync_Returns_Hash_For_Image()
  {
    var filePath = CreateTestImage(100, 100, Color.ParseHex("FF0000").ToPixel<Rgba32>());

    var hash = await _sut.ComputeHashAsync(filePath);

    Assert.NotEmpty(hash);
  }

  [Fact]
  public async Task ComputeHashAsync_Identical_Images_Same_Hash()
  {
    var file1 = CreateTestImage(100, 100, Color.ParseHex("0000FF").ToPixel<Rgba32>());
    var file2 = CreateTestImage(100, 100, Color.ParseHex("0000FF").ToPixel<Rgba32>());

    var hash1 = await _sut.ComputeHashAsync(file1);
    var hash2 = await _sut.ComputeHashAsync(file2);

    Assert.Equal(hash1, hash2);
  }

  private string CreateTestImage(int width, int height, Rgba32 color)
  {
    var filePath = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.png");
    using var image = new Image<Rgba32>(width, height, color);
    image.SaveAsPng(filePath);
    return filePath;
  }
}
