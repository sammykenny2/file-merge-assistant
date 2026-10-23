using System.Globalization;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>
/// Number, size and date formats used by the workbench screens. Dates and the joins follow the display language
/// (the <c>format.*</c> catalog entries); numbers, sizes and the time of day are the same in every language.
/// </summary>
public static class Fmt
{
  private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

  /// <summary>1,284</summary>
  public static string Count(long value) => value.ToString("N0", Invariant);

  /// <summary>2.2 GB, 38 MB, 48 KB.</summary>
  public static string Size(long bytes)
  {
    if (bytes <= 0)
    {
      return "0 B";
    }
    var mb = bytes / 1024.0 / 1024.0;
    if (mb >= 1024)
    {
      return (mb / 1024).ToString("0.0", Invariant) + " GB";
    }
    if (mb >= 1)
    {
      return Math.Round(mb).ToString("0", Invariant) + " MB";
    }
    return Math.Max(1, Math.Round(bytes / 1024.0)).ToString("0", Invariant) + " KB";
  }

  /// <summary>English Oct 22 this year and Mar 11, 2024 otherwise; Chinese 10/22 and 2024/03/11.</summary>
  public static string Day(DateTimeOffset when, DateTimeOffset now) =>
    when.ToString(AppText.Get(when.Year == now.Year ? "format.date.thisYear" : "format.date.otherYear"), AppText.Culture);

  /// <summary>Today 09:12, Yesterday 21:40, Oct 21, Mar 11, 2024 (今天 09:12, 昨天 21:40, 10/21, 2024/03/11).</summary>
  public static string When(DateTimeOffset when, DateTimeOffset now)
  {
    var days = (now.Date - when.Date).Days;
    return days switch
    {
      0 => AppText.Get("format.today", ("time", Time(when))),
      1 => AppText.Get("format.yesterday", ("time", Time(when))),
      _ => Day(when, now),
    };
  }

  /// <summary>Today 09:12, Yesterday 21:40, Sep 28, 22:14 (今天 09:12, 昨天 21:40, 9/28 22:14).</summary>
  public static string WhenWithTime(DateTimeOffset when, DateTimeOffset now)
  {
    var days = (now.Date - when.Date).Days;
    return days is 0 or 1 ? When(when, now) : AppText.Get("format.dateTime", ("date", Day(when, now)), ("time", Time(when)));
  }

  public static string Time(DateTimeOffset when) => when.ToString("HH:mm", Invariant);

  /// <summary>Names in a row: "a, b, c" in English, 「a、b、c」 in Chinese.</summary>
  public static string List(IEnumerable<string> items) => string.Join(AppText.Get("format.listSeparator"), items);

  /// <summary>Independent clauses of one line, joined with a semicolon; empty ones are left out.</summary>
  public static string Clauses(params string?[] clauses) => Join("format.clauses", clauses);

  /// <summary>Whole sentences of one paragraph: a space between them in English, none in Chinese; empty ones are left out.</summary>
  public static string Sentences(params string?[] sentences) => Join("format.sentences", sentences);

  /// <summary>Splits a path into the dimmed parent and the emphasised last part.</summary>
  public static (string Prefix, string Tail) SplitPath(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    var trimmed = path.TrimEnd('\\');
    var slash = trimmed.LastIndexOf('\\');
    if (slash < 0 || trimmed.Length <= 3)
    {
      return (string.Empty, path);
    }
    return (path[..(slash + 1)], path[(slash + 1)..]);
  }

  private static string Join(string key, string?[] parts)
  {
    ArgumentNullException.ThrowIfNull(parts);
    var text = string.Empty;
    foreach (var part in parts)
    {
      if (string.IsNullOrEmpty(part))
      {
        continue;
      }
      text = text.Length == 0 ? part : AppText.Get(key, ("first", text), ("second", part));
    }
    return text;
  }
}
