namespace FileWorkbench.Services;

public interface IByteByByteVerifier
{
  Task<bool> AreFilesIdenticalAsync(string filePath1, string filePath2, CancellationToken cancellationToken = default);
}

public class ByteByByteVerifier : IByteByByteVerifier
{
  private const int BufferSize = 81920;

  public async Task<bool> AreFilesIdenticalAsync(string filePath1, string filePath2, CancellationToken cancellationToken = default)
  {
    var info1 = new FileInfo(filePath1);
    var info2 = new FileInfo(filePath2);

    if (info1.Length != info2.Length)
      return false;

    if (info1.Length == 0)
      return true;

    await using var stream1 = new FileStream(
      filePath1, FileMode.Open, FileAccess.Read, FileShare.Read,
      BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
    await using var stream2 = new FileStream(
      filePath2, FileMode.Open, FileAccess.Read, FileShare.Read,
      BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

    var buffer1 = new byte[BufferSize];
    var buffer2 = new byte[BufferSize];

    while (true)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var bytesRead1 = await stream1.ReadAsync(buffer1, cancellationToken);
      var bytesRead2 = await stream2.ReadAsync(buffer2, cancellationToken);

      if (bytesRead1 != bytesRead2)
        return false;

      if (bytesRead1 == 0)
        return true;

      if (!buffer1.AsSpan(0, bytesRead1).SequenceEqual(buffer2.AsSpan(0, bytesRead2)))
        return false;
    }
  }
}
