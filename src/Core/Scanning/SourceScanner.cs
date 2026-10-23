using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Enumeration;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Sources;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Scanning;

/// <summary>What a scan needs to know about its source.</summary>
public sealed record ScanSource(
  string Id,
  string Root,
  bool SkipRegenerable,
  bool SkipCloudOnly,
  IReadOnlyList<string> RegenerableNames,
  IReadOnlyList<string> Exclusions)
{
  /// <summary>Absolute folders never indexed, such as the app's own data folder and its recycle area.</summary>
  public IReadOnlyList<string> ExcludedRoots { get; init; } = [];
}

/// <summary>How a scan ended.</summary>
public enum ScanStop
{
  Completed,
  Paused,
  Canceled,
}

public sealed record ScanProgress(
  ScanPhase Phase,
  long Discovered,
  long Candidates,
  long Hashed,
  long Succeeded,
  long Skipped,
  long Errors,
  string? CurrentPath,
  long CurrentSize,
  DateTimeOffset? CheckpointAt);

/// <summary>
/// Scans one source in the stages of ADR-004. Discovery records path, size and time for every file in batches; each
/// committed batch is a checkpoint, and an interrupted scan simply runs again over the same rows. Confirmation computes
/// a content fingerprint only for files that share their size with another file, in any source that is online.
/// Fingerprints stay valid while size and modification time are unchanged. Nothing on disk is ever written.
/// </summary>
public sealed class SourceScanner
{
  public const int BatchSize = 500;
  public const string HashPrefix = "xxh128:";

  /// <summary>Files whose name marks the folder as a code project; such files never form single-file stacks.</summary>
  public static readonly IReadOnlyList<string> ProjectMarkers =
    ["package.json", "pyproject.toml", "Cargo.toml", "go.mod", "pom.xml", "build.gradle", "*.sln", "*.csproj", "*.vcxproj", "CMakeLists.txt"];

  private const int HashBatchSize = 200;
  private const FileAttributes CloudPlaceholder = FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

  private readonly IndexDatabase _database;
  private readonly IFileAnalysisEngine _engine;
  private readonly AnalysisEngineSnapshot _analysis;

