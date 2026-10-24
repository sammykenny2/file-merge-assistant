using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FileWorkbench.Localization;

namespace FileWorkbench.Updates;

/// <summary>A published release found in the feed: tag <c>vX.Y.Z</c> of the configured repository.</summary>
public sealed record UpdateRelease(string Repository, string Tag, Version Version, Uri PageUrl);

/// <summary>The installer a release offers for this platform and architecture, as its update manifest describes it.</summary>
public sealed record UpdatePackage(Version Version, string Tag, string Summary, string FileName, long Size, string Sha256, Uri DownloadUrl, Uri PageUrl);

/// <summary>
/// An update step that failed; <see cref="Code"/> is stable and the message, in the display language when it was thrown,
/// is shown to the user as is.
/// </summary>
public sealed class UpdateException : Exception
{
  public UpdateException(string code, string message, Exception? inner = null) : base(message, inner) => Code = code;

  public string Code { get; }
}

/// <summary>
/// Rules for finding an update in a repository's public GitHub release feed (<c>releases.atom</c>) and reading the
/// update manifest that each release carries. No GitHub REST API is used, so the API's per-IP quota does not apply.
/// </summary>
public static partial class UpdateFeed
{
  public const string Platform = "windows";
  public const string ManifestFileName = "file-workbench-update-windows.json";
  public const long MaxPackageBytes = 512L * 1024 * 1024;
  public const int MaxFeedBytes = 2 * 1024 * 1024;
  public const int MaxManifestBytes = 64 * 1024;
  public const int MaxSummaryLength = 500;

  [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\z")]
  private static partial Regex VersionPattern();

  [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})/[A-Za-z0-9._-]{1,100}\z")]
  private static partial Regex RepositoryPattern();

  [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,199}\z")]
  private static partial Regex FileNamePattern();

  [GeneratedRegex(@"^[0-9a-f]{64}\z")]
  private static partial Regex DigestPattern();

