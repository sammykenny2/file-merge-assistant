using FileMergeAssistant.Hashing;
using Xunit;

namespace FileMergeAssistant.Tests.Hashing;

public class HashAlgorithmFactoryExtendedTests
{
  [Fact]
  public void Create_VideoPerceptual_Returns_VideoFrameHashAlgorithm()
  {
    var algo = HashAlgorithmFactory.Create("video-perceptual");
    Assert.IsType<VideoFrameHashAlgorithm>(algo);
    Assert.Equal("video-perceptual", algo.Name);
  }

  [Theory]
  [InlineData("sha256")]
  [InlineData("xxhash")]
  [InlineData("perceptual")]
  [InlineData("video-perceptual")]
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
