using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Infrastructure;
using FileWorkbench.Models;
using FileWorkbench.Plans;
using FileWorkbench.Scanning;
using FileWorkbench.Services;
using FileWorkbench.Stacks;
using FileWorkbench.Sources;
using FileWorkbench.Tests.Scanning;
using FileWorkbench.Workbench;
using NSubstitute;
using Serilog;
using Xunit;

namespace FileWorkbench.Tests.Analysis;

public sealed class PathTextTests
{
  private static string Unsupported => ((char)0xd800).ToString();
  private static string DataRoot() => Path.Combine(TestEnvironment.DataRoot, "path-text-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void Valid_Supplementary_And_Replacement_Characters_Are_Distinct_From_Unsupported_Code_Units()
  {
    const string good = "相片-\U0001F600-\ufffd.txt";
    Assert.True(PathText.IsSupported(good));
    Assert.Equal(good, PathText.EscapeUnsupported(good));
    Assert.False(PathText.IsSupported(Unsupported));
    Assert.False(PathText.IsSupported(((char)0xdc00).ToString()));
    Assert.False(PathText.IsSupported("a\0b"));
    Assert.Equal("a\\uD800b\\uDC00", PathText.EscapeUnsupported("a" + Unsupported + "b" + ((char)0xdc00)));
  }

  [Fact]
  public void Contracts_And_Data_Roots_Reject_Unsupported_Text_Before_Encoding_Or_Creating_Files()
  {
    using var dataset = new KnownAnswerDataset();
    var badRoot = Path.Combine(dataset.Root, "folder-" + Unsupported);
    var bad = Path.Combine(badRoot, "file.txt");
    var request = ManagedAnalysisEngineTests.Request(dataset);
    var files = request.Files.SetItem(0, request.Files[0] with { Path = bad });
    Assert.Throws<ArgumentException>(() => AnalysisResultValidator.ValidateRequest(request with { Files = files }));
    Assert.Throws<ArgumentException>(() => FileObservation.Capture(bad));
    Assert.Throws<ArgumentException>(() => AppDataPaths.ResolveRoot(badRoot, dataset.Root));
    Assert.Throws<ArgumentException>(() => IndexDatabase.OpenOrCreate(Path.Combine(badRoot, "index.db")));
    Assert.Throws<ArgumentException>(() => new ReadOnlyAnalysisSession(AnalysisEngineRegistry.ManagedOnly(), badRoot));
    Assert.False(Directory.Exists(badRoot));
    Assert.False(Directory.Exists(badRoot.Replace(Unsupported, "\ufffd", StringComparison.Ordinal)));
  }

  [Fact]
  public async Task A_Registered_Root_Cannot_Be_Substituted_With_A_Replacement_Named_Sibling()
  {
    using var tree = new TempTree();
    var bad = tree.PathOf("folder-" + Unsupported);
    var good = tree.PathOf("folder-\ufffd");
    Directory.CreateDirectory(bad); Directory.CreateDirectory(good);
    var index = Path.Combine(DataRoot(), "index.db");
    using var store = IndexedWorkbenchStore.Open(index, context: null, startWorker: false);
    var errors = await store.AddSourcesAsync([bad, good], new SourceOptions(true, true, []), startScan: false);
    Assert.Single(errors);
    Assert.Contains("\\uD800", errors[0], StringComparison.Ordinal);
    using var connection = IndexDatabase.OpenOrCreate(index).Open();
    Assert.Equal(1L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM sources"));
    Assert.Equal(good, IndexDatabase.Scalar<string>(connection, "SELECT root FROM sources"));
    Assert.True(Directory.Exists(bad));
  }

  [Fact]
  public Task Inventory_Preserves_Valid_Siblings_And_Reports_Unsupported_Entries_As_Partial() => Inventory(AnalysisEngineRegistry.ManagedOnly());

  [NativeFact]
  public Task Native_Inventory_Uses_The_Same_Lossless_Path_Boundary() => Inventory(NativeRegistry());

  private static AnalysisEngineRegistry NativeRegistry() => new([new ManagedAnalysisEngine(),
    new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable))]);

