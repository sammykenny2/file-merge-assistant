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
  private readonly IRecycleBinService _recycleBinService;
  private readonly ISpaceSavingsAnalyzer _spaceSavingsAnalyzer;

  [ObservableProperty]
  private ScanResult? _scanResult;

  [ObservableProperty]
  private bool _isDryRun;

  [ObservableProperty]
  private bool _useRecycleBin = true;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(ResolveCommand))]
  [NotifyCanExecuteChangedFor(nameof(AutoResolveKeepOldestCommand))]
  [NotifyCanExecuteChangedFor(nameof(AutoResolveKeepNewestCommand))]
  [NotifyCanExecuteChangedFor(nameof(AutoResolveByMetadataCommand))]
  [NotifyCanExecuteChangedFor(nameof(UndoLastResolveCommand))]
  private bool _isResolving;

  [ObservableProperty]
  private int _filesDeleted;

  [ObservableProperty]
  private long _bytesFreed;

  [ObservableProperty]
  private string _resolveStatusText = string.Empty;

  [ObservableProperty]
  private string _wastedPercentage = "0%";

  [ObservableProperty]
  private ObservableCollection<ExtensionBreakdownViewModel> _extensionBreakdowns = [];

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
    IFileOperationService fileOperationService,
    IRecycleBinService recycleBinService,
    ISpaceSavingsAnalyzer spaceSavingsAnalyzer)
  {
    _resolutionService = resolutionService;
    _fileOperationService = fileOperationService;
    _recycleBinService = recycleBinService;
    _spaceSavingsAnalyzer = spaceSavingsAnalyzer;
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

    UpdateSpaceSavingsReport(result);
  }

  private void UpdateSpaceSavingsReport(ScanResult result)
  {
    var report = _spaceSavingsAnalyzer.Analyze(result);
    WastedPercentage = $"{report.WastedPercentage:F1}%";

    ExtensionBreakdowns.Clear();
    foreach (var ext in report.ByExtension.Take(10))
    {
      ExtensionBreakdowns.Add(new ExtensionBreakdownViewModel(ext));
    }
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
  private async Task AutoResolveByMetadataAsync()
  {
    if (ScanResult == null) return;

    var decisions = _resolutionService.AutoResolveByMetadata(ScanResult.DuplicateGroups);
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

  private bool CanUndo() => !IsResolving;

  [RelayCommand(CanExecute = nameof(CanUndo))]
  private async Task UndoLastResolveAsync()
  {
    IsResolving = true;
    ResolveStatusText = "Restoring files...";

    try
    {
      await _recycleBinService.RestoreAllAsync();
      ResolveStatusText = "All files restored from recycle bin.";
    }
    catch (Exception ex)
    {
      ResolveStatusText = $"Restore failed: {ex.Message}";
    }
    finally
    {
      IsResolving = false;
    }
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
          switch (action.Action)
          {
            case ResolveAction.Delete:
            {
              if (!File.Exists(action.FilePath))
                continue;

              var fileSize = new FileInfo(action.FilePath).Length;

              if (UseRecycleBin && !IsDryRun)
              {
                await _recycleBinService.RecycleAsync(action.FilePath);
              }
              else
              {
                await _fileOperationService.DeleteAsync(action.FilePath, IsDryRun);
              }

              FilesDeleted++;
              BytesFreed += fileSize;
              break;
            }

            case ResolveAction.Move when action.MoveDestination != null:
            {
              if (!File.Exists(action.FilePath))
                continue;

              await _fileOperationService.MoveAsync(action.FilePath, action.MoveDestination, IsDryRun);
              break;
            }

            case ResolveAction.Hardlink:
            {
              if (!File.Exists(action.FilePath))
                continue;

              await _fileOperationService.CreateHardlinkAsync(decision.KeptFilePath, action.FilePath, IsDryRun);
              break;
            }

            case ResolveAction.Symlink:
            {
              if (!File.Exists(action.FilePath))
                continue;

              await _fileOperationService.CreateSymlinkAsync(decision.KeptFilePath, action.FilePath, IsDryRun);
              break;
            }
          }
        }
      }

      var dryRunSuffix = IsDryRun ? " (DRY RUN)" : "";
      var recycleSuffix = UseRecycleBin && !IsDryRun ? " (files in recycle bin - undo available)" : "";
      ResolveStatusText = string.Format(
        CultureInfo.InvariantCulture,
        "Done! {0} files deleted, {1} freed{2}{3}",
        FilesDeleted,
        SizeFormatter.Format(BytesFreed),
        dryRunSuffix,
        recycleSuffix);
      OnPropertyChanged(nameof(BytesFreedFormatted));
    }
    finally
    {
      IsResolving = false;
    }
  }
}

public class ExtensionBreakdownViewModel
{
  public string Extension { get; }
  public string FileCount { get; }
  public string WastedSpace { get; }
  public int GroupCount { get; }

  public ExtensionBreakdownViewModel(ExtensionBreakdown breakdown)
  {
    Extension = breakdown.Extension;
    FileCount = breakdown.FileCount.ToString(CultureInfo.InvariantCulture);
    WastedSpace = SizeFormatter.Format(breakdown.WastedBytes);
    GroupCount = breakdown.DuplicateGroupCount;
  }
}
