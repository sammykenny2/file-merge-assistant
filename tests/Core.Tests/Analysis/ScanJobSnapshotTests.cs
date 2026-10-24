using System.Runtime.CompilerServices;
using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Scanning;
using FileWorkbench.Sources;
using FileWorkbench.Tests.Scanning;
using FileWorkbench.Workbench;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class ScanJobSnapshotTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly string _index = Path.Combine(TestEnvironment.DataRoot, $"jobs-{Guid.NewGuid():N}.db");
  private readonly Volumes _volumes = new();
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

  private sealed class Volumes : IVolumeInfoProvider
  {
    public string Serial { get; set; } = "FIXTURE";
    public bool SwitchAfterFirstProbe { get; set; }
    public int Probes;
    public VolumeInfo? Describe(string path)
    {
      var serial = SwitchAfterFirstProbe && Interlocked.Increment(ref Probes) > 1 ? "OTHER" : Serial;
      return new(Path.GetPathRoot(path)!, serial, "fixture", VolumeKind.Fixed);
    }
  }

  public void Dispose() => _tree.Dispose();

  private IndexedWorkbenchStore Open(AnalysisEngineRegistry? engines = null) =>
    IndexedWorkbenchStore.Open(_index, _volumes, context: null, startWorker: false, engines: engines);

  private async Task Add(IndexedWorkbenchStore store, params string[] folders)
  {
    foreach (var folder in folders)
    {
      _tree.Write(Path.Combine(folder, "one.txt"), "same content");
      _tree.Write(Path.Combine(folder, "two.txt"), "same content");
    }
    Assert.Empty(await store.AddSourcesAsync(folders.Select(_tree.PathOf).ToArray(), new SourceOptions(true, true, []), startScan: true));
  }

  private void Execute(string sql, params (string Name, object? Value)[] values)
  {
    using var connection = IndexDatabase.OpenOrCreate(_index).Open();
    IndexDatabase.Execute(connection, sql, values);
  }

  [Fact]
  public async Task New_Job_Selection_Persists_Without_Rewriting_Existing_Queued_Snapshots()
  {
    string oldId, oldSnapshot;
    using (var store = Open())
    {
      await Add(store, "a");
      oldId = store.Jobs.Single().Id;
      oldSnapshot = store.Jobs.Single().Snapshot!.Serialize();
      store.SelectNewJobEngine(store.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256));
    }
    using var reopened = Open();
    Assert.Equal(ContentFingerprints.Sha256, reopened.NewJobEngine.Algorithm);
    Assert.Null(reopened.AnalysisConfigurationError);
    Assert.Equal(oldSnapshot, reopened.Jobs.Single().Snapshot!.Serialize());
    await Add(reopened, "b");
    Assert.Equal(ContentFingerprints.Sha256, reopened.Jobs.Single(job => job.Id != oldId).Snapshot!.Engine.Algorithm);
    Assert.Equal(oldSnapshot, reopened.Jobs.Single(job => job.Id == oldId).Snapshot!.Serialize());
  }

  [Fact]
  public async Task Corrupt_Saved_Selection_Is_Diagnosable_And_Never_Silently_Falls_Back()
  {
    IndexDatabase.OpenOrCreate(_index).SetMeta("analysis.default", "not-json");
    using var store = Open();
    Assert.NotNull(store.AnalysisConfigurationError);
    Assert.Equal("invalid-configuration", store.NewJobEngine.EngineId);
    await Add(store, "a");
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    var oldId = store.Jobs.Single().Id;
    Assert.Equal(ScanJobState.WaitingForEngine, store.Jobs.Single().State);
    Assert.Equal(0L, store.Sources.Single().IndexedFiles);
    store.SelectNewJobEngine(store.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128));
    Assert.Null(store.AnalysisConfigurationError);
    await store.RestartJobAsync(oldId);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.Canceled, store.Jobs.Single(job => job.Id == oldId).State);
    Assert.Equal(ScanJobState.Completed, store.Jobs.Single(job => job.Id != oldId).State);
  }

  [Fact]
  public async Task Queued_Scope_And_Engine_Stay_Fixed_Across_Settings_Changes_And_Reopen()
  {
    string original, jobId;
    using (var store = Open())
    {
      await Add(store, "a", "b");
      Assert.Equal(2, store.Jobs.Count);
      Assert.All(store.Jobs, job => Assert.Equal(2, job.Snapshot!.Sources.Length));
      var job = store.Jobs[0];
      jobId = job.Id;
      original = job.Snapshot!.Serialize();
      store.SelectNewJobEngine(store.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256));
      Execute("UPDATE sources SET exclusions='*.txt', skip_regenerable=0; UPDATE jobs SET state='Paused' WHERE id=$id", ("$id", jobId));
    }
    using var reopened = Open();
    reopened.SelectNewJobEngine(reopened.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256));
    Assert.Equal(original, reopened.Jobs.Single(job => job.Id == jobId).Snapshot!.Serialize());
    await reopened.ResumeJobAsync(jobId);
    reopened.StartScanning();
    await reopened.WaitForScansAsync(Timeout);
    reopened.RefreshNow();
    Assert.All(reopened.Jobs, job => Assert.Equal(ScanJobState.Completed, job.State));
    Assert.Equal(original, reopened.Jobs.Single(job => job.Id == jobId).Snapshot!.Serialize());
    Assert.Equal(4L, reopened.Sources.Sum(source => source.IndexedFiles));
    using var connection = IndexDatabase.OpenOrCreate(_index).Open();
    Assert.Equal(4L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
  }

  [Fact]
  public async Task Missing_Original_Version_Waits_And_Restart_Uses_A_New_Job_Id()
  {
    using var store = Open();
    await Add(store, "a");
    var original = store.Jobs.Single();
    var obsolete = original.Snapshot! with { Engine = original.Snapshot!.Engine with { EngineVersion = "obsolete" } };
    Execute("UPDATE jobs SET analysis_snapshot=$snapshot WHERE id=$id", ("$snapshot", obsolete.Serialize()), ("$id", original.Id));
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.WaitingForEngine, store.Jobs.Single().State);
    Assert.Equal(0L, store.Sources.Single().IndexedFiles);
    store.SelectNewJobEngine(store.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256));
    await store.ResumeJobAsync(original.Id);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal("obsolete", store.Jobs.Single().Snapshot!.Engine.EngineVersion);
    Assert.Equal(ScanJobState.WaitingForEngine, store.Jobs.Single().State);
    await store.RestartJobAsync(original.Id);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.Canceled, store.Jobs.Single(job => job.Id == original.Id).State);
    var replacement = store.Jobs.Single(job => job.Id != original.Id);
    Assert.Equal(ScanJobState.Completed, replacement.State);
    Assert.Equal(ContentFingerprints.Sha256, replacement.Snapshot!.Engine.Algorithm);
  }

  [Fact]
  public async Task Malformed_And_Legacy_Queued_Jobs_Do_Not_Block_The_Queue_Or_Infer_An_Engine()
  {
    using var store = Open();
    await Add(store, "a", "b");
    var first = store.Jobs[0];
    Execute("UPDATE jobs SET analysis_snapshot=$snapshot WHERE id=$id", ("$id", first.Id),
      ("$snapshot", "{\"version\":1,\"sourceId\":\"broken\",\"engine\":null,\"sources\":[null]}"));
    Execute("UPDATE jobs SET analysis_snapshot=NULL WHERE id=$id", ("$id", store.Jobs[1].Id));
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.All(store.Jobs, job => Assert.Equal(ScanJobState.NeedsNewJob, job.State));
    Assert.All(store.Sources, source => Assert.Equal(0L, source.IndexedFiles));
    await store.ResumeJobAsync(first.Id);
    store.RefreshNow();
    Assert.Equal(ScanJobState.NeedsNewJob, store.Jobs.Single(job => job.Id == first.Id).State);
  }

  [Fact]
  public async Task Replacing_The_Root_On_The_Same_Volume_Does_Not_Scan_The_New_Folder()
  {
    using var store = Open();
    await Add(store, "a");
    Assert.NotNull(store.Jobs.Single().Snapshot!.Sources.Single().RootIdentity);
    Directory.Move(_tree.PathOf("a"), _tree.PathOf("old-a"));
    Directory.CreateDirectory(_tree.PathOf("a"));
    _tree.Write(Path.Combine("a", "new.txt"), "new unrelated source");
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.WaitingForSource, store.Jobs.Single().State);
    Assert.Equal(0L, store.Sources.Single().IndexedFiles);
    Assert.Equal("new unrelated source", File.ReadAllText(_tree.PathOf(Path.Combine("a", "new.txt"))));
  }

  private sealed class ChangedVolumeEngine(Volumes volumes) : IFileAnalysisEngine
  {
    private readonly ManagedAnalysisEngine _managed = new();
    public AnalysisEngineDescriptor Descriptor => _managed.Descriptor;
    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      await foreach (var batch in _managed.AnalyzeAsync(request, cancellationToken))
      {
        volumes.Serial = "OTHER";
        yield return batch;
      }
    }
  }

  private sealed class SlowCancelEngine : IFileAnalysisEngine
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AnalysisEngineDescriptor Descriptor { get; } = new ManagedAnalysisEngine().Descriptor;
    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      try { await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        CancellationSeen.TrySetResult();
        await Release.Task;
      }
      cancellationToken.ThrowIfCancellationRequested();
      yield break;
    }
  }

  private sealed class GatedVolumes : IVolumeInfoProvider
  {
    public bool Block { get; set; }
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public VolumeInfo? Describe(string path)
    {
      if (Block)
      {
        Entered.TrySetResult();
        if (!Release.Task.Wait(Timeout)) { throw new TimeoutException("Test preflight was not released."); }
      }
      return new(Path.GetPathRoot(path)!, Block ? "OTHER" : "FIXTURE", "fixture", VolumeKind.Fixed);
    }
  }

  [Fact]
  public async Task Cancellation_During_A_Slow_Preflight_Wins_Over_Waiting_For_Source()
  {
    var volumes = new GatedVolumes();
    using var store = IndexedWorkbenchStore.Open(_index, volumes, context: null, startWorker: false);
    await Add(store, "a");
    var jobId = store.Jobs.Single().Id;
    volumes.Block = true;
    store.StartScanning();
    try
    {
      await volumes.Entered.Task.WaitAsync(Timeout);
      await store.CancelJobAsync(jobId);
      using var connection = IndexDatabase.OpenOrCreate(_index).Open();
      Assert.Equal("Cancelling", IndexDatabase.Scalar<string>(connection, "SELECT state FROM jobs WHERE id=$id", ("$id", jobId)));
    }
    finally { volumes.Release.TrySetResult(); }
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.Canceled, store.Jobs.Single().State);
    Assert.Equal(0L, store.Sources.Single().IndexedFiles);
    await store.ResumeJobAsync(jobId);
    Assert.Equal(ScanJobState.Canceled, store.Jobs.Single().State);
  }

  [Fact]
  public async Task Cancellation_Remains_Visible_Until_The_Engine_Acknowledges_Stopping()
  {
    var engine = new SlowCancelEngine();
    using var store = Open(new AnalysisEngineRegistry([engine]));
    await Add(store, "a");
    var jobId = store.Jobs.Single().Id;
    store.StartScanning();
    try
    {
      await engine.Started.Task.WaitAsync(Timeout);
      await store.CancelJobAsync(jobId);
      await engine.CancellationSeen.Task.WaitAsync(Timeout);
      store.RefreshNow();
      Assert.Equal(ScanJobState.Cancelling, store.Jobs.Single().State);
      Assert.NotNull(store.Jobs.Single().Snapshot);
    }
    finally { engine.Release.TrySetResult(); }
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.Canceled, store.Jobs.Single().State);
    Assert.Equal(2L, store.Sources.Single().IndexedFiles);
  }

  [Fact]
  public async Task Own_Source_Cannot_Fall_Out_Of_The_Guard_Between_Initial_Probes()
  {
    using var store = Open();
    await Add(store, "a");
    _volumes.SwitchAfterFirstProbe = true;
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.True(_volumes.Probes >= 2);
    Assert.Equal(ScanJobState.WaitingForSource, store.Jobs.Single().State);
    using var connection = IndexDatabase.OpenOrCreate(_index).Open();
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
  }

  [Fact]
  public async Task Volume_Change_During_Analysis_Retains_Inventory_And_Rejects_The_Result_Checkpoint()
  {
    using var store = Open(new AnalysisEngineRegistry([new ChangedVolumeEngine(_volumes)]));
    await Add(store, "a");
    store.StartScanning();
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(ScanJobState.WaitingForSource, store.Jobs.Single().State);
    using var connection = IndexDatabase.OpenOrCreate(_index).Open();
    Assert.Equal(2L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
  }
}
