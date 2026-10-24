using System.Globalization;
using System.Text;

namespace FileMergeAssistant;

/// <summary>Paths crossing UTF-8 JSON/SQLite boundaries must preserve every UTF-16 code unit.</summary>
public static class PathText
{
  public static bool IsSupported(string? value)
  {
    if (value is null) { return false; }
    for (var i = 0; i < value.Length; i++)
    {
      var current = value[i];
      if (current == '\0') { return false; }
      if (!char.IsSurrogate(current)) { continue; }
      if (!char.IsHighSurrogate(current) || i + 1 == value.Length || !char.IsLowSurrogate(value[++i])) { return false; }
    }
    return true;
  }

  public static void RequireSupported(string value, string? parameterName = null)
  {
    ArgumentNullException.ThrowIfNull(value, parameterName);
    if (!IsSupported(value)) { throw new ArgumentException("Paths must contain well-formed Unicode and no NUL characters.", parameterName); }
  }

  public static void RequireReadable(string value)
  {
    if (!IsSupported(value)) { throw new InvalidDataException("Unsupported path encoding: " + EscapeUnsupported(value)); }
  }

  /// <summary>Retains unsupported code units in diagnostics without substituting another valid filename.</summary>
  public static string EscapeUnsupported(string value)
  {
    ArgumentNullException.ThrowIfNull(value);
    var result = new StringBuilder(value.Length);
    for (var i = 0; i < value.Length; i++)
    {
      var current = value[i];
      if (char.IsHighSurrogate(current) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
      {
        result.Append(current).Append(value[++i]);
      }
      else if (char.IsSurrogate(current) || current == '\0')
      {
        result.Append("\\u").Append(((int)current).ToString("X4", CultureInfo.InvariantCulture));
      }
      else { result.Append(current); }
    }
    return result.ToString();
  }
}
