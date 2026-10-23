using System.Runtime.CompilerServices;
using System.Diagnostics;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Tests.Scanning;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class ReadOnlyAnalysisSessionTests
{
  private static string DataRoot() => Path.Combine(TestEnvironment.DataRoot, "sessions-" + Guid.NewGuid().ToString("N"));

  [Theory]
  [InlineData(ContentFingerprints.XxHash128)]
  [InlineData(ContentFingerprints.Sha256)]
  public async Task Known_Answers_Include_The_Full_Inventory_And_Separate_Versioned_Results(string algorithm)
  {
    using var dataset = new KnownAnswerDataset();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var snapshot = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, algorithm);
    var report = await new ReadOnlyAnalysisSession(registry, DataRoot()).RunAsync(
      [Path.Combine(dataset.Root, "primary"), Path.Combine(dataset.Root, "backup")], snapshot);
    AssertReport(dataset, report);
    Assert.True(File.Exists(report.IndexPath));
    using var connection = IndexDatabase.OpenOrCreate(report.IndexPath).Open();
    Assert.Equal(7L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM decisions"));
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM batches"));
    Assert.True(report.Metrics.WorkingSetBeforeBytes > 0);
    Assert.NotNull(report.Metrics.ProcessReadBytes);
    Assert.Contains("index I/O", report.Metrics.Scope, StringComparison.Ordinal);
    Assert.Contains("not controlled", report.Metrics.CachePolicy, StringComparison.Ordinal);
  }

  [NativeFact]
  public async Task Native_Session_Independently_Matches_The_Oracle_And_Does_Not_Change_Sources()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = new AnalysisEngineRegistry([new ManagedAnalysisEngine(),
      new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]);
    var snapshot = registry.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
    var report = await new ReadOnlyAnalysisSession(registry, DataRoot()).RunAsync(
      [Path.Combine(dataset.Root, "primary"), Path.Combine(dataset.Root, "backup")], snapshot);
    AssertReport(dataset, report);
    Assert.Equal(NativeAnalysisEngine.UpstreamCommit, report.Engine.UpstreamCommit);
    Assert.All(report.Files.Where(file => file.Fingerprint is not null), file => Assert.StartsWith("blake3-v1:", file.Fingerprint, StringComparison.Ordinal));
  }

  private static void AssertReport(KnownAnswerDataset dataset, AnalysisSessionReport report)
  {
    Assert.True(report.IsComplete, report.ToJson());
    Assert.Equal(1, report.FormatVersion);
    Assert.Equal(7, report.Files.Length);
    Assert.Equal(2, report.Files.Select(file => file.SourceId).Distinct(StringComparer.Ordinal).Count());
    var originalIds = dataset.Files.ToDictionary(file => file.Path, file => file.Id, StringComparer.OrdinalIgnoreCase);
    var currentIds = report.Files.ToDictionary(file => file.Id, file => originalIds[file.Path]);
    var groups = report.ExactGroups.Select(group => string.Join(',', group.FileIds.Select(id => currentIds[id]).Order())).Order(StringComparer.Ordinal);
    Assert.Equal(dataset.ExactGroups.Select(group => string.Join(',', group)).Order(StringComparer.Ordinal), groups);
    Assert.All(report.Jobs, job => Assert.True(job.Complete));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public async Task Missing_Engine_Rejects_Before_Creating_An_Index_Or_Reading_Source_Files()
  {
    using var dataset = new KnownAnswerDataset();
    var root = DataRoot();
    var native = new NativeAnalysisEngine(Path.Combine(root, "missing-native"));
    var registry = new AnalysisEngineRegistry([native]);
    var descriptor = native.Descriptor;
    var snapshot = new AnalysisEngineSnapshot(descriptor.Id, descriptor.Version, descriptor.UpstreamCommit, descriptor.ContractVersion,
      descriptor.AbiVersion, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3, "{}", "{}");
    await Assert.ThrowsAsync<AnalysisEngineUnavailableException>(() => new ReadOnlyAnalysisSession(registry, root).RunAsync([dataset.Root], snapshot));
    Assert.False(Directory.Exists(root));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public async Task An_Aliased_Data_Folder_Inside_A_Source_Is_Frozen_As_A_Physical_Exclusion()
  {
    using var dataset = new KnownAnswerDataset();
    using var aliases = new TempTree();
    var target = Path.Combine(dataset.Root, "analysis-output");
    Directory.CreateDirectory(target);
    var alias = aliases.PathOf("data-alias");
    var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe"))
    {
      UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
    };
    start.ArgumentList.Add("-NoProfile");
    start.ArgumentList.Add("-NonInteractive");
    start.ArgumentList.Add("-Command");
    start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + alias.Replace("'", "''", StringComparison.Ordinal)
      + "' -Target '" + target.Replace("'", "''", StringComparison.Ordinal) + "' -ErrorAction Stop | Out-Null");
    using (var create = Process.Start(start)!)
    {
      await create.WaitForExitAsync();
      Assert.True(create.ExitCode == 0, await create.StandardError.ReadToEndAsync());
    }
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var snapshot = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    // The leaf does not exist before the session; resolving only the constructor input would miss this alias.
    var report = await new ReadOnlyAnalysisSession(registry, Path.Combine(alias, "new-leaf")).RunAsync([dataset.Root], snapshot);
    Assert.True(report.IsComplete, report.ToJson());
    Assert.Equal(7, report.Files.Length);
    Assert.DoesNotContain(report.Files, file => file.Path.Contains("analysis-output", StringComparison.Ordinal));
    Assert.Contains(report.Jobs.Single().Snapshot.Sources.Single().Source.ExcludedRoots,
      root => root.StartsWith(target, StringComparison.OrdinalIgnoreCase));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  [Fact]
  public async Task An_App_Data_Folder_Cannot_Be_Analyzed_Or_Receive_A_New_Index_As_A_Source()
  {
    using var dataset = new KnownAnswerDataset();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var snapshot = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    await Assert.ThrowsAsync<ArgumentException>(() => new ReadOnlyAnalysisSession(registry, dataset.Root).RunAsync([dataset.Root], snapshot));
    Assert.Empty(Directory.GetFiles(dataset.Root, "analysis-*.db"));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }

  private sealed class CancelEngine : IFileAnalysisEngine
  {
    public AnalysisEngineDescriptor Descriptor { get; } = new ManagedAnalysisEngine().Descriptor;
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      Started.TrySetResult();
      try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        Stopping.TrySetResult();
        await Release.Task;
      }
      cancellationToken.ThrowIfCancellationRequested();
      yield break;
    }
  }

  private sealed class CancelWhenCompleted(CancellationTokenSource cancel) : IProgress<System.Collections.Immutable.ImmutableArray<AnalysisSessionJob>>
  {
    public void Report(System.Collections.Immutable.ImmutableArray<AnalysisSessionJob> jobs)
    {
      if (!jobs.IsEmpty && jobs.All(job => job.State == FileMergeAssistant.Sources.ScanJobState.Completed)) { cancel.Cancel(); }
    }
  }

  [Fact]
  public async Task Cancellation_After_Worker_Completion_Does_Not_Claim_A_Completed_Report()
  {
    using var dataset = new KnownAnswerDataset();
    using var cancel = new CancellationTokenSource();
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var snapshot = registry.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var report = await new ReadOnlyAnalysisSession(registry, DataRoot()).RunAsync([dataset.Root], snapshot, new CancelWhenCompleted(cancel), cancel.Token);
    Assert.Equal("Canceled", report.Outcome);
    Assert.False(report.IsComplete);
    Assert.All(report.Jobs, job => Assert.True(job.Complete));
    using var connection = IndexDatabase.OpenOrCreate(report.IndexPath).Open();
    Assert.Equal(7L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
  }

  [Fact]
  public async Task Session_Cancellation_Waits_For_Acknowledgement_And_Preserves_Saved_Inventory()
  {
    using var dataset = new KnownAnswerDataset();
    var engine = new CancelEngine();
    var registry = new AnalysisEngineRegistry([engine]);
    var snapshot = registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    using var cancel = new CancellationTokenSource();
    var running = new ReadOnlyAnalysisSession(registry, DataRoot()).RunAsync([dataset.Root], snapshot, cancellationToken: cancel.Token);
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
    Assert.False(report.IsComplete);
    using var connection = IndexDatabase.OpenOrCreate(report.IndexPath).Open();
    Assert.Equal(7L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files"));
    Assert.Empty(report.ExactGroups);
    Assert.True(File.Exists(report.IndexPath));
    Assert.All(dataset.Files, file => Assert.Equal(file.Bytes, File.ReadAllBytes(file.Path)));
  }
}
