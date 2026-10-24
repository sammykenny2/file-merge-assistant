using System.Collections.Immutable;
using System.Text.Json;
using System.Data.Common;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Scanning;

public sealed record AnalysisComparisonProgress(int Pass, string EngineId, ImmutableArray<AnalysisSessionJob> Jobs, string Stage = "Analysis");
public sealed record AnalysisComparisonReport(int FormatVersion, string Outcome, bool ObservationsMatch, string ObservationRule,
  AnalysisSessionReport First, AnalysisSessionReport? Second, int MatchingGroups,
  ImmutableArray<ImmutableArray<string>> OnlyInFirst, ImmutableArray<ImmutableArray<string>> OnlyInSecond, string? Error = null)
{
  public bool IsComplete => Outcome == "Completed";
  public bool GroupsAgree => IsComplete && OnlyInFirst.IsEmpty && OnlyInSecond.IsEmpty;
  public string ToJson() => JsonSerializer.Serialize(this, AnalysisJson.Options);
}

/// <summary>Two sequential, independently validated read-only runs; differences never create a combined operation plan.</summary>
public sealed class AnalysisEngineComparison(AnalysisEngineRegistry engines, string? dataRoot = null, IVolumeInfoProvider? volumes = null,
  ISourcePathService? sourcePaths = null)
{
  public Task<AnalysisComparisonReport> RunAsync(IReadOnlyList<string> paths, AnalysisEngineSnapshot first, AnalysisEngineSnapshot second,
    IProgress<AnalysisComparisonProgress>? progress = null, CancellationToken cancellationToken = default)
    => RunPreparedAsync(paths.ToImmutableArray(), default, first, second, progress, cancellationToken);

  internal Task<AnalysisComparisonReport> RunConfiguredAsync(ImmutableArray<ScanSourceSnapshot> sources, AnalysisEngineSnapshot first,
    AnalysisEngineSnapshot second, IProgress<AnalysisComparisonProgress>? progress, CancellationToken cancellationToken)
    => RunPreparedAsync(sources.Select(source => source.Source.Root).ToImmutableArray(), sources, first, second, progress, cancellationToken);

  private async Task<AnalysisComparisonReport> RunPreparedAsync(ImmutableArray<string> paths, ImmutableArray<ScanSourceSnapshot> sources,
    AnalysisEngineSnapshot first, AnalysisEngineSnapshot second, IProgress<AnalysisComparisonProgress>? progress, CancellationToken cancellationToken)
  {
    engines.Resolve(first);
    engines.Resolve(second);
    if (first.Capability != AnalysisCapability.ExactDuplicates || second.Capability != AnalysisCapability.ExactDuplicates)
    {
      throw new ArgumentException("This comparison requires two exact-content configurations.", nameof(first));
    }
    var session = new ReadOnlyAnalysisSession(engines, dataRoot, volumes, sourcePaths);
    Task<AnalysisSessionReport> Run(AnalysisEngineSnapshot engine, int pass) => sources.IsDefault
      ? session.RunAsync(paths, engine, new ForwardProgress(pass, engine.EngineId, progress), cancellationToken)
      : session.RunConfiguredAsync(sources, engine, new ForwardProgress(pass, engine.EngineId, progress), cancellationToken);
    progress?.Report(new(1, first.EngineId, []));
    var a = await Run(first, 1).ConfigureAwait(false);
    if (!a.IsComplete || cancellationToken.IsCancellationRequested)
    {
      return new(1, cancellationToken.IsCancellationRequested ? "Canceled" : "Partial", false, ObservationRule, a, null, 0, [], []);
    }
    progress?.Report(new(2, second.EngineId, []));
    AnalysisSessionReport b;
    try { b = await Run(second, 2).ConfigureAwait(false); }
    catch (OperationCanceledException)
    {
      return new(1, "Canceled", false, ObservationRule, a, null, 0, [], []);
    }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or DbException)
    {
      return new(1, "Partial", false, ObservationRule, a, null, 0, [], [], error.Message);
    }
    if (!b.IsComplete) { return new(1, b.Outcome == "Canceled" ? "Canceled" : "Partial", false, ObservationRule, a, b, 0, [], []); }
    try
    {
      progress?.Report(new(2, second.EngineId, b.Jobs, "Comparison"));
      cancellationToken.ThrowIfCancellationRequested();
      var observationsMatch = ObservationsMatch(a, b, cancellationToken);
      var firstGroups = Groups(a, cancellationToken);
      var secondGroups = Groups(b, cancellationToken);
      var matching = 0;
      var onlyFirst = ImmutableArray.CreateBuilder<ImmutableArray<string>>();
      var onlySecond = ImmutableArray.CreateBuilder<ImmutableArray<string>>();
      foreach (var pair in firstGroups)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (secondGroups.ContainsKey(pair.Key)) { matching++; } else { onlyFirst.Add(pair.Value); }
      }
      foreach (var pair in secondGroups)
      {
        cancellationToken.ThrowIfCancellationRequested();
        if (!firstGroups.ContainsKey(pair.Key)) { onlySecond.Add(pair.Value); }
      }
      cancellationToken.ThrowIfCancellationRequested();
      return new(1, observationsMatch ? "Completed" : "Incomparable", observationsMatch, ObservationRule, a, b,
        matching, onlyFirst.ToImmutable(), onlySecond.ToImmutable());
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      return new(1, "Canceled", false, ObservationRule, a, b, 0, [], []);
    }
  }

  private const string ObservationRule = "File paths, size, UTC mtime and available identity must match across runs. This is not an atomic filesystem snapshot or a correctness oracle.";

  private static bool ObservationsMatch(AnalysisSessionReport a, AnalysisSessionReport b, CancellationToken cancellationToken)
  {
    if (a.Files.Length != b.Files.Length) { return false; }
    var first = new Dictionary<string, AnalysisSessionFile>(StringComparer.Ordinal);
    foreach (var file in a.Files) { cancellationToken.ThrowIfCancellationRequested(); first.Add(file.Path, file); }
    foreach (var file in b.Files)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!first.TryGetValue(file.Path, out var previous) || !previous.Observation.Matches(file.Observation)
        || !file.Observation.Matches(previous.Observation)) { return false; }
    }
    return true;
  }

  private static Dictionary<string, ImmutableArray<string>> Groups(AnalysisSessionReport report, CancellationToken cancellationToken)
  {
    var paths = new Dictionary<long, string>();
    foreach (var file in report.Files) { cancellationToken.ThrowIfCancellationRequested(); paths.Add(file.Id, file.Path); }
    var groups = new Dictionary<string, ImmutableArray<string>>(StringComparer.Ordinal);
    foreach (var group in report.ExactGroups)
    {
      var members = new SortedSet<string>(StringComparer.Ordinal);
      foreach (var id in group.FileIds) { cancellationToken.ThrowIfCancellationRequested(); members.Add(paths[id]); }
      cancellationToken.ThrowIfCancellationRequested();
      var normalized = members.ToImmutableArray();
      groups.Add(JsonSerializer.Serialize(normalized), normalized);
    }
    cancellationToken.ThrowIfCancellationRequested();
    return groups;
  }

  private sealed class ForwardProgress(int pass, string engineId, IProgress<AnalysisComparisonProgress>? target) : IProgress<ImmutableArray<AnalysisSessionJob>>
  {
    public void Report(ImmutableArray<AnalysisSessionJob> value) => target?.Report(new(pass, engineId, value));
  }
}
