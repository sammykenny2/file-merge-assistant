using FileWorkbench.Analysis;
using FileWorkbench.Scanning;
using FileWorkbench.Workbench;
using FileWorkbench.Index;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class AnalysisEngineComparisonTests
{
  private static string DataRoot() => Path.Combine(TestEnvironment.DataRoot, "comparison-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public async Task Independent_Configurations_Compare_Normalized_Groups_Without_Creating_A_Plan()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync([dataset.Root], a, b);
    Assert.True(report.IsComplete, report.ToJson());
    Assert.True(report.GroupsAgree);
    Assert.True(report.ObservationsMatch);
    Assert.Equal(2, report.MatchingGroups);
    Assert.Empty(report.OnlyInFirst);
    Assert.Empty(report.OnlyInSecond);
    Assert.NotEqual(report.First.IndexPath, report.Second!.IndexPath);
    Assert.NotEqual(report.First.Engine.Algorithm, report.Second.Engine.Algorithm);
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  private sealed class ChangeBeforeSecond(KnownAnswerDataset dataset) : IProgress<AnalysisComparisonProgress>
  {
    private bool _changed;
    public void Report(AnalysisComparisonProgress value)
    {
      if (value.Pass != 2 || _changed) { return; }
      _changed = true;
      File.WriteAllText(dataset.Files[0].Path, "external change between engines");
    }
  }

  private sealed class BeforeSecond(Action action) : IProgress<AnalysisComparisonProgress>
  {
    private bool _called;
    public void Report(AnalysisComparisonProgress value)
    {
      if (value.Pass == 2 && !_called) { _called = true; action(); }
    }
  }

  private sealed class CancelNormalization(CancellationTokenSource cancel) : IProgress<AnalysisComparisonProgress>
  {
    public void Report(AnalysisComparisonProgress value)
    {
      if (value.Stage == "Comparison") { cancel.Cancel(); }
    }
  }

  [Fact]
  public async Task Cancellation_During_Comparison_Retains_Both_Indexes_Without_Claiming_Agreement()
  {
    using var dataset = new KnownAnswerDataset();
    using var cancel = new CancellationTokenSource();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync([dataset.Root], a, b, new CancelNormalization(cancel), cancel.Token);
    Assert.Equal("Canceled", report.Outcome);
    Assert.False(report.GroupsAgree);
    Assert.True(report.First.IsComplete);
    Assert.True(report.Second!.IsComplete);
    Assert.True(File.Exists(report.First.IndexPath));
    Assert.True(File.Exists(report.Second.IndexPath));
  }

  [Fact]
  public async Task Workbench_Comparison_Freezes_Custom_Exclusions_And_Included_Regenerable_Folders_In_Both_Runs()
  {
    using var tree = new TempTree();
    tree.Write("source/node_modules/one.txt", "same");
    tree.Write("source/node_modules/two.txt", "same");
    tree.Write("source/private/one.txt", "hide");
    tree.Write("source/private/two.txt", "hide");
    tree.Write("source/allowed.tmp", "solo");
    var source = tree.PathOf("source");
    var index = tree.PathOf("source/app-data/main.db");
    var registry = AnalysisEngineRegistry.ManagedOnly();
    using var store = IndexedWorkbenchStore.Open(index, startWorker: false, engines: registry);
    Assert.Empty(await store.AddSourcesAsync([source], new SourceOptions(false, true, [source]), startScan: false));
    using (var connection = IndexDatabase.OpenOrCreate(index).Open())
    {
      IndexDatabase.Execute(connection, "UPDATE sources SET exclusions='private'");
    }
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    // Mutating the live policy between passes must not broaden the frozen comparison scope.
    var report = await store.CompareEnginesAsync(a, b, new BeforeSecond(() =>
    {
      using var connection = IndexDatabase.OpenOrCreate(index).Open();
      IndexDatabase.Execute(connection, "UPDATE sources SET exclusions='',skip_regenerable=1");
    }));
    Assert.True(report.GroupsAgree, report.ToJson());
    foreach (var pass in new[] { report.First, report.Second! })
    {
      Assert.Equal(3, pass.Files.Length);
      Assert.Contains(pass.Files, file => file.Path.EndsWith("allowed.tmp", StringComparison.Ordinal));
      Assert.DoesNotContain(pass.Files, file => file.Path.Contains("private", StringComparison.Ordinal) || file.Path.Contains("app-data", StringComparison.Ordinal));
      var policy = pass.Jobs.Single().Snapshot.Sources.Single();
      Assert.False(policy.Source.SkipRegenerable);
      Assert.True(policy.Protected);
      Assert.Equal(["private"], policy.Source.Exclusions);
      Assert.Equal(2, Assert.Single(pass.ExactGroups).FileIds.Length);
    }
    Assert.Empty(store.Jobs);
    Assert.Empty(store.Plan);
    Assert.Empty(store.History);
    Assert.Equal("hide", File.ReadAllText(tree.PathOf("source/private/one.txt")));
  }

  [Fact]
  public async Task A_Second_Run_Preflight_Failure_Retains_The_First_Report_Without_Claiming_Comparison_Success()
  {
    using var dataset = new KnownAnswerDataset();
    using var moved = new TempTree();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync([dataset.Root], a, b,
      new BeforeSecond(() => Directory.Move(dataset.Root, moved.PathOf("relocated-source"))));
    Assert.True(report.First.IsComplete);
    Assert.Null(report.Second);
    Assert.Equal("Partial", report.Outcome);
    Assert.False(report.IsComplete);
    Assert.False(report.GroupsAgree);
    Assert.NotNull(report.Error);
    Assert.True(File.Exists(report.First.IndexPath));
  }

  [Fact]
  public async Task Changing_The_Callers_Path_List_Does_Not_Expand_The_Second_Run_Scope()
  {
    using var dataset = new KnownAnswerDataset();
    using var other = new KnownAnswerDataset();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var paths = new List<string> { dataset.Root };
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync(paths, a, b,
      new BeforeSecond(() => paths[0] = other.Root));
    Assert.True(report.GroupsAgree);
    Assert.All(report.Second!.Files, file => Assert.StartsWith(dataset.Root, file.Path, StringComparison.Ordinal));
    Assert.All(other.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public async Task Changed_Observations_Produce_An_Incomparable_Report_Instead_Of_A_Claim_About_Engine_Accuracy()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync([dataset.Root], a, b, new ChangeBeforeSecond(dataset));
    Assert.True(report.First.IsComplete);
    Assert.True(report.Second!.IsComplete);
    Assert.Equal("Incomparable", report.Outcome);
    Assert.False(report.GroupsAgree);
    Assert.False(report.ObservationsMatch);
    Assert.Contains("not an atomic", report.ObservationRule, StringComparison.Ordinal);
  }

  [NativeFact]
  public async Task Managed_And_Native_Known_Answers_Agree_With_Separate_Time_Io_And_Memory_Records()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]);
    var a = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var b = registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
    var report = await new AnalysisEngineComparison(registry, DataRoot()).RunAsync([dataset.Root], a, b);
    Assert.True(report.GroupsAgree, report.ToJson());
    Assert.Equal(2, report.MatchingGroups);
    Assert.NotNull(report.First.Metrics.ProcessReadBytes);
    Assert.NotNull(report.Second!.Metrics.ProcessReadBytes);
    Assert.True(report.Second.Metrics.ProcessLifetimePeakWorkingSetBytes > 0);
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
    if (Environment.GetEnvironmentVariable("FWB_COMPARISON_REPORT_DIR") is { } reports)
    {
      Directory.CreateDirectory(reports);
      await File.WriteAllTextAsync(Path.Combine(reports, "known-answer-comparison.json"), report.ToJson());
    }
  }
}
