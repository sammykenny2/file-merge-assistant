using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Models;
using FileMergeAssistant.Resolution;
using FileMergeAssistant.Services;
using FileMergeAssistant.Utilities;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class ResultsViewModel : ObservableObject
{
  private readonly IResolutionService _resolutionService;
  private readonly IFileOperationService _fileOperationService;

  [ObservableProperty]
  private ScanResult? _scanResult;

  [ObservableProperty]
  private bool _isDryRun;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(ResolveCommand))]
  [NotifyCanExecuteChangedFor(nameof(AutoResolveKeepOldestCommand))]
  [NotifyCanExecuteChangedFor(nameof(AutoResolveKeepNewestCommand))]
  private bool _isResolving;

  [ObservableProperty]
  private int _filesDeleted;

  [ObservableProperty]
  private long _bytesFreed;

  [ObservableProperty]
  private string _resolveStatusText = string.Empty;

  public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

  public int TotalFilesScanned => ScanResult?.TotalFilesScanned ?? 0;
  public long TotalSizeBytes => ScanResult?.TotalSizeBytes ?? 0;
  public int DuplicateGroupCount => ScanResult?.DuplicateGroups.Count ?? 0;
  public int DuplicateFileCount => ScanResult?.DuplicateFileCount ?? 0;
  public long WastedSpaceBytes => ScanResult?.WastedSpaceBytes ?? 0;
  public string TotalSizeFormatted => SizeFormatter.Format(TotalSizeBytes);
  public string WastedSpaceFormatted => SizeFormatter.Format(WastedSpaceBytes);
  public string BytesFreedFormatted => SizeFormatter.Format(BytesFreed);

  public ResultsViewModel(
    IResolutionService resolutionService,
    IFileOperationService fileOperationService)
  {
    _resolutionService = resolutionService;
    _fileOperationService = fileOperationService;
  }

  public void LoadScanResult(ScanResult result)
  {
    ScanResult = result;
    DuplicateGroups.Clear();
    foreach (var group in result.DuplicateGroups)
    {
      DuplicateGroups.Add(new DuplicateGroupViewModel(group));
    }
    OnPropertyChanged(nameof(TotalFilesScanned));
    OnPropertyChanged(nameof(TotalSizeBytes));
    OnPropertyChanged(nameof(DuplicateGroupCount));
    OnPropertyChanged(nameof(DuplicateFileCount));
    OnPropertyChanged(nameof(WastedSpaceBytes));
    OnPropertyChanged(nameof(TotalSizeFormatted));
    OnPropertyChanged(nameof(WastedSpaceFormatted));
  }

  private bool CanResolve() => !IsResolving && DuplicateGroups.Count > 0;

  [RelayCommand(CanExecute = nameof(CanResolve))]
  private async Task AutoResolveKeepOldestAsync()
  {
    if (ScanResult == null) return;

    foreach (var group in DuplicateGroups)
    {
      for (var i = 0; i < group.Files.Count; i++)
      {
        group.Files[i].IsSelectedToKeep = i == 0;
      }
    }

    var decisions = _resolutionService.AutoResolveKeepOldest(ScanResult.DuplicateGroups);
    await ExecuteDecisionsAsync(decisions);
  }

  [RelayCommand(CanExecute = nameof(CanResolve))]
  private async Task AutoResolveKeepNewestAsync()
  {
    if (ScanResult == null) return;

    foreach (var group in DuplicateGroups)
    {
      for (var i = 0; i < group.Files.Count; i++)
      {
        group.Files[i].IsSelectedToKeep = i == group.Files.Count - 1;
      }
    }

    var decisions = _resolutionService.AutoResolveKeepNewest(ScanResult.DuplicateGroups);
    await ExecuteDecisionsAsync(decisions);
  }

  [RelayCommand(CanExecute = nameof(CanResolve))]
  private async Task ResolveAsync()
  {
    var decisions = new List<ResolutionDecision>();
    foreach (var group in DuplicateGroups)
    {
      var decision = group.CreateResolutionDecision();
      if (decision != null)
        decisions.Add(decision);
    }

    await ExecuteDecisionsAsync(decisions);
  }

  private async Task ExecuteDecisionsAsync(List<ResolutionDecision> decisions)
  {
    IsResolving = true;
    FilesDeleted = 0;
    BytesFreed = 0;
    ResolveStatusText = "Resolving...";

    try
    {
      foreach (var decision in decisions)
      {
        foreach (var action in decision.Actions)
        {
          if (action.Action == ResolveAction.Delete)
          {
            if (!File.Exists(action.FilePath))
              continue;

            var fileSize = new FileInfo(action.FilePath).Length;
            await _fileOperationService.DeleteAsync(action.FilePath, IsDryRun);
            FilesDeleted++;
            BytesFreed += fileSize;
          }
        }
      }

      var dryRunSuffix = IsDryRun ? " (DRY RUN)" : "";
      ResolveStatusText = string.Format(
        CultureInfo.InvariantCulture,
        "Done! {0} files deleted, {1} freed{2}",
        FilesDeleted,
        SizeFormatter.Format(BytesFreed),
        dryRunSuffix);
      OnPropertyChanged(nameof(BytesFreedFormatted));
    }
    finally
    {
      IsResolving = false;
    }
  }
}
