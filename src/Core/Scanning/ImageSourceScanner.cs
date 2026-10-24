using System.Collections.Immutable;
using FileWorkbench.Analysis;
using FileWorkbench.Index;
using FileWorkbench.Sources;

namespace FileWorkbench.Scanning;

/// <summary>Inventory and checkpoint perceptual evidence through a separate publication entry point.</summary>
internal sealed class ImageSourceScanner(IndexDatabase database, IFileAnalysisEngine engine, AnalysisEngineSnapshot snapshot)
{
  public const int MaximumImages = 2000;
  private const int CohortSize = 100;
  private const FileAttributes CloudPlaceholder = FileAttributes.Offline | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

  public static bool SupportsPath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
  public static bool IsCloudOnly(FileAttributes attributes) => (attributes & CloudPlaceholder) != 0;

  public ScanStop Run(ScanSource source, string jobId, Action<ScanProgress> report, Func<bool> pauseRequested,
    Action checkScope, CancellationToken cancellationToken)
    => RunAsync(source, jobId, report, pauseRequested, checkScope, cancellationToken).GetAwaiter().GetResult();

  private async Task<ScanStop> RunAsync(ScanSource source, string jobId, Action<ScanProgress> report, Func<bool> pauseRequested,
    Action checkScope, CancellationToken cancellationToken)
  {
    if (snapshot.Capability != AnalysisCapability.SimilarImages) { throw new ArgumentException("This scanner requires similar-image analysis."); }
    new AnalysisEngineRegistry([engine]).Resolve(snapshot);
    using var connection = database.Open();
    var evidenceStore = new AnalysisEvidenceStore(connection, jobId, snapshot);
    var progress = new ScanProgress(ScanPhase.Discover, 0, 0, 0, 0, 0, 0, null, 0, null);
    var stop = SourceScanner.Inventory(database, source, jobId, value => { progress = value; report(value); }, pauseRequested,
      checkScope, cancellationToken);
    if (stop != ScanStop.Completed) { return stop; }
    var inputs = ImmutableArray.CreateBuilder<AnalysisInput>();
    var complete = progress.Errors == 0;
    void Error(string path, string message)
    {
      complete = false;
      progress = progress with { Errors = progress.Errors + 1 };
      IndexDatabase.Execute(connection, "INSERT INTO scan_errors(job_id,path,message) VALUES ($job,$path,$message)",
        ("$job", jobId), ("$path", path), ("$message", message));
    }
    bool CloudOnly(long id, FileInfo file)
    {
      if (!IsCloudOnly(file.Attributes)) { return false; }
      IndexDatabase.Execute(connection, "UPDATE files SET cloud_only=1,hash=NULL WHERE id=$id", ("$id", id));
      progress = progress with { Skipped = progress.Skipped + 1 };
      Error(file.FullName, "The image is cloud-only; it was not downloaded for analysis.");
      return true;
    }
    bool ValidateInput(AnalysisInput input)
    {
      cancellationToken.ThrowIfCancellationRequested();
      checkScope();
      try
      {
        var file = new FileInfo(input.Path);
        if (!SourceScanner.Approved(source, Path.GetRelativePath(source.Root, input.Path), file))
        {
          Error(input.Path, "The image is no longer inside the approved source policy.");
          return false;
        }
        if (CloudOnly(input.Id, file)) { return false; }
        var current = FileObservation.Capture(input.Path);
        if (input.Observation.Matches(current)) { return true; }
        IndexDatabase.Execute(connection, "UPDATE files SET size=$size,mtime=$mtime,hash=NULL WHERE id=$id",
          ("$size", current.Size), ("$mtime", current.ModifiedUtcTicks), ("$id", input.Id));
        Error(input.Path, "The image changed before analysis or checkpoint.");
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Error(input.Path, error.Message); }
      return false;
    }
    using (var command = IndexDatabase.Command(connection,
      "SELECT id,rel_path,size,mtime FROM files WHERE source_id=$source AND cloud_only=0 ORDER BY id", ("$source", source.Id)))
    using (var reader = command.ExecuteReader())
    {
      while (reader.Read())
      {
        cancellationToken.ThrowIfCancellationRequested();
        var relative = reader.GetString(1);
        if (!SupportsPath(relative)) { continue; }
        if (inputs.Count >= MaximumImages) { throw new InvalidOperationException($"Experimental image analysis is limited to {MaximumImages} images per source; choose a smaller scope."); }
        var path = Path.Combine(source.Root, relative);
        try
        {
          checkScope();
          var file = new FileInfo(path);
          if (!SourceScanner.Approved(source, relative, file))
          {
            Error(path, "The image is no longer inside the approved source policy.");
            continue;
          }
          if (CloudOnly(reader.GetInt64(0), file)) { continue; }
          var observed = FileObservation.Capture(path);
          if (observed.Size != reader.GetInt64(2) || observed.ModifiedUtcTicks != reader.GetInt64(3))
          {
            Error(path, "The image changed after inventory.");
            continue;
          }
          inputs.Add(new(reader.GetInt64(0), source.Id, path, observed));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Error(path, error.Message); }
      }
    }
    progress = progress with { Phase = ScanPhase.Confirm, Candidates = inputs.Count, IsCheckpoint = false };
    report(progress);
    long confirmed = 0;
    foreach (var cohort in inputs.ToImmutable().Chunk(CohortSize))
    {
      cancellationToken.ThrowIfCancellationRequested();
      checkScope();
      var pending = ImmutableArray.CreateBuilder<AnalysisInput>();
      var successful = new List<(AnalysisInput Input, string Hash)>();
      foreach (var input in cohort)
      {
        if (!ValidateInput(input)) { continue; }
        var cached = evidenceStore.Cached(input);
        if (cached is null) { pending.Add(input); } else { successful.Add((input, cached)); }
      }
      if (pending.Count > 0)
      {
        var request = new AnalysisRequest(jobId, snapshot, pending.Where(ValidateInput).ToImmutableArray());
        var byId = request.Files.ToDictionary(input => input.Id);
        var validator = new AnalysisResultValidator(request);
        if (!request.Files.IsEmpty)
        {
          await foreach (var batch in engine.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false))
          {
            validator.Accept(batch);
            if (batch.IsFinal && !batch.IsComplete) { complete = false; }
            foreach (var result in batch.Files)
            {
              var input = byId[result.FileId];
              progress = progress with { CurrentPath = input.Path, CurrentSize = input.Observation.Size, IsCheckpoint = false };
              if (result.Status == AnalysisFileStatus.Succeeded) { successful.Add((input, result.Fingerprint!)); }
              else { Error(input.Path, result.Error ?? "Image analysis did not produce complete evidence."); }
              report(progress);
            }
          }
          validator.RequireFinal();
        }
      }
      foreach (var item in successful.ToArray())
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (ValidateInput(item.Input)) { continue; }
        successful.Remove(item);
      }
      cancellationToken.ThrowIfCancellationRequested();
      IndexDatabase.InTransaction(connection, () => { checkScope(); evidenceStore.Checkpoint(successful); });
      confirmed += successful.Count;
      progress = progress with { Hashed = confirmed, Succeeded = confirmed, IsCheckpoint = true, CheckpointAt = DateTimeOffset.Now };
      report(progress);
      if (pauseRequested()) { return ScanStop.Paused; }
    }
    cancellationToken.ThrowIfCancellationRequested();
    if (complete) { evidenceStore.PublishPerceptual(confirmed, checkScope, Error, cancellationToken); }
    report(progress with { Phase = ScanPhase.Done, IsCheckpoint = true, CheckpointAt = DateTimeOffset.Now });
    return ScanStop.Completed;
  }
}
