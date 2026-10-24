using System.Globalization;
using System.Text;
using System.Text.Json;
using FileWorkbench.Localization;

namespace FileWorkbench.Infrastructure;

/// <summary>
/// ADR-014: startup behaviour, the last update check and the display language, kept in <c>host-preferences.json</c> in
/// the data folder. They belong to this computer, not to the index, and an unreadable file never blocks startup.
/// </summary>
/// <param name="StartAtLogin">Start the app when the user signs in to Windows (on by default).</param>
/// <param name="CloseToTray">Closing the main window keeps the app in the tray (on by default).</param>
/// <param name="OpenMainAtLogin">Show the main window after a sign-in start (off by default).</param>
/// <param name="LastUpdateCheck">When updates were last checked, ISO 8601 in UTC.</param>
/// <param name="Language">"auto", "en" or "zh-TW".</param>
public sealed record HostPreferences(
  bool StartAtLogin = true,
  bool CloseToTray = true,
  bool OpenMainAtLogin = false,
  string? LastUpdateCheck = null,
  string Language = "auto")
{
  public const string FileName = "host-preferences.json";
  private const int MaxBytes = 4096;

  /// <summary>Unknown versions, damaged files and invalid values fall back to the defaults instead of blocking startup.</summary>
  public static HostPreferences Parse(ReadOnlySpan<byte> bytes)
  {
    var defaults = new HostPreferences();
    try
    {
      using var document = JsonDocument.Parse(bytes.ToArray());
      var value = document.RootElement;
      if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("version", out var version)
          || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1)
      {
        return defaults;
      }
      static bool Flag(JsonElement value, string key, bool fallback) =>
        value.TryGetProperty(key, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.GetBoolean() : fallback;
      string? checkedAt = null;
      if (value.TryGetProperty("lastUpdateCheck", out var field) && field.ValueKind == JsonValueKind.String && field.TryGetDateTimeOffset(out var time))
      {
        checkedAt = time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
      }
      // An unknown or damaged language falls back to "auto" on its own; the other settings still apply.
      var language = value.TryGetProperty("language", out var chosen) && chosen.ValueKind == JsonValueKind.String
        && AppText.ValidPreference(chosen.GetString()) ? chosen.GetString()! : defaults.Language;
      return new HostPreferences(
        Flag(value, "startAtLogin", defaults.StartAtLogin),
        Flag(value, "closeToTray", defaults.CloseToTray),
        Flag(value, "openMainAtLogin", defaults.OpenMainAtLogin),
        checkedAt,
        language);
    }
    catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException)
    {
      return defaults;
    }
  }

  public byte[] Serialize() => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
  {
    version = 1,
    startAtLogin = StartAtLogin,
    closeToTray = CloseToTray,
    openMainAtLogin = OpenMainAtLogin,
    lastUpdateCheck = LastUpdateCheck,
    language = Language,
  }) + "\n");

  public static HostPreferences Read(string directory)
  {
    try
    {
      var file = new FileInfo(Path.Combine(directory, FileName));
      return file.Exists && file.Length <= MaxBytes ? Parse(File.ReadAllBytes(file.FullName)) : new HostPreferences();
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      return new HostPreferences();
    }
  }

  /// <exception cref="IOException">The file could not be written.</exception>
  /// <exception cref="UnauthorizedAccessException">The file could not be written.</exception>
  public void Write(string directory) => HostFiles.Replace(directory, FileName, Serialize());

  /// <summary>Applies one named switch from the settings page; other names are refused.</summary>
  public HostPreferences With(string name, bool value) => name switch
  {
    "startAtLogin" => this with { StartAtLogin = value },
    "closeToTray" => this with { CloseToTray = value },
    "openMainAtLogin" => this with { OpenMainAtLogin = value },
    _ => throw new ArgumentException($"There is no startup setting named {name}.", nameof(name)),
  };

  /// <summary>The display language: "auto", "en" or "zh-TW"; anything else is refused.</summary>
  public HostPreferences WithLanguage(string value) => AppText.ValidPreference(value)
    ? this with { Language = value }
    : throw new ArgumentException($"The display language {value} is not supported.", nameof(value));
}

/// <summary>Atomic small-file replacement for the settings beside the index.</summary>
public static class HostFiles
{
  /// <summary>Writes through a temporary file, flushes it to disk and renames it over the target.</summary>
  public static void Replace(string directory, string name, byte[] bytes)
  {
    ArgumentNullException.ThrowIfNull(bytes);
    Directory.CreateDirectory(directory);
    var target = Path.Combine(directory, name);
    var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try
    {
      using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
      {
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
      }
      File.Move(temporary, target, overwrite: true);
    }
    finally
    {
      if (File.Exists(temporary))
      {
        File.Delete(temporary);
      }
    }
  }
}
