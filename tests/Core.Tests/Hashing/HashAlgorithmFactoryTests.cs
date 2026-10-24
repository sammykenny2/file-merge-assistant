using FileMergeAssistant.Hashing;
using Xunit;

namespace FileMergeAssistant.Tests.Hashing;

public class HashAlgorithmFactoryTests
{
  [Fact]
  public void Create_Sha256_Returns_Sha256HashAlgorithm()
  {
    var algorithm = HashAlgorithmFactory.Create("sha256");

    Assert.IsType<Sha256HashAlgorithm>(algorithm);
  }

  [Fact]
  public void Create_XxHash_Returns_XxHashAlgorithm()
  {
    var algorithm = HashAlgorithmFactory.Create("xxhash");

    Assert.IsType<XxHashAlgorithm>(algorithm);
  }

  [Fact]
  public void Create_Perceptual_Reports_Disabled_Feature()
  {
    Assert.Equal(PerceptualHashAlgorithm.DisabledReason,
      Assert.Throws<NotSupportedException>(() => HashAlgorithmFactory.Create("perceptual")).Message);
  }

  [Fact]
  public void Create_Unknown_Throws_ArgumentException()
  {
    var ex = Assert.Throws<ArgumentException>(() => HashAlgorithmFactory.Create("unknown"));

    Assert.Contains("Unknown hash algorithm", ex.Message);
  }

  [Theory]
  [InlineData("SHA256")]
  [InlineData("Sha256")]
  [InlineData("XXHASH")]
  [InlineData("XxHash")]
  public void Create_Is_Case_Insensitive(string algorithmName)
  {
    var algorithm = HashAlgorithmFactory.Create(algorithmName);

    Assert.NotNull(algorithm);
  }
}
