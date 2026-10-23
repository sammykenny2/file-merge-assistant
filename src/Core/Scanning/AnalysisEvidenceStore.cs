using System.Globalization;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Scanning;

/// <summary>Checkpoints reusable per-file evidence separately from the active, complete result generation.</summary>
internal sealed class AnalysisEvidenceStore
{
  private readonly SqliteConnection _connection;
  private readonly string _jobId;
  private readonly AnalysisEngineSnapshot _engine;

  public AnalysisEvidenceStore(SqliteConnection connection, string jobId, AnalysisEngineSnapshot engine)
  {
    _connection = connection;
    _jobId = jobId;
    _engine = engine;
    Generation = IndexDatabase.Scalar<long>(connection,
      "UPDATE jobs SET analysis_generation = analysis_generation + 1, analysis_complete = 0 WHERE id = $id RETURNING analysis_generation",
      ("$id", jobId));
    if (Generation <= 0) { throw new InvalidOperationException("The analysis job must exist before scanning."); }
  }

  public long Generation { get; }

  public string? Cached(AnalysisInput input)
  {
    using var command = IndexDatabase.Command(_connection,
      """
      SELECT hash, size, mtime, file_identity FROM file_fingerprints
      WHERE file_id = $id AND algorithm = $algorithm AND parameters = $parameters
        AND engine_id = $engine AND engine_version = $version AND upstream_commit IS $commit
      """,
      ("$id", input.Id), ("$algorithm", _engine.Algorithm), ("$parameters", _engine.ParametersJson),
      ("$engine", _engine.EngineId), ("$version", _engine.EngineVersion), ("$commit", _engine.UpstreamCommit));
    using var reader = command.ExecuteReader();
    if (!reader.Read()) { return null; }
    var hash = reader.GetString(0);
    FileObservation? identity;
    try { identity = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<FileObservation>(reader.GetString(3), AnalysisJson.Options); }
    catch (JsonException) { return null; }
    if (!PerceptualFingerprints.IsValid(_engine, hash) || identity is null
      || reader.GetInt64(1) != input.Observation.Size || reader.GetInt64(2) != input.Observation.ModifiedUtcTicks
      || !input.Observation.Matches(identity) || !identity.Matches(input.Observation)) { return null; }
    return hash;
  }

