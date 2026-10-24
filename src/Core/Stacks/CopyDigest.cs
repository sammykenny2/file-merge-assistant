using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileMergeAssistant.Stacks;

/// <summary>
/// A short fingerprint of a copy's file list: every relative path with its size and modification time. A decision
/// remembers the digest of each copy; when the index or the disk no longer gives the same digest, the copy changed
/// after the user looked at it, and the decision must be confirmed again (ADR-006, PLAN-001 Task 5.2).
/// </summary>
public static class CopyDigest
{
  /// <summary>Versioned indexed evidence, separate from the metadata digest used to recheck files on disk.</summary>
  public static string EvidenceOf(IEnumerable<(string Relative, string? Hash)> files)
  {
    ArgumentNullException.ThrowIfNull(files);
    var bytes = JsonSerializer.SerializeToUtf8Bytes(files.OrderBy(file => file.Relative, StringComparer.Ordinal)
      .Select(file => { PathText.RequireReadable(file.Relative); return new { file.Relative, file.Hash }; }));
    return "evidence-v1:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
  }

  public static string Of(IEnumerable<(string Relative, long Size, long ModifiedTicks)> files)
  {
    ArgumentNullException.ThrowIfNull(files);
    var hasher = new XxHash64();
    var count = 0;
    foreach (var (relative, size, modified) in files.OrderBy(f => f.Relative, StringComparer.Ordinal))
    {
      PathText.RequireReadable(relative);
      hasher.Append(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{relative}|{size}|{modified}\n")));
      count++;
    }
    return string.Create(CultureInfo.InvariantCulture, $"{count}:{Convert.ToHexStringLower(hasher.GetCurrentHash())}");
  }
}
