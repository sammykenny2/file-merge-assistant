using System.Text.Json;
using System.Text.Json.Serialization;
using FileWorkbench.Models;

namespace FileWorkbench.Services;

public class JsonScanResultStore : IScanResultStore
{
  private static readonly JsonSerializerOptions Options = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
  };

  public async Task SaveAsync(ScanResult result, string filePath, CancellationToken cancellationToken = default)
  {
    PathText.RequireReadable(filePath);
    RequirePaths(result);
    var directory = Path.GetDirectoryName(filePath);
    if (!string.IsNullOrEmpty(directory))
      Directory.CreateDirectory(directory);

    await using var stream = File.Create(filePath);
    await JsonSerializer.SerializeAsync(stream, result, Options, cancellationToken);
  }

  public async Task<ScanResult> LoadAsync(string filePath, CancellationToken cancellationToken = default)
  {
    PathText.RequireReadable(filePath);
    await using var stream = File.OpenRead(filePath);
    var result = await JsonSerializer.DeserializeAsync<ScanResult>(stream, Options, cancellationToken)
      ?? throw new InvalidOperationException($"Failed to deserialize scan result from: {filePath}");
    RequirePaths(result);
    return result;
  }

  private static void RequirePaths(ScanResult result)
  {
    PathText.RequireReadable(result.ScannedPath);
    foreach (var file in result.DuplicateGroups.SelectMany(group => group.Files)) { PathText.RequireReadable(file.FilePath); }
  }
}
