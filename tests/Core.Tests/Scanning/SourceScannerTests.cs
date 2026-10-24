using FileWorkbench.Index;
using FileWorkbench.Analysis;
using System.Runtime.CompilerServices;
using FileWorkbench.Scanning;
using FileWorkbench.Sources;
using Xunit;

namespace FileWorkbench.Tests.Scanning;

public sealed class SourceScannerTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly IndexDatabase _database;

  public SourceScannerTests()
  {
    _database = IndexDatabase.OpenOrCreate(Path.Combine(TestEnvironment.DataRoot, $"scanner-{Guid.NewGuid():N}.db"));
  }

  public void Dispose() => _tree.Dispose();

  private ScanSource AddSource(string id, string relative)
  {
    var root = _tree.PathOf(relative);
    Directory.CreateDirectory(root);
    using var connection = _database.Open();
    IndexDatabase.Execute(connection,
      """
      INSERT INTO sources(id, name, root, is_protected, skip_regenerable, skip_cloud, regenerable_names, exclusions, added_at, position)
      VALUES ($id, $id, $root, 0, 1, 1, $names, $exclusions, '2026-10-23T00:00:00Z', 1)
      """,
      ("$id", id), ("$root", root), ("$names", string.Join('\n', RegenerableFolders.Defaults)), ("$exclusions", "*.tmp\nThumbs.db"));
    IndexDatabase.Execute(connection,
      "INSERT INTO jobs(id, source_id, title, kind, state, phase, queued_at, position) VALUES ($j, $id, $id, 'FirstScan', 'Running', 'Discover', '2026-10-23T00:00:00Z', 1)",
      ("$j", "job-" + id), ("$id", id));
    return new ScanSource(id, root, true, true, RegenerableFolders.Defaults, ["*.tmp", "Thumbs.db"]);
  }

  [Fact]
  public void Generic_Folder_Names_Count_As_Regenerable_Only_Next_To_A_Project_Marker()
  {
    _tree.Write(@"Music\Library\song.mp3", "music");
    _tree.Write(@"Music\bin\old.txt", "kept on purpose");
    _tree.Write(@"app\package.json", "{}");
    _tree.Write(@"app\dist\bundle.js", "built");
    _tree.Write(@"loose\node_modules\x\index.js", "module");
    var filter = new ScanFilter(new ScanSource("s", _tree.Root, true, true, RegenerableFolders.Defaults, []), []);

    EntryKind Kind(string relative) => filter.Classify(new DirectoryInfo(_tree.PathOf(relative)), relative);

    Assert.Equal(EntryKind.Index, Kind(@"Music\Library"));
    Assert.Equal(EntryKind.Index, Kind(@"Music\bin"));
    Assert.Equal(EntryKind.Regenerable, Kind(@"app\dist"));
    Assert.Equal(EntryKind.Regenerable, Kind(@"loose\node_modules"));
  }

  private ScanStop Scan(ScanSource source, Func<bool>? pause = null, List<ScanProgress>? progress = null)
  {
    var roots = new Dictionary<string, string>();
    using (var connection = _database.Open())
    using (var command = IndexDatabase.Command(connection, "SELECT id, root FROM sources"))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        roots[reader.GetString(0)] = reader.GetString(1);
      }
    }
    return new SourceScanner(_database).Run(source, "job-" + source.Id, roots, p => progress?.Add(p), pause ?? (() => false), CancellationToken.None);
  }

  private Dictionary<string, (long Size, string? Hash)> Files(string sourceId)
  {
    var files = new Dictionary<string, (long Size, string? Hash)>(StringComparer.OrdinalIgnoreCase);
    using var connection = _database.Open();
    using var command = IndexDatabase.Command(connection, "SELECT rel_path, size, hash FROM files WHERE source_id = $s", ("$s", sourceId));
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      files[reader.GetString(0)] = (reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    }
    return files;
  }

  [Fact]
  public void Analysis_Is_Delegated_Only_For_Size_Candidates_While_All_Files_Are_Indexed()
  {
    var source = AddSource("a", "a");
    _tree.Write(@"a\one.txt", "same");
    _tree.Write(@"a\copy.txt", "same");
    _tree.Write(@"a\unique.txt", "a different size");
    var engine = new RecordingEngine(false);

    Assert.Equal(ScanStop.Completed, new SourceScanner(_database, engine).Run(source, "job-a",
      new Dictionary<string, string> { [source.Id] = source.Root }, _ => { }, () => false, CancellationToken.None));

    Assert.Equal(3, Files("a").Count);
    Assert.Equal(["copy.txt", "one.txt"], engine.Paths.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    Assert.Null(Files("a")["unique.txt"].Hash);
    Assert.All(Files("a").Where(file => file.Value.Hash is not null), file => Assert.StartsWith("xxh128:", file.Value.Hash));
  }

  [Fact]
  public void Unknown_Engine_File_Is_Rejected_Before_The_Cohort_Is_Committed()
  {
    var source = AddSource("a", "a");
    _tree.Write(@"a\one.txt", "same");
    _tree.Write(@"a\copy.txt", "same");

    Assert.Throws<InvalidDataException>(() => new SourceScanner(_database, new RecordingEngine(true)).Run(source, "job-a",
      new Dictionary<string, string> { [source.Id] = source.Root }, _ => { }, () => false, CancellationToken.None));

    Assert.All(Files("a"), file => Assert.Null(file.Value.Hash));
  }

  [Fact]
  public void Changed_Engine_Metadata_Is_Reobserved_By_Core_Before_Index_Update()
  {
    var source = AddSource("a", "a");
    _tree.Write(@"a\one.txt", "same");
    _tree.Write(@"a\copy.txt", "same");

    Assert.Equal(ScanStop.Completed, new SourceScanner(_database, new RecordingEngine(false, claimChange: true)).Run(source, "job-a",
      new Dictionary<string, string> { [source.Id] = source.Root }, _ => { }, () => false, CancellationToken.None));

    Assert.All(Files("a"), file => Assert.Equal(new FileInfo(_tree.PathOf(@"a\" + file.Key)).Length, file.Value.Size));
    using var connection = _database.Open();
    Assert.Equal(0, IndexDatabase.Scalar<long>(connection, "SELECT COUNT(*) FROM files WHERE mtime > $max", ("$max", DateTime.UtcNow.Ticks)));
  }

  private sealed class RecordingEngine(bool tamper, bool claimChange = false) : IFileAnalysisEngine
  {
    private readonly ManagedAnalysisEngine _inner = new();
    public List<string> Paths { get; } = [];
    public AnalysisEngineDescriptor Descriptor => _inner.Descriptor;

    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      Paths.AddRange(request.Files.Select(file => file.Path));
      await foreach (var batch in _inner.AnalyzeAsync(request, cancellationToken))
      {
        if (claimChange && batch.Sequence == 1 && batch.Files.Length > 0)
        {
          yield return batch with { Files = [batch.Files[0] with { Status = AnalysisFileStatus.Changed, Fingerprint = null,
            Error = "claimed change", Observation = new(999, DateTime.MaxValue.Ticks) }] };
        }
        else if (claimChange && batch.IsFinal) { yield return batch with { IsComplete = false }; }
        else { yield return tamper && batch.Sequence == 2 && batch.Files.Length > 0
          ? batch with { Files = [batch.Files[0] with { FileId = long.MaxValue }] } : batch; }
      }
    }
  }

  [Fact]
  public void Discovery_Records_Files_Measures_Regenerable_Folders_And_Skips_Exclusions()
  {
    var source = AddSource("a", "a");
    _tree.Write(@"a\app\index.js", "console.log(1);");
    _tree.Write(@"a\app\node_modules\left-pad\index.js", "module.exports = 1;");
    _tree.Write(@"a\app\node_modules\left-pad\package.json", "{}");
    _tree.Write(@"a\app\cache.tmp", "junk");
    _tree.Write(@"a\app\.git\HEAD", "ref: refs/heads/main");

    Assert.Equal(ScanStop.Completed, Scan(source));

    var files = Files("a");
    Assert.Equal([@"app\index.js"], files.Keys);
    using var connection = _database.Open();
    Assert.Equal(2, IndexDatabase.Scalar<long>(connection, "SELECT files FROM regenerable_dirs WHERE rel_path = 'app\\node_modules'"));
    Assert.Equal(".git", IndexDatabase.Scalar<string>(connection, "SELECT marker FROM project_dirs WHERE rel_path = 'app'"));
  }

  [Fact]
  public void Only_Files_That_Share_A_Size_Get_A_Fingerprint()
  {
    var a = AddSource("a", "a");
    var b = AddSource("b", "b");
    _tree.Write(@"a\same.txt", "identical content");
    _tree.Write(@"b\copy.txt", "identical content");
    _tree.Write(@"a\unique.txt", "this size occurs once in the whole index");

    Scan(a);
    Scan(b);

    var filesA = Files("a");
    Assert.StartsWith(SourceScanner.HashPrefix, filesA["same.txt"].Hash, StringComparison.Ordinal);
    Assert.Null(filesA["unique.txt"].Hash);
    Assert.Equal(filesA["same.txt"].Hash, Files("b")["copy.txt"].Hash);
  }

  [Fact]
  public void Rescanning_Keeps_Valid_Fingerprints_And_Drops_Gone_Files()
  {
    var source = AddSource("a", "a");
    _tree.Write(@"a\one.txt", "aaaa");
    _tree.Write(@"a\two.txt", "bbbb");
    _tree.Write(@"a\three.txt", "cccc");
    Scan(source);
    var before = Files("a");
    Assert.NotNull(before["one.txt"].Hash);

    _tree.Write(@"a\two.txt", "BBBB", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    File.Delete(_tree.PathOf(@"a\three.txt"));
    Scan(source);

    var after = Files("a");
    Assert.Equal(before["one.txt"].Hash, after["one.txt"].Hash);
    Assert.NotEqual(before["two.txt"].Hash, after["two.txt"].Hash);
    Assert.False(after.ContainsKey("three.txt"));
  }

  [Fact]
  public void Pausing_Stops_After_A_Saved_Batch()
  {
    var source = AddSource("a", "a");
    for (var i = 0; i < SourceScanner.BatchSize + 20; i++)
    {
      _tree.Write($@"a\f{i:0000}.txt", $"{i}");
    }

    var stop = Scan(source, pause: () => true);

    Assert.Equal(ScanStop.Paused, stop);
    Assert.Equal(SourceScanner.BatchSize, Files("a").Count);

    // Running again completes; the saved batch is updated in place, not duplicated.
    Assert.Equal(ScanStop.Completed, Scan(source));
    Assert.Equal(SourceScanner.BatchSize + 20, Files("a").Count);
  }

  [Fact]
  public void Progress_Reports_Every_Stage()
  {
    var a = AddSource("a", "a");
    _tree.Write(@"a\x.txt", "1234");
    _tree.Write(@"a\y.txt", "5678");
    var progress = new List<ScanProgress>();

    Scan(a, progress: progress);

    Assert.Contains(progress, p => p.Phase == ScanPhase.Discover && p.Discovered == 2);
    Assert.Contains(progress, p => p.Phase == ScanPhase.Confirm && p.Hashed == 2);
    Assert.Equal(ScanPhase.Done, progress[^1].Phase);
  }

  [Theory]
  [InlineData(@"cache.tmp", "cache.tmp", true)]
  [InlineData(@"docs\Thumbs.db", "Thumbs.db", true)]
  [InlineData(@"legacy-erp\secrets", "secrets", true)]
  [InlineData(@"legacy-erp\secrets\key.pem", "key.pem", true)]
  [InlineData(@"legacy-erp\secretsauce.txt", "secretsauce.txt", false)]
  [InlineData(@"src\main.cs", "main.cs", false)]
  public void Exclusions_Match_Names_Or_Relative_Folders(string relative, string name, bool expected)
  {
    Assert.Equal(expected, SourceScanner.IsExcluded(relative, name, ["*.tmp", "Thumbs.db", @"legacy-erp\secrets"]));
  }

  [Fact]
  public void An_Index_From_A_Newer_App_Is_Refused()
  {
    var path = Path.Combine(TestEnvironment.DataRoot, $"newer-{Guid.NewGuid():N}.db");
    var database = IndexDatabase.OpenOrCreate(path);
    using (var connection = database.Open())
    {
      IndexDatabase.Execute(connection, $"PRAGMA user_version = {IndexDatabase.SchemaVersion + 1}");
    }

    Assert.Throws<InvalidOperationException>(() => IndexDatabase.OpenOrCreate(path));
  }
}
