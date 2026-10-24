using System.Buffers;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace FileMergeAssistant.Analysis;

public static class ContentFingerprints
{
  private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdef");
  public const string XxHash128 = "xxh128-v1";
  public const string Sha256 = "sha256-v1";
  public const string Blake3 = "blake3-v1";

  public static string Prefix(string algorithm) => algorithm == XxHash128 ? "xxh128:" : algorithm + ":";

  public static bool IsValid(string algorithm, string fingerprint)
  {
    var digits = algorithm == XxHash128 ? 32 : algorithm is Sha256 or Blake3 ? 64 : 0;
    var prefix = Prefix(algorithm);
    return digits > 0 && fingerprint.StartsWith(prefix, StringComparison.Ordinal) && fingerprint.Length == prefix.Length + digits
      && fingerprint.AsSpan(prefix.Length).IndexOfAnyExcept(HexDigits) < 0;
  }

  public static string Compute(string path, string algorithm, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    if (algorithm is not (XxHash128 or Sha256)) { throw new ArgumentException("Unsupported managed content hash.", nameof(algorithm)); }
    var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
    var fast = algorithm == XxHash128 ? new System.IO.Hashing.XxHash128() : null;
    using var crypto = algorithm == Sha256 ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
    try
    {
      using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
      int read;
      while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
      {
        cancellationToken.ThrowIfCancellationRequested();
        fast?.Append(buffer.AsSpan(0, read));
        crypto?.AppendData(buffer, 0, read);
      }
      cancellationToken.ThrowIfCancellationRequested();
      return Prefix(algorithm) + Convert.ToHexStringLower(fast?.GetCurrentHash() ?? crypto!.GetHashAndReset());
    }
    finally { ArrayPool<byte>.Shared.Return(buffer); }
  }
}
