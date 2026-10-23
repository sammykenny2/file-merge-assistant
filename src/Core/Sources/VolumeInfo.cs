using System.Globalization;
using System.Runtime.InteropServices;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Sources;

public enum VolumeKind
{
  Fixed,
  Removable,
  Network,
  Other,
}

/// <summary>What identifies the volume a folder lives on (ADR-010). The serial survives drive-letter changes.</summary>
/// <param name="Root">Volume root, for example <c>E:\</c>.</param>
/// <param name="Serial">Volume serial number as <c>7C1E-09AB</c>, or null when Windows does not report one.</param>
/// <param name="Label">Volume label, for display only.</param>
/// <param name="Kind">Drive type.</param>
public sealed record VolumeInfo(string Root, string? Serial, string? Label, VolumeKind Kind)
{
  /// <summary>Plain description in the display language, for example 「外接硬碟，磁碟區序號 7C1E-09AB」.</summary>
  public string Describe()
  {
    var kind = Kind switch
    {
      VolumeKind.Removable => "scan.volume.removable",
      VolumeKind.Network => "scan.volume.network",
      VolumeKind.Fixed => "scan.volume.fixed",
      _ => "scan.volume.other",
    };
    return Serial is null ? AppText.Get(kind) : AppText.Get(kind + "Serial", ("serial", Serial));
  }
}

public interface IVolumeInfoProvider
{
  /// <summary>The volume holding <paramref name="path"/>, or null when it is not available.</summary>
  VolumeInfo? Describe(string path);
}

/// <summary>Reads volume facts through <c>GetVolumeInformationW</c>; <see cref="DriveInfo"/> has no serial number.</summary>
public sealed partial class Win32VolumeInfoProvider : IVolumeInfoProvider
{
  public VolumeInfo? Describe(string path)
  {
    ArgumentNullException.ThrowIfNull(path);
    string root;
    try
    {
      root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(path)) ?? string.Empty;
    }
    catch (ArgumentException)
    {
      return null;
    }
    if (root.Length == 0 || !Directory.Exists(root))
    {
      return null;
    }

    var kind = VolumeKind.Other;
    try
    {
      kind = new DriveInfo(root).DriveType switch
      {
        DriveType.Fixed => VolumeKind.Fixed,
        DriveType.Removable => VolumeKind.Removable,
        DriveType.Network => VolumeKind.Network,
        _ => VolumeKind.Other,
      };
    }
    catch (ArgumentException)
    {
      // UNC roots are not drives; they stay Other.
    }

    var label = new char[261];
    var fileSystem = new char[261];
    if (!GetVolumeInformationW(root, label, label.Length, out var serial, out _, out _, fileSystem, fileSystem.Length))
    {
      return new VolumeInfo(root, null, null, kind);
    }
    var text = new string(label).TrimEnd('\0');
    var serialText = string.Create(CultureInfo.InvariantCulture, $"{serial >> 16:X4}-{serial & 0xFFFF:X4}");
    return new VolumeInfo(root, serialText, text.Length == 0 ? null : text, kind);
  }

  [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static partial bool GetVolumeInformationW(
    string rootPathName,
    [Out] char[] volumeNameBuffer,
    int volumeNameSize,
    out uint volumeSerialNumber,
    out uint maximumComponentLength,
    out uint fileSystemFlags,
    [Out] char[] fileSystemNameBuffer,
    int fileSystemNameSize);
}
