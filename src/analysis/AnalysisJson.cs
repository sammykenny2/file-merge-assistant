using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileWorkbench.Analysis;

public static class AnalysisJson
{
  public static JsonSerializerOptions Options { get; } = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    MaxDepth = 64,
    Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
  };
}
