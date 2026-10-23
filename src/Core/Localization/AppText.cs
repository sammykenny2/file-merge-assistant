using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FileMergeAssistant.Localization;

/// <summary>
/// The text the app shows, in English and Traditional Chinese (ADR-014, docs/i18n.md). The display language is
/// "auto", "en" or "zh-TW", stored in host-preferences.json; "auto" takes the first Windows display language that
/// matches, else English. The rules repeat desktop-work-fleet's HostText.
/// <para>
/// Each area keeps its strings in its own file (<c>AppText.&lt;Area&gt;.cs</c>) as two tables, <c>&lt;Area&gt;English</c>
/// and <c>&lt;Area&gt;Chinese</c>, with the same keys and {name} placeholders. Every key starts with its area's name
/// in lower camel case and a dot. Tests check that both tables of every area match and that no key is defined twice.
/// </para>
/// </summary>
public static partial class AppText
{
  public const string DefaultLocale = "en";
  public static IReadOnlyList<string> Locales { get; } = ["en", "zh-TW"];

  /// <summary>"auto" follows the Windows display languages; the others pin a locale.</summary>
  public static IReadOnlyList<string> Preferences { get; } = ["auto", .. Locales];

  private sealed record State(string Preference, IReadOnlyList<string> System, string Locale);

  private static readonly object Gate = new();
  private static readonly Lazy<Catalog> Tables = new(Merge, LazyThreadSafetyMode.ExecutionAndPublication);
  private static volatile State _state = Start();

  /// <summary>Raised on the thread that called <see cref="Apply"/> when the resolved locale changes.</summary>
  public static event EventHandler? Changed;

  public static string Preference => _state.Preference;

  /// <summary>The Windows display languages, most preferred first, as last read by <see cref="Apply"/>.</summary>
  public static IReadOnlyList<string> System => _state.System;

  public static string Locale => _state.Locale;

  /// <summary>The culture that numbers and dates follow in the current locale.</summary>
  public static CultureInfo Culture => CultureInfo.GetCultureInfo(Locale);

  public static bool ValidPreference(string? value) => value is not null && Preferences.Contains(value, StringComparer.Ordinal);

