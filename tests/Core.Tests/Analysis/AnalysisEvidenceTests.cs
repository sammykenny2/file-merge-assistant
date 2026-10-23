using System.Runtime.CompilerServices;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Tests.Scanning;
using Xunit;

namespace FileMergeAssistant.Tests.Analysis;

public sealed class AnalysisEvidenceTests : IDisposable
{
  private readonly TempTree _tree = new();
  private readonly IndexDatabase _database = IndexDatabase.OpenOrCreate(Path.Combine(TestEnvironment.DataRoot, $"evidence-{Guid.NewGuid():N}.db"));
  private int _jobs;

  public void Dispose() => _tree.Dispose();

  private ScanSource CreateSource(int files, string relativeRoot = "", string sourceId = "s")
  {
    var root = _tree.PathOf(relativeRoot);
    Directory.CreateDirectory(root);
    for (var i = 0; i < files; i++) { _tree.Write(Path.Combine(relativeRoot, $"file-{i:0000}.txt"), "same bytes"); }
    using var connection = _database.Open();
    IndexDatabase.Execute(connection,
      """
      INSERT INTO sources(id,name,root,is_protected,skip_regenerable,skip_cloud,regenerable_names,exclusions,added_at,position)
      VALUES ($id,$id,$root,0,1,1,'','','2026-10-24',1)
      """, ("$root", root), ("$id", sourceId));
    return new(sourceId, root, true, true, [], []);
  }

  private (string Id, AnalysisEngineSnapshot Engine) Job(IFileAnalysisEngine engine, string algorithm, string sourceId = "s")
  {
    var snapshot = new AnalysisEngineRegistry([engine]).Snapshot(engine.Descriptor.Id, AnalysisCapability.ExactDuplicates, algorithm);
    var id = $"job-{++_jobs}";
    using var connection = _database.Open();
    var frozen = ScanJobSnapshot.Capture(connection, sourceId, snapshot, []);
    IndexDatabase.Execute(connection,
      """
      INSERT INTO jobs(id,source_id,title,kind,state,phase,queued_at,position,analysis_snapshot)
      VALUES ($id,$source,$source,'Update','Running','Discover','2026-10-24',1,$snapshot)
      """, ("$id", id), ("$source", sourceId), ("$snapshot", frozen.Serialize()));
    return (id, frozen.Engine);
  }

  private ScanStop Run(ScanSource source, IFileAnalysisEngine engine, (string Id, AnalysisEngineSnapshot Engine) job, Func<bool>? pause = null) =>
    new SourceScanner(_database, engine, job.Engine).Run(source, job.Id, new Dictionary<string, string> { [source.Id] = source.Root },
      _ => { }, pause ?? (() => false), CancellationToken.None);

  private long Count(string sql, params (string Name, object? Value)[] values)
  {
    using var connection = _database.Open();
    return IndexDatabase.Scalar<long>(connection, sql, values);
  }

  private sealed class ControlledEngine : IFileAnalysisEngine
  {
    private readonly ManagedAnalysisEngine _managed = new();
    public AnalysisEngineDescriptor Descriptor => _managed.Descriptor;
    public int Requests { get; private set; }
    public List<long> Files { get; } = [];
    public List<string> Paths { get; } = [];
    public int? CancelRequest { get; init; }
    public bool FailOne { get; init; }
    public bool IncompleteFinal { get; init; }
    public Action<int>? BeforeRequest { get; init; }
    private bool _failed;

