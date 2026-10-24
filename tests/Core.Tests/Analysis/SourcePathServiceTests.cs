using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class SourcePathServiceTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly Paths _paths = new();
  private readonly Volumes _volumes = new();
  private string IndexPath => _tree.PathOf("state/index.db");
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
  private static readonly FileIdentity Original = new(77, 11, "fixture-volume");
  private static readonly FileIdentity Replacement = new(77, 12, "fixture-volume");

  public void Dispose() => _tree.Dispose();

  private IndexedWorkbenchStore Open(AnalysisEngineRegistry? engines = null) => IndexedWorkbenchStore.Open(IndexPath,
    _volumes, startWorker: false, engines: engines, sourcePaths: _paths);

  private string Source(string name)
  {
    _tree.Write(Path.Combine(name, "a.txt"), "abc");
    _tree.Write(Path.Combine(name, "b.txt"), "abc");
    _tree.Write(Path.Combine(name, "c.txt"), "xyz");
    return _tree.PathOf(name);
  }

  [Theory]
  [InlineData("canonical")]
  [InlineData("identity")]
  [InlineData("inside")]
  [InlineData("app-data")]
  public async Task Registration_Uses_The_Injected_Canonical_And_Physical_Overlap_Rules(string kind)
  {
    var first = Source("a"); var candidate = Source("b");
    using var store = Open();
    Assert.Empty(await store.AddSourcesAsync([first], new SourceOptions(true, true, []), startScan: false));
    switch (kind)
    {
      case "canonical": _paths.Resolved[candidate] = first; break;
      case "identity": _paths.Identities[first] = Original; _paths.Identities[candidate] = Original; break;
      case "inside": _paths.Related = (inner, outer) => _paths.Comparer.Equals(inner, candidate) && _paths.Comparer.Equals(outer, first) ? true : null; break;
      case "app-data": _paths.Resolved[candidate] = Path.GetDirectoryName(IndexPath)!; break;
      default: throw new InvalidOperationException();
    }
    Assert.Single(await store.AddSourcesAsync([candidate], new SourceOptions(true, true, []), startScan: false));
    Assert.Single(store.Sources);
    Assert.Empty(store.Jobs);
    Assert.Equal("abc", File.ReadAllText(_tree.PathOf("a/a.txt")));
    Assert.Equal("abc", File.ReadAllText(_tree.PathOf("b/a.txt")));
  }

  [Fact]
  public async Task Snapshot_Uses_Injected_Identity_And_Rejects_Unknown_Or_Replaced_Root_With_Unchanged_Metadata()
  {
    var root = Source("source"); _paths.Identities[root] = Original;
    using var store = Open();
    Assert.Empty(await store.AddSourcesAsync([root], new SourceOptions(true, true, []), startScan: false));
    using var connection = IndexDatabase.OpenOrCreate(IndexPath).Open();
    var snapshot = ScanJobSnapshot.Capture(connection, store.Sources.Single().Id, store.NewJobEngine, [], _paths);
    var frozen = ScanJobSnapshot.Deserialize(snapshot.Serialize()).Sources.Single();
    Assert.Equal(Original, frozen.RootIdentity);
    var modified = Directory.GetLastWriteTimeUtc(root); var size = new FileInfo(_tree.PathOf("source/a.txt")).Length;
    Assert.True(ScanJobSnapshot.IsOnline(frozen, _volumes, _paths));
    _paths.Identities[root] = Replacement;
    Assert.False(ScanJobSnapshot.IsOnline(frozen, _volumes, _paths));
    _paths.Identities[root] = null;
    Assert.False(ScanJobSnapshot.IsOnline(frozen, _volumes, _paths));
    _paths.Identities[root] = Original;
    Assert.True(ScanJobSnapshot.IsOnline(frozen, _volumes, _paths));
    var calls = _paths.Probes.GetValueOrDefault(root);
    _paths.Missing = root;
    Assert.False(ScanJobSnapshot.IsOnline(frozen, _volumes, _paths));
    Assert.Equal(calls, _paths.Probes.GetValueOrDefault(root));
    Assert.Equal(modified, Directory.GetLastWriteTimeUtc(root));
    Assert.Equal(size, new FileInfo(_tree.PathOf("source/a.txt")).Length);
  }

  [Fact]
  public async Task Worker_Rechecks_The_Same_Provider_Before_Publication_And_Resumes_The_Frozen_Job()
  {
    var root = Source("source"); _paths.Identities[root] = Original;
    var changing = new ChangeDuringAnalysis(() => _paths.Identities[root] = Replacement);
    using var store = Open(new AnalysisEngineRegistry([changing]));
    Assert.Empty(await store.AddSourcesAsync([root], new SourceOptions(true, true, []), startScan: true));
    var job = store.Jobs.Single(); var frozen = job.Snapshot!.Serialize();
    Assert.Equal(Original, job.Snapshot.Sources.Single().RootIdentity);
    store.StartScanning(); await store.WaitForScansAsync(Timeout); store.RefreshNow();
    Assert.Equal(ScanJobState.WaitingForSource, store.Jobs.Single().State);
    using (var connection = IndexDatabase.OpenOrCreate(IndexPath).Open())
    {
      Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
    }
    _paths.Identities[root] = Original;
    await store.ResumeJobAsync(job.Id); await store.WaitForScansAsync(Timeout); store.RefreshNow();
    Assert.Equal(job.Id, store.Jobs.Single().Id);
    Assert.Equal(frozen, store.Jobs.Single().Snapshot!.Serialize());
    Assert.Equal(ScanJobState.Completed, store.Jobs.Single().State);
    Assert.Equal(3L, store.Sources.Single().IndexedFiles);
    using (var connection = IndexDatabase.OpenOrCreate(IndexPath).Open())
    {
      Assert.Equal(3L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
    }
  }

  [Fact]
  public async Task Comparison_And_Its_Isolated_Sessions_Use_The_Same_Injected_Root_Identity()
  {
    var root = Source("source"); _paths.Identities[root] = Original;
    using var store = Open();
    Assert.Empty(await store.AddSourcesAsync([root], new SourceOptions(true, true, []), startScan: false));
    // The physical root is unchanged, but later canonical resolution returns different casing.
    _paths.Resolved[root] = root.ToUpperInvariant();
    var first = store.Engines.Snapshot("csharp", AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var second = store.Engines.Snapshot("csharp", AnalysisCapability.ExactDuplicates, ContentFingerprints.Sha256);
    var report = await store.CompareEnginesAsync(first, second);
    Assert.True(report.IsComplete); Assert.True(report.GroupsAgree);
    Assert.All(report.First.Jobs, job => Assert.Equal(Original, job.Snapshot.Sources.Single().RootIdentity));
    Assert.All(report.Second!.Jobs, job => Assert.Equal(Original, job.Snapshot.Sources.Single().RootIdentity));
    Assert.All(report.First.Jobs, job => Assert.Equal(root, job.Snapshot.Sources.Single().Source.Root));
    Assert.All(report.Second.Jobs, job => Assert.Equal(root, job.Snapshot.Sources.Single().Source.Root));
    Assert.Empty(store.Plan); Assert.Empty(store.History); Assert.Empty(store.Jobs);
  }

  private sealed class Volumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "FIXTURE", "fixture", VolumeKind.Fixed);
  }

  private sealed class Paths : ISourcePathService
  {
    private static WindowsSourcePathService Default => WindowsSourcePathService.Instance;
    public ConcurrentDictionary<string, string> Resolved { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, FileIdentity?> Identities { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, int> Probes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, string, bool?>? Related { get; set; }
    public string? Missing { get; set; }
    public StringComparer Comparer => Default.Comparer;
    public bool DirectoryExists(string path) => !Comparer.Equals(path, Missing) && Default.DirectoryExists(path);
    public string Resolve(string path) => Resolved.TryGetValue(path, out var result) ? result : Default.Resolve(path);
    public FileIdentity? Identify(string path)
    {
      Probes.AddOrUpdate(path, 1, (_, count) => count + 1);
      return Identities.TryGetValue(path, out var result) ? result : Default.Identify(path);
    }
    public bool Same(string first, string second) => Default.Same(first, second);
    public bool Inside(string inner, string outer) => Default.Inside(inner, outer);
    public bool? SameOrInside(string inner, string outer) => Related?.Invoke(inner, outer) ?? Default.SameOrInside(inner, outer);
  }

  private sealed class ChangeDuringAnalysis(Action change) : IFileAnalysisEngine
  {
    private readonly ManagedAnalysisEngine _inner = new();
    private int _changed;
    public AnalysisEngineDescriptor Descriptor => _inner.Descriptor;
    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request,
      [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      if (Interlocked.Exchange(ref _changed, 1) == 0) { change(); }
      await foreach (var batch in _inner.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false)) { yield return batch; }
    }
  }
}
