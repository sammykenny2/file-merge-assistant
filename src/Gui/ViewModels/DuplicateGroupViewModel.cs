using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FileMergeAssistant.Models;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class DuplicateGroupViewModel : ObservableObject
{
  public DuplicateGroup Group { get; }

  [ObservableProperty]
  private bool _isExpanded;

  [ObservableProperty]
  private bool _isSkipped;

  public ObservableCollection<ScannedFileViewModel> Files { get; }

  public string Hash => Group.Hash[..Math.Min(16, Group.Hash.Length)] + "...";
  public int FileCount => Group.Files.Count;
  public long WastedBytes => Group.WastedBytes;

  public DuplicateGroupViewModel(DuplicateGroup group)
  {
    Group = group;
    Files = new ObservableCollection<ScannedFileViewModel>(
      group.Files.Select((f, i) => new ScannedFileViewModel(f, isSelectedToKeep: i == 0)));
  }

  public ResolutionDecision? CreateResolutionDecision()
  {
    if (IsSkipped) return null;

    var keptFile = Files.FirstOrDefault(f => f.IsSelectedToKeep);
    if (keptFile == null)
      return null;

    var actions = Files.Select(f => new FileAction
    {
      FilePath = f.File.FilePath,
      Action = f.IsSelectedToKeep ? ResolveAction.Keep : ResolveAction.Delete
    }).ToList();

    return new ResolutionDecision
    {
      Group = Group,
      KeptFilePath = keptFile.File.FilePath,
      Actions = actions
    };
  }
}
