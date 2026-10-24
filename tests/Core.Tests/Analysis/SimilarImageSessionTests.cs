using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Scanning;
using FileWorkbench.Tests.Scanning;
using Xunit;
using System.Runtime.CompilerServices;
using System.Collections.Immutable;

namespace FileWorkbench.Tests.Analysis;

public sealed class SimilarImageSessionTests
{
  private sealed class ConstantImages(bool waitForCancellation = false, Action<AnalysisRequest>? beforeResults = null) : IFileAnalysisEngine
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Calls { get; private set; }
    public List<string> SubmittedPaths { get; } = [];
    public AnalysisEngineDescriptor Descriptor { get; } = new ManagedAnalysisEngine().Descriptor with
    {
      Id = "fixture-images", Name = "Synthetic image test engine",
      Capabilities = [new(AnalysisCapability.SimilarImages, [PerceptualFingerprints.Mean8D8], AnalysisMaturity.Experimental)],
    };
    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      Calls++;
      SubmittedPaths.AddRange(request.Files.Select(file => file.Path));
      if (waitForCancellation)
      {
        Started.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
          Stopping.TrySetResult();
          await Release.Task;
        }
        cancellationToken.ThrowIfCancellationRequested();
      }
      await Task.Yield();
      beforeResults?.Invoke(request);
      yield return new(request.JobId, Descriptor.Id, 1,
        request.Files.Select(file => new AnalysisFileEvidence(file.Id, file.Observation, AnalysisFileStatus.Succeeded,
          PerceptualFingerprints.Mean8D8 + ":123456789abcdef0", null)).ToImmutableArray(), [], new("Done", request.Files.Length, request.Files.Length, 0, null), true, true);
    }
  }

  [Fact]
  public async Task Perceptual_Publication_Preserves_Existing_Exact_Content_Proof()
  {
    using var tree = new TempTree();
    var path = tree.Write("source/one.png", "synthetic input");
    var exact = ContentFingerprints.Compute(path, ContentFingerprints.XxHash128, CancellationToken.None);
    var data = tree.PathOf("data");
    var engine = new ConstantImages(beforeResults: _ =>
    {
      var index = Directory.GetFiles(data, "analysis-*.db").Single();
      using var connection = IndexDatabase.OpenOrCreate(index).Open();
      IndexDatabase.Execute(connection, "UPDATE files SET hash=$hash", ("$hash", exact));
    });
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var report = await new SimilarImageAnalysis(registry, data).RunAsync([tree.PathOf("source")], snapshot);
    Assert.True(report.IsComplete, report.ToJson());
    using var final = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(exact, IndexDatabase.Scalar<string>(final, "SELECT hash FROM files"));
    Assert.StartsWith(PerceptualFingerprints.Mean8D8 + ":", IndexDatabase.Scalar<string>(final, "SELECT hash FROM file_fingerprints"), StringComparison.Ordinal);
    Assert.Empty(report.Analysis.ExactGroups);
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Later_Cohort_Cloud_Eviction_Or_Same_Metadata_Replacement_Is_Rejected_Before_Backend_Upload(bool cloudOnly)
  {
    using var tree = new TempTree();
    for (var i = 0; i < 101; i++) { tree.Write($"source/{i}.png", "synthetic input"); }
    string? changedPath = null;
    FileObservation? original = null;
    var engine = new ConstantImages(beforeResults: request =>
    {
      if (changedPath is not null) { return; }
      var submitted = request.Files.Select(file => file.Path).ToHashSet(StringComparer.Ordinal);
      changedPath = Directory.GetFiles(tree.PathOf("source")).Single(path => !submitted.Contains(path));
      original = FileObservation.Capture(changedPath);
      if (cloudOnly) { File.SetAttributes(changedPath, File.GetAttributes(changedPath) | FileAttributes.Offline); }
      else
      {
        var replacement = changedPath + ".replacement";
        File.WriteAllBytes(replacement, File.ReadAllBytes(changedPath));
        File.SetLastWriteTimeUtc(replacement, new DateTime(original.ModifiedUtcTicks, DateTimeKind.Utc));
        File.Move(replacement, changedPath, overwrite: true);
      }
    });
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot);
    try
    {
      Assert.NotNull(changedPath);
      Assert.DoesNotContain(changedPath, engine.SubmittedPaths);
      var current = FileObservation.Capture(changedPath);
      Assert.Equal(original!.Size, current.Size);
      Assert.Equal(original.ModifiedUtcTicks, current.ModifiedUtcTicks);
      Assert.Equal("Partial", report.Outcome);
      Assert.Empty(report.Pairs);
      Assert.False(report.Analysis.Jobs.Single().Complete);
      Assert.Contains(report.Errors, error => error.Path == changedPath);
      using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
      Assert.Equal(cloudOnly ? 1L : 0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE cloud_only=1"));
      Assert.Equal(100L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM file_fingerprints"));
    }
    finally { if (changedPath is not null && cloudOnly) { File.SetAttributes(changedPath, FileAttributes.Normal); } }
  }

  [Fact]
  public async Task An_Earlier_Checkpoint_Becoming_Cloud_Only_Blocks_Final_Generation_Publication()
  {
    using var tree = new TempTree();
    for (var i = 0; i < 101; i++) { tree.Write($"source/{i}.png", "synthetic input"); }
    string? firstPath = null;
    var engine = new ConstantImages(beforeResults: request =>
    {
      if (firstPath is null) { firstPath = request.Files[0].Path; }
      else { File.SetAttributes(firstPath, File.GetAttributes(firstPath) | FileAttributes.Offline); }
    });
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    try
    {
      var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot);
      Assert.Equal("Partial", report.Outcome);
      Assert.Empty(report.Pairs);
      Assert.False(report.Analysis.Jobs.Single().Complete);
      Assert.Contains(report.Analysis.Files, file => file.Path == firstPath && file.Status == "CloudOnly" && file.Fingerprint is null);
      Assert.Contains(report.Errors, error => error.Path == firstPath && error.Message.Contains("cloud-only", StringComparison.Ordinal));
    }
    finally { if (firstPath is not null) { File.SetAttributes(firstPath, FileAttributes.Normal); } }
  }

  private sealed class BeforeReport(Action action) : IProgress<ImmutableArray<AnalysisSessionJob>>
  {
    private bool _called;
    public void Report(ImmutableArray<AnalysisSessionJob> jobs)
    {
      if (!_called && !jobs.IsEmpty && jobs.All(job => job.State == FileWorkbench.Sources.ScanJobState.Completed))
      {
        _called = true;
        action();
      }
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task Report_Assembly_Rejects_New_Cloud_State_Or_Missing_Perceptual_Coverage_After_Worker_Completion(bool cloudOnly)
  {
    using var tree = new TempTree();
    var path = tree.Write("source/one.png", "synthetic input");
    var data = tree.PathOf("data");
    var engine = new ConstantImages();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    try
    {
      var report = await new SimilarImageAnalysis(registry, data).RunAsync([tree.PathOf("source")], snapshot,
        progress: new BeforeReport(() =>
        {
          if (cloudOnly) { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Offline); }
          else
          {
            using var connection = IndexDatabase.OpenOrCreate(Directory.GetFiles(data, "analysis-*.db").Single()).Open();
            IndexDatabase.Execute(connection, "DELETE FROM file_fingerprints");
          }
        }));
      Assert.True(report.Analysis.Jobs.Single().Complete); // It was complete at the publication checkpoint.
      Assert.Equal("Partial", report.Outcome);
      Assert.Empty(report.Pairs);
      var file = Assert.Single(report.Analysis.Files);
      Assert.Equal(cloudOnly ? "CloudOnly" : "Unconfirmed", file.Status);
      Assert.Null(file.Fingerprint);
    }
    finally { if (cloudOnly) { File.SetAttributes(path, FileAttributes.Normal); } }
  }

  [Fact]
  public async Task Source_Image_Limit_Retains_Inventory_And_Stops_Before_Backend_Upload()
  {
    using var tree = new TempTree();
    for (var i = 0; i <= SimilarImageAnalysis.MaximumImages; i++) { tree.Write($"source/{i}.png", "synthetic input"); }
    var engine = new ConstantImages();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot);
    Assert.False(report.IsComplete);
    Assert.Empty(report.Pairs);
    Assert.Equal(0, engine.Calls);
    Assert.Contains("2000", report.Analysis.Jobs.Single().Reason, StringComparison.Ordinal);
    using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(2001L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM file_fingerprints"));
  }

  [Fact]
  public async Task Image_Cancellation_Waits_For_Backend_Stop_And_Retains_Durable_Inventory()
  {
    using var tree = new TempTree();
    tree.Write("source/one.png", "synthetic input");
    var engine = new ConstantImages(waitForCancellation: true);
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    using var cancel = new CancellationTokenSource();
    var running = new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot, cancellationToken: cancel.Token);
    try
    {
      await engine.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
      cancel.Cancel();
      await engine.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(15));
      Assert.False(running.IsCompleted);
    }
    finally { engine.Release.TrySetResult(); }
    var report = await running.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal("Canceled", report.Outcome);
    Assert.Empty(report.Pairs);
    Assert.False(report.Analysis.Jobs.Single().Complete);
    using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(1L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
  }

  [Fact]
  public async Task Too_Many_Pairs_Is_An_Explicit_Limit_Outcome_And_Does_Not_Return_A_Truncated_Success()
  {
    using var tree = new TempTree();
    for (var i = 0; i < 142; i++) { tree.Write($"source/{i}.png", "synthetic fake-engine input"); }
    var engine = new ConstantImages();
    // The fake explicitly supports only perceptual evidence; product managed support remains unchanged.
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(), engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);
    var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("source")], snapshot);
    Assert.True(report.Analysis.IsComplete, report.ToJson());
    Assert.Equal("LimitExceeded", report.Outcome);
    Assert.False(report.IsComplete);
    Assert.Empty(report.Pairs);
    Assert.Contains("10000", report.Error, StringComparison.Ordinal);
    using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(142L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM file_fingerprints"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
  }
  private static AnalysisEngineRegistry Registry() => new([new ManagedAnalysisEngine(),
    new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]);

  private static AnalysisEngineSnapshot Snapshot(AnalysisEngineRegistry registry) => registry.Snapshot(NativeAnalysisEngine.EngineId,
    AnalysisCapability.SimilarImages, PerceptualFingerprints.Mean8D8);

  private static void CopyFixture(TempTree tree)
  {
    foreach (var image in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "similar-images")))
    {
      if (Path.GetExtension(image) is ".png" or ".jpg")
      {
        var folder = Path.GetFileName(image).Contains("resize", StringComparison.Ordinal) ? "backup" : "primary";
        var destination = tree.PathOf(Path.Combine(folder, Path.GetFileName(image)));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(image, destination);
      }
    }
    tree.Write("primary/notes.txt", "not an image");
  }

  [NativeFact]
  public async Task Persistent_Image_Evidence_Produces_Cross_Source_Pairs_Without_Exact_Hashes_Or_Plans()
  {
    using var tree = new TempTree();
    CopyFixture(tree);
    var registry = Registry();
    var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync(
      [tree.PathOf("primary"), tree.PathOf("backup")], Snapshot(registry));
    Assert.True(report.IsComplete, report.ToJson());
    Assert.Equal(6, report.Pairs.Length);
    Assert.All(report.Pairs, pair => Assert.InRange(pair.Distance, 0, 6));
    Assert.Contains(report.Pairs, pair => report.Analysis.Files.Single(file => file.Id == pair.FirstId).SourceId
      != report.Analysis.Files.Single(file => file.Id == pair.SecondId).SourceId);
    Assert.Empty(report.Analysis.ExactGroups);
    Assert.Equal(8, report.Analysis.Files.Length);
    Assert.Single(report.Analysis.Files, file => file.Status == "NotImage");
    Assert.All(report.Analysis.Jobs, job =>
    {
      Assert.Equal(1, job.Generation);
      Assert.True(job.Complete);
      Assert.Equal(AnalysisCapability.SimilarImages, job.Snapshot.Engine.Capability);
    });
    using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
    Assert.Equal(7L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM file_fingerprints WHERE algorithm='image-mean8-d8-v1'"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM decisions"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM batches"));
    Assert.Empty(report.Errors);
  }

  [NativeFact]
  public async Task Corrupt_Image_Preserves_Inventory_And_Partial_Evidence_But_Does_Not_Publish_Complete_Candidates()
  {
    using var tree = new TempTree();
    CopyFixture(tree);
    tree.Write("primary/broken.png", "not PNG");
    var registry = Registry();
    var report = await new SimilarImageAnalysis(registry, tree.PathOf("data")).RunAsync([tree.PathOf("primary")], Snapshot(registry));
    Assert.Equal("Partial", report.Outcome);
    Assert.Empty(report.Pairs);
    Assert.Contains(report.Errors, error => error.Path.EndsWith("broken.png", StringComparison.Ordinal));
    Assert.False(report.Analysis.Jobs.Single().Complete);
    using var connection = IndexDatabase.OpenOrCreate(report.Analysis.IndexPath).Open();
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
    Assert.Equal(8L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal("not PNG", File.ReadAllText(tree.PathOf("primary/broken.png")));
  }
}