  private static async Task Inventory(AnalysisEngineRegistry registry)
  {
    using var tree = new TempTree();
    var bad = tree.Write(Unsupported + ".txt", "bad");
    var good = tree.Write("\ufffd.txt", "yes");
    var low = ((char)0xdc00).ToString();
    tree.Write(low + "-folder/child.txt", "bad");
    var goodChild = tree.Write("\ufffd-folder/child.txt", "two");
    File.SetLastWriteTimeUtc(bad, File.GetLastWriteTimeUtc(good));
    var native = registry.Descriptors.Any(item => item.Id == "czkawka");
    var engine = registry.Snapshot(native ? "czkawka" : "csharp", AnalysisCapability.ExactDuplicates,
      native ? ContentFingerprints.Blake3 : ContentFingerprints.XxHash128);
    var report = await new ReadOnlyAnalysisSession(registry, DataRoot()).RunAsync([tree.Root], engine);
    Assert.False(report.IsComplete);
    Assert.Equal("Partial", report.Outcome);
    Assert.Equal(new[] { good, goodChild }.Select(Path.GetFullPath).Order(StringComparer.Ordinal), report.Files.Select(file => file.Path).Order(StringComparer.Ordinal));
    Assert.Equal(2L, report.Jobs.Single().Errors);
    using var connection = IndexDatabase.OpenOrCreate(report.IndexPath).Open();
    using var command = IndexDatabase.Command(connection, "SELECT path,message FROM scan_errors");
    using var reader = command.ExecuteReader();
    var messages = new List<string>();
    while (reader.Read()) { Assert.True(PathText.IsSupported(reader.GetString(0))); messages.Add(reader.GetString(1)); }
    Assert.Contains(messages, text => text.Contains("\\uD800", StringComparison.Ordinal));
    Assert.Contains(messages, text => text.Contains("\\uDC00", StringComparison.Ordinal));
    Assert.Equal("yes", File.ReadAllText(good));
    Assert.Equal("bad", File.ReadAllText(bad));
    Assert.Empty(report.ExactGroups);
  }

  [NativeFact]
  public async Task Native_Request_Rejects_An_Ambiguous_Path_Before_JSON_Can_Target_Another_File()
  {
    using var tree = new TempTree();
    var bad = tree.Write(Unsupported + ".txt", "bad");
    var good = tree.Write("\ufffd.txt", "yes");
    var registry = NativeRegistry();
    var snapshot = registry.Snapshot("czkawka", AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
    var observation = FileObservation.Capture(good) with { Identity = null };
    var request = new AnalysisRequest("unsupported-path", snapshot, [new(1, "s", bad, observation)]);
    await Assert.ThrowsAsync<ArgumentException>(async () => { await foreach (var _ in registry.Resolve(snapshot).AnalyzeAsync(request)) { } });
    Assert.Equal("bad", File.ReadAllText(bad)); Assert.Equal("yes", File.ReadAllText(good));
  }

  [Fact]
  public async Task Legacy_Scanner_Skips_Unsupported_Entries_With_A_Lossless_Diagnostic()
  {
    using var tree = new TempTree();
    var bad = tree.Write(Unsupported + ".txt", "bad");
    var good = tree.Write("\ufffd.txt", "yes");
    var emoji = tree.Write("\U0001f600.txt", "two");
    var metadata = Substitute.For<IMetadataExtractor>();
    metadata.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ExifMetadata?>(null));
    var logger = Substitute.For<ILogger>();
    var scanner = new FileScanner(metadata, logger);
    Assert.Equal(2, scanner.CountFiles(tree.Root, true));
    var files = new List<ScannedFile>();
    await foreach (var file in scanner.ScanAsync(new ScanOptions { Path = tree.Root })) { files.Add(file); }
    Assert.Equal(new[] { good, emoji }.Order(StringComparer.Ordinal), files.Select(file => file.FilePath).Order(StringComparer.Ordinal));
    logger.Received().Warning("Skipping unsupported path encoding: {FilePath}", PathText.EscapeUnsupported(bad));
  }

  [Fact]
  public void Copy_Evidence_And_Operations_Cannot_Lose_An_Unsupported_Name()
  {
    using var tree = new TempTree();
    var bad = tree.Write("folder/" + Unsupported + ".txt", "bad");
    var good = tree.Write("folder/\ufffd.txt", "yes");
    Assert.Throws<InvalidDataException>(() => CopySnapshot.Of(tree.PathOf("folder"), _ => false));
    Assert.Throws<InvalidDataException>(() => CopyDigest.Of([(Unsupported, 3, 1)]));
    Assert.Throws<InvalidDataException>(() => CopyDigest.EvidenceOf([(Unsupported, "xxh128:value")]));
    var source = new ScanSource("s", tree.Root, true, true, [], []);
    Assert.Throws<InvalidDataException>(() => new ScanFilter(source, []).Walk(tree.PathOf("folder"), "folder").ToList());
    var destination = tree.PathOf("created.txt");
    Assert.Throws<InvalidDataException>(() => PlanExecutor.CopyVerified(bad, destination));
    Assert.False(File.Exists(destination));
    Assert.Equal("bad", File.ReadAllText(bad)); Assert.Equal("yes", File.ReadAllText(good));
  }