    public async IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
      Requests++;
      BeforeRequest?.Invoke(Requests);
      Files.AddRange(request.Files.Select(file => file.Id));
      Paths.AddRange(request.Files.Select(file => file.Path));
      if (Requests == CancelRequest) { throw new OperationCanceledException("Isolated interruption fixture.", cancellationToken); }
      await foreach (var batch in _managed.AnalyzeAsync(request, cancellationToken))
      {
        if (FailOne && !_failed && !batch.Files.IsEmpty)
        {
          _failed = true;
          yield return batch with { Files = [batch.Files[0] with { Status = AnalysisFileStatus.Failed, Fingerprint = null, Error = "Fixture read failure." }] };
        }
        else { yield return batch.IsFinal && (_failed || IncompleteFinal) ? batch with { IsComplete = false } : batch; }
      }
    }
  }

  [Fact]
  public void Interrupted_New_Algorithm_Stages_Checkpoints_Without_Replacing_The_Complete_Result()
  {
    var source = CreateSource(450);
    var baseline = new ControlledEngine();
    Run(source, baseline, Job(baseline, ContentFingerprints.XxHash128));
    var interrupted = new ControlledEngine { CancelRequest = 2 };
    var job = Job(interrupted, ContentFingerprints.Sha256);
    Assert.Throws<OperationCanceledException>(() => Run(source, interrupted, job));
    Assert.Equal(450L, Count("SELECT COUNT(*) FROM files"));
    Assert.Equal(450L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    Assert.Equal(0L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
    Assert.Equal(200L, Count("SELECT COUNT(*) FROM analysis_results WHERE job_id=$id AND generation=1", ("$id", job.Id)));
    Assert.Equal(200L, Count("SELECT COUNT(*) FROM file_fingerprints WHERE algorithm='sha256-v1'"));

    // Restart the same snapshot: reobserve and reuse committed evidence; no partial generation becomes active.
    var resumed = new ControlledEngine();
    Assert.Equal(ScanStop.Completed, Run(source, resumed, job));
    Assert.Equal(250, resumed.Files.Count);
    Assert.Equal(450L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'sha256-v1:%'"));
    Assert.Equal(1L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
    Assert.Equal(2L, Count("SELECT analysis_generation FROM jobs WHERE id=$id", ("$id", job.Id)));
    Assert.Equal(900L, Count("SELECT COUNT(*) FROM file_fingerprints"));
    Assert.Equal(200L, Count("SELECT COUNT(*) FROM analysis_results WHERE job_id=$id AND generation=1", ("$id", job.Id)));
    Assert.All(Directory.GetFiles(_tree.Root), file => Assert.Equal("same bytes", File.ReadAllText(file)));
  }

  [Fact]
  public void Comparison_Candidates_Use_The_Frozen_Source_Exclusions_Instead_Of_Old_Inventory_Policy()
  {
    var a = CreateSource(2, "a", "a");
    var b = CreateSource(0, "b", "b");
    _tree.Write(@"b\blocked\one.txt", "same bytes");
    _tree.Write(@"b\allowed\two.txt", "same bytes");
    var baseline = new ControlledEngine();
    Run(b, baseline, Job(baseline, ContentFingerprints.XxHash128, b.Id));
    var engine = new ControlledEngine();
    var job = Job(engine, ContentFingerprints.Sha256, a.Id);
    var roots = new Dictionary<string, string> { [a.Id] = a.Root, [b.Id] = b.Root };
    var policies = new Dictionary<string, ScanSource> { [a.Id] = a, [b.Id] = b with { Exclusions = ["blocked"] } };
    new SourceScanner(_database, engine, job.Engine).Run(a, job.Id, roots, _ => { }, () => false, CancellationToken.None, scopeSources: policies);
    Assert.Equal(3, engine.Paths.Count);
    Assert.DoesNotContain(engine.Paths, path => path.Contains("\\blocked\\", StringComparison.Ordinal));
    Assert.Equal(3L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'sha256-v1:%'"));
    Assert.Equal(1L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    Assert.Equal(1L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
  }

  [Fact]
  public void Replacing_A_Previous_Cohort_File_Before_Publication_Withholds_The_Generation()
  {
    var source = CreateSource(250);
    var first = new ControlledEngine();
    Run(source, first, Job(first, ContentFingerprints.XxHash128));
    // The first cohort is saved; during the second cohort an external program replaces one of its files.
    var target = _tree.PathOf("file-0000.txt");
    var old = FileObservation.Capture(target);
    var next = new ControlledEngine
    {
      BeforeRequest = request =>
      {
        if (request != 2) { return; }
        var replacement = _tree.Write("replacement.tmp", "diff bytes");
        File.SetLastWriteTimeUtc(replacement, new DateTime(old.ModifiedUtcTicks, DateTimeKind.Utc));
        File.Move(replacement, target, overwrite: true);
      },
    };
    var job = Job(next, ContentFingerprints.Sha256);
    Run(source, next, job);
    Assert.Equal(2, next.Requests);
    Assert.Equal(old.Size, FileObservation.Capture(target).Size);
    Assert.Equal(old.ModifiedUtcTicks, FileObservation.Capture(target).ModifiedUtcTicks);
    Assert.Equal(0L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
    Assert.Equal(0L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'sha256-v1:%'"));
    Assert.Equal(249L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    Assert.Equal(1L, Count("SELECT COUNT(*) FROM scan_errors WHERE job_id=$id", ("$id", job.Id)));
  }

  [Fact]
  public void Pausing_During_A_New_Algorithm_Preserves_The_Old_Active_Result()
  {
    var source = CreateSource(250);
    var engine = new ControlledEngine();
    Run(source, engine, Job(engine, ContentFingerprints.XxHash128));
    var next = new ControlledEngine();
    var job = Job(next, ContentFingerprints.Sha256);
    Assert.Equal(ScanStop.Paused, Run(source, next, job, () => true));
    Assert.Equal(250L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    Assert.Equal(200L, Count("SELECT COUNT(*) FROM analysis_results WHERE job_id=$id", ("$id", job.Id)));
    Assert.Equal(0L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
  }

  [Fact]
  public void Cache_Is_Algorithm_And_Observation_Specific_And_Detects_Replaced_File_Identity()
  {
    var source = CreateSource(2);
    var first = new ControlledEngine();
    var originalJob = Job(first, ContentFingerprints.XxHash128);
    Run(source, first, originalJob);
    var second = new ControlledEngine();
    Run(source, second, Job(second, ContentFingerprints.Sha256));
    Assert.Equal(2, second.Files.Count);
    var back = new ControlledEngine();
    Run(source, back, Job(back, ContentFingerprints.XxHash128));
    Assert.Empty(back.Files);
    Assert.Equal(0, back.Requests);
    Assert.Equal(4L, Count("SELECT COUNT(*) FROM file_fingerprints"));
    Assert.Equal(2L, Count("SELECT COUNT(*) FROM file_fingerprints WHERE algorithm='xxh128-v1' AND job_id=$job", ("$job", originalJob.Id)));

    var target = _tree.PathOf("file-0000.txt");
    var before = FileObservation.Capture(target);
    var replacement = _tree.PathOf("replacement.tmp");
    _tree.Write("replacement.tmp", "diff bytes");
    File.SetLastWriteTimeUtc(replacement, new DateTime(before.ModifiedUtcTicks, DateTimeKind.Utc));
    File.Move(replacement, target, overwrite: true);
    Assert.Equal(before.Size, FileObservation.Capture(target).Size);
    Assert.Equal(before.ModifiedUtcTicks, FileObservation.Capture(target).ModifiedUtcTicks);
    Assert.False(before.Matches(FileObservation.Capture(target)));
    var changed = new ControlledEngine();
    Run(source, changed, Job(changed, ContentFingerprints.XxHash128));
    Assert.Single(changed.Files);
    Assert.Equal(2L, Count("SELECT COUNT(DISTINCT hash) FROM files"));
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public void Partial_Or_Declared_Incomplete_Analysis_Does_Not_Publish_New_Hashes(bool fileFailure)
  {
    var source = CreateSource(2);
    var first = new ControlledEngine();
    Run(source, first, Job(first, ContentFingerprints.XxHash128));
    var partial = new ControlledEngine { FailOne = fileFailure, IncompleteFinal = !fileFailure };
    var job = Job(partial, ContentFingerprints.Sha256);
    if (fileFailure) { Run(source, partial, job); }
    else { Assert.Throws<InvalidDataException>(() => Run(source, partial, job)); }
    Assert.Equal(0L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'sha256-v1:%'"));
    Assert.Equal(fileFailure ? 1L : 2L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'xxh128:%'"));
    Assert.Equal(0L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
  }

  [Fact]
  public void Empty_File_Candidates_Are_Confirmed_And_Unique_Files_Remain_Indexed()
  {
    var source = CreateSource(0);
    File.WriteAllBytes(_tree.PathOf("empty-one"), []);
    File.WriteAllBytes(_tree.PathOf("empty-two"), []);
    _tree.Write("unique", "unique");
    var engine = new ControlledEngine();
    var job = Job(engine, ContentFingerprints.XxHash128);
    Run(source, engine, job);
    Assert.Equal(2, engine.Files.Count);
    Assert.Equal(3L, Count("SELECT COUNT(*) FROM files"));
    Assert.Equal(2L, Count("SELECT COUNT(*) FROM files WHERE hash IS NOT NULL"));
    Assert.Equal(1L, Count("SELECT COUNT(DISTINCT hash) FROM files WHERE hash IS NOT NULL"));
    Assert.Equal(1L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
  }

  [NativeFact]
  public void Switching_To_The_Real_Native_Engine_Preserves_Both_Algorithms_And_Copy_Verification()
  {
    var source = CreateSource(2);
    var managed = new ControlledEngine();
    Run(source, managed, Job(managed, ContentFingerprints.XxHash128));
    var original = SourceScanner.Fingerprint(_tree.PathOf("file-0000.txt"), CancellationToken.None);
    var native = new NativeAnalysisEngine(Environment.GetEnvironmentVariable(NativeFactAttribute.LibraryDirectoryVariable));
    Assert.Equal(AnalysisAvailability.Available, native.Descriptor.Availability);
    var job = Job(native, ContentFingerprints.Blake3);
    Run(source, native, job);
    Assert.Equal(2L, Count("SELECT COUNT(*) FROM files WHERE hash LIKE 'blake3-v1:%'"));
    Assert.Equal(4L, Count("SELECT COUNT(*) FROM file_fingerprints"));
    Assert.Equal(1L, Count("SELECT analysis_complete FROM jobs WHERE id=$id", ("$id", job.Id)));
    Assert.Equal(original, SourceScanner.Fingerprint(_tree.PathOf("file-0000.txt"), CancellationToken.None));
  }
}
