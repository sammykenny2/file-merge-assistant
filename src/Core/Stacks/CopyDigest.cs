using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace FileMergeAssistant.Stacks;

/// <summary>
/// A short fingerprint of a copy's file list: every relative path with its size and modification time. A decision
/// remembers the digest of each copy; when the index or the disk no longer gives the same digest, the copy changed
/// after the user looked at it, and the decision must be confirmed again (ADR-006, PLAN-001 Task 5.2).
/// </summary>
public static class CopyDigest
{
  public static string Of(IEnumerable<(string Relative, long Size, long ModifiedTicks)> files)
  {
    ArgumentNullException.ThrowIfNull(files);
    var hasher = new XxHash64();
    var count = 0;
    foreach (var (relative, size, modified) in files.OrderBy(f => f.Relative, StringComparer.Ordinal))
    {
      hasher.Append(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{relative}|{size}|{modified}\n")));
      count++;
    }
    return string.Create(CultureInfo.InvariantCulture, $"{count}:{Convert.ToHexStringLower(hasher.GetCurrentHash())}");
  }
}
