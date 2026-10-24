using FileWorkbench.AnalysisVerifier;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class VerificationRootsTests
{
  [Fact]
  public void Preflight_Of_Two_New_Roots_Does_Not_Create_Or_Write_Them()
  {
    using var tree = new TempTree();
    var (first, second) = VerificationRoots.Validate(tree.PathOf("first"), tree.PathOf("second"));
    Assert.Equal(tree.PathOf("first"), first); Assert.Equal(tree.PathOf("second"), second);
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  [Theory]
  [InlineData("same")]
  [InlineData("nested")]
  [InlineData("existing")]
  [InlineData("missing-parent")]
  public void Invalid_Second_Root_Is_Rejected_Before_Any_New_Directory_Or_Overwrite(string kind)
  {
    using var tree = new TempTree();
    var sentinel = tree.Write("existing/sentinel.txt", "unchanged");
    var first = tree.PathOf("new");
    var second = kind switch
    {
      "same" => first,
      "nested" => Path.Combine(first, "child"),
      "existing" => tree.PathOf("existing"),
      "missing-parent" => tree.PathOf("absent/second"),
      _ => throw new InvalidOperationException(),
    };
    Assert.Throws<ArgumentException>(() => VerificationRoots.Validate(first, second));
    Assert.False(Directory.Exists(first)); Assert.Equal("unchanged", File.ReadAllText(sentinel));
    Assert.Single(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  [Fact]
  public async Task Sibling_Folders_Have_The_Same_Stable_Actual_OS_Volume_Identity()
  {
    using var tree = new TempTree();
    var first = Directory.CreateDirectory(tree.PathOf("first")).FullName;
    var second = Directory.CreateDirectory(tree.PathOf("second")).FullName;
    var a = await VolumeProof.ObserveAsync(first); var b = await VolumeProof.ObserveAsync(second);
    Assert.NotNull(a.Identity); Assert.Equal(a.Identity, b.Identity);
    Assert.Equal(a.Identity, (await VolumeProof.ObserveAsync(first)).Identity);
  }
}
