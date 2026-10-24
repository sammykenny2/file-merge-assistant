using System.Runtime.InteropServices;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Tests.Scanning;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class NativeAnalysisPathsTests
{
  [Fact]
  public void Cli_Shares_Only_A_Marked_Sibling_App_And_Never_Bypasses_A_Present_Local_Module()
  {
    using var tree = new TempTree();
    var cli = tree.PathOf("cli"); var app = tree.PathOf("app");
    Directory.CreateDirectory(cli); Directory.CreateDirectory(app);
    var own = Path.Combine(cli, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    var shared = Path.Combine(app, "runtimes", RuntimeInformation.RuntimeIdentifier, "native");
    Assert.Equal(own, NativeAnalysisPaths.Resolve(cli));
    File.WriteAllText(Path.Combine(cli, NativeAnalysisPaths.PackageMarker), "owned package");
    Assert.Equal(own, NativeAnalysisPaths.Resolve(cli));
    File.WriteAllText(Path.Combine(app, NativeAnalysisPaths.PackageMarker), "owned package");
    Assert.Equal(shared, NativeAnalysisPaths.Resolve(cli));
    Directory.CreateDirectory(own);
    File.WriteAllText(Path.Combine(own, NativeAnalysisPaths.LibraryName), "invalid local module");
    Assert.Equal(own, NativeAnalysisPaths.Resolve(cli));
    var availability = new NativeAnalysisEngine(NativeAnalysisPaths.Resolve(cli)).Descriptor.Availability;
    if (OperatingSystem.IsWindows()) { Assert.Equal(AnalysisAvailability.Incompatible, availability); }
    else { Assert.Contains(availability, new[] { AnalysisAvailability.Incompatible, AnalysisAvailability.MissingDependency }); }
    Assert.Throws<ArgumentException>(() => NativeAnalysisPaths.Resolve("relative"));
    var other = tree.PathOf("some-other-program"); Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other, NativeAnalysisPaths.PackageMarker), "owned");
    Assert.StartsWith(other, NativeAnalysisPaths.Resolve(other), StringComparison.Ordinal);
  }
}
