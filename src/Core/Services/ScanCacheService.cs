using System.Text.Json;
using System.Text.Json.Serialization;
using FileWorkbench.Infrastructure;
using FileWorkbench.Models;
using Serilog;

namespace FileWorkbench.Services;

public class ScanCacheService : IScanCacheService
{
  private static readonly JsonSerializerOptions JsonOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
  };

  private readonly string _cacheDirectory;
  private readonly ILogger _logger;

  public ScanCacheService(ILogger logger, string? cacheDirectory = null)
  {
    _logger = logger;
    _cacheDirectory = cacheDirectory ?? AppDataPaths.CacheDirectory;
    PathText.RequireSupported(_cacheDirectory, nameof(cacheDirectory));
  }

  public async Task<ScanCache?> LoadCacheAsync(string scannedPath, string algorithm, CancellationToken cancellationToken = default)
  {
    var cachePath = GetCachePath(scannedPath, algorithm);
    if (!File.Exists(cachePath))
      return null;

    try
    {
      await using var stream = File.OpenRead(cachePath);
      var cache = await JsonSerializer.DeserializeAsync<ScanCache>(stream, JsonOptions, cancellationToken);
      if (cache is not null) { RequirePaths(cache); }
      _logger.Information("Loaded scan cache with {Count} entries for {Path}", cache?.Entries.Count ?? 0, scannedPath);
      return cache;
    }
    catch (Exception ex)
    {
      _logger.Warning(ex, "Failed to load scan cache, will perform full scan");
      return null;
    }
  }

  public async Task SaveCacheAsync(ScanCache cache, CancellationToken cancellationToken = default)
  {
    RequirePaths(cache);
    var cachePath = GetCachePath(cache.ScannedPath, cache.HashAlgorithm);
    Directory.CreateDirectory(_cacheDirectory);

    await using var stream = File.Create(cachePath);
    await JsonSerializer.SerializeAsync(stream, cache, JsonOptions, cancellationToken);
    _logger.Information("Saved scan cache with {Count} entries for {Path}", cache.Entries.Count, cache.ScannedPath);
  }

  public bool IsEntryValid(CachedFileEntry entry, string filePath)
  {
    try
    {
      var info = new FileInfo(filePath);
      return info.Exists
        && info.Length == entry.SizeBytes
        && info.LastWriteTimeUtc == entry.ModifiedUtc;
    }
    catch
    {
      return false;
    }
  }

  private string GetCachePath(string scannedPath, string algorithm)
  {
    PathText.RequireReadable(scannedPath);
    PathText.RequireReadable(algorithm);
    var safeName = Convert.ToHexString(
      System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(scannedPath).ToLowerInvariant())))[..16];
    return Path.Combine(_cacheDirectory, $"cache_{safeName}_{algorithm}.json");
  }

  private static void RequirePaths(ScanCache cache)
  {
    PathText.RequireReadable(cache.ScannedPath);
    foreach (var path in cache.Entries.Keys) { PathText.RequireReadable(path); }
  }
}
