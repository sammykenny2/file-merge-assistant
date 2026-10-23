using System.Globalization;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>Number, size and date formats used by the workbench screens.</summary>
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

  /// <summary>10/22 this year, 2024/03/11 otherwise.</summary>
  public static string Day(DateTimeOffset when, DateTimeOffset now) =>
    when.Year == now.Year ? when.ToString("M/d", Invariant) : when.ToString("yyyy/MM/dd", Invariant);

  /// <summary>今天 09:12, 昨天 21:40, 10/21, 2024/03/11.</summary>
  public static string When(DateTimeOffset when, DateTimeOffset now)
  {
    var days = (now.Date - when.Date).Days;
    return days switch
    {
      0 => "今天 " + Time(when),
      1 => "昨天 " + Time(when),
      _ => Day(when, now),
    };
  }

  /// <summary>今天 09:12, 昨天 21:40, 9/28 22:14.</summary>
  public static string WhenWithTime(DateTimeOffset when, DateTimeOffset now)
  {
    var days = (now.Date - when.Date).Days;
    return days is 0 or 1 ? When(when, now) : Day(when, now) + " " + Time(when);
  }

  public static string Time(DateTimeOffset when) => when.ToString("HH:mm", Invariant);

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
}
