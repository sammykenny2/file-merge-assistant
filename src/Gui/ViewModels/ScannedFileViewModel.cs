using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FileMergeAssistant.Models;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class ScannedFileViewModel : ObservableObject
{
  public ScannedFile File { get; }

  [ObservableProperty]
  private bool _isSelectedToKeep;

  public string FileName => Path.GetFileName(File.FilePath);
  public string DirectoryName => Path.GetDirectoryName(File.FilePath) ?? string.Empty;
  public long SizeBytes => File.SizeBytes;
  public DateTime CreatedUtc => File.CreatedUtc;
  public DateTime ModifiedUtc => File.ModifiedUtc;

  public bool HasMetadata => File.Metadata != null;
  public string MetadataSummary => FormatMetadata();
  public string? CameraModel => File.Metadata?.CameraModel;
  public string? DateTaken => File.Metadata?.DateTaken?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
  public string? Resolution => File.Metadata?.Width != null && File.Metadata?.Height != null
    ? $"{File.Metadata.Width}x{File.Metadata.Height}"
    : null;

  public ScannedFileViewModel(ScannedFile file, bool isSelectedToKeep = false)
  {
    File = file;
    _isSelectedToKeep = isSelectedToKeep;
  }

  private string FormatMetadata()
  {
    if (File.Metadata == null) return string.Empty;
    var parts = new List<string>();
    if (File.Metadata.DateTaken.HasValue)
      parts.Add($"Taken: {File.Metadata.DateTaken:yyyy-MM-dd}");
    if (!string.IsNullOrEmpty(File.Metadata.CameraModel))
      parts.Add(File.Metadata.CameraModel);
    if (File.Metadata.Width.HasValue && File.Metadata.Height.HasValue)
      parts.Add($"{File.Metadata.Width}x{File.Metadata.Height}");
    return string.Join(" | ", parts);
  }
}
