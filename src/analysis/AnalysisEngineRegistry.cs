using System.Collections.Immutable;
using System.Text.Json;

namespace FileWorkbench.Analysis;

public sealed class AnalysisEngineRegistry
{
  private readonly ImmutableDictionary<string, IFileAnalysisEngine> _engines;

  public AnalysisEngineRegistry(IEnumerable<IFileAnalysisEngine> engines)
  {
    ArgumentNullException.ThrowIfNull(engines);
    _engines = engines.ToImmutableDictionary(engine => engine.Descriptor.Id, StringComparer.Ordinal);
  }

  public ImmutableArray<AnalysisEngineDescriptor> Descriptors => _engines.Values.Select(engine => engine.Descriptor).OrderBy(item => item.Id, StringComparer.Ordinal).ToImmutableArray();

  public static AnalysisEngineRegistry ManagedOnly() => new([new ManagedAnalysisEngine()]);

  public static AnalysisEngineRegistry CreateDefault() => new([new ManagedAnalysisEngine(), new NativeAnalysisEngine()]);

  public string ToJson() => JsonSerializer.Serialize(new { formatVersion = 1, engines = Descriptors }, AnalysisJson.Options);

  public AnalysisEngineSnapshot Snapshot(string engineId, AnalysisCapability capability, string algorithm,
    string parametersJson = "{}", string scopeJson = "{}")
  {
    var engine = Find(engineId);
    RequireSupport(engine.Descriptor, capability, algorithm);
    RequireObject(parametersJson);
    RequireObject(scopeJson);
    var descriptor = engine.Descriptor;
    return new(descriptor.Id, descriptor.Version, descriptor.UpstreamCommit, descriptor.ContractVersion, descriptor.AbiVersion,
      capability, algorithm, parametersJson, scopeJson);
  }

  public IFileAnalysisEngine Resolve(AnalysisEngineSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);
    var engine = Find(snapshot.EngineId);
    var descriptor = engine.Descriptor;
    RequireSupport(descriptor, snapshot.Capability, snapshot.Algorithm);
    if (descriptor.Version != snapshot.EngineVersion || descriptor.UpstreamCommit != snapshot.UpstreamCommit
      || descriptor.ContractVersion != snapshot.ContractVersion || descriptor.AbiVersion != snapshot.AbiVersion)
    {
      throw new AnalysisEngineUnavailableException(AnalysisAvailability.Incompatible, "The job requires a different analysis engine version. Create a new job to change it.");
    }
    RequireObject(snapshot.ParametersJson);
    RequireObject(snapshot.ScopeJson);
    return engine;
  }

  private IFileAnalysisEngine Find(string id) => _engines.TryGetValue(id, out var engine) ? engine
    : throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, $"Unknown analysis engine: {id}");

  internal static void RequireSupport(AnalysisEngineDescriptor descriptor, AnalysisCapability capability, string algorithm)
  {
    if (descriptor.Availability != AnalysisAvailability.Available)
    {
      throw new AnalysisEngineUnavailableException(descriptor.Availability, descriptor.UnavailableReason ?? "The analysis engine is unavailable.");
    }
    if (!descriptor.Supports(capability, algorithm))
    {
      throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, $"{descriptor.Id} does not support {capability} with {algorithm}.");
    }
  }

  private static void RequireObject(string json)
  {
    using var document = JsonDocument.Parse(json);
    if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Analysis settings must be a JSON object.", nameof(json)); }
  }
}
