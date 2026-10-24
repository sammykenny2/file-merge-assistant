using System.Collections.Immutable;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Analysis;

public enum AnalysisCapability { ExactDuplicates, SimilarImages, SimilarVideos }
public enum AnalysisMaturity { Experimental, Preview, Validated }
public enum AnalysisAvailability { Available, Unsupported, MissingDependency, Incompatible }
public enum AnalysisFileStatus { Succeeded, Changed, Skipped, Failed }

public sealed record AnalysisCapabilityInfo(AnalysisCapability Capability, ImmutableArray<string> Algorithms, AnalysisMaturity Maturity);

public sealed record AnalysisEngineDescriptor(
  string Id, string Name, string Version, string? UpstreamCommit, int ContractVersion, int AbiVersion,
  string Platform, AnalysisAvailability Availability, string? UnavailableReason,
  ImmutableArray<AnalysisCapabilityInfo> Capabilities)
{
  public bool Supports(AnalysisCapability capability, string algorithm) =>
    Capabilities.Any(item => item.Capability == capability && item.Algorithms.Contains(algorithm, StringComparer.Ordinal));
}

public sealed record AnalysisEngineSnapshot(
  string EngineId, string EngineVersion, string? UpstreamCommit, int ContractVersion, int AbiVersion,
  AnalysisCapability Capability, string Algorithm, string ParametersJson, string ScopeJson);

public sealed record FileObservation(long Size, long ModifiedUtcTicks, FileIdentity? Identity = null)
{
  public static FileObservation Capture(string path)
  {
    PathText.RequireSupported(path, nameof(path));
    var info = new FileInfo(path);
    if (!info.Exists) { throw new FileNotFoundException("The analysis input no longer exists.", path); }
    return new(info.Length, info.LastWriteTimeUtc.Ticks, OperatingSystem.IsWindows() ? PathIdentity.Identify(path) : null);
  }

  public bool Matches(FileObservation other) => Size == other.Size && ModifiedUtcTicks == other.ModifiedUtcTicks
    && (Identity is null || other.Identity is { } identity && Identity.Value.SameAs(identity));
}

public sealed record AnalysisInput(long Id, string SourceId, string Path, FileObservation Observation);
public sealed record AnalysisRequest(string JobId, AnalysisEngineSnapshot Engine, ImmutableArray<AnalysisInput> Files);
public sealed record AnalysisFileEvidence(long FileId, FileObservation Observation, AnalysisFileStatus Status, string? Fingerprint, string? Error);
public sealed record AnalysisCandidateGroup(AnalysisCapability Kind, ImmutableArray<long> FileIds, double? Distance, string? DistanceDefinition);
public sealed record AnalysisProgress(string Stage, long Processed, long Total, long BytesRead, string? CurrentPath);
public sealed record AnalysisBatch(
  string JobId, string EngineId, long Sequence, ImmutableArray<AnalysisFileEvidence> Files,
  ImmutableArray<AnalysisCandidateGroup> Groups, AnalysisProgress Progress, bool IsFinal, bool IsComplete);

public interface IFileAnalysisEngine
{
  AnalysisEngineDescriptor Descriptor { get; }
  IAsyncEnumerable<AnalysisBatch> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken = default);
}

public sealed class AnalysisEngineUnavailableException(AnalysisAvailability availability, string message) : InvalidOperationException(message)
{
  public AnalysisAvailability Availability { get; } = availability;
}
