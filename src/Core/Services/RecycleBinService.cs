using System.Text.Json;
using System.Text.Json.Serialization;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Models;
using Serilog;

namespace FileMergeAssistant.Services;

public class RecycleBinService : IRecycleBinService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
  };

  private readonly string _recycleBinDir;
  private readonly string _manifestPath;
  private readonly ILogger _logger;

  public RecycleBinService(ILogger logger, string? recycleBinDir = null)
  {
    _logger = logger;
    _recycleBinDir = recycleBinDir ?? AppDataPaths.RecycleBinDirectory;
    PathText.RequireSupported(_recycleBinDir, nameof(recycleBinDir));
    _manifestPath = Path.Combine(_recycleBinDir, "manifest.json");
  }

  public async Task<string> RecycleAsync(string filePath, CancellationToken cancellationToken = default)
  {
    PathText.RequireReadable(filePath);
    Directory.CreateDirectory(_recycleBinDir);

    var manifest = await LoadManifestAsync(cancellationToken);
    var fileName = Path.GetFileName(filePath);
    var guidPart = Guid.NewGuid().ToString("N")[..8];
    var uniqueName = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{guidPart}_{fileName}";
    var recyclePath = Path.Combine(_recycleBinDir, uniqueName);

    File.Move(filePath, recyclePath);

    var entry = new RecycleBinEntry
    {
      OriginalPath = Path.GetFullPath(filePath),
      RecycleBinPath = recyclePath,
      DeletedUtc = DateTime.UtcNow,
      SizeBytes = new FileInfo(recyclePath).Length
    };

    manifest.Entries.Add(entry);
    await SaveManifestAsync(manifest, cancellationToken);

    _logger.Information("Recycled: {FilePath} -> {RecyclePath}", filePath, recyclePath);
    return recyclePath;
  }

  public async Task RestoreAsync(string originalPath, CancellationToken cancellationToken = default)
  {
    PathText.RequireReadable(originalPath);
    var manifest = await LoadManifestAsync(cancellationToken);
    var entry = manifest.Entries.FirstOrDefault(e =>
      string.Equals(e.OriginalPath, Path.GetFullPath(originalPath), StringComparison.OrdinalIgnoreCase));

    if (entry == null)
      throw new InvalidOperationException($"No recycle bin entry found for: {originalPath}");

    if (!File.Exists(entry.RecycleBinPath))
      throw new FileNotFoundException($"Recycle bin file not found: {entry.RecycleBinPath}");

    var destDir = Path.GetDirectoryName(entry.OriginalPath);
    if (!string.IsNullOrEmpty(destDir))
      Directory.CreateDirectory(destDir);

    File.Move(entry.RecycleBinPath, entry.OriginalPath, overwrite: false);
    manifest.Entries.Remove(entry);
    await SaveManifestAsync(manifest, cancellationToken);

    _logger.Information("Restored: {RecyclePath} -> {OriginalPath}", entry.RecycleBinPath, entry.OriginalPath);
  }

  public async Task RestoreAllAsync(CancellationToken cancellationToken = default)
  {
    var manifest = await LoadManifestAsync(cancellationToken);
    var toRestore = manifest.Entries.ToList();

    foreach (var entry in toRestore)
    {
      if (!File.Exists(entry.RecycleBinPath))
      {
        _logger.Warning("Recycle bin file missing, skipping: {Path}", entry.RecycleBinPath);
        manifest.Entries.Remove(entry);
        continue;
      }

      var destDir = Path.GetDirectoryName(entry.OriginalPath);
      if (!string.IsNullOrEmpty(destDir))
        Directory.CreateDirectory(destDir);

      try
      {
        File.Move(entry.RecycleBinPath, entry.OriginalPath, overwrite: false);
        manifest.Entries.Remove(entry);
        _logger.Information("Restored: {Path}", entry.OriginalPath);
      }
      catch (IOException ex)
      {
        _logger.Warning(ex, "Could not restore file (already exists?): {Path}", entry.OriginalPath);
      }
    }

    await SaveManifestAsync(manifest, cancellationToken);
  }

  public async Task<IReadOnlyList<RecycleBinEntry>> ListEntriesAsync(CancellationToken cancellationToken = default)
  {
    var manifest = await LoadManifestAsync(cancellationToken);
    return manifest.Entries.AsReadOnly();
  }

  public async Task PurgeAsync(CancellationToken cancellationToken = default)
  {
    var manifest = await LoadManifestAsync(cancellationToken);
    foreach (var entry in manifest.Entries)
    {
      if (File.Exists(entry.RecycleBinPath))
        File.Delete(entry.RecycleBinPath);
    }

    manifest.Entries.Clear();
    await SaveManifestAsync(manifest, cancellationToken);
    _logger.Information("Recycle bin purged");
  }

  public async Task PurgeOlderThanAsync(TimeSpan age, CancellationToken cancellationToken = default)
  {
    var manifest = await LoadManifestAsync(cancellationToken);
    var cutoff = DateTime.UtcNow - age;
    var toRemove = manifest.Entries.Where(e => e.DeletedUtc < cutoff).ToList();

    foreach (var entry in toRemove)
    {
      if (File.Exists(entry.RecycleBinPath))
        File.Delete(entry.RecycleBinPath);
      manifest.Entries.Remove(entry);
    }

    await SaveManifestAsync(manifest, cancellationToken);
    _logger.Information("Purged {Count} entries older than {Age}", toRemove.Count, age);
  }

  private async Task<RecycleBinManifest> LoadManifestAsync(CancellationToken cancellationToken)
  {
    if (!File.Exists(_manifestPath))
      return new RecycleBinManifest();

    await using var stream = File.OpenRead(_manifestPath);
    var manifest = await JsonSerializer.DeserializeAsync<RecycleBinManifest>(stream, JsonOptions, cancellationToken)
      ?? new RecycleBinManifest();
    foreach (var entry in manifest.Entries)
    {
      PathText.RequireReadable(entry.OriginalPath);
      PathText.RequireReadable(entry.RecycleBinPath);
    }
    return manifest;
  }

  private async Task SaveManifestAsync(RecycleBinManifest manifest, CancellationToken cancellationToken)
  {
    Directory.CreateDirectory(_recycleBinDir);
    await using var stream = File.Create(_manifestPath);
    await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
  }
}
