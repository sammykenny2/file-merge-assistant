using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileMergeAssistant.Sources;

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

  /// <summary>Name prefix of the probe files <see cref="ProbeSeenAt"/> writes for a moment; scans never index them.</summary>
  public const string ProbePrefix = ".fma-probe-";

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
    => FileIdentityReader.Identify(path);

  /// <summary>True for a UNC path, a mapped network drive, or a link that leads onto either.</summary>
  public static bool IsNetwork(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    if (path.StartsWith(@"\\", StringComparison.Ordinal))
    {
      return true;
    }
    var real = Resolve(path);
    if (real.StartsWith(@"\\", StringComparison.Ordinal))
    {
      return true;
    }
    try
    {
      return Path.GetPathRoot(real) is { Length: > 0 } root && new DriveInfo(root).DriveType == DriveType.Network;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  /// <summary>
  /// Asks the disk instead of trusting names or ids: writes an empty, uniquely named probe file into
  /// <paramref name="folder"/> (deleted again when this returns) and looks for it in each of <paramref name="candidates"/>.
  /// Needed where ids cannot be trusted: a NAS can report a different volume serial for each share name of one folder.
  /// </summary>
  /// <returns>The index of the first candidate that shows the probe, -1 for none, or null when no probe can be written.</returns>
  public static int? ProbeSeenAt(string folder, IReadOnlyList<string> candidates)
  {
    ArgumentNullException.ThrowIfNull(folder);
    ArgumentNullException.ThrowIfNull(candidates);
    var name = ProbePrefix + Guid.NewGuid().ToString("N");
    FileStream probe;
    try
    {
      probe = new FileStream(Path.Combine(folder, name), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1,
        FileOptions.DeleteOnClose);
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    {
      return null;
    }
    using (probe)
    {
      for (var i = 0; i < candidates.Count; i++)
      {
        if (File.Exists(Path.Combine(candidates[i], name)))
        {
          return i;
        }
      }
      return -1;
    }
  }

  /// <summary><paramref name="path"/> and every folder above it, as given.</summary>
  public static IReadOnlyList<string> SelfAndAncestors(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    var chain = new List<string>();
    for (var folder = Path.GetFullPath(path).TrimEnd('\\'); folder is not null; folder = Parent(folder))
    {
      chain.Add(folder.Length == 2 && folder[1] == ':' ? folder + "\\" : folder);
    }
    return chain;
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
    if (self.SameAs(target))
    {
      return true;
    }
    foreach (var start in new[] { inner, Resolve(inner) })
    {
      for (var folder = Parent(start); folder is not null; folder = Parent(folder))
      {
        if (Identify(folder) is { } above && above.SameAs(target))
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

}
