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
  private readonly ISourcePathService _paths;

  public ReadOnlyAnalysisSession(AnalysisEngineRegistry engines, string? dataRoot = null, IVolumeInfoProvider? volumes = null,
    ISourcePathService? sourcePaths = null)
  {
    _engines = engines;
    dataRoot ??= AppDataPaths.Root;
    PathText.RequireSupported(dataRoot, nameof(dataRoot));
    if (!Path.IsPathFullyQualified(dataRoot)) { throw new ArgumentException("The analysis data directory must be absolute.", nameof(dataRoot)); }
    _dataRoot = Path.GetFullPath(dataRoot);
    _volumes = volumes ?? new Win32VolumeInfoProvider();
    _paths = sourcePaths ?? WindowsSourcePathService.Instance;
  }

  public Task<AnalysisSessionReport> RunAsync(IReadOnlyList<string> paths, AnalysisEngineSnapshot engine,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress = null, CancellationToken cancellationToken = default)
    => RunPreparedAsync(paths, default, engine, progress, cancellationToken);

  internal Task<AnalysisSessionReport> RunConfiguredAsync(ImmutableArray<ScanSourceSnapshot> sources, AnalysisEngineSnapshot engine,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
    => RunPreparedAsync(sources.Select(source => source.Source.Root).ToImmutableArray(), sources, engine, progress, cancellationToken);

  private Task<AnalysisSessionReport> RunPreparedAsync(IReadOnlyList<string> paths, ImmutableArray<ScanSourceSnapshot> sources,
    AnalysisEngineSnapshot engine, IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(paths);
    if (paths.Count == 0) { throw new ArgumentException("At least one source folder is required.", nameof(paths)); }
    foreach (var path in paths) { PathText.RequireSupported(path, nameof(paths)); }
    if (engine.Capability is not (AnalysisCapability.ExactDuplicates or AnalysisCapability.SimilarImages)) { throw new ArgumentException("This session does not support the requested capability.", nameof(engine)); }
    _engines.Resolve(engine);
    var frozenPaths = paths.ToImmutableArray();
    return Task.Run(() => RunCoreAsync(frozenPaths, sources, engine, progress, cancellationToken), cancellationToken);
  }

  private async Task<AnalysisSessionReport> RunCoreAsync(IReadOnlyList<string> paths, ImmutableArray<ScanSourceSnapshot> sources, AnalysisEngineSnapshot engine,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
  {
    var clock = Stopwatch.StartNew();
    var before = AnalysisProcessMetrics.Capture();
    var owned = _paths.Resolve(_dataRoot);
    PathText.RequireSupported(owned, nameof(_dataRoot));
    foreach (var path in paths)
    {
      var source = _paths.Resolve(Path.GetFullPath(path));
      if (_paths.Same(source, owned) || _paths.Inside(source, owned) || _paths.SameOrInside(source, owned) == true)
      {
        throw new ArgumentException("An analysis source cannot be inside the application's data directory.", nameof(paths));
      }
    }
    Directory.CreateDirectory(_dataRoot);
    // Persist the physical location as the exclusion, including aliases whose final folder was just created.
    var dataRoot = _paths.Resolve(_dataRoot);
    PathText.RequireSupported(dataRoot, nameof(_dataRoot));
    var index = Path.Combine(dataRoot, $"analysis-{Guid.NewGuid():N}.db");
    // Claim a new name: no existing index or user work can be overwritten.
    using (File.Open(index, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
    var database = IndexDatabase.OpenOrCreate(index);
    using (var setup = IndexedWorkbenchStore.Open(index, _volumes, context: null, startWorker: false, engines: _engines, sourcePaths: _paths))
    {
      if (engine.Capability == AnalysisCapability.ExactDuplicates) { setup.SelectNewJobEngine(engine); }
      var problems = await setup.AddSourcesAsync(paths, new SourceOptions(true, true, paths), startScan: true).ConfigureAwait(false);
      if (problems.Count > 0) { throw new ArgumentException(string.Join(Environment.NewLine, problems), nameof(paths)); }
    }
    if (!sources.IsDefault || engine.Capability == AnalysisCapability.SimilarImages) { BindConfiguredSources(database, engine, sources, dataRoot); }
    using var worker = new ScanWorker(database, _volumes, [dataRoot], _engines, _paths);
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
    var online = new HashSet<string>(StringComparer.Ordinal);
    foreach (var source in finalJobs.SelectMany(job => job.Snapshot.Sources).DistinctBy(source => source.Source.Id))
    {
      if (cancellationToken.IsCancellationRequested) { break; }
      if (ScanJobSnapshot.IsOnline(source, _volumes, _paths)) { online.Add(source.Source.Id); }
    }
    if (finalJobs.Any(job => !online.Contains(job.SourceId))) { complete = false; }
    var files = ReadFiles(database, engine, online, ref complete, cancellationToken);
    var groups = ImmutableArray<AnalysisSessionGroup>.Empty;
    try
    {
      var byHash = new Dictionary<string, List<long>>(StringComparer.Ordinal);
      foreach (var file in files)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (engine.Capability == AnalysisCapability.ExactDuplicates && file.Status == "Confirmed" && file.Fingerprint is { } fingerprint)
        {
          if (!byHash.TryGetValue(fingerprint, out var ids)) { byHash[fingerprint] = ids = []; }
          ids.Add(file.Id);
        }
      }
      var builder = ImmutableArray.CreateBuilder<AnalysisSessionGroup>();
      foreach (var ids in byHash.Values)
      {
        cancellationToken.ThrowIfCancellationRequested();
        // ReadFiles is ordered by ID, so no uninterruptible large-group sort is needed.
        if (ids.Count > 1) { builder.Add(new(ids.ToImmutableArray())); }
      }
      groups = builder.ToImmutable();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    canceled |= cancellationToken.IsCancellationRequested;
    var outcome = canceled ? "Canceled" : finalJobs.Any(job => job.State == ScanJobState.WaitingForEngine) ? "Unavailable" : complete ? "Completed" : "Partial";
    var descriptor = _engines.Descriptors.Single(item => item.Id == engine.EngineId);
    clock.Stop();
    return new(1, index, descriptor, engine, outcome, finalJobs, files, groups, AnalysisProcessMetrics.Difference(before, clock.ElapsedMilliseconds));
  }

  private void BindConfiguredSources(IndexDatabase database, AnalysisEngineSnapshot engine,
    ImmutableArray<ScanSourceSnapshot> sources, string dataRoot)
  {
    using var connection = database.Open();
    using var transaction = connection.BeginTransaction();
    var registered = ScanJobSnapshot.Capture(connection, "binding", engine, [dataRoot], _paths).Sources;
    if (sources.IsDefault) { sources = registered; }
    var bound = registered.Select(item =>
    {
      var canonical = _paths.Resolve(item.Source.Root);
      var matches = sources.Where(source => _paths.Same(_paths.Resolve(source.Source.Root), canonical)).ToArray();
      if (matches.Length != 1) { throw new InvalidDataException("The registered source must map uniquely to its frozen source policy."); }
      var policy = matches[0];
      return policy with { Source = policy.Source with { Id = item.Source.Id,
        ExcludedRoots = policy.Source.ExcludedRoots.Append(dataRoot).Distinct(_paths.Comparer).ToImmutableArray() } };
    }).ToImmutableArray();
    foreach (var item in bound)
    {
      var source = item.Source;
      IndexDatabase.Execute(connection,
        """
        UPDATE sources SET is_protected=$protected,skip_regenerable=$regen,skip_cloud=$cloud,regenerable_names=$names,
          exclusions=$exclusions,volume_serial=$serial WHERE id=$id
        """, ("$protected", item.Protected ? 1 : 0), ("$regen", source.SkipRegenerable ? 1 : 0), ("$cloud", source.SkipCloudOnly ? 1 : 0),
        ("$names", string.Join('\n', source.RegenerableNames)), ("$exclusions", string.Join('\n', source.Exclusions)),
        ("$serial", item.VolumeSerial), ("$id", source.Id));
      var snapshot = ScanJobSnapshot.Create(source.Id, engine, bound);
      IndexDatabase.Execute(connection, "UPDATE jobs SET analysis_snapshot=$snapshot WHERE source_id=$id",
        ("$snapshot", snapshot.Serialize()), ("$id", source.Id));
    }
    transaction.Commit();
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

  private static ImmutableArray<AnalysisSessionFile> ReadFiles(IndexDatabase database, AnalysisEngineSnapshot engine, HashSet<string> online,
    ref bool complete, CancellationToken cancellationToken)
  {
    var files = ImmutableArray.CreateBuilder<AnalysisSessionFile>();
    using var connection = database.Open();
    using var command = IndexDatabase.Command(connection,
      """
      SELECT f.id,f.source_id,s.root,f.rel_path,f.size,f.mtime,
        CASE WHEN $images=1 THEN p.hash ELSE f.hash END,f.cloud_only,p.file_identity
      FROM files f JOIN sources s ON s.id=f.source_id
      LEFT JOIN file_fingerprints p ON p.file_id=f.id AND ($images=1 OR p.hash=f.hash) AND p.algorithm=$algorithm AND p.parameters=$parameters
        AND p.engine_id=$engine AND p.engine_version=$version AND p.upstream_commit IS $commit
      ORDER BY f.id
      """, ("$algorithm", engine.Algorithm), ("$parameters", engine.ParametersJson), ("$engine", engine.EngineId),
      ("$version", engine.EngineVersion), ("$commit", engine.UpstreamCommit), ("$images", engine.Capability == AnalysisCapability.SimilarImages ? 1 : 0));
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      if (cancellationToken.IsCancellationRequested) { complete = false; break; }
      var path = Path.Combine(reader.GetString(2), reader.GetString(3));
      var observation = new FileObservation(reader.GetInt64(4), reader.GetInt64(5));
      var hash = reader.IsDBNull(6) ? null : reader.GetString(6);
      if (hash is not null)
      {
        FileObservation? evidence = null;
        try { if (!reader.IsDBNull(8)) { evidence = JsonSerializer.Deserialize<FileObservation>(reader.GetString(8), AnalysisJson.Options); } }
        catch (JsonException) { }
        if (!PerceptualFingerprints.IsValid(engine, hash) || evidence is null || !observation.Matches(evidence)) { hash = null; complete = false; }
        else { observation = evidence; }
      }
      var status = reader.GetInt64(7) != 0 ? "CloudOnly" : hash is not null ? "Confirmed" : complete ? "NotContentCandidate" : "Unconfirmed";
      if (status == "CloudOnly") { hash = null; }
      if (engine.Capability == AnalysisCapability.SimilarImages && status != "CloudOnly")
      {
        status = !ImageSourceScanner.SupportsPath(path) ? "NotImage" : hash is not null ? "PerceptualEvidence" : "Unconfirmed";
        if (status == "Unconfirmed") { complete = false; }
      }
      if (!online.Contains(reader.GetString(1))) { status = "SourceUnavailable"; hash = null; complete = false; }
      if (status is not ("CloudOnly" or "SourceUnavailable"))
      {
        try
        {
          if (engine.Capability == AnalysisCapability.SimilarImages && ImageSourceScanner.IsCloudOnly(File.GetAttributes(path)))
          {
            status = "CloudOnly"; hash = null; complete = false;
            IndexDatabase.Execute(connection, "UPDATE files SET cloud_only=1,hash=NULL WHERE id=$id", ("$id", reader.GetInt64(0)));
          }
          else
          {
            var current = FileObservation.Capture(path);
            if (!observation.Matches(current)) { hash = null; status = "Changed"; complete = false; }
            else { observation = current; }
          }
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
