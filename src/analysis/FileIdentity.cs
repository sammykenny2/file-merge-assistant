using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileMergeAssistant.Sources;

/// <summary>Opaque volume/file identity. Windows additionally records the local volume GUID when available.</summary>
public readonly record struct FileIdentity(uint VolumeSerial, ulong FileId, string? VolumeGuid)
{
  public bool SameAs(FileIdentity other) => FileId == other.FileId && VolumeSerial == other.VolumeSerial
    && (VolumeGuid is null || other.VolumeGuid is null || string.Equals(VolumeGuid, other.VolumeGuid, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Optional physical identity, independent of WPF, SQLite and the operation workflow.</summary>
public static partial class FileIdentityReader
{
  public static FileIdentity? Identify(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    if (!OperatingSystem.IsWindows()) { return null; }
    string full;
    try { full = Path.GetFullPath(path); }
    catch (ArgumentException) { return null; }
    using var handle = CreateFileW(full, 0, 0x1 | 0x2 | 0x4, 0, 3, 0x02000000, 0);
    if (handle.IsInvalid) { return null; }
    // BY_HANDLE_FILE_INFORMATION, unchanged from Core. ReFS 128-bit IDs retain the existing low-half limitation.
    var info = new byte[52];
    if (!GetFileInformationByHandle(handle, info)) { return null; }
    var serial = BitConverter.ToUInt32(info, 28);
    var index = ((ulong)BitConverter.ToUInt32(info, 44) << 32) | BitConverter.ToUInt32(info, 48);
    if (index == 0) { return null; }
    var buffer = new char[1024];
    var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0x1);
    string? guid = null;
    if (length > 0 && length < buffer.Length)
    {
      var final = new string(buffer, 0, (int)length);
      var start = final.IndexOf("Volume{", StringComparison.OrdinalIgnoreCase);
      var end = start < 0 ? -1 : final.IndexOf('}', start);
      guid = end > start ? final[start..(end + 1)] : null;
    }
    return new(serial, index, guid);
  }

  [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
    uint creationDisposition, uint flagsAndAttributes, nint templateFile);

  [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] filePath, uint filePathLength, uint flags);

  [LibraryImport("kernel32.dll", SetLastError = true)]
  [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetFileInformationByHandle(SafeFileHandle file, [Out] byte[] info);
}
