using FileMergeAssistant.Hashing;
using Xunit;

namespace FileMergeAssistant.Tests.Hashing;

public class HashAlgorithmFactoryExtendedTests
{
  [Theory]
  [InlineData("video-perceptual")]
  [InlineData("VIDEO-PERCEPTUAL")]
  [InlineData("PERCEPTUAL")]
  public void Create_Legacy_Media_Reports_Disabled_Feature(string name)
  {
    Assert.Contains("ImageSharp is not included",
      Assert.Throws<NotSupportedException>(() => HashAlgorithmFactory.Create(name)).Message);
  }

  [Theory]
  [InlineData("sha256")]
  [InlineData("xxhash")]
  public void Create_All_Valid_Algorithms(string name)
  {
    var algo = HashAlgorithmFactory.Create(name);
    Assert.NotNull(algo);
    Assert.Equal(name, algo.Name);
  }

  [Fact]
  public void Create_Unknown_Throws_ArgumentException()
  {
    Assert.Throws<ArgumentException>(() => HashAlgorithmFactory.Create("unknown"));
  }
}
