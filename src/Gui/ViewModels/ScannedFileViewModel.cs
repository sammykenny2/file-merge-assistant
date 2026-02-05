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

  public ScannedFileViewModel(ScannedFile file, bool isSelectedToKeep = false)
  {
    File = file;
    _isSelectedToKeep = isSelectedToKeep;
  }
}
