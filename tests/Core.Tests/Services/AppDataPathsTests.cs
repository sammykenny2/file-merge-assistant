using FileMergeAssistant.Infrastructure;
using Xunit;

namespace FileMergeAssistant.Tests.Services;

public class AppDataPathsTests
{
  [Fact]
  public void ResolveRoot_Without_Override_Uses_Local_Application_Data()
  {
    var root = AppDataPaths.ResolveRoot(null, @"C:\Users\someone\AppData\Local");

    Assert.Equal(@"C:\Users\someone\AppData\Local\FileMergeAssistant", root);
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
