using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileMergeAssistant.Sources;

/// <summary>
/// The real location behind a path (ADR-010). A junction, a symbolic link, a <c>subst</c> drive or a mapped drive can
/// make one folder appear under two names; resolving both through <c>GetFinalPathNameByHandle</c> shows they are the
/// same, so a folder is never added twice and never treated as a copy of itself.
/// </summary>
public static partial class PathIdentity
{
  private const uint FileShareAll = 0x1 | 0x2 | 0x4;
  private const uint OpenExisting = 3;
  private const uint BackupSemantics = 0x02000000;

  /// <summary>The final path of an existing file or folder, or the full path when Windows cannot resolve it.</summary>
  public static string Resolve(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    var full = Path.GetFullPath(path);
    using var handle = CreateFileW(full, 0, FileShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);
    if (handle.IsInvalid)
    {
      return full;
    }
    var buffer = new char[1024];
    var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
    if (length == 0 || length >= buffer.Length)
    {
      return full;
    }
    var final = new string(buffer, 0, (int)length);
    if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
    {
      final = @"\\" + final[8..];
    }
    else if (final.StartsWith(@"\\?\", StringComparison.Ordinal))
    {
      final = final[4..];
    }
    var trimmed = final.TrimEnd('\\');
    return trimmed.Length == 2 && trimmed[1] == ':' ? trimmed + "\\" : trimmed;
  }

  [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
    uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

  [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint filePathLength, uint flags);
}
