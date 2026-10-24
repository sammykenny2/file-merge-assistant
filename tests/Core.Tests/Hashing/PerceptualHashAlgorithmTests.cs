using FileWorkbench.Hashing;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Hashing;

public class PerceptualHashAlgorithmTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task Disabled_Legacy_Media_Algorithms_Reject_Without_Reading_Or_Modifying_Files(bool video)
  {
    using var tree = new TempTree();
    var path = tree.Write(video ? "clip.mp4" : "photo.png", "unchanged bytes");
    IHashAlgorithm algorithm = video ? new VideoFrameHashAlgorithm() : new PerceptualHashAlgorithm();
    var error = await Assert.ThrowsAsync<NotSupportedException>(() => algorithm.ComputeHashAsync(path));
    Assert.Contains("ImageSharp is not included", error.Message);
    Assert.Equal("unchanged bytes", File.ReadAllText(path));
    Assert.Equal(video ? "video-perceptual" : "perceptual", algorithm.Name);
    await Assert.ThrowsAsync<NotSupportedException>(() => algorithm.ComputeHashAsync(tree.PathOf("absent")));
  }
}
