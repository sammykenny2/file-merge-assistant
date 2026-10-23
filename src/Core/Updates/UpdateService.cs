using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Updates;

public interface IUpdateService
{
  /// <summary>The newest installer newer than <paramref name="current"/>, or null when this version is the newest.</summary>
  Task<UpdatePackage?> CheckAsync(Version current, CancellationToken cancellationToken = default);

  /// <summary>Downloads the installer, verifying its size and SHA-256 against the manifest, and returns its path.</summary>
  Task<string> DownloadAsync(UpdatePackage package, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Checks a repository's public GitHub releases for a newer version and downloads its installer. No credentials or
/// cookies are sent; the feed is fetched without redirects, and downloads follow at most a few HTTPS redirects to the
/// GitHub release-asset hosts.
/// </summary>
public sealed class UpdateService : IUpdateService, IDisposable
{
  public const string RepositoryVariable = "FMA_UPDATE_REPOSITORY";
  public const string InstallerPrefix = "FileMergeAssistant-";
  private const int MaxRedirects = 5;
  private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(30);
  // A download may take long on a slow link, but it must keep moving.
  private static readonly TimeSpan DownloadIdleTimeout = TimeSpan.FromSeconds(60);
  private static readonly HashSet<string> RedirectHosts = new(StringComparer.OrdinalIgnoreCase)
  {
    "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"
  };

  private readonly HttpClient _http;
  private readonly string _repository;
  private readonly string _downloadDirectory;
  private readonly string _architecture;

  public UpdateService(string repository, string downloadDirectory, HttpMessageHandler? handler = null, string architecture = "x64")
  {
    _repository = repository;
    _downloadDirectory = downloadDirectory;
    _architecture = architecture;
    _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }, disposeHandler: true)
    {
      Timeout = Timeout.InfiniteTimeSpan
    };
    _http.DefaultRequestHeaders.UserAgent.ParseAdd($"FileMergeAssistant/{CurrentVersion.ToString(3)}");
  }

  /// <summary>
  /// The repository built into the app (UpdateRepository in Directory.Build.props). Debug builds also accept
  /// <c>FMA_UPDATE_REPOSITORY</c>, for testing against another repository.
  /// </summary>
  public static string ConfiguredRepository
  {
    get
    {
#if DEBUG
      if (Environment.GetEnvironmentVariable(RepositoryVariable) is { Length: > 0 } fromEnvironment)
        return fromEnvironment;
#endif
      return typeof(UpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "UpdateRepository")?.Value ?? string.Empty;
    }
  }

  public static Version CurrentVersion => typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

  public string Repository => _repository;

  public static UpdateService CreateDefault() => new(ConfiguredRepository, AppDataPaths.UpdatesDirectory);

  /// <summary>
  /// Opens a downloaded installer so that no other process can change it, and checks its size and SHA-256 again.
  /// Keep the returned stream open until the installer process has started.
  /// </summary>
  public static FileStream OpenVerifiedInstaller(string path, UpdatePackage package)
  {
    FileStream stream;
    try
    {
      stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw new UpdateException("io_error", AppText.Get("update.openFailed"), ex);
    }

    try
    {
      if (stream.Length != package.Size || Convert.ToHexStringLower(SHA256.HashData(stream)) != package.Sha256)
        throw new UpdateException("checksum_mismatch", AppText.Get("update.changed"));
      stream.Position = 0;
      return stream;
    }
    catch (IOException ex)
    {
      stream.Dispose();
      throw new UpdateException("io_error", AppText.Get("update.readFailed"), ex);
    }
    catch
    {
      stream.Dispose();
      throw;
    }
  }

  public async Task<UpdatePackage?> CheckAsync(Version current, CancellationToken cancellationToken = default)
  {
    var feed = await GetBytesAsync(
      UpdateFeed.FeedUrl(_repository), UpdateFeed.MaxFeedBytes, followRedirects: false,
      AppText.Get("update.feedNotFound"), cancellationToken);
    var release = UpdateFeed.Latest(feed, _repository, current);
    if (release is null)
      return null;

    var manifest = await GetBytesAsync(
      UpdateFeed.AssetUrl(release, UpdateFeed.ManifestFileName), UpdateFeed.MaxManifestBytes, followRedirects: true,
      AppText.Get("update.manifestNotFound", ("tag", release.Tag)), cancellationToken);
    return UpdateFeed.ReadManifest(manifest, release, _architecture);
  }

