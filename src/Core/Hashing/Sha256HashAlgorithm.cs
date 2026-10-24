using System.Security.Cryptography;

namespace FileWorkbench.Hashing;

public class Sha256HashAlgorithm : IHashAlgorithm
{
  private const int BufferSize = 8192;

  public string Name => "sha256";

  public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
  {
    using var sha256 = SHA256.Create();
    await using var stream = new FileStream(
      filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
      BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    var buffer = new byte[BufferSize];
    int bytesRead;
    while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
    {
      sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
    }

    sha256.TransformFinalBlock([], 0, 0);
    return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
  }
}
