using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Scanning;

public sealed record AnalysisSessionJob(string Id, string SourceId, ScanJobState State, ScanJobSnapshot Snapshot,
  long Generation, bool Complete, long Discovered, long Confirmed, long Skipped, long Errors, string? Reason);
public sealed record AnalysisSessionFile(long Id, string SourceId, string Path, FileObservation Observation, string Status, string? Fingerprint);
public sealed record AnalysisSessionGroup(ImmutableArray<long> FileIds);
public sealed record AnalysisSessionMetrics(long ElapsedMilliseconds, long? ProcessReadBytes, long? ProcessWriteBytes,
  long WorkingSetBeforeBytes, long WorkingSetAfterBytes, long ProcessLifetimePeakWorkingSetBytes, string Scope, string CachePolicy);
public sealed record AnalysisSessionReport(int FormatVersion, string IndexPath, AnalysisEngineDescriptor Descriptor,
  AnalysisEngineSnapshot Engine, string Outcome, ImmutableArray<AnalysisSessionJob> Jobs,
  ImmutableArray<AnalysisSessionFile> Files, ImmutableArray<AnalysisSessionGroup> ExactGroups, AnalysisSessionMetrics Metrics)
{
  public string ToJson() => JsonSerializer.Serialize(this, AnalysisJson.Options);
  public bool IsComplete => Outcome == "Completed";
}

/// <summary>A read-only analysis run with its own index. Never shares or creates an operation plan.</summary>
public sealed class ReadOnlyAnalysisSession
{
  private readonly AnalysisEngineRegistry _engines;
  private readonly string _dataRoot;
  private readonly IVolumeInfoProvider _volumes;

  public ReadOnlyAnalysisSession(AnalysisEngineRegistry engines, string? dataRoot = null, IVolumeInfoProvider? volumes = null)
  {
    _engines = engines;
    dataRoot ??= AppDataPaths.Root;
    if (!Path.IsPathFullyQualified(dataRoot)) { throw new ArgumentException("The analysis data directory must be absolute.", nameof(dataRoot)); }
    _dataRoot = Path.GetFullPath(dataRoot);
    _volumes = volumes ?? new Win32VolumeInfoProvider();
  }

  public Task<AnalysisSessionReport> RunAsync(IReadOnlyList<string> paths, AnalysisEngineSnapshot engine,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress = null, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(paths);
    if (paths.Count == 0) { throw new ArgumentException("At least one source folder is required.", nameof(paths)); }
    if (engine.Capability != AnalysisCapability.ExactDuplicates) { throw new ArgumentException("This session supports exact-content analysis only.", nameof(engine)); }
    _engines.Resolve(engine);
    return Task.Run(() => RunCoreAsync(paths, engine, progress, cancellationToken), cancellationToken);
  }