  /// <summary>
  /// One BCP 47 tag or POSIX locale name to a supported locale, or null. English variants map to en; Traditional or
  /// unmarked Chinese (zh, zh-TW, zh-Hant, zh-HK, zh-MO) maps to zh-TW; Simplified Chinese is not supported yet.
  /// </summary>
  public static string? Match(string? tag)
  {
    if (tag is null)
    {
      return null;
    }
    var value = PosixSuffix().Replace(tag.Trim(), string.Empty).Replace('_', '-').ToLowerInvariant();
    if (value == "en" || value.StartsWith("en-", StringComparison.Ordinal))
    {
      return "en";
    }
    if (value is "zh" or "zh-tw" or "zh-hant" or "zh-hk" or "zh-mo"
      || TraditionalPrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)))
    {
      return "zh-TW";
    }
    return null;
  }

  private static readonly string[] TraditionalPrefixes = ["zh-tw-", "zh-hant-", "zh-hk-", "zh-mo-"];

  /// <summary>A pinned locale wins; "auto" (or anything unknown) takes the first matching system language, else English.</summary>
  public static string Resolve(string? preference, IEnumerable<string?>? languages)
  {
    if (preference is not null && Locales.Contains(preference, StringComparer.Ordinal))
    {
      return preference;
    }
    foreach (var tag in languages ?? [])
    {
      if (Match(tag) is { } locale)
      {
        return locale;
      }
    }
    return DefaultLocale;
  }

  /// <summary>Stores the preference for this process and re-resolves the locale against freshly read system languages.</summary>
  /// <param name="system">The system languages to resolve against; null reads them from Windows.</param>
  public static void Apply(string? preference, IReadOnlyList<string>? system = null)
  {
    bool changed;
    lock (Gate)
    {
      var value = ValidPreference(preference) ? preference! : "auto";
      var languages = system ?? SystemLanguages();
      var next = new State(value, languages, Resolve(value, languages));
      changed = next.Locale != _state.Locale;
      _state = next;
    }
    if (changed)
    {
      Changed?.Invoke(null, EventArgs.Empty);
    }
  }

  /// <summary>The text for the key in the current locale, with {name} placeholders filled; English when a key is missing.</summary>
  public static string Get(string key, params (string Name, object? Value)[] values) => Format(Locale, key, values);

  public static string Format(string locale, string key, params (string Name, object? Value)[] values)
  {
    ArgumentNullException.ThrowIfNull(key);
    ArgumentNullException.ThrowIfNull(values);
    var tables = Tables.Value;
    var text = (locale == "zh-TW" ? tables.Chinese : tables.English).TryGetValue(key, out var found) ? found
      : tables.English.TryGetValue(key, out var fallback) ? fallback : key;
    if (values.Length == 0)
    {
      return text;
    }
    return Placeholder().Replace(text, match =>
    {
      foreach (var (name, value) in values)
      {
        if (name == match.Groups[1].Value && value is not null)
        {
          return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
      }
      return match.Value;
    });
  }

  /// <summary>Whether the key exists in the English table.</summary>
  public static bool Has(string key) => Tables.Value.English.ContainsKey(key);

  /// <summary>Every key and its text in one locale.</summary>
  public static IReadOnlyDictionary<string, string> Table(string locale) =>
    locale == "zh-TW" ? Tables.Value.Chinese : Tables.Value.English;

  /// <summary>The area tables by area name, for the catalog tests: (English, Chinese).</summary>
  public static IReadOnlyDictionary<string, (IReadOnlyDictionary<string, string> English, IReadOnlyDictionary<string, string>? Chinese)> Areas()
  {
    var fields = typeof(AppText).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
      .Where(f => f.FieldType == typeof(Dictionary<string, string>))
      .ToDictionary(f => f.Name, f => (Dictionary<string, string>)f.GetValue(null)!, StringComparer.Ordinal);
    var areas = new Dictionary<string, (IReadOnlyDictionary<string, string>, IReadOnlyDictionary<string, string>?)>(StringComparer.Ordinal);
    foreach (var (name, table) in fields)
    {
      if (name.EndsWith("English", StringComparison.Ordinal))
      {
        var area = name[..^"English".Length];
        areas[area] = (table, fields.GetValueOrDefault(area + "Chinese"));
      }
    }
    return areas;
  }

  /// <summary>The user's Windows display-language list; the current UI culture when Windows does not report one.</summary>
  public static IReadOnlyList<string> SystemLanguages()
  {
    try
    {
      uint size = 0;
      if (GetUserPreferredUILanguages(MuiLanguageName, out _, null, ref size) && size is > 0 and <= 4096)
      {
        var buffer = new char[size];
        if (GetUserPreferredUILanguages(MuiLanguageName, out _, buffer, ref size))
        {
          var tags = new string(buffer, 0, (int)Math.Min(size, (uint)buffer.Length))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
          if (tags.Length > 0)
          {
            return [.. tags.Take(32)];
          }
        }
      }
    }
    catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
    {
      // Fall back to the UI culture below.
    }
    var culture = CultureInfo.CurrentUICulture.Name;
    return culture.Length > 0 ? [culture] : [];
  }

  private static State Start()
  {
    var languages = SystemLanguages();
    return new State("auto", languages, Resolve("auto", languages));
  }

  private sealed record Catalog(IReadOnlyDictionary<string, string> English, IReadOnlyDictionary<string, string> Chinese);

  /// <summary>Joins every area's tables; a key defined in two areas throws, which the catalog tests catch.</summary>
  private static Catalog Merge()
  {
    var english = new Dictionary<string, string>(StringComparer.Ordinal);
    var chinese = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var (_, (en, zh)) in Areas())
    {
      foreach (var (key, value) in en)
      {
        english.Add(key, value);
      }
      foreach (var (key, value) in zh ?? new Dictionary<string, string>())
      {
        chinese.Add(key, value);
      }
    }
    return new Catalog(english, chinese);
  }

  private const uint MuiLanguageName = 0x8;

  [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetUserPreferredUILanguages(uint flags, out uint count, [Out] char[]? buffer, ref uint size);

  [GeneratedRegex(@"[.@].*\z")]
  private static partial Regex PosixSuffix();

  [GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
  public static partial Regex Placeholder();
}
