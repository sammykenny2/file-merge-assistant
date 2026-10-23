using System.Collections.Immutable;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Scanning;

public sealed record SimilarImagePair(long FirstId, long SecondId, int Distance);
public sealed record ImageAnalysisError(string JobId, string Path, string Message);
public sealed record SimilarImageReport(int FormatVersion, string Outcome, int Threshold, string DistanceDefinition,
  AnalysisSessionReport Analysis, ImmutableArray<SimilarImagePair> Pairs, ImmutableArray<ImageAnalysisError> Errors, string? Error = null)
{
  public bool IsComplete => Outcome == "Completed";
  public string ToJson() => JsonSerializer.Serialize(this, AnalysisJson.Options);
}

/// <summary>Global pair comparison over validated per-file evidence, with no exact-content plan or grouping.</summary>
public sealed class SimilarImageAnalysis(AnalysisEngineRegistry engines, string? dataRoot = null, IVolumeInfoProvider? volumes = null)
{
  public const int DefaultThreshold = 6;
  public const int MaximumThreshold = 16;
  public const int MaximumPairs = 10000;
  public const int MaximumImages = ImageSourceScanner.MaximumImages;

  public Task<SimilarImageReport> RunAsync(IReadOnlyList<string> paths, AnalysisEngineSnapshot engine, int threshold = DefaultThreshold,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress = null, CancellationToken cancellationToken = default)
    => RunPreparedAsync(paths.ToImmutableArray(), default, engine, threshold, progress, cancellationToken);

  internal Task<SimilarImageReport> RunConfiguredAsync(ImmutableArray<ScanSourceSnapshot> sources, AnalysisEngineSnapshot engine, int threshold,
    IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
    => RunPreparedAsync(sources.Select(source => source.Source.Root).ToImmutableArray(), sources, engine, threshold, progress, cancellationToken);

  private async Task<SimilarImageReport> RunPreparedAsync(ImmutableArray<string> paths, ImmutableArray<ScanSourceSnapshot> sources,
    AnalysisEngineSnapshot engine, int threshold, IProgress<ImmutableArray<AnalysisSessionJob>>? progress, CancellationToken cancellationToken)
  {
    if (engine.Capability != AnalysisCapability.SimilarImages || engine.Algorithm != PerceptualFingerprints.Mean8D8)
    {
      throw new ArgumentException("This analysis requires supported similar-image evidence.", nameof(engine));
    }
    if (threshold is < 0 or > MaximumThreshold) { throw new ArgumentOutOfRangeException(nameof(threshold)); }
    engines.Resolve(engine);
    var session = new ReadOnlyAnalysisSession(engines, dataRoot, volumes);
    var report = sources.IsDefault
      ? await session.RunAsync(paths, engine, progress, cancellationToken).ConfigureAwait(false)
      : await session.RunConfiguredAsync(sources, engine, progress, cancellationToken).ConfigureAwait(false);
    return await Task.Run(() => Compare(report, threshold, cancellationToken), CancellationToken.None).ConfigureAwait(false);
  }

  private static SimilarImageReport Compare(AnalysisSessionReport report, int threshold, CancellationToken cancellationToken)
  {
    var errors = ImmutableArray.CreateBuilder<ImageAnalysisError>();
    using (var connection = IndexDatabase.OpenOrCreate(report.IndexPath).Open())
    using (var command = IndexDatabase.Command(connection, "SELECT job_id,path,message FROM scan_errors ORDER BY job_id,path"))
    using (var reader = command.ExecuteReader())
    {
      while (!cancellationToken.IsCancellationRequested && reader.Read()) { errors.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2))); }
    }
    SimilarImageReport Result(string outcome, ImmutableArray<SimilarImagePair> pairs, string? error = null)
    {
      var savedErrors = errors.ToImmutable();
      if (cancellationToken.IsCancellationRequested) { outcome = "Canceled"; }
      return new(1, outcome, threshold, PerceptualFingerprints.DistanceDefinition, report, outcome == "Completed" ? pairs : [], savedErrors, error);
    }
    if (cancellationToken.IsCancellationRequested) { return Result("Canceled", []); }
    if (!report.IsComplete) { return Result(report.Outcome, []); }
    var images = report.Files.Where(file => file.Status == "PerceptualEvidence").ToImmutableArray();
    if (images.Length > MaximumImages)
    {
      return Result("LimitExceeded", [], $"Experimental image analysis is limited to {MaximumImages} images across all sources; choose a smaller scope.");
    }
    var hashes = new ulong[images.Length][];
    for (var i = 0; i < images.Length; i++)
    {
      if (cancellationToken.IsCancellationRequested) { return Result("Canceled", []); }
      hashes[i] = PerceptualFingerprints.Parse(images[i].Fingerprint!);
    }
    var pairs = ImmutableArray.CreateBuilder<SimilarImagePair>();
    for (var i = 0; i < images.Length; i++)
    {
      for (var j = i + 1; j < images.Length; j++)
      {
        if (cancellationToken.IsCancellationRequested) { return Result("Canceled", []); }
        var distance = PerceptualFingerprints.Distance(hashes[i], hashes[j]);
        if (distance > threshold) { continue; }
        if (pairs.Count >= MaximumPairs)
        {
          return Result("LimitExceeded", [], $"The comparison exceeds the experimental limit of {MaximumPairs} pairs; choose a smaller scope or a lower distance threshold.");
        }
        pairs.Add(new(images[i].Id, images[j].Id, distance));
      }
    }
    return Result(cancellationToken.IsCancellationRequested ? "Canceled" : "Completed", pairs.ToImmutable());
  }
}