  /// <summary>Called inside the scanner's transaction, after validating an entire engine cohort.</summary>
  public void Checkpoint(IReadOnlyList<(AnalysisInput Input, string Hash)> results)
  {
    if (IndexDatabase.Scalar<long>(_connection, "SELECT analysis_generation FROM jobs WHERE id=$job", ("$job", _jobId)) != Generation)
    {
      throw new InvalidOperationException("The analysis generation is no longer current.");
    }
    foreach (var (input, hash) in results)
    {
      if (!PerceptualFingerprints.IsValid(_engine, hash)) { throw new InvalidDataException("Evidence does not match the analysis capability."); }
      IndexDatabase.Execute(_connection,
        """
        INSERT INTO file_fingerprints(file_id, algorithm, parameters, hash, size, mtime, file_identity,
          engine_id, engine_version, upstream_commit, job_id, generation, observed_at)
        VALUES ($id, $algorithm, $parameters, $hash, $size, $mtime, $identity, $engine, $version, $commit, $job, $generation, $at)
        ON CONFLICT(file_id, algorithm, parameters) DO UPDATE SET
          hash=excluded.hash, size=excluded.size, mtime=excluded.mtime, file_identity=excluded.file_identity,
          engine_id=excluded.engine_id, engine_version=excluded.engine_version, upstream_commit=excluded.upstream_commit,
          job_id=excluded.job_id, generation=excluded.generation, observed_at=excluded.observed_at
        WHERE file_fingerprints.hash <> excluded.hash OR file_fingerprints.size <> excluded.size OR file_fingerprints.mtime <> excluded.mtime
          OR file_fingerprints.file_identity IS NOT excluded.file_identity
          OR file_fingerprints.engine_id <> excluded.engine_id OR file_fingerprints.engine_version <> excluded.engine_version
          OR file_fingerprints.upstream_commit IS NOT excluded.upstream_commit
        """,
        ("$id", input.Id), ("$algorithm", _engine.Algorithm), ("$parameters", _engine.ParametersJson), ("$hash", hash),
        ("$size", input.Observation.Size), ("$mtime", input.Observation.ModifiedUtcTicks),
        ("$identity", JsonSerializer.Serialize(input.Observation, AnalysisJson.Options)), ("$engine", _engine.EngineId),
        ("$version", _engine.EngineVersion), ("$commit", _engine.UpstreamCommit), ("$job", _jobId),
        ("$generation", Generation), ("$at", DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
      IndexDatabase.Execute(_connection,
        """
        INSERT INTO analysis_results(job_id, generation, file_id, hash, size, mtime, path, observation)
        VALUES ($job, $generation, $id, $hash, $size, $mtime, $path, $observation)
        """,
        ("$job", _jobId), ("$generation", Generation), ("$id", input.Id), ("$hash", hash),
        ("$size", input.Observation.Size), ("$mtime", input.Observation.ModifiedUtcTicks), ("$path", input.Path),
        ("$observation", JsonSerializer.Serialize(input.Observation, AnalysisJson.Options)));
    }
  }

  public bool Publish(long expected, Action checkScope, Action<string, string> reportError, CancellationToken cancellationToken)
  {
    if (_engine.Capability != AnalysisCapability.ExactDuplicates) { throw new InvalidOperationException("Only exact-content evidence can update the active content hash."); }
    return PublishCore(expected, checkScope, reportError, activateContent: true, cancellationToken);
  }

  public bool PublishPerceptual(long expected, Action checkScope, Action<string, string> reportError, CancellationToken cancellationToken)
  {
    if (_engine.Capability != AnalysisCapability.SimilarImages) { throw new InvalidOperationException("This publication requires similar-image evidence."); }
    return PublishCore(expected, checkScope, reportError, activateContent: false, cancellationToken);
  }

  private bool PublishCore(long expected, Action checkScope, Action<string, string> reportError, bool activateContent, CancellationToken cancellationToken)
  {
    var unchanged = true;
    foreach (var (id, path, observation) in StagedInputs(checkScope))
    {
      cancellationToken.ThrowIfCancellationRequested();
      try
      {
        if (_engine.Capability == AnalysisCapability.SimilarImages && ImageSourceScanner.IsCloudOnly(File.GetAttributes(path)))
        {
          checkScope();
          IndexDatabase.Execute(_connection, "UPDATE files SET cloud_only=1,hash=NULL WHERE id=$id", ("$id", id));
          reportError(path, "The image became cloud-only after its checkpoint; it was not downloaded.");
          unchanged = false;
          continue;
        }
        var current = FileObservation.Capture(path);
        if (observation.Matches(current)) { continue; }
        checkScope();
        IndexDatabase.Execute(_connection, "UPDATE files SET hash=NULL, size=$size, mtime=$mtime WHERE id=$id",
          ("$id", id), ("$size", current.Size), ("$mtime", current.ModifiedUtcTicks));
        reportError(path, "The file changed after its analysis checkpoint.");
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException)
      {
        checkScope();
        IndexDatabase.Execute(_connection, "UPDATE files SET hash=NULL WHERE id=$id", ("$id", id));
        reportError(path, error.Message);
      }
      unchanged = false;
    }
    if (!unchanged) { return false; }
    var published = false;
    IndexDatabase.InTransaction(_connection, () =>
    {
      cancellationToken.ThrowIfCancellationRequested();
      checkScope();
      if (IndexDatabase.Scalar<long>(_connection, "SELECT analysis_generation FROM jobs WHERE id=$job", ("$job", _jobId)) != Generation) { return; }
      var matching = IndexDatabase.Scalar<long>(_connection,
        """
        SELECT COUNT(*) FROM analysis_results r JOIN files f ON f.id=r.file_id
        WHERE r.job_id=$job AND r.generation=$generation AND f.size=r.size AND f.mtime=r.mtime AND f.cloud_only=0
        """, ("$job", _jobId), ("$generation", Generation));
      if (matching != expected) { return; }
      if (activateContent)
      {
        IndexDatabase.Execute(_connection,
          """
          UPDATE files SET hash = (SELECT r.hash FROM analysis_results r WHERE r.job_id=$job AND r.generation=$generation AND r.file_id=files.id)
          WHERE id IN (SELECT file_id FROM analysis_results WHERE job_id=$job AND generation=$generation)
          """, ("$job", _jobId), ("$generation", Generation));
      }
      IndexDatabase.Execute(_connection, "UPDATE jobs SET analysis_complete=1 WHERE id=$job AND analysis_generation=$generation",
        ("$job", _jobId), ("$generation", Generation));
      cancellationToken.ThrowIfCancellationRequested();
      published = true;
    });
    return published;
  }

  private IEnumerable<(long Id, string Path, FileObservation Observation)> StagedInputs(Action checkScope)
  {
    long after = 0;
    while (true)
    {
      checkScope();
      var page = new List<(long Id, string Path, FileObservation Observation)>();
      using (var command = IndexDatabase.Command(_connection,
        """
        SELECT file_id, path, observation FROM analysis_results
        WHERE job_id=$job AND generation=$generation AND file_id>$after ORDER BY file_id LIMIT 500
        """, ("$job", _jobId), ("$generation", Generation), ("$after", after)))
      using (var reader = command.ExecuteReader())
      {
        while (reader.Read())
        {
          var observation = reader.IsDBNull(2) ? null : JsonSerializer.Deserialize<FileObservation>(reader.GetString(2), AnalysisJson.Options);
          if (reader.IsDBNull(1) || observation is null) { throw new InvalidDataException("A staged result is missing its original observation."); }
          page.Add((reader.GetInt64(0), reader.GetString(1), observation));
        }
      }
      if (page.Count == 0) { yield break; }
      foreach (var input in page) { yield return input; }
      after = page[^1].Id;
    }
  }
}
