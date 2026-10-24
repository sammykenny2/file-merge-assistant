using FileWorkbench.Hashing;
using Xunit;

namespace FileWorkbench.Tests.Hashing;

public class Sha256HashAlgorithmTests : IDisposable
{
  private readonly string _tempDir;
  private readonly Sha256HashAlgorithm _sut = new();

  public Sha256HashAlgorithmTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"file-workbench-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public void Name_Returns_Sha256()
  {
    Assert.Equal("sha256", _sut.Name);
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
  public async Task ComputeHashAsync_Returns_Known_Hash_For_Known_Content()
  {
    var filePath = CreateTempFile("hello world");

    var hash = await _sut.ComputeHashAsync(filePath);

    // SHA-256 of "hello world" is well-known
    Assert.Equal("b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9", hash);
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

  [Fact]
  public async Task ComputeHashAsync_Empty_File_Returns_Hash()
  {
    var filePath = CreateTempFile("");

    var hash = await _sut.ComputeHashAsync(filePath);

    Assert.NotEmpty(hash);
  }

  private string CreateTempFile(string content)
  {
    var path = Path.Combine(_tempDir, $"{Guid.NewGuid():N}.dat");
    File.WriteAllText(path, content);
    return path;
  }
}