  private async Task<AnalysisSessionReport> RunCoreAsync(IReadOnlyList<string> paths, AnalysisEngineSnapshot engine,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
  {
    var clock = Stopwatch.StartNew();
    var before = AnalysisProcessMetrics.Capture();
    var owned = PathIdentity.Resolve(_dataRoot);
    foreach (var path in paths)
    {
      var source = PathIdentity.Resolve(Path.GetFullPath(path));
      if (LocationSuggester.Same(source, owned) || LocationSuggester.Inside(source, owned) || PathIdentity.SameOrInside(source, owned) == true)
      {
        throw new ArgumentException("An analysis source cannot be inside the application's data directory.", nameof(paths));
      }
    }
    Directory.CreateDirectory(_dataRoot);
    // Persist the physical location as the exclusion, including aliases whose final folder was just created.
    var dataRoot = PathIdentity.Resolve(_dataRoot);
    var index = Path.Combine(dataRoot, $"analysis-{Guid.NewGuid():N}.db");
    // Claim a new name: no existing index or user work can be overwritten.
    using (File.Open(index, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    var database = IndexDatabase.OpenOrCreate(index);
    using (var setup = IndexedWorkbenchStore.Open(index, _volumes, context: null, startWorker: false, engines: _engines))
    {
      setup.SelectNewJobEngine(engine);
      var problems = await setup.AddSourcesAsync(paths, new SourceOptions(true, true, paths), startScan: true).ConfigureAwait(false);
      if (problems.Count > 0) { throw new ArgumentException(string.Join(Environment.NewLine, problems), nameof(paths)); }
    }
    using var worker = new ScanWorker(database, _volumes, [dataRoot], _engines);
    var canceled = false;
    worker.Start();
    worker.Wake();
    var snapshots = ReadJobs(database).ToDictionary(job => job.Id, job => job.Snapshot, StringComparer.Ordinal);
    while (true)
    {
      var jobs = ReadJobs(database, snapshots);
      if (cancellationToken.IsCancellationRequested)
      {
        canceled = true;
        foreach (var job in jobs.Where(job => job.State is not (ScanJobState.Completed or ScanJobState.Canceled))) { worker.Cancel(job.Id); }
      }
      progress?.Report(jobs);
      if (jobs.All(job => job.State is not (ScanJobState.Queued or ScanJobState.Running or ScanJobState.Pausing or ScanJobState.Cancelling))) { break; }
      await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);
    }
    var finalJobs = ReadJobs(database, snapshots);
    var complete = finalJobs.All(job => job.State == ScanJobState.Completed && job.Complete);
    var online = finalJobs.SelectMany(job => job.Snapshot.Sources).DistinctBy(source => source.Source.Id)
      .Where(source => ScanJobSnapshot.IsOnline(source, _volumes)).Select(source => source.Source.Id).ToHashSet(StringComparer.Ordinal);
    if (finalJobs.Any(job => !online.Contains(job.SourceId))) { complete = false; }
    var files = ReadFiles(database, engine, online, ref complete);
    var groups = files.Where(file => file.Status == "Confirmed" && file.Fingerprint is not null)
      .GroupBy(file => file.Fingerprint!, StringComparer.Ordinal).Where(group => group.Count() > 1)
      .Select(group => new AnalysisSessionGroup(group.Select(file => file.Id).Order().ToImmutableArray())).ToImmutableArray();
    var outcome = canceled ? "Canceled" : finalJobs.Any(job => job.State == ScanJobState.WaitingForEngine) ? "Unavailable" : complete ? "Completed" : "Partial";
    var descriptor = _engines.Descriptors.Single(item => item.Id == engine.EngineId);
    clock.Stop();
    return new(1, index, descriptor, engine, outcome, finalJobs, files, groups, AnalysisProcessMetrics.Difference(before, clock.ElapsedMilliseconds));
  }

  private static ImmutableArray<AnalysisSessionJob> ReadJobs(IndexDatabase database, Dictionary<string, ScanJobSnapshot>? snapshots = null)
  {
    using var connection = database.Open();
    using var command = IndexDatabase.Command(connection,
      """
      SELECT id,source_id,state,analysis_snapshot,analysis_generation,analysis_complete,discovered,succeeded,skipped,errors,reason
      FROM jobs ORDER BY position
      """);
    using var reader = command.ExecuteReader();
    var jobs = ImmutableArray.CreateBuilder<AnalysisSessionJob>();
    while (reader.Read())
    {
      jobs.Add(new(reader.GetString(0), reader.GetString(1), Enum.Parse<ScanJobState>(reader.GetString(2)),
        snapshots?.GetValueOrDefault(reader.GetString(0)) ?? ScanJobSnapshot.Deserialize(reader.GetString(3)), reader.GetInt64(4), reader.GetInt64(5) != 0, reader.GetInt64(6),
        reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
    }
    return jobs.ToImmutable();
  }

  private static ImmutableArray<AnalysisSessionFile> ReadFiles(IndexDatabase database, AnalysisEngineSnapshot engine, HashSet<string> online, ref bool complete)
  {
    var files = ImmutableArray.CreateBuilder<AnalysisSessionFile>();
    using var connection = database.Open();
    using var command = IndexDatabase.Command(connection,
      """
      SELECT f.id,f.source_id,s.root,f.rel_path,f.size,f.mtime,f.hash,f.cloud_only,p.file_identity
      FROM files f JOIN sources s ON s.id=f.source_id
      LEFT JOIN file_fingerprints p ON p.file_id=f.id AND p.hash=f.hash AND p.algorithm=$algorithm AND p.parameters=$parameters
      ORDER BY f.id
      """, ("$algorithm", engine.Algorithm), ("$parameters", engine.ParametersJson));
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      var path = Path.Combine(reader.GetString(2), reader.GetString(3));
      var observation = new FileObservation(reader.GetInt64(4), reader.GetInt64(5));
      var hash = reader.IsDBNull(6) ? null : reader.GetString(6);
      if (hash is not null)
      {
        FileObservation? evidence = null;
        try { if (!reader.IsDBNull(8)) { evidence = JsonSerializer.Deserialize<FileObservation>(reader.GetString(8), AnalysisJson.Options); } }
        catch (JsonException) { }
        if (!ContentFingerprints.IsValid(engine.Algorithm, hash) || evidence is null || !observation.Matches(evidence)) { hash = null; complete = false; }
        else { observation = evidence; }
      }
      var status = reader.GetInt64(7) != 0 ? "CloudOnly" : hash is not null ? "Confirmed" : complete ? "NotContentCandidate" : "Unconfirmed";
      if (!online.Contains(reader.GetString(1))) { status = "SourceUnavailable"; hash = null; complete = false; }
      if (status is not ("CloudOnly" or "SourceUnavailable"))
      {
        try
        {
          var current = FileObservation.Capture(path);
          if (!observation.Matches(current)) { hash = null; status = "Changed"; complete = false; }
          else { observation = current; }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
          status = "Unavailable"; hash = null; complete = false;
        }
      }
      files.Add(new(reader.GetInt64(0), reader.GetString(1), path, observation, status, hash));
    }
    return files.ToImmutable();
  }
}
