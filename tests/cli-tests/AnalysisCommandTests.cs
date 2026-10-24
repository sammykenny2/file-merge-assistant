using System.CommandLine;
using System.CommandLine.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Cli.Commands;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Tests.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FileMergeAssistant.Cli.Tests;

public sealed class AnalysisCommandTests
{
  [ModuleInitializer]
  internal static void IsolateData()
  {
    Environment.SetEnvironmentVariable(AppDataPaths.DataDirectoryVariable, Path.Combine(Path.GetTempPath(), "fma-cli-tests-" + Environment.ProcessId));
    AppText.Apply("en", []);
  }

  private static string DataRoot() => Path.Combine(AppDataPaths.Root, Guid.NewGuid().ToString("N"));

  private static RootCommand Root(IServiceProvider services, string dataRoot)
  {
    var root = new RootCommand();
    root.AddCommand(AnalysisCommands.Engines(services));
    root.AddCommand(AnalysisCommands.Analyze(services, dataRoot));
    root.AddCommand(AnalysisCommands.Compare(services, dataRoot));
    root.AddCommand(AnalysisCommands.SimilarImages(services, dataRoot));
    return root;
  }

  [Fact]
  public async Task Engine_Metadata_Exposes_Capability_Maturity_And_Unavailability_Without_Analyzing()
  {
    var data = DataRoot();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Path.Combine(data, "missing-native"))]);
    using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(0, await Root(services, data).InvokeAsync(["engines"], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.Equal(1, json.RootElement.GetProperty("formatVersion").GetInt32());
    var engines = json.RootElement.GetProperty("engines").EnumerateArray().ToArray();
    Assert.Equal("MissingDependency", engines.Single(item => item.GetProperty("id").GetString() == NativeAnalysisEngine.EngineId).GetProperty("availability").GetString());
    Assert.Equal("Experimental", engines.Single(item => item.GetProperty("id").GetString() == ManagedAnalysisEngine.EngineId)
      .GetProperty("capabilities")[0].GetProperty("maturity").GetString());
    Assert.False(Directory.Exists(data));
  }

  [Theory]
  [InlineData(null)]
  [InlineData(ContentFingerprints.Sha256)]
  public async Task Explicit_Analysis_Uses_The_Shared_Contract_And_Stable_Json_Report(string? algorithm)
  {
    using var dataset = new KnownAnswerDataset();
    using var services = new ServiceCollection().AddSingleton(AnalysisEngineRegistry.ManagedOnly()).BuildServiceProvider();
    var console = new TestConsole();
    var args = new List<string> { "analyze", dataset.Root };
    if (algorithm is not null) { args.AddRange(["--algorithm", algorithm]); }
    Assert.Equal(0, await Root(services, DataRoot()).InvokeAsync([.. args], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.True(json.RootElement.GetProperty("isComplete").GetBoolean());
    Assert.Equal(7, json.RootElement.GetProperty("files").GetArrayLength());
    Assert.Equal(2, json.RootElement.GetProperty("exactGroups").GetArrayLength());
    Assert.Equal(algorithm ?? ContentFingerprints.XxHash128, json.RootElement.GetProperty("engine").GetProperty("algorithm").GetString());
    Assert.True(File.Exists(json.RootElement.GetProperty("indexPath").GetString()));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Theory]
  [InlineData("czkawka", null)]
  [InlineData("unknown-engine", null)]
  [InlineData("csharp", "unknown-algorithm")]
  public async Task Unsupported_Or_Missing_Engine_Is_A_Nonzero_Diagnostic_And_Never_A_Successful_Empty_Result(string engine, string? algorithm)
  {
    using var dataset = new KnownAnswerDataset();
    var data = DataRoot();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Path.Combine(data, "missing-native"))]);
    using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    var console = new TestConsole();
    var args = new List<string> { "analyze", dataset.Root, "--engine", engine };
    if (algorithm is not null) { args.AddRange(["--algorithm", algorithm]); }
    Assert.Equal(2, await Root(services, data).InvokeAsync([.. args], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.False(json.RootElement.GetProperty("isComplete").GetBoolean());
    Assert.True(json.RootElement.TryGetProperty("error", out _));
    Assert.False(Directory.Exists(data));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [CliNativeFact]
  public async Task Native_Analysis_Is_Explicit_And_Reports_The_Pinned_Version()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR"))]);
    using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(0, await Root(services, DataRoot()).InvokeAsync(["analyze", dataset.Root, "--engine", "czkawka"], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.Equal(ContentFingerprints.Blake3, json.RootElement.GetProperty("engine").GetProperty("algorithm").GetString());
    Assert.Equal(NativeAnalysisEngine.UpstreamCommit, json.RootElement.GetProperty("engine").GetProperty("upstreamCommit").GetString());
    Assert.Equal(2, json.RootElement.GetProperty("exactGroups").GetArrayLength());
  }

  [Fact]
  public async Task Comparison_Reports_Independent_Algorithms_Normalized_Groups_And_Process_Metrics()
  {
    using var dataset = new KnownAnswerDataset();
    using var services = new ServiceCollection().AddSingleton(AnalysisEngineRegistry.ManagedOnly()).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(0, await Root(services, DataRoot()).InvokeAsync(
      ["compare-engines", dataset.Root, "--second", "csharp", "--second-algorithm", "sha256-v1"], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.True(json.RootElement.GetProperty("groupsAgree").GetBoolean());
    Assert.True(json.RootElement.GetProperty("observationsMatch").GetBoolean());
    Assert.Equal(2, json.RootElement.GetProperty("matchingGroups").GetInt32());
    Assert.NotEqual(json.RootElement.GetProperty("first").GetProperty("indexPath").GetString(), json.RootElement.GetProperty("second").GetProperty("indexPath").GetString());
    Assert.True(json.RootElement.GetProperty("second").GetProperty("metrics").TryGetProperty("processReadBytes", out _));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [CliNativeFact]
  public async Task Image_Command_Produces_Review_Pairs_With_Defined_Distance_And_No_Exact_Groups()
  {
    using var tree = new TempTree();
    var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images");
    File.Copy(Path.Combine(fixture, "a-original.png"), tree.PathOf("original.png"));
    File.Copy(Path.Combine(fixture, "a-resize.png"), tree.PathOf("resize.png"));
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR"))]);
    using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(0, await Root(services, DataRoot()).InvokeAsync(["similar-images", tree.Root, "--distance", "6"], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.True(json.RootElement.GetProperty("isComplete").GetBoolean());
    Assert.Equal(1, json.RootElement.GetProperty("pairs").GetArrayLength());
    Assert.Equal(0, json.RootElement.GetProperty("pairs")[0].GetProperty("distance").GetInt32());
    Assert.Equal(0, json.RootElement.GetProperty("analysis").GetProperty("exactGroups").GetArrayLength());
    Assert.Contains("Hamming", json.RootElement.GetProperty("distanceDefinition").GetString(), StringComparison.Ordinal);
  }

  [Fact]
  public async Task An_Unsupported_Image_Engine_Is_Diagnosed_Before_An_Index_Is_Created()
  {
    using var tree = new TempTree();
    var data = DataRoot();
    using var services = new ServiceCollection().AddSingleton(AnalysisEngineRegistry.ManagedOnly()).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(2, await Root(services, data).InvokeAsync(["similar-images", tree.Root, "--engine", "csharp"], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.Equal("Unsupported", json.RootElement.GetProperty("outcome").GetString());
    Assert.False(json.RootElement.GetProperty("isComplete").GetBoolean());
    Assert.False(Directory.Exists(data));
  }

  [Fact]
  public async Task Missing_Comparison_Engine_Is_Diagnosed_Before_Reading_The_First_Engine_Scope()
  {
    using var dataset = new KnownAnswerDataset();
    var data = DataRoot();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Path.Combine(data, "missing"))]);
    using var services = new ServiceCollection().AddSingleton(registry).BuildServiceProvider();
    var console = new TestConsole();
    Assert.Equal(2, await Root(services, data).InvokeAsync(["compare-engines", dataset.Root], console));
    using var json = JsonDocument.Parse(console.Out.ToString()!);
    Assert.Equal("MissingDependency", json.RootElement.GetProperty("outcome").GetString());
    Assert.False(Directory.Exists(data));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public void Legacy_Scan_Defaults_Stay_Unchanged_And_Do_Not_Resolve_The_Optional_Native_Engine()
  {
    using var dataset = new KnownAnswerDataset();
    var resolved = false;
    using var services = new ServiceCollection().AddSingleton(_ => { resolved = true; return AnalysisEngineRegistry.CreateDefault(); }).BuildServiceProvider();
    var root = Root(services, DataRoot());
    var legacy = ScanCommand.Create(services);
    root.AddCommand(legacy);
    var parsed = root.Parse(["scan", dataset.Root]);
    Assert.Empty(parsed.Errors);
    Assert.Equal("sha256", parsed.GetValueForOption((Option<string>)legacy.Options.Single(option => option.Name == "algorithm")));
    Assert.Equal("content", parsed.GetValueForOption((Option<string>)legacy.Options.Single(option => option.Name == "match-mode")));
    Assert.True(parsed.GetValueForOption((Option<bool>)legacy.Options.Single(option => option.Name == "recursive")));
    Assert.False(parsed.GetValueForOption((Option<bool>)legacy.Options.Single(option => option.Name == "cleanup-empty")));
    Assert.Equal("all", parsed.GetValueForOption((Option<string>)legacy.Options.Single(option => option.Name == "format")));
    Assert.Equal("./fma-results/", parsed.GetValueForOption((Option<string>)legacy.Options.Single(option => option.Name == "output")));
    Assert.False(resolved);
  }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class CliNativeFactAttribute : FactAttribute
{
  public CliNativeFactAttribute()
  {
    if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FMA_NATIVE_LIBRARY_DIR"))) { Skip = "Optional native CLI integration: run Build-NativeAnalysis.ps1 -Test."; }
  }
}
