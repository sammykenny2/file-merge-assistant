using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileMergeAssistant.Sources;

/// <summary>The identity of a file or folder on disk: its volume's serial number and its file id on that volume.</summary>
public readonly record struct FileIdentity(uint VolumeSerial, ulong FileId);

/// <summary>
/// The real location behind a path (ADR-010). A junction, a symbolic link, a <c>subst</c> drive, a mapped drive or a UNC
/// path (<c>\\server\share</c>, <c>\\localhost\C$</c>) can make one folder appear under several names. Names are never
/// trusted to tell folders apart: <see cref="Identify"/> asks Windows for the volume serial and file id, which are the
/// same under every name, so a folder is never added twice and never treated as a copy of itself.
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
    using var handle = Open(full);
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

  /// <summary>The identity of an existing file or folder (links are followed to their target), or null when it cannot be opened.</summary>
  public static FileIdentity? Identify(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    string full;
    try
    {
      full = Path.GetFullPath(path);
    }
    catch (ArgumentException)
    {
      return null;
    }
    using var handle = Open(full);
    if (handle.IsInvalid)
    {
      return null;
    }
    // The same call for every path, so two names of one folder can never come back in different forms (ReFS ids are
    // 128-bit; their low half is used here, which can only make two folders look the same, never the reverse).
    var info = new byte[52];
    if (GetFileInformationByHandle(handle, info))
    {
      var serial = BitConverter.ToUInt32(info, 28);
      var index = ((ulong)BitConverter.ToUInt32(info, 44) << 32) | BitConverter.ToUInt32(info, 48);
      return new FileIdentity(serial, index);
    }
    return null;
  }

  /// <summary>
  /// Whether <paramref name="inner"/> is <paramref name="outer"/> itself or lies inside it, under any name. Every folder
  /// from <paramref name="inner"/> up to its root, both as given and as resolved, is compared by identity.
  /// </summary>
  /// <returns>Null when <paramref name="outer"/> or <paramref name="inner"/> cannot be identified.</returns>
  public static bool? SameOrInside(string inner, string outer)
  {
    ArgumentNullException.ThrowIfNull(inner);
    ArgumentNullException.ThrowIfNull(outer);
    if (Identify(outer) is not { } target || Identify(inner) is not { } self)
    {
      return null;
    }
    if (self == target)
    {
      return true;
    }
    foreach (var start in new[] { inner, Resolve(inner) })
    {
      for (var folder = Parent(start); folder is not null; folder = Parent(folder))
      {
        if (Identify(folder) == target)
        {
          return true;
        }
      }
    }
    return false;
  }

  private static string? Parent(string path)
  {
    try
    {
      return Path.GetDirectoryName(Path.GetFullPath(path).TrimEnd('\\'));
    }
    catch (ArgumentException)
    {
      return null;
    }
  }

  private static SafeFileHandle Open(string full) =>
    CreateFileW(full, 0, FileShareAll, IntPtr.Zero, OpenExisting, BackupSemantics, IntPtr.Zero);

  [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
    uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

  [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint filePathLength, uint flags);

  // BY_HANDLE_FILE_INFORMATION: dwVolumeSerialNumber at 28, nFileIndexHigh at 44, nFileIndexLow at 48.
  [LibraryImport("kernel32.dll", SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetFileInformationByHandle(SafeFileHandle file, [Out] byte[] info);
}
