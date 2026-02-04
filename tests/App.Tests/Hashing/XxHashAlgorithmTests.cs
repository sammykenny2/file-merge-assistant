using FileMergeAssistant.Hashing;
using Xunit;

namespace FileMergeAssistant.Tests.Hashing;

public class XxHashAlgorithmTests : IDisposable
{
  private readonly string _tempDir;
  private readonly XxHashAlgorithm _sut = new();

  public XxHashAlgorithmTests()
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
  public void Name_Returns_XxHash()
  {
    Assert.Equal("xxhash", _sut.Name);
  }

  [Fact]
  public async Task ComputeHashAsync_Returns_Consistent_Hash()
  {
    var filePath = CreateTempFile("hello world");

    var hash1 = await _sut.ComputeHashAsync(filePath);
    var hash2 = await _sut.ComputeHashAsync(filePath);

    Assert.Equal(hash1, hash2);
    Assert.NotEmpty(hash1);
  }

  [Fact]
  public async Task ComputeHashAsync_Different_Content_Different_Hash()
  {
    var file1 = CreateTempFile("content A");
    var file2 = CreateTempFile("content B");

    var hash1 = await _sut.ComputeHashAsync(file1);
    var hash2 = await _sut.ComputeHashAsync(file2);

    Assert.NotEqual(hash1, hash2);
  }

  [Fact]
  public async Task ComputeHashAsync_Same_Content_Same_Hash()
  {
    var file1 = CreateTempFile("identical content");
    var file2 = CreateTempFile("identical content");

    var hash1 = await _sut.ComputeHashAsync(file1);
    var hash2 = await _sut.ComputeHashAsync(file2);

    Assert.Equal(hash1, hash2);
  }

  private string CreateTempFile(string content)
  {
    var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.dat");
    File.WriteAllText(path, content);
    return path;
  }
}
