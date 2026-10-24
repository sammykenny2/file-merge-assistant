using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Scanning;

public enum ImageReviewChoice { Pending, Related, Different }
public sealed record ImagePairDecision(long FirstId, long SecondId, ImageReviewChoice Choice);
public sealed record ImageReviewJob(string Id, string SourceId, ScanJobState State, long Generation, bool Complete, string? Reason);
public sealed record ImageReviewDocument(int FormatVersion, string IndexPath, AnalysisEngineDescriptor Descriptor, AnalysisEngineSnapshot Engine,
  string Outcome, int Threshold, string DistanceDefinition, ImmutableArray<ScanSourceSnapshot> Sources, ImmutableArray<ImageReviewJob> Jobs,
  ImmutableArray<AnalysisSessionFile> Images, ImmutableArray<SimilarImagePair> Pairs, ImmutableArray<ImageAnalysisError> Errors,
  ImmutableArray<ImagePairDecision> Decisions, bool DetailsTruncated, string? Error)
{
  [JsonIgnore]
  public string Revision { get; init; } = string.Empty;
}

/// <summary>Bounded local review metadata. It never creates file-operation decisions.</summary>
public sealed class ImageReviewArchive
{
  private const int MaximumBytes = 64 * 1024 * 1024;
  private const int MaximumErrors = 500;
  private static readonly ConcurrentDictionary<string, SemaphoreSlim> SaveGates = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
  private readonly IVolumeInfoProvider _volumes;
  private readonly ISourcePathService _paths;
  public string DirectoryPath { get; }

  public ImageReviewArchive(string dataRoot, IVolumeInfoProvider? volumes = null, ISourcePathService? sourcePaths = null)
  {
    if (!Path.IsPathFullyQualified(dataRoot)) { throw new ArgumentException("The archive root must be absolute.", nameof(dataRoot)); }
    _paths = sourcePaths ?? WindowsSourcePathService.Instance;
    DirectoryPath = Path.Combine(_paths.Resolve(dataRoot), "image-reviews");
    _volumes = volumes ?? new Win32VolumeInfoProvider();
  }

  public Task<string> CreateAsync(SimilarImageReport report, CancellationToken cancellationToken = default) => Task.Run(async () =>
  {
    var referenced = report.Pairs.SelectMany(pair => new[] { pair.FirstId, pair.SecondId }).ToHashSet();
    var supported = report.Analysis.Files.Where(file => ImageSourceScanner.SupportsPath(file.Path));
    var images = supported.Where(file => referenced.Contains(file.Id))
      .Concat(supported.Where(file => !referenced.Contains(file.Id) && file.Status == "PerceptualEvidence"))
      .Concat(supported.Where(file => !referenced.Contains(file.Id) && file.Status != "PerceptualEvidence"))
      .Take(SimilarImageAnalysis.MaximumImages).ToImmutableArray();
    var document = new ImageReviewDocument(1, report.Analysis.IndexPath, report.Analysis.Descriptor, report.Analysis.Engine,
      report.Outcome, report.Threshold, report.DistanceDefinition,
      report.Analysis.Jobs.Select(job => job.Snapshot.Sources.Single(source => source.Source.Id == job.SourceId)).ToImmutableArray(),
      report.Analysis.Jobs.Select(job => new ImageReviewJob(job.Id, job.SourceId, job.State, job.Generation, job.Complete, job.Reason)).ToImmutableArray(),
      images, report.Pairs, report.Errors.Take(MaximumErrors).ToImmutableArray(), [],
      images.Length < report.Analysis.Files.Count(file => ImageSourceScanner.SupportsPath(file.Path)) || report.Errors.Length > MaximumErrors, report.Error);
    var path = Path.Combine(DirectoryPath, $"review-{Guid.NewGuid():N}.json");
    await WriteAsync(path, document, overwrite: false, cancellationToken).ConfigureAwait(false);
    return path;
  }, cancellationToken);

  public Task<ImageReviewDocument> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.Run(async () =>
  {
    path = RequireOwnedPath(path);
    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
    if (stream.Length > MaximumBytes) { throw new InvalidDataException("The image review exceeds the archive size limit."); }
    var document = await JsonSerializer.DeserializeAsync<ImageReviewDocument>(stream, AnalysisJson.Options, cancellationToken).ConfigureAwait(false)
      ?? throw new InvalidDataException("The image review is missing.");
    Validate(document);
    stream.Position = 0;
    var revision = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    return document with { Revision = revision };
  }, cancellationToken);