  /// <summary>A strict <c>x.y.z</c> version without leading zeros, or null.</summary>
  public static Version? ParseVersion(string? text)
  {
    var match = text is null ? null : VersionPattern().Match(text);
    return match is { Success: true }
      ? new Version(
        int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
        int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture))
      : null;
  }

  public static bool IsValidRepository(string? repository) => repository is not null && RepositoryPattern().IsMatch(repository);

  public static Uri FeedUrl(string repository)
  {
    RequireRepository(repository);
    return new Uri($"https://github.com/{repository}/releases.atom");
  }

  public static Uri ReleasesPageUrl(string repository)
  {
    RequireRepository(repository);
    return new Uri($"https://github.com/{repository}/releases");
  }

  /// <summary>The only installer name a release may offer: what scripts/windows/Build-Installer.ps1 produces.</summary>
  public static string InstallerFileName(Version version, string architecture) =>
    $"FileWorkbench-{version.ToString(3)}-{architecture}-setup.exe";

  public static Uri AssetUrl(UpdateRelease release, string fileName)
  {
    if (!FileNamePattern().IsMatch(fileName))
      throw Invalid(AppText.Get("update.invalidFile"));
    return new Uri($"https://github.com/{release.Repository}/releases/download/{release.Tag}/{fileName}");
  }

  /// <summary>
  /// The highest <c>vX.Y.Z</c> release in the feed that is newer than <paramref name="current"/>, or null when the
  /// running version is the newest. Publication order is ignored. The feed lists prereleases too, and every release
  /// is treated as an update candidate.
  /// </summary>
  public static UpdateRelease? Latest(ReadOnlySpan<byte> feed, string repository, Version current)
  {
    RequireRepository(repository);
    var pagePrefix = $"https://github.com/{repository}/releases/tag/";
    UpdateRelease? best = null;
    var found = false;
    XElement[] entries;
    try
    {
      using var stream = new MemoryStream(feed.ToArray());
      using var reader = XmlReader.Create(stream, new XmlReaderSettings
      {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxFeedBytes
      });
      var document = XDocument.Load(reader);
      XNamespace atom = "http://www.w3.org/2005/Atom";
      if (document.Root?.Name != atom + "feed")
        throw Invalid(AppText.Get("update.feedInvalid"));
      entries = document.Root.Elements(atom + "entry").ToArray();
      foreach (var entry in entries)
      {
        foreach (var link in entry.Elements(atom + "link"))
        {
          if ((string?)link.Attribute("rel") is { } rel && rel != "alternate")
            continue;
          var page = (string?)link.Attribute("href");
          if (page is null || !page.StartsWith(pagePrefix, StringComparison.Ordinal))
            continue;
          var tag = page[pagePrefix.Length..];
          if (!tag.StartsWith('v') || ParseVersion(tag[1..]) is not { } version)
            continue;
          found = true;
          if (version > current && (best is null || version > best.Version))
            best = new UpdateRelease(repository, tag, version, new Uri(page));
        }
      }
    }
    catch (XmlException ex)
    {
      throw Invalid(AppText.Get("update.feedUnreadable"), ex);
    }

    // A feed that lists releases, none of them versioned, cannot prove that this version is the newest.
    if (entries.Length > 0 && !found)
      throw new UpdateException("release_missing", AppText.Get("update.releaseMissing"));
    return best;
  }

  /// <summary>Checks the manifest against the release it came from and picks the installer for <paramref name="architecture"/>.</summary>
  public static UpdatePackage ReadManifest(ReadOnlySpan<byte> manifest, UpdateRelease release, string architecture)
  {
    try
    {
      using var document = JsonDocument.Parse(manifest.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object
          || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number
          || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
        throw Invalid(AppText.Get("update.manifestFormat"));

      var summary = Text(root, "summary") ?? string.Empty;
      if (Text(root, "platform") != Platform || Text(root, "tag") != release.Tag || ParseVersion(Text(root, "version")) != release.Version)
        throw Invalid(AppText.Get("update.manifestMismatch"));
      if (summary.Length > 2000)
        throw Invalid(AppText.Get("update.summaryTooLong"));

      if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        throw Invalid(AppText.Get("update.noInstallers"));
      var matches = assets.EnumerateArray()
        .Where(asset => asset.ValueKind == JsonValueKind.Object && Text(asset, "arch") == architecture)
        .ToArray();
      if (matches.Length != 1)
        throw Invalid(AppText.Get("update.noArchitecture", ("architecture", architecture)));

      var chosen = matches[0];
      var name = Text(chosen, "name");
      var sha256 = Text(chosen, "sha256");
      // Only this release's setup program may be downloaded and started, never another file in the release.
      if (name != InstallerFileName(release.Version, architecture))
        throw Invalid(AppText.Get("update.unexpectedInstaller"));
      if (sha256 is null || !DigestPattern().IsMatch(sha256)
          || !chosen.TryGetProperty("size", out var sizeElement) || sizeElement.ValueKind != JsonValueKind.Number
          || !sizeElement.TryGetInt64(out var size)
          || size <= 0 || size > MaxPackageBytes)
        throw Invalid(AppText.Get("update.installerDescription"));

      return new UpdatePackage(
        release.Version, release.Tag,
        summary.Length > MaxSummaryLength ? summary[..MaxSummaryLength] : summary,
        name!, size, sha256, AssetUrl(release, name!), release.PageUrl);
    }
    catch (JsonException ex)
    {
      throw Invalid(AppText.Get("update.manifestUnreadable"), ex);
    }
  }

  private static string? Text(JsonElement value, string key) =>
    value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;

  private static void RequireRepository(string repository)
  {
    if (!IsValidRepository(repository))
      throw new UpdateException("not_configured", AppText.Get("update.notConfigured"));
  }

  private static UpdateException Invalid(string message, Exception? inner = null) => new("metadata_invalid", message, inner);
}
