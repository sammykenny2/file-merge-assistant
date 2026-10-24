using System.Text.Json.Nodes;
using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Plans;
using FileWorkbench.Sources;
using FileWorkbench.Stacks;
using FileWorkbench.Tests.Scanning;
using FileWorkbench.Workbench;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class EnginePlanCompatibilityTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly string _index = Path.Combine(TestEnvironment.DataRoot, $"engine-plan-{Guid.NewGuid():N}.db");
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  public void Dispose() => _tree.Dispose();

  private IndexedWorkbenchStore Open(AnalysisEngineRegistry? engines = null) =>
    IndexedWorkbenchStore.Open(_index, new Volumes(), context: null, engines: engines ?? AnalysisEngineRegistry.ManagedOnly());

  private async Task Prepare(IndexedWorkbenchStore store)
  {
    _tree.Project(@"work\project");
    _tree.Project(@"backup\project");
    _tree.Write(@"backup\project\notes.md", "unique extra file");
    Assert.Empty(await store.AddSourcesAsync([_tree.PathOf("work"), _tree.PathOf("backup")],
      new SourceOptions(true, true, [_tree.PathOf("work")]), startScan: true));
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    var stack = Assert.Single(store.Stacks);
    Assert.Equal(StackKind.Folder, stack.Kind);
    store.Decide(StackDecision.Suggested(stack));
    Assert.Single(store.Plan);
    // Complete the background decision snapshot before a fixture edits its persisted legacy JSON.
    await store.RecheckPlanAsync();
  }

  [Fact]
  public Task Algorithm_Change_Reopens_The_Pending_Plan_And_Operation_Verification_Still_Works() =>
    SwitchExecuteRestore(AnalysisEngineRegistry.ManagedOnly(), ManagedAnalysisEngine.EngineId, ContentFingerprints.Sha256);

  [NativeFact]
  public Task Native_Analysis_History_Can_Restore_In_A_Fresh_Process_With_The_Native_Library_Absent() =>
    SwitchExecuteRestore(new AnalysisEngineRegistry([new ManagedAnalysisEngine(),
      new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]),
      NativeAnalysisEngine.EngineId, ContentFingerprints.Blake3);

  private async Task SwitchExecuteRestore(AnalysisEngineRegistry registry, string engineId, string algorithm)
  {
    string batchId;
    using (var store = Open(registry))
    {
      await Prepare(store);
      var before = store.Stacks.Single();
      var metadata = before.Copies.ToDictionary(copy => copy.Id, copy => copy.Digest);
      var evidence = before.Copies.ToDictionary(copy => copy.Id, copy => copy.EvidenceDigest);
      store.SelectNewJobEngine(registry.Snapshot(engineId, AnalysisCapability.ExactDuplicates, algorithm));
      foreach (var source in store.Sources) { await store.ScanSourceAsync(source.Id); }
      await store.WaitForScansAsync(Timeout);
      store.RefreshNow();
      var after = Assert.Single(store.Stacks);
      Assert.Equal(before.Id, after.Id);
      Assert.Equal(StackStatus.Open, after.Status);
      Assert.Empty(store.Plan);
      Assert.NotEmpty(after.EvidenceChangedCopyIds);
      Assert.All(after.Copies, copy =>
      {
        Assert.Equal(metadata[copy.Id], copy.Digest);
        Assert.NotEqual(evidence[copy.Id], copy.EvidenceDigest);
      });
      // A fresh human decision is necessary after the evidence changes.
      store.Decide(StackDecision.Suggested(after));
      var batch = await store.ExecutePlanAsync(CancellationToken.None);
      Assert.NotEmpty(batch.Items);
      Assert.All(batch.Items, item => Assert.Equal(BatchItemState.Done, item.State));
      batchId = batch.Id;
      Assert.Equal("unique extra file", File.ReadAllText(_tree.PathOf(@"work\project\notes.md")));
      Assert.False(Directory.Exists(_tree.PathOf(@"backup\project")));
      await store.WaitForScansAsync(Timeout);
    }

    if (engineId == NativeAnalysisEngine.EngineId)
    {
      var reportPath = _tree.PathOf("cold-restore.json");
      await using var child = ScanHostProcess.Start("restore", _index, batchId, _tree.PathOf("missing-native"), reportPath);
      await child.ExitSuccessfully();
      var report = await child.Marker(reportPath);
      Assert.Equal(child.Id, report.GetProperty("processId").GetInt32());
      Assert.NotEqual(Environment.ProcessId, report.GetProperty("processId").GetInt32());
      Assert.Equal("MissingDependency", report.GetProperty("nativeAvailability").GetString());
      Assert.Equal(0, report.GetProperty("nativeModules").GetInt32());
      Assert.True(report.GetProperty("restored").GetInt32() > 0);
    }
    // Historical inspection and restore remain independent of the registered analysis engines.
    using var reopened = Open();
    Assert.DoesNotContain(reopened.Engines.Descriptors, descriptor => descriptor.Id == NativeAnalysisEngine.EngineId);
    Assert.Equal(batchId, Assert.Single(reopened.History).Id);
    if (engineId != NativeAnalysisEngine.EngineId) { await reopened.RestoreBatchAsync(batchId); }
    Assert.All(reopened.History.Single().Items, item => Assert.Equal(BatchItemState.Restored, item.State));
    Assert.True(Directory.Exists(_tree.PathOf(@"backup\project")));
    Assert.False(File.Exists(_tree.PathOf(@"work\project\notes.md")));
    Assert.Equal("unique extra file", File.ReadAllText(_tree.PathOf(@"backup\project\notes.md")));
    await reopened.WaitForScansAsync(Timeout);
  }

  [Fact]
  public async Task A_Legacy_Pending_Decision_Without_Content_Evidence_Requires_Review_After_Upgrade()
  {
    using (var store = Open()) { await Prepare(store); }
    using (var connection = IndexDatabase.OpenOrCreate(_index).Open())
    {
      var stored = IndexDatabase.Scalar<string>(connection, "SELECT json FROM decisions")!;
      var legacy = JsonNode.Parse(stored)!.AsObject();
      Assert.True(legacy.Remove("EvidenceDigests"));
      IndexDatabase.Execute(connection, "UPDATE decisions SET json=$json", ("$json", legacy.ToJsonString()));
    }
    using var reopened = Open();
    Assert.Empty(reopened.Plan);
    var stack = Assert.Single(reopened.Stacks);
    Assert.Equal(StackStatus.Open, stack.Status);
    Assert.NotNull(reopened.DecisionFor(stack.Id));
    Assert.NotEmpty(stack.EvidenceChangedCopyIds);
  }
}