  public SourceScanner(IndexDatabase database, IFileAnalysisEngine? engine = null, AnalysisEngineSnapshot? analysis = null)
  {
    _database = database;
    engine ??= new ManagedAnalysisEngine();
    var registry = new AnalysisEngineRegistry([engine]);
    _analysis = analysis ?? registry.Snapshot(engine.Descriptor.Id, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
    _engine = registry.Resolve(_analysis);
  }

  /// <summary>Runs discovery and then confirmation for <paramref name="source"/>.</summary>
  /// <param name="source">The source to scan.</param>
  /// <param name="jobId">Job the errors are recorded under.</param>
  /// <param name="onlineRoots">Roots of every online source, by id; confirmation may fingerprint their files too.</param>
  /// <param name="report">Receives progress after each file and each checkpoint.</param>
  /// <param name="pauseRequested">Checked between batches; when true the scan stops after the current batch.</param>
  /// <param name="cancellationToken">Stops the scan as soon as possible; committed batches stay.</param>
  public ScanStop Run(ScanSource source, string jobId, IReadOnlyDictionary<string, string> onlineRoots, Action<ScanProgress> report,
    Func<bool> pauseRequested, CancellationToken cancellationToken)
  {
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(onlineRoots);
    ArgumentNullException.ThrowIfNull(report);
    ArgumentNullException.ThrowIfNull(pauseRequested);

    using var connection = _database.Open();
    var state = new State(source, jobId, connection, report);
    var stop = Discover(state, pauseRequested, cancellationToken);
    if (stop != ScanStop.Completed)
    {
      return stop;
    }
    return ConfirmAsync(state, onlineRoots, pauseRequested, cancellationToken).GetAwaiter().GetResult();
  }

  /// <summary>Content fingerprint of a file: XxHash128 over all its bytes, tagged with the algorithm.</summary>
  public static string Fingerprint(string path, CancellationToken cancellationToken) =>
    ContentFingerprints.Compute(path, ContentFingerprints.XxHash128, cancellationToken);

  public static bool IsExcluded(string relativePath, string name, IReadOnlyList<string> patterns)
  {
    ArgumentNullException.ThrowIfNull(relativePath);
    ArgumentNullException.ThrowIfNull(patterns);
    foreach (var raw in patterns)
    {
      var pattern = raw.Trim();
      if (pattern.Length == 0)
      {
        continue;
      }
      if (pattern.Contains('\\', StringComparison.Ordinal) || pattern.Contains('/', StringComparison.Ordinal))
      {
        var prefix = pattern.Replace('/', '\\').Trim('\\');
        if (relativePath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
          || relativePath.StartsWith(prefix + "\\", StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
      else if (FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: true))
      {
        return true;
      }
    }
    return false;
  }

  private static ScanStop Discover(State state, Func<bool> pauseRequested, CancellationToken cancellationToken)
  {
    var connection = state.Connection;
    var source = state.Source;
    var root = WithSeparator(source.Root);
    // One statement, so two scans of the same source can never share a scan number.
    var scan = IndexDatabase.Scalar<long>(connection, "UPDATE sources SET scan_number = scan_number + 1 WHERE id = $id RETURNING scan_number",
      ("$id", source.Id));
    var filter = new ScanFilter(source, source.ExcludedRoots);

    var pending = new List<(string Path, long Size, long Mtime, bool Cloud)>(BatchSize);
    var regenerable = new List<(string Path, string Name, long Bytes, long Files)>();
    var projects = new List<(string Path, string Marker)>();
    var erroredDirectories = new List<string>();
    var directories = new Stack<string>();
    directories.Push(string.Empty);
    var options = new EnumerationOptions
    {
      RecurseSubdirectories = false,
      IgnoreInaccessible = false,
      AttributesToSkip = 0,
      ReturnSpecialDirectories = false,
    };

    while (directories.Count > 0)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var relativeDirectory = directories.Pop();
      var fullDirectory = relativeDirectory.Length == 0 ? root : root + relativeDirectory;
      try
      {
        foreach (var entry in new DirectoryInfo(fullDirectory).EnumerateFileSystemInfos("*", options))
        {
          var relative = relativeDirectory.Length == 0 ? entry.Name : relativeDirectory + "\\" + entry.Name;
          var kind = filter.Classify(entry, relative);
          if (kind == EntryKind.GitFolder)
          {
            projects.Add((relativeDirectory, ".git"));
            continue;
          }
          if (kind == EntryKind.Regenerable)
          {
            var (bytes, files) = Measure((DirectoryInfo)entry);
            regenerable.Add((relative, entry.Name, bytes, files));
            continue;
          }
          if (kind == EntryKind.Skip)
          {
            state.Skipped++;
            continue;
          }
          if (entry is DirectoryInfo)
          {
            directories.Push(relative);
            continue;
          }

          var file = (FileInfo)entry;
          if (ScanFilter.ProjectMarker(file.Name) is not null)
          {
            projects.Add((relativeDirectory, file.Name));
          }
          var cloud = (file.Attributes & CloudPlaceholder) != 0;
          pending.Add((relative, file.Length, file.LastWriteTimeUtc.Ticks, cloud));
          state.Discovered++;
          state.Current = (file.FullName, file.Length);
          if (pending.Count >= BatchSize)
          {
            WriteFiles(state, scan, pending);
            state.Report(ScanPhase.Discover, checkpoint: true);
            if (pauseRequested())
            {
              return ScanStop.Paused;
            }
          }
        }
      }
      catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
      {
        erroredDirectories.Add(relativeDirectory);
        state.RecordError(fullDirectory, e.Message);
      }
    }

    WriteFiles(state, scan, pending);
    IndexDatabase.InTransaction(connection, () =>
    {
      foreach (var (path, name, bytes, files) in regenerable)
      {
        IndexDatabase.Execute(connection,
          """
          INSERT INTO regenerable_dirs(source_id, rel_path, name, bytes, files, seen) VALUES ($s, $p, $n, $b, $f, $seen)
          ON CONFLICT(source_id, rel_path) DO UPDATE SET name = excluded.name, bytes = excluded.bytes, files = excluded.files, seen = excluded.seen
          """,
          ("$s", source.Id), ("$p", path), ("$n", name), ("$b", bytes), ("$f", files), ("$seen", scan));
      }
      foreach (var (path, marker) in projects.DistinctBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
      {
        IndexDatabase.Execute(connection,
          """
          INSERT INTO project_dirs(source_id, rel_path, marker, seen) VALUES ($s, $p, $m, $seen)
          ON CONFLICT(source_id, rel_path) DO UPDATE SET marker = excluded.marker, seen = excluded.seen
          """,
          ("$s", source.Id), ("$p", path), ("$m", marker), ("$seen", scan));
      }
    });
    RemoveUnseen(state, scan, erroredDirectories);
    state.Report(ScanPhase.Discover, checkpoint: true);
    return ScanStop.Completed;
  }

  /// <summary>
  /// A file is only recorded as gone after a complete walk; rows under a folder that could not be read this time are kept.
  /// </summary>
  private static void RemoveUnseen(State state, long scan, List<string> erroredDirectories)
  {
    var connection = state.Connection;
    bool UnderError(string path) => erroredDirectories.Any(d =>
      d.Length == 0 || path.Equals(d, StringComparison.OrdinalIgnoreCase) || path.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase));

    foreach (var table in new[] { "files", "regenerable_dirs", "project_dirs" })
    {
      var stale = new List<string>();
      using (var command = IndexDatabase.Command(connection, $"SELECT rel_path FROM {table} WHERE source_id = $s AND seen <> $seen",
        ("$s", state.Source.Id), ("$seen", scan)))
      using (var reader = command.ExecuteReader())
      {
        while (reader.Read())
        {
          stale.Add(reader.GetString(0));
        }
      }
      var remove = stale.Where(p => !UnderError(p)).ToList();
      if (remove.Count == 0)
      {
        continue;
      }
      IndexDatabase.InTransaction(connection, () =>
      {
        using var delete = IndexDatabase.Command(connection, $"DELETE FROM {table} WHERE source_id = $s AND rel_path = $p",
          ("$s", state.Source.Id), ("$p", string.Empty));
        foreach (var path in remove)
        {
          delete.Parameters["$p"].Value = path;
          delete.ExecuteNonQuery();
        }
      });
    }
  }

  private async Task<ScanStop> ConfirmAsync(State state, IReadOnlyDictionary<string, string> onlineRoots, Func<bool> pauseRequested,
    CancellationToken cancellationToken)
  {
    var connection = state.Connection;
    var candidates = new List<(long Id, string SourceId, string Path, long Size, long Mtime)>();
    using (var command = IndexDatabase.Command(connection,
      """
      SELECT f.id, f.source_id, f.rel_path, f.size, f.mtime FROM files f
      WHERE f.hash IS NULL AND f.cloud_only = 0 AND f.size > 0
        AND f.size IN (SELECT size FROM files WHERE cloud_only = 0 AND size > 0 GROUP BY size HAVING COUNT(*) > 1)
      ORDER BY (f.source_id = $s) DESC, f.size
      """,
      ("$s", state.Source.Id)))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        var sourceId = reader.GetString(1);
        if (onlineRoots.ContainsKey(sourceId))
        {
          candidates.Add((reader.GetInt64(0), sourceId, reader.GetString(2), reader.GetInt64(3), reader.GetInt64(4)));
        }
      }
    }
    state.Candidates = candidates.Count + IndexDatabase.Scalar<long>(connection,
      "SELECT COUNT(*) FROM files WHERE source_id = $s AND hash IS NOT NULL", ("$s", state.Source.Id));
    state.Report(ScanPhase.Confirm, checkpoint: false);

    var hashed = new List<(long Id, string Hash)>(HashBatchSize);
    var changed = new List<(long Id, long Size, long Mtime)>();
    var becameCloud = new List<long>();
    var sinceCommit = Stopwatch.StartNew();
    void Commit()
    {
      if (hashed.Count == 0 && changed.Count == 0 && becameCloud.Count == 0)
      {
        return;
      }
      IndexDatabase.InTransaction(connection, () =>
      {
        using var update = IndexDatabase.Command(connection, "UPDATE files SET hash = $h WHERE id = $id", ("$h", string.Empty), ("$id", 0L));
        foreach (var (id, hash) in hashed)
        {
          update.Parameters["$h"].Value = hash;
          update.Parameters["$id"].Value = id;
          update.ExecuteNonQuery();
        }
        using var stale = IndexDatabase.Command(connection, "UPDATE files SET size = $size, mtime = $mtime, hash = NULL WHERE id = $id",
          ("$size", 0L), ("$mtime", 0L), ("$id", 0L));
        foreach (var (id, size, mtime) in changed)
        {
          stale.Parameters["$size"].Value = size;
          stale.Parameters["$mtime"].Value = mtime;
          stale.Parameters["$id"].Value = id;
          stale.ExecuteNonQuery();
        }
        using var cloud = IndexDatabase.Command(connection, "UPDATE files SET cloud_only = 1, hash = NULL WHERE id = $id", ("$id", 0L));
        foreach (var id in becameCloud)
        {
          cloud.Parameters["$id"].Value = id;
          cloud.ExecuteNonQuery();
        }
      });
      becameCloud.Clear();
      hashed.Clear();
      changed.Clear();
      sinceCommit.Restart();
      state.Report(ScanPhase.Confirm, checkpoint: true);
    }

    foreach (var cohort in candidates.Chunk(HashBatchSize))
    {
      var inputs = ImmutableArray.CreateBuilder<AnalysisInput>();
      foreach (var (id, sourceId, relative, size, mtime) in cohort)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var path = WithSeparator(onlineRoots[sourceId]) + relative;
        state.Current = (path, size);
        try
        {
          var file = new FileInfo(path);
          if (!file.Exists) { state.Skipped++; continue; }
          if (file.Length != size || file.LastWriteTimeUtc.Ticks != mtime)
          {
            changed.Add((id, file.Length, file.LastWriteTimeUtc.Ticks));
            state.Skipped++;
            continue;
          }
          if ((file.Attributes & CloudPlaceholder) != 0)
          {
            becameCloud.Add(id);
            state.Skipped++;
            continue;
          }
          inputs.Add(new(id, sourceId, path, new(size, mtime, OperatingSystem.IsWindows() ? PathIdentity.Identify(path) : null)));
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException)
        {
          state.RecordError(path, error.Message);
        }
      }
      var request = new AnalysisRequest(state.JobId, _analysis, inputs.ToImmutable());
      var validator = new AnalysisResultValidator(request);
      var byId = request.Files.ToDictionary(file => file.Id);
      await foreach (var batch in _engine.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false))
      {
        validator.Accept(batch);
        foreach (var result in batch.Files)
        {
          var input = byId[result.FileId];
          state.Current = (input.Path, input.Observation.Size);
          if (result.Status == AnalysisFileStatus.Succeeded)
          {
            try
            {
              var current = FileObservation.Capture(input.Path);
              if (input.Observation.Matches(current))
              {
                hashed.Add((input.Id, result.Fingerprint!));
                state.Hashed++;
                state.Succeeded++;
              }
              else { changed.Add((input.Id, current.Size, current.ModifiedUtcTicks)); state.Skipped++; }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException) { state.RecordError(input.Path, error.Message); }
          }
          else if (result.Status == AnalysisFileStatus.Changed)
          {
            try
            {
              var current = FileObservation.Capture(input.Path);
              changed.Add((input.Id, current.Size, current.ModifiedUtcTicks));
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException) { state.RecordError(input.Path, error.Message); }
            state.Skipped++;
          }
          else if (result.Status == AnalysisFileStatus.Skipped)
          {
            state.Skipped++;
            try
            {
              var current = new FileInfo(input.Path);
              if (current.Exists && (current.Attributes & CloudPlaceholder) != 0) { becameCloud.Add(input.Id); }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException) { state.RecordError(input.Path, error.Message); }
          }
          else { state.RecordError(input.Path, result.Error!); }
          state.Report(ScanPhase.Confirm, checkpoint: false);
        }
      }
      validator.RequireFinal();
      if (hashed.Count > 0 || changed.Count > 0 || becameCloud.Count > 0 || sinceCommit.Elapsed > TimeSpan.FromSeconds(10))
      {
        Commit();
        if (pauseRequested())
        {
          return ScanStop.Paused;
        }
      }
    }
    Commit();
    state.Report(ScanPhase.Done, checkpoint: true);
    return ScanStop.Completed;
  }

  private static void WriteFiles(State state, long scan, List<(string Path, long Size, long Mtime, bool Cloud)> pending)
  {
    if (pending.Count == 0)
    {
      return;
    }
    var connection = state.Connection;
    IndexDatabase.InTransaction(connection, () =>
    {
      // Size and time unchanged keeps the fingerprint (ADR-004); anything else clears it.
      using var upsert = IndexDatabase.Command(connection,
        """
        INSERT INTO files(source_id, rel_path, size, mtime, hash, cloud_only, seen) VALUES ($s, $p, $size, $mtime, NULL, $cloud, $seen)
        ON CONFLICT(source_id, rel_path) DO UPDATE SET
          hash = CASE WHEN files.size = excluded.size AND files.mtime = excluded.mtime THEN files.hash ELSE NULL END,
          size = excluded.size, mtime = excluded.mtime, cloud_only = excluded.cloud_only, seen = excluded.seen
        """,
        ("$s", state.Source.Id), ("$p", string.Empty), ("$size", 0L), ("$mtime", 0L), ("$cloud", 0), ("$seen", scan));
      foreach (var (path, size, mtime, cloud) in pending)
      {
        upsert.Parameters["$p"].Value = path;
        upsert.Parameters["$size"].Value = size;
        upsert.Parameters["$mtime"].Value = mtime;
        upsert.Parameters["$cloud"].Value = cloud ? 1 : 0;
        upsert.ExecuteNonQuery();
      }
    });
    pending.Clear();
  }

  private static (long Bytes, long Files) Measure(DirectoryInfo directory)
  {
    long bytes = 0, files = 0;
    try
    {
      var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
      foreach (var file in directory.EnumerateFiles("*", options))
      {
        bytes += file.Length;
        files++;
      }
    }
    catch (Exception e) when (e is UnauthorizedAccessException or IOException)
    {
      // A partly measured folder is still only a size estimate.
    }
    return (bytes, files);
  }

  private static string WithSeparator(string root) => root.EndsWith('\\') ? root : root + "\\";

  private sealed class State(ScanSource source, string jobId, SqliteConnection connection, Action<ScanProgress> report)
  {
    public string JobId { get; } = jobId;
    public ScanSource Source { get; } = source;
    public SqliteConnection Connection { get; } = connection;
    public long Discovered { get; set; }
    public long Candidates { get; set; }
    public long Hashed { get; set; }
    public long Succeeded { get; set; }
    public long Skipped { get; set; }
    public long Errors { get; set; }
    public (string Path, long Size) Current { get; set; }
    public DateTimeOffset? CheckpointAt { get; set; }

    public void Report(ScanPhase phase, bool checkpoint)
    {
      if (checkpoint)
      {
        CheckpointAt = DateTimeOffset.Now;
      }
      report(new ScanProgress(phase, Discovered, Candidates, Hashed, Succeeded, Skipped, Errors, Current.Path, Current.Size, CheckpointAt));
    }

    public void RecordError(string path, string message)
    {
      Errors++;
      IndexDatabase.Execute(Connection, "INSERT INTO scan_errors(job_id, path, message) VALUES ($j, $p, $m)",
        ("$j", JobId), ("$p", path), ("$m", message));
    }
  }
}