  [Fact]
  public void Snapshot_Serialization_Rejects_Unsupported_Rules_Before_JSON_Replaces_Them()
  {
    using var tree = new TempTree();
    var engine = AnalysisEngineRegistry.ManagedOnly().Snapshot("csharp", AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var source = new ScanSource("s", tree.Root, true, true, [], [Unsupported]);
    var snapshot = new ScanJobSnapshot(1, "s", engine, [new(source, null, null, false)]);
    Assert.Throws<InvalidDataException>(() => snapshot.Serialize());
  }

  [Fact]
  public async Task Physical_Data_Root_And_Source_Aliases_Cannot_Hide_Unsupported_Text()
  {
    using var tree = new TempTree();
    var target = tree.PathOf("target-" + Unsupported);
    Directory.CreateDirectory(target);
    var alias = tree.PathOf("alias");
    using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
      FileName = "cmd.exe", ArgumentList = { "/c", "mklink", "/J", alias, target },
      UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
    })!)
    {
      process.WaitForExit(); Assert.Equal(0, process.ExitCode);
    }
    Assert.Contains(Unsupported, PathIdentity.Resolve(alias), StringComparison.Ordinal);
    var registry = AnalysisEngineRegistry.ManagedOnly();
    var engine = registry.Snapshot("csharp", AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    var source = tree.PathOf("sources"); Directory.CreateDirectory(source);
    await Assert.ThrowsAsync<ArgumentException>(() => new ReadOnlyAnalysisSession(registry, alias).RunAsync([source], engine));
    Assert.Throws<ArgumentException>(() => new RecycleAreaLocator(alias));
    Assert.Empty(Directory.GetFiles(target));
    var index = Path.Combine(DataRoot(), "index.db");
    using var store = IndexedWorkbenchStore.Open(index, context: null, startWorker: false);
    Assert.Single(await store.AddSourcesAsync([alias], new SourceOptions(true, true, []), startScan: false));
    using var connection = IndexDatabase.OpenOrCreate(index).Open();
    Assert.Equal(0L, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM sources"));
  }

  [Fact]
  public async Task Legacy_Recycle_Cache_And_JSON_Stores_Reject_Lossy_Paths_Before_Writes()
  {
    using var tree = new TempTree();
    var bad = tree.Write(Unsupported + ".txt", "bad");
    var good = tree.Write("\ufffd.txt", "yes");
    var logger = Substitute.For<ILogger>();
    var recycle = tree.PathOf("recycle");
    await Assert.ThrowsAsync<InvalidDataException>(() => new RecycleBinService(logger, recycle).RecycleAsync(bad));
    Assert.False(Directory.Exists(recycle));
    Assert.Equal("bad", File.ReadAllText(bad)); Assert.Equal("yes", File.ReadAllText(good));
    Assert.Throws<ArgumentException>(() => new RecycleBinService(logger, tree.PathOf(Unsupported)));
    var cacheDirectory = tree.PathOf("cache");
    var cache = new ScanCacheService(logger, cacheDirectory);
    await Assert.ThrowsAsync<InvalidDataException>(() => cache.LoadCacheAsync(bad, "sha256"));
    var entries = new Dictionary<string, CachedFileEntry> { [bad] = new() { Hash = "test", ModifiedUtc = DateTime.UtcNow, SizeBytes = 3 } };
    await Assert.ThrowsAsync<InvalidDataException>(() => cache.SaveCacheAsync(new ScanCache
    { ScannedPath = tree.Root, CacheDateUtc = DateTime.UtcNow, HashAlgorithm = "sha256", Entries = entries }));
    Assert.False(Directory.Exists(cacheDirectory));
    var output = tree.Write("report.json", "existing report must survive");
    var result = new ScanResult
    {
      ScannedPath = bad, ScanDateUtc = DateTime.UtcNow, HashAlgorithm = "sha256",
      TotalFilesScanned = 0, TotalSizeBytes = 0, DuplicateGroups = [],
    };
    await Assert.ThrowsAsync<InvalidDataException>(() => new JsonScanResultStore().SaveAsync(result, output));
    var invalidFile = new ScannedFile { FilePath = bad, SizeBytes = 3, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow };
    await Assert.ThrowsAsync<InvalidDataException>(() => new JsonScanResultStore().SaveAsync(result with
    { ScannedPath = tree.Root, DuplicateGroups = [new DuplicateGroup { Hash = "hash", Files = [invalidFile] }] }, output));
    Assert.Equal("existing report must survive", File.ReadAllText(output));
  }
}
