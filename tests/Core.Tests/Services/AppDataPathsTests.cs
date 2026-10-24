using FileWorkbench.Infrastructure;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Services;

public class AppDataPathsTests
{
  [Fact]
  public void Existing_Legacy_Data_Is_Reused_Without_Moving_Or_Changing_It()
  {
    using var tree = new TempTree();
    var index = tree.Write("FileMergeAssistant/index.db", "existing index sentinel");
    var journal = tree.Write("FileMergeAssistant/recycle-areas/run/manifest.json", "legacy absolute paths stay intact");
    Directory.CreateDirectory(tree.PathOf("FileWorkbench"));

    Assert.Equal(tree.PathOf("FileMergeAssistant"), AppDataPaths.ResolveRoot(null, tree.Root));
    Assert.Contains("existing index sentinel", File.ReadAllText(index));
    Assert.Contains("legacy absolute paths stay intact", File.ReadAllText(journal));
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.PathOf("FileWorkbench")));
  }

  [Fact]
  public void Fresh_Profile_Uses_New_Name_Without_Creating_A_Directory()
  {
    using var tree = new TempTree();
    Assert.Equal(tree.PathOf("FileWorkbench"), AppDataPaths.ResolveRoot(null, tree.Root));
    Assert.Empty(Directory.EnumerateFileSystemEntries(tree.Root));
  }

  [Fact]
  public void Explicit_New_Override_Wins_Over_Legacy_Override_And_Existing_Data()
  {
    using var tree = new TempTree();
    tree.Write("FileMergeAssistant/index.db", "sentinel");
    Assert.Equal(tree.PathOf("explicit"), AppDataPaths.ResolveRoot(tree.PathOf("explicit"), tree.Root, tree.PathOf("legacy")));
  }

  [Theory]
  [InlineData(null)]
  [InlineData(" ")]
  public void Legacy_Override_Remains_Usable_When_New_Override_Is_Absent(string? newOverride)
  {
    using var tree = new TempTree();
    Assert.Equal(tree.PathOf("legacy"), AppDataPaths.ResolveRoot(newOverride, tree.Root, tree.PathOf("legacy")));
  }

  [Fact]
  public void ResolveRoot_Without_Override_Uses_Local_Application_Data()
  {
    var root = AppDataPaths.ResolveRoot(null, @"C:\Users\someone\AppData\Local");

    Assert.Equal(@"C:\Users\someone\AppData\Local\FileWorkbench", root);
  }

  [Fact]
  public void ResolveRoot_Override_Wins_And_Is_Made_Absolute()
  {
    var root = AppDataPaths.ResolveRoot(@"D:\portable\data\", @"C:\Users\someone\AppData\Local");

    Assert.Equal(@"D:\portable\data\", root);
  }

  [Fact]
  public void ResolveRoot_Without_Any_Location_Throws()
  {
    Assert.Throws<InvalidOperationException>(() => AppDataPaths.ResolveRoot("  ", ""));
  }

  [Fact]
  public void Test_Run_Never_Uses_Real_Local_Application_Data()
  {
    var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppDataPaths.ApplicationFolderName);

    Assert.Equal(Path.GetFullPath(TestEnvironment.DataRoot), AppDataPaths.Root);
    Assert.NotEqual(real, AppDataPaths.Root, StringComparer.OrdinalIgnoreCase);
  }

  [Fact]
  public void Data_Folders_Live_Under_Root()
  {
    foreach (var path in new[] { AppDataPaths.CacheDirectory, AppDataPaths.RecycleBinDirectory, AppDataPaths.LogDirectory, AppDataPaths.ResultsDirectory, AppDataPaths.UpdatesDirectory })
      Assert.StartsWith(AppDataPaths.Root + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
  }
}