  public async Task<string> DownloadAsync(UpdatePackage package, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
  {
    var target = Path.Combine(_downloadDirectory, package.FileName);
    var partial = target + ".partial";
    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    try
    {
      Directory.CreateDirectory(_downloadDirectory);
      RemoveOldDownloads();
      idle.CancelAfter(MetadataTimeout);
      using var response = await OverNetwork(() => SendAsync(package.DownloadUrl, followRedirects: true, HttpCompletionOption.ResponseHeadersRead,
        $"The installer {package.FileName} was not found in release {package.Tag}.", idle.Token, cancellationToken));
      if (response.Content.Headers.ContentLength is long length && length != package.Size)
        throw SizeMismatch();

      using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
      await using (var source = await OverNetwork(() => response.Content.ReadAsStreamAsync(idle.Token)))
      await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
      {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
          idle.CancelAfter(DownloadIdleTimeout);
          var read = await OverNetwork(() => source.ReadAsync(buffer, idle.Token).AsTask());
          if (read == 0)
            break;
          total += read;
          if (total > package.Size)
            throw SizeMismatch();
          hash.AppendData(buffer, 0, read);
          await file.WriteAsync(buffer.AsMemory(0, read), idle.Token);
          progress?.Report((double)total / package.Size);
        }
        if (total != package.Size)
          throw SizeMismatch();
      }

      if (Convert.ToHexStringLower(hash.GetHashAndReset()) != package.Sha256)
        throw new UpdateException("checksum_mismatch", AppText.Get("update.checksumMismatch"));

      File.Move(partial, target, overwrite: true);
      return target;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      throw new UpdateException("timeout", AppText.Get("update.stalled"));
    }
    // Network failures were already turned into "offline" by OverNetwork, so what remains here is the local disk.
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      throw new UpdateException("io_error", AppText.Get("update.saveFailed", ("detail", ex.Message)), ex);
    }
    finally
    {
      TryDelete(partial);
    }
  }

  public void Dispose() => _http.Dispose();

  /// <summary>
  /// Runs one network step and reports any transport failure as "offline". A dropped connection can surface as
  /// HttpRequestException, HttpIOException or a plain IOException from the socket stream.
  /// </summary>
  private static async Task<T> OverNetwork<T>(Func<Task<T>> step)
  {
    try
    {
      return await step();
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException)
    {
      throw Offline(ex);
    }
  }

  /// <summary>Best effort: removes earlier installers and partial downloads that this service wrote.</summary>
  private void RemoveOldDownloads()
  {
    foreach (var file in Directory.EnumerateFiles(_downloadDirectory))
    {
      var name = Path.GetFileName(file);
      if (name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
          || (name.StartsWith(InstallerPrefix, StringComparison.Ordinal) && name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase)))
        TryDelete(file);
    }
  }

  private static void TryDelete(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      // A file still in use is left for the next download to remove.
    }
  }

  private async Task<byte[]> GetBytesAsync(Uri url, int maxBytes, bool followRedirects, string notFoundMessage, CancellationToken cancellationToken)
  {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(MetadataTimeout);
    try
    {
      using var response = await SendAsync(url, followRedirects, HttpCompletionOption.ResponseHeadersRead, notFoundMessage, timeout.Token, cancellationToken);
      await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
      using var buffer = new MemoryStream();
      var chunk = new byte[16384];
      int read;
      while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
      {
        if (buffer.Length + read > maxBytes)
          throw new UpdateException("metadata_invalid", AppText.Get("update.tooLarge"));
        buffer.Write(chunk, 0, read);
      }
      return buffer.ToArray();
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      throw new UpdateException("timeout", AppText.Get("update.noResponse"));
    }
    // Only network reads happen here, so every transport or stream failure means the connection failed.
    catch (Exception ex) when (ex is HttpRequestException or IOException)
    {
      throw Offline(ex);
    }
  }

  private async Task<HttpResponseMessage> SendAsync(
    Uri url, bool followRedirects, HttpCompletionOption completion, string notFoundMessage,
    CancellationToken token, CancellationToken callerToken)
  {
    var current = url;
    for (var redirects = 0; ; redirects++)
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, current);
      var response = await _http.SendAsync(request, completion, token);
      var status = (int)response.StatusCode;
      if (status is >= 300 and < 400)
      {
        var location = response.Headers.Location;
        response.Dispose();
        if (!followRedirects || location is null)
          throw new UpdateException("provider_rejected", AppText.Get("update.redirected"));
        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
        if (redirects >= MaxRedirects || next.Scheme != Uri.UriSchemeHttps || !RedirectHosts.Contains(next.Host) || !string.IsNullOrEmpty(next.UserInfo))
          throw new UpdateException("provider_rejected", AppText.Get("update.redirectedDownload"));
        current = next;
        continue;
      }

      if (response.IsSuccessStatusCode)
        return response;

      var code = response.StatusCode;
      var retryAfter = response.Headers.RetryAfter is not null;
      response.Dispose();
      callerToken.ThrowIfCancellationRequested();
      throw code switch
      {
        HttpStatusCode.NotFound => new UpdateException("not_found", notFoundMessage),
        HttpStatusCode.TooManyRequests => RateLimited(),
        HttpStatusCode.Forbidden when retryAfter => RateLimited(),
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized =>
          new UpdateException("provider_rejected", AppText.Get("update.refused")),
        _ when status >= 500 => new UpdateException("provider_unavailable", AppText.Get("update.providerUnavailable")),
        _ => new UpdateException("provider_rejected", AppText.Get("update.providerStatus", ("status", status)))
      };
    }
  }

  private static UpdateException RateLimited() =>
    new("rate_limited", AppText.Get("update.rateLimited"));

  private static UpdateException SizeMismatch() =>
    new("size_mismatch", "The downloaded installer has a different size than published, so it was deleted.");

  private static UpdateException Offline(Exception ex) =>
    new("offline", AppText.Get("update.offline"), ex);
}
