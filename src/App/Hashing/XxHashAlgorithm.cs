using System.IO.Hashing;

namespace FileMergeAssistant.Hashing;

public class XxHashAlgorithm : IHashAlgorithm
{
  private const int BufferSize = 8192;

  public string Name => "xxhash";

  public async Task<string> ComputeHashAsync(string filePath, CancellationToken cancellationToken = default)
  {
    var xxHash = new XxHash64();
    await using var stream = new FileStream(
      filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
      BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    var buffer = new byte[BufferSize];
    int bytesRead;
    while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
    {
      xxHash.Append(buffer.AsSpan(0, bytesRead));
    }

    var hashBytes = xxHash.GetCurrentHash();
    return Convert.ToHexString(hashBytes).ToLowerInvariant();
  }
}
