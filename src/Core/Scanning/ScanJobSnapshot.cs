using System.Collections.Immutable;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Index;
using FileMergeAssistant.Sources;
using Microsoft.Data.Sqlite;

namespace FileMergeAssistant.Scanning;

public sealed record ScanSourceSnapshot(ScanSource Source, string? VolumeSerial, FileIdentity? RootIdentity, bool Protected);

/// <summary>Immutable queue-time scope. Mutable source settings never resume an existing job.</summary>
public sealed record ScanJobSnapshot(int Version, string SourceId, AnalysisEngineSnapshot Engine, ImmutableArray<ScanSourceSnapshot> Sources)
{
  public string Serialize()
  {
    RequirePathText(Sources);
    return JsonSerializer.Serialize(this, AnalysisJson.Options);
  }

  public static ScanJobSnapshot Deserialize(string json)
  {
    var snapshot = JsonSerializer.Deserialize<ScanJobSnapshot>(json, AnalysisJson.Options)
      ?? throw new InvalidDataException("The scan job snapshot is missing.");
    if (snapshot.Version != 1 || string.IsNullOrWhiteSpace(snapshot.SourceId) || snapshot.Sources.IsDefaultOrEmpty || snapshot.Engine is null
      || snapshot.Sources.Any(item => item is null || item.Source is null || string.IsNullOrWhiteSpace(item.Source.Id)
        || !Path.IsPathFullyQualified(item.Source.Root) || item.Source.RegenerableNames is null || item.Source.Exclusions is null
        || item.Source.ExcludedRoots is null || item.Source.ExcludedRoots.Any(root => !Path.IsPathFullyQualified(root)))
      || snapshot.Sources.Count(item => item.Source.Id == snapshot.SourceId) != 1
      || snapshot.Sources.Select(item => item.Source.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Sources.Length
      || !JsonElement.DeepEquals(JsonSerializer.SerializeToElement(new { sourceId = snapshot.SourceId, sources = snapshot.Sources }, AnalysisJson.Options),
        JsonSerializer.Deserialize<JsonElement>(snapshot.Engine.ScopeJson)))
    {
      throw new InvalidDataException("The scan job scope is invalid or uses an unsupported version.");
    }
    return snapshot;
  }

  public static ScanJobSnapshot Capture(SqliteConnection connection, string sourceId, AnalysisEngineSnapshot engine,
    IReadOnlyList<string> excludedRoots)
  {
    var sources = ImmutableArray.CreateBuilder<ScanSourceSnapshot>();
    using var command = IndexDatabase.Command(connection,
      "SELECT id, root, skip_regenerable, skip_cloud, regenerable_names, exclusions, volume_serial, is_protected FROM sources ORDER BY position");
    using var reader = command.ExecuteReader();
    while (reader.Read())
    {
      var root = reader.GetString(1);
      var source = new ScanSource(reader.GetString(0), root, reader.GetInt64(2) != 0, reader.GetInt64(3) != 0,
        ScanWorker.SplitLines(reader.GetString(4)), ScanWorker.SplitLines(reader.GetString(5)))
      {
        ExcludedRoots = excludedRoots.ToImmutableArray(),
      };
      // Do not probe an offline root. A captured identity additionally detects replacement of the root folder.
      var identity = Directory.Exists(root) && OperatingSystem.IsWindows() ? PathIdentity.Identify(root) : null;
      sources.Add(new(source, reader.IsDBNull(6) ? null : reader.GetString(6), identity, reader.GetInt64(7) != 0));
    }
    return Create(sourceId, engine, sources.ToImmutable());
  }

  internal static ScanJobSnapshot Create(string sourceId, AnalysisEngineSnapshot engine, ImmutableArray<ScanSourceSnapshot> sources)
  {
    RequirePathText(sources);
    return new(1, sourceId, engine with { ScopeJson = JsonSerializer.Serialize(new { sourceId, sources }, AnalysisJson.Options) }, sources);
  }

  private static void RequirePathText(ImmutableArray<ScanSourceSnapshot> sources)
  {
    if (sources.IsDefault) { throw new InvalidDataException("The source set is missing."); }
    foreach (var item in sources)
    {
      if (item?.Source is not { } source) { throw new InvalidDataException("A source is missing."); }
      PathText.RequireReadable(source.Root);
      foreach (var path in source.ExcludedRoots) { PathText.RequireReadable(path); }
      foreach (var rule in source.Exclusions.Concat(source.RegenerableNames)) { PathText.RequireReadable(rule); }
    }
  }

  public static bool IsOnline(ScanSourceSnapshot snapshot, IVolumeInfoProvider volumes)
  {
    var root = snapshot.Source.Root;
    if (!Directory.Exists(root)) { return false; }
    if (snapshot.VolumeSerial is { } expected && volumes.Describe(root)?.Serial != expected) { return false; }
    return snapshot.RootIdentity is not { } identity
      || OperatingSystem.IsWindows() && PathIdentity.Identify(root) is { } current && identity.SameAs(current);
  }
}

public sealed class ScanSourceUnavailableException(string message) : InvalidOperationException(message);
