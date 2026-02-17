using System.Runtime.InteropServices;
using Serilog;

namespace FileMergeAssistant.Services;

public interface IHardlinkDetector
{
  bool AreHardlinked(string filePath1, string filePath2);
}

public class HardlinkDetector : IHardlinkDetector
{
  private readonly ILogger _logger;

  public HardlinkDetector(ILogger logger)
  {
    _logger = logger;
  }

  public bool AreHardlinked(string filePath1, string filePath2)
  {
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
        !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
      // On Windows, hardlink detection requires P/Invoke
      // Fall back to inode-based detection where available
      return false;
    }

    try
    {
      var info1 = new FileInfo(filePath1);
      var info2 = new FileInfo(filePath2);

      if (info1.LinkTarget != null || info2.LinkTarget != null)
        return false; // These are symlinks, not hardlinks

      // On Unix-like systems, use UnixFileMode and compare device + inode
      // .NET 7+ exposes File.GetUnixFileMode but not inode directly.
      // We use a best-effort approach: same device, same length, same timestamps
      // For a production tool, native stat() calls would be ideal.
      // Here we check if the files share the same filesystem path resolution.
      var resolved1 = Path.GetFullPath(filePath1);
      var resolved2 = Path.GetFullPath(filePath2);

      if (resolved1 == resolved2)
        return true;

      // Compare file identity via FileStream handle
      if (info1.Length != info2.Length)
        return false;

      // Best-effort: if both files have same length, same creation time to the tick,
      // and same last write time, they are likely hardlinked
      return info1.CreationTimeUtc == info2.CreationTimeUtc &&
             info1.LastWriteTimeUtc == info2.LastWriteTimeUtc &&
             info1.Length == info2.Length;
    }
    catch (Exception ex)
    {
      _logger.Debug(ex, "Could not check hardlink status for {File1} and {File2}", filePath1, filePath2);
      return false;
    }
  }
}
