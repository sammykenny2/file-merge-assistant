using FileMergeAssistant.Services;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class ByteByByteVerifierTests : IDisposable
{
  private readonly string _tempDir;
  private readonly ByteByByteVerifier _sut = new();

  public ByteByByteVerifierTests()
  {
    _tempDir = Path.Combine(Path.GetTempPath(), $"fma-bbv-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(_tempDir);
  }

  public void Dispose()
  {
    if (Directory.Exists(_tempDir))
      Directory.Delete(_tempDir, recursive: true);
    GC.SuppressFinalize(this);
  }

  [Fact]
  public async Task Identical_Files_Returns_True()
  {
    var content = "Hello, this is identical content for testing.";
    var file1 = CreateFile("file1.txt", content);
    var file2 = CreateFile("file2.txt", content);

    var result = await _sut.AreFilesIdenticalAsync(file1, file2);

    Assert.True(result);
  }

  [Fact]
  public async Task Different_Content_Returns_False()
  {
    var file1 = CreateFile("file1.txt", "Content A");
    var file2 = CreateFile("file2.txt", "Content B");

    var result = await _sut.AreFilesIdenticalAsync(file1, file2);

    Assert.False(result);
  }

  [Fact]
  public async Task Different_Sizes_Returns_False()
  {
    var file1 = CreateFile("file1.txt", "short");
    var file2 = CreateFile("file2.txt", "this is a much longer content string");

    var result = await _sut.AreFilesIdenticalAsync(file1, file2);

    Assert.False(result);
  }

  [Fact]
  public async Task Empty_Files_Returns_True()
  {
    var file1 = CreateFile("file1.txt", "");
    var file2 = CreateFile("file2.txt", "");

    var result = await _sut.AreFilesIdenticalAsync(file1, file2);

    Assert.True(result);
  }

  [Fact]
  public async Task Large_Identical_Files_Returns_True()
  {
    var content = new string('X', 200_000);
    var file1 = CreateFile("large1.bin", content);
    var file2 = CreateFile("large2.bin", content);

    var result = await _sut.AreFilesIdenticalAsync(file1, file2);

    Assert.True(result);
  }

  private string CreateFile(string name, string content)
  {
    var path = Path.Combine(_tempDir, name);
    File.WriteAllText(path, content);
    return path;
  }
}
