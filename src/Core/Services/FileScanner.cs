using System.Runtime.CompilerServices;
using FileMergeAssistant.Hashing;
using FileMergeAssistant.Models;
using Serilog;

namespace FileMergeAssistant.Services;

public class FileScanner : IFileScanner
{
  private readonly IMetadataExtractor _metadataExtractor;
  private readonly ILogger _logger;

  public FileScanner(
    IMetadataExtractor metadataExtractor,
    ILogger logger)
  {
    _metadataExtractor = metadataExtractor;
    _logger = logger;
  }

  public int CountFiles(string path, bool recursive, long minSizeBytes = 0)
  {
    PathText.RequireSupported(path, nameof(path));
    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    return Directory.EnumerateFiles(path, "*", searchOption)
      .Count(f =>
      {
        try
        {
          if (!PathText.IsSupported(f)) { return false; }
          return new FileInfo(f).Length >= minSizeBytes;
        }
        catch
        {
          return false;
        }
      });
  }

  public async IAsyncEnumerable<ScannedFile> ScanAsync(
    ScanOptions options,
    IProgress<ScanProgress>? progress = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
  {
    var hashAlgorithm = HashAlgorithmFactory.Create(options.Algorithm);
    var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
    var allFiles = EnumerateAllPaths(options, searchOption);
    var totalFiles = CountAllFiles(options);
    var scanned = 0;

    foreach (var filePath in allFiles)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!PathText.IsSupported(filePath))
      {
        _logger.Warning("Skipping unsupported path encoding: {FilePath}", PathText.EscapeUnsupported(filePath));
        continue;
      }

      FileInfo fileInfo;
      try
      {
        fileInfo = new FileInfo(filePath);
        if (fileInfo.Length < options.MinSizeBytes)
          continue;
        if (options.MaxSizeBytes > 0 && fileInfo.Length > options.MaxSizeBytes)
          continue;
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Could not access file: {FilePath}", filePath);
        continue;
      }

      if (IsExcluded(filePath, options))
        continue;

      string? hash = null;
      try
      {
        hash = await hashAlgorithm.ComputeHashAsync(filePath, cancellationToken);
      }
      catch (NotSupportedException)
      {
        _logger.Debug("Skipping unsupported file for {Algorithm}: {FilePath}", options.Algorithm, filePath);
        continue;
      }
      catch (Exception ex)
      {
        _logger.Warning(ex, "Could not hash file: {FilePath}", filePath);
        continue;
      }

      var metadata = await _metadataExtractor.ExtractAsync(filePath, cancellationToken);

      scanned++;
      progress?.Report(new ScanProgress(scanned, totalFiles, filePath));

      yield return new ScannedFile
      {
        FilePath = filePath,
        SizeBytes = fileInfo.Length,
        CreatedUtc = fileInfo.CreationTimeUtc,
        ModifiedUtc = fileInfo.LastWriteTimeUtc,
        Hash = hash,
        HashAlgorithm = hashAlgorithm.Name,
        Metadata = metadata
      };
    }
  }

  private static IEnumerable<string> EnumerateAllPaths(ScanOptions options, SearchOption searchOption)
  {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var path in options.AllPaths)
    {
      PathText.RequireSupported(path, nameof(options));
      if (!Directory.Exists(path))
        continue;
      foreach (var file in Directory.EnumerateFiles(path, "*", searchOption))
      {
        var fullPath = Path.GetFullPath(file);
        if (seen.Add(fullPath))
          yield return file;
      }
    }
  }

  private int CountAllFiles(ScanOptions options)
  {
    var total = 0;
    foreach (var path in options.AllPaths)
    {
      if (Directory.Exists(path))
        total += CountFiles(path, options.Recursive, options.MinSizeBytes);
    }
    return total;
  }

  private static bool IsExcluded(string filePath, ScanOptions options)
  {
    if (options.ExcludeExtensions.Count > 0)
    {
      var ext = Path.GetExtension(filePath);
      if (options.ExcludeExtensions.Any(e =>
        string.Equals(e.StartsWith('.') ? e : "." + e, ext, StringComparison.OrdinalIgnoreCase)))
        return true;
    }

    if (options.ExcludePatterns.Count > 0)
    {
      var normalizedPath = filePath.Replace('\\', '/');
      foreach (var pattern in options.ExcludePatterns)
      {
        var normalizedPattern = pattern.Replace('\\', '/');
        if (normalizedPath.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase))
          return true;
      }
    }

    return false;
  }
}
