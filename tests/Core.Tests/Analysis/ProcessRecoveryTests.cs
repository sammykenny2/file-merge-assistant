using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class ProcessRecoveryTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly string _index = Path.Combine(TestEnvironment.DataRoot, $"process-recovery-{Guid.NewGuid():N}.db");
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  public void Dispose() => _tree.Dispose();

  [NativeFact]
  public async Task Process_Loss_During_Native_Upload_Recovers_The_Original_Snapshot_And_Committed_Checkpoints()
  {
    var realDirectory = Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable)!;
    var blockingDirectory = Environment.GetEnvironmentVariable("FMA_CANCEL_UPLOAD_LIBRARY_DIR");
    Assert.False(string.IsNullOrWhiteSpace(blockingDirectory), "Build native test fixtures with Build-NativeAnalysis.ps1 -Test.");
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), new NativeAnalysisEngine(realDirectory)]);
    for (var i = 0; i < 450; i++) { _tree.Write($@"source\file-{i:0000}.txt", "fixture data"); }
    using (var baseline = IndexedWorkbenchStore.Open(_index, new Volumes(), context: null, engines: registry))
    {
      Assert.Empty(await baseline.AddSourcesAsync([_tree.PathOf("source")], new SourceOptions(true, true, []), startScan: true));
      await baseline.WaitForScansAsync(Timeout);
    }

    string jobId, snapshot;
    using (var queued = IndexedWorkbenchStore.Open(_index, new Volumes(), context: null, startWorker: false, engines: registry))
    {
      queued.SelectNewJobEngine(registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3));
      await queued.ScanSourceAsync(queued.Sources.Single().Id);
      var job = queued.Jobs.Single(job => job.State == ScanJobState.Queued);
      jobId = job.Id;
      snapshot = job.Snapshot!.Serialize();
    }

    var markerPath = _tree.PathOf("native-entered.json");
    await using (var child = ScanHostProcess.Start("interrupt", _index, realDirectory, blockingDirectory!, markerPath))
    {
      var marker = await child.Marker(markerPath);
      Assert.Equal(child.Id, marker.GetProperty("processId").GetInt32());
      Assert.Equal(200L, marker.GetProperty("staged").GetInt64());
      await child.Terminate();
    }

    using (var recovered = IndexedWorkbenchStore.Open(_index, new Volumes(), context: null, startWorker: false))
    {
      var job = recovered.Jobs.Single(job => job.Id == jobId);
      Assert.Equal(ScanJobState.Interrupted, job.State);
      Assert.Equal(snapshot, job.Snapshot!.Serialize());
      Assert.Equal(450L, job.Saved);
      Assert.False(job.AnalysisComplete);
      using var connection = IndexDatabase.OpenOrCreate(_index).Open();
      Assert.Equal(450L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
      Assert.Equal(200L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM analysis_results WHERE job_id=$id AND generation=1", ("$id", jobId)));
      await recovered.ResumeJobAsync(jobId);
      recovered.StartScanning();
      await recovered.WaitForScansAsync(Timeout);
      recovered.RefreshNow();
      Assert.Equal(ScanJobState.WaitingForEngine, recovered.Jobs.Single(job => job.Id == jobId).State);
      Assert.Equal(450L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    }

    using var resumed = IndexedWorkbenchStore.Open(_index, new Volumes(), context: null, startWorker: false, engines: registry);
    await resumed.ResumeJobAsync(jobId);
    resumed.StartScanning();
    await resumed.WaitForScansAsync(Timeout);
    resumed.RefreshNow();
    var finished = resumed.Jobs.Single(job => job.Id == jobId);
    Assert.Equal(ScanJobState.Completed, finished.State);
    Assert.Equal(snapshot, finished.Snapshot!.Serialize());
    Assert.Equal(2L, finished.AnalysisGeneration);
    Assert.True(finished.AnalysisComplete);
    using var final = IndexDatabase.OpenOrCreate(_index).Open();
    Assert.Equal(450L, IndexDatabase.Scalar<long>(final, "SELECT COUNT(*) FROM files WHERE hash LIKE 'blake3-v1:%'"));
    Assert.Equal(900L, IndexDatabase.Scalar<long>(final, "SELECT COUNT(*) FROM file_fingerprints"));
    Assert.All(Directory.GetFiles(_tree.PathOf("source")), path => Assert.Equal("fixture data", File.ReadAllText(path)));
  }
}
