using System.Collections.Immutable;

namespace FileMergeAssistant.Analysis;

/// <summary>Checks an engine stream before the caller commits any of its evidence.</summary>
public sealed class AnalysisResultValidator
{
  private readonly AnalysisRequest _request;
  private readonly ImmutableDictionary<long, AnalysisInput> _inputs;
  private readonly HashSet<long> _seen = [];
  private readonly Dictionary<long, string> _fingerprints = [];
  private long _sequence;
  private long _bytesRead;
  private bool _ended;
  private bool _allSucceeded = true;

  public AnalysisResultValidator(AnalysisRequest request)
  {
    ValidateRequest(request);
    _request = request;
    _inputs = request.Files.ToImmutableDictionary(file => file.Id);
  }

  public static void ValidateRequest(AnalysisRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Engine);
    ArgumentException.ThrowIfNullOrWhiteSpace(request.JobId);
    if (request.Files.IsDefault) { throw new ArgumentException("The file set must be explicit.", nameof(request)); }
    var ids = new HashSet<long>();
    foreach (var file in request.Files)
    {
      if (file is null || file.Observation is null || file.Id <= 0 || !ids.Add(file.Id) || string.IsNullOrWhiteSpace(file.SourceId) || !Path.IsPathFullyQualified(file.Path)
        || file.Observation.Size < 0 || file.Observation.ModifiedUtcTicks < 0 || file.Observation.ModifiedUtcTicks > DateTime.MaxValue.Ticks)
      {
        throw new ArgumentException("The analysis request contains invalid or duplicate file observations.", nameof(request));
      }
    }
  }

  public void Accept(AnalysisBatch batch)
  {
    ArgumentNullException.ThrowIfNull(batch);
    if (_ended || batch.JobId != _request.JobId || batch.EngineId != _request.Engine.EngineId || batch.Sequence != _sequence + 1
      || batch.Files.IsDefault || batch.Groups.IsDefault || batch.Progress is null || batch.Progress.Processed < 0 || batch.Progress.Processed > _inputs.Count
      || batch.Progress.Total != _inputs.Count || batch.Progress.BytesRead < _bytesRead || batch.IsComplete && !batch.IsFinal)
    {
      throw new InvalidDataException("Invalid analysis batch identity, sequence or progress.");
    }
    var inBatch = new HashSet<long>();
    foreach (var evidence in batch.Files)
    {
      if (evidence is null || evidence.Observation is null || evidence.Observation.Size < 0 || evidence.Observation.ModifiedUtcTicks < 0
        || evidence.Observation.ModifiedUtcTicks > DateTime.MaxValue.Ticks
        || !_inputs.TryGetValue(evidence.FileId, out var input) || _seen.Contains(evidence.FileId) || !inBatch.Add(evidence.FileId)
        || !Enum.IsDefined(evidence.Status)) { throw new InvalidDataException("The engine returned an unknown or duplicate file."); }
      if (evidence.Status == AnalysisFileStatus.Succeeded)
      {
        if (!input.Observation.Matches(evidence.Observation) || evidence.Error is not null || evidence.Fingerprint is null
          || !ContentFingerprints.IsValid(_request.Engine.Algorithm, evidence.Fingerprint))
        {
          throw new InvalidDataException("The content evidence does not match the requested observation or algorithm.");
        }
      }
      else if (evidence.Fingerprint is not null || string.IsNullOrWhiteSpace(evidence.Error))
      {
        throw new InvalidDataException("Incomplete content evidence must include an error and no fingerprint.");
      }
    }
    foreach (var group in batch.Groups)
    {
      if (group is null || group.Kind != _request.Engine.Capability || group.FileIds.IsDefault || group.FileIds.Length < 2
        || group.FileIds.Distinct().Count() != group.FileIds.Length || group.FileIds.Any(id => !_inputs.ContainsKey(id))
        || group.Distance is { } distance && (!double.IsFinite(distance) || distance < 0))
      {
        throw new InvalidDataException("The engine returned an invalid candidate group.");
      }
      if (group.Kind == AnalysisCapability.ExactDuplicates)
      {
        var evidenceValues = group.FileIds.Select(id => batch.Files.FirstOrDefault(file => file.FileId == id)?.Fingerprint
          ?? _fingerprints.GetValueOrDefault(id)).ToArray();
        if (evidenceValues.Any(value => value is null) || evidenceValues.Distinct(StringComparer.Ordinal).Count() != 1)
        {
          throw new InvalidDataException("An exact duplicate group requires identical complete content evidence.");
        }
      }
    }
    var allSucceeded = _allSucceeded && batch.Files.All(file => file.Status == AnalysisFileStatus.Succeeded);
    if (batch.Progress.Processed != _seen.Count + inBatch.Count
      || batch.IsFinal && (_seen.Count + inBatch.Count != _inputs.Count || batch.IsComplete != allSucceeded))
    {
      throw new InvalidDataException("The engine claimed a final result without accounting for every requested file.");
    }
    _seen.UnionWith(inBatch);
    foreach (var evidence in batch.Files.Where(file => file.Status == AnalysisFileStatus.Succeeded)) { _fingerprints.Add(evidence.FileId, evidence.Fingerprint!); }
    _bytesRead = batch.Progress.BytesRead;
    _allSucceeded = allSucceeded;
    _sequence = batch.Sequence;
    _ended = batch.IsFinal;
  }

  public void RequireFinal()
  {
    if (!_ended) { throw new InvalidDataException("The engine ended without a final coverage result."); }
  }
}