  public Task<ImageReviewDocument> SaveDecisionsAsync(string path, string expectedRevision, ImmutableArray<ImagePairDecision> decisions,
    CancellationToken cancellationToken = default)
    => Task.Run(async () =>
    {
      path = RequireOwnedPath(path);
      var gate = SaveGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
      try
      {
        var lockPath = path + ".lock";
        if (new FileInfo(lockPath).LinkTarget is not null) { throw new IOException("The review lock must be a regular file."); }
        using var ownedLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existing = await LoadAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(expectedRevision) || existing.Revision != expectedRevision)
        {
          throw new IOException("This review was changed by another save. Reopen it before recording another choice.");
        }
        var updated = existing with { Decisions = decisions };
        var revision = await WriteAsync(path, updated, overwrite: true, cancellationToken).ConfigureAwait(false);
        return updated with { Revision = revision };
      }
      finally { gate.Release(); }
    }, cancellationToken);

  /// <summary>Checks current frozen scope/cloud/identity and holds the local file open without write/delete sharing.</summary>
  public FileStream OpenPreview(ImageReviewDocument document, long fileId)
  {
    var file = document.Images.Single(image => image.Id == fileId);
    var source = document.Sources.Single(item => item.Source.Id == file.SourceId);
    void Check()
    {
      if (file.Status != "PerceptualEvidence" || !ScanJobSnapshot.IsOnline(source, _volumes, _paths)
        || !SourceScanner.Approved(source.Source, Path.GetRelativePath(source.Source.Root, file.Path), new FileInfo(file.Path))
        || ImageSourceScanner.IsCloudOnly(File.GetAttributes(file.Path)) || !file.Observation.Matches(FileObservation.Capture(file.Path)))
      {
        throw new IOException("The image is no longer a matching local file in its frozen source scope. Run analysis again.");
      }
    }
    Check();
    var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    try { Check(); return stream; }
    catch { stream.Dispose(); throw; }
  }

  private async Task<string> WriteAsync(string path, ImageReviewDocument document, bool overwrite, CancellationToken cancellationToken)
  {
    Validate(document);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(document, AnalysisJson.Options);
    if (bytes.Length > MaximumBytes) { throw new InvalidDataException("The image review exceeds the archive size limit."); }
    Directory.CreateDirectory(DirectoryPath);
    path = RequireOwnedPath(path);
    var pending = Path.Combine(DirectoryPath, $"review-{Guid.NewGuid():N}.partial");
    try
    {
      await using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
      {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
      }
      cancellationToken.ThrowIfCancellationRequested();
      File.Move(pending, path, overwrite);
      return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
    finally { if (File.Exists(pending)) { File.Delete(pending); } }
  }

  private string RequireOwnedPath(string path)
  {
    if (!Path.IsPathFullyQualified(path) || !_paths.Comparer.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), DirectoryPath)
      || !Path.GetFileName(path).StartsWith("review-", StringComparison.Ordinal) || Path.GetExtension(path) != ".json")
    {
      throw new ArgumentException("Choose an image review in this application's image-reviews folder.", nameof(path));
    }
    if (_paths.DirectoryExists(DirectoryPath) && !_paths.Comparer.Equals(_paths.Resolve(DirectoryPath), DirectoryPath)
      || File.Exists(path) && new FileInfo(path).LinkTarget is not null)
    {
      throw new IOException("The image review archive must use its own physical directory and regular files.");
    }
    return Path.GetFullPath(path);
  }

  private static void Validate(ImageReviewDocument document)
  {
    if (document is null || document.FormatVersion != 1 || document.Engine is null || document.Descriptor is null
      || document.Descriptor.Id != document.Engine.EngineId || document.Descriptor.Version != document.Engine.EngineVersion
      || document.Descriptor.UpstreamCommit != document.Engine.UpstreamCommit || document.Engine.Capability != AnalysisCapability.SimilarImages
      || document.Engine.Algorithm != PerceptualFingerprints.Mean8D8 || document.Threshold is < 0 or > SimilarImageAnalysis.MaximumThreshold
      || document.DistanceDefinition != PerceptualFingerprints.DistanceDefinition || document.Sources.IsDefault || document.Jobs.IsDefault
      || document.Images.IsDefault || document.Pairs.IsDefault || document.Errors.IsDefault || document.Decisions.IsDefault
      || document.Images.Length > SimilarImageAnalysis.MaximumImages || document.Pairs.Length > SimilarImageAnalysis.MaximumPairs
      || document.Errors.Length > MaximumErrors || !Path.IsPathFullyQualified(document.IndexPath)
      || document.Outcome is not ("Completed" or "Partial" or "Canceled" or "Unavailable" or "LimitExceeded")
      || document.Outcome != "Completed" && !document.Pairs.IsEmpty)
    {
      throw new InvalidDataException("The image review format or capability is invalid.");
    }
    var sources = new Dictionary<string, ScanSourceSnapshot>(StringComparer.Ordinal);
    foreach (var source in document.Sources)
    {
      if (source?.Source is null || string.IsNullOrWhiteSpace(source.Source.Id) || !Path.IsPathFullyQualified(source.Source.Root)
        || source.Source.RegenerableNames is null || source.Source.Exclusions is null || source.Source.ExcludedRoots is null
        || source.Source.RegenerableNames.Any(rule => rule is null) || source.Source.Exclusions.Any(rule => rule is null)
        || source.Source.ExcludedRoots.Any(root => root is null || !Path.IsPathFullyQualified(root)) || !sources.TryAdd(source.Source.Id, source))
      {
        throw new InvalidDataException("The image review contains an invalid source policy.");
      }
    }
    var jobIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var job in document.Jobs)
    {
      if (job is null || string.IsNullOrWhiteSpace(job.Id) || !jobIds.Add(job.Id) || !sources.ContainsKey(job.SourceId)
        || !Enum.IsDefined(job.State) || job.Generation < 0)
      {
        throw new InvalidDataException("The image review contains invalid job metadata.");
      }
    }
    if (document.Errors.Any(error => error is null || !jobIds.Contains(error.JobId) || !Path.IsPathFullyQualified(error.Path)
      || string.IsNullOrWhiteSpace(error.Message))) { throw new InvalidDataException("The image review contains invalid errors."); }
    var files = new Dictionary<long, AnalysisSessionFile>();
    foreach (var file in document.Images)
    {
      if (file is null || !files.TryAdd(file.Id, file) || !sources.TryGetValue(file.SourceId, out var source) || !Path.IsPathFullyQualified(file.Path)
        || !ImageSourceScanner.SupportsPath(file.Path) || file.Id <= 0 || file.Observation is null || file.Observation.Size < 0
        || file.Observation.ModifiedUtcTicks < 0 || file.Observation.ModifiedUtcTicks > DateTime.MaxValue.Ticks
        || !Inside(file.Path, source.Source.Root)
        || file.Status is not ("PerceptualEvidence" or "CloudOnly" or "Unconfirmed" or "SourceUnavailable" or "Changed" or "Unavailable")
        || document.Outcome == "Completed" && file.Status is not ("PerceptualEvidence" or "CloudOnly")
        || file.Status == "PerceptualEvidence" && (file.Fingerprint is null || !PerceptualFingerprints.IsValid(document.Engine.Algorithm, file.Fingerprint)))
      {
        throw new InvalidDataException("The image review contains invalid file observations.");
      }
    }
    var pairs = new HashSet<(long, long)>();
    foreach (var pair in document.Pairs)
    {
      if (pair is null || pair.FirstId >= pair.SecondId || !pairs.Add((pair.FirstId, pair.SecondId))
        || !files.TryGetValue(pair.FirstId, out var first) || !files.TryGetValue(pair.SecondId, out var second)
        || first.Status != "PerceptualEvidence" || second.Status != "PerceptualEvidence"
        || pair.Distance > document.Threshold || pair.Distance != PerceptualFingerprints.Distance(first.Fingerprint!, second.Fingerprint!))
      {
        throw new InvalidDataException("The image review contains an invalid candidate pair.");
      }
    }
    var reviewed = new HashSet<(long, long)>();
    foreach (var decision in document.Decisions)
    {
      if (decision is null || !Enum.IsDefined(decision.Choice) || decision.Choice == ImageReviewChoice.Pending
        || !pairs.Contains((decision.FirstId, decision.SecondId)) || !reviewed.Add((decision.FirstId, decision.SecondId)))
      {
        throw new InvalidDataException("The image review contains an invalid review choice.");
      }
    }
  }

  private static bool Inside(string path, string root)
  {
    if (!Path.IsPathFullyQualified(root)) { return false; }
    var relative = Path.GetRelativePath(root, path);
    return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
  }
}
