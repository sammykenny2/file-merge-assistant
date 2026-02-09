using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class ScanViewModel : ObservableObject, IDisposable
{
  private readonly IFileScanner _fileScanner;
  private readonly IDuplicateDetector _duplicateDetector;
  private readonly IScanResultStore _scanResultStore;
  private CancellationTokenSource? _cts;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
  private string _selectedPath = string.Empty;

  [ObservableProperty]
  private bool _isRecursive = true;

  [ObservableProperty]
  private string _selectedAlgorithm = "sha256";

  [ObservableProperty]
  private long _minSizeBytes;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
  private bool _isScanning;

  [ObservableProperty]
  private double _currentProgress;

  [ObservableProperty]
  private int _totalFiles;

  [ObservableProperty]
  private string _currentFile = string.Empty;

  [ObservableProperty]
  private string _statusText = string.Empty;

  [ObservableProperty]
  private string _excludeExtensionsText = string.Empty;

  [ObservableProperty]
  private string _excludePatternsText = string.Empty;

  public ObservableCollection<string> Algorithms { get; } = ["sha256", "xxhash", "perceptual", "video-perceptual"];

  public event EventHandler<ScanResult>? ScanCompleted;

  public ScanViewModel(
    IFileScanner fileScanner,
    IDuplicateDetector duplicateDetector,
    IScanResultStore scanResultStore)
  {
    _fileScanner = fileScanner;
    _duplicateDetector = duplicateDetector;
    _scanResultStore = scanResultStore;
  }

  [RelayCommand]
  private void BrowseFolder()
  {
    var dialog = new OpenFolderDialog
    {
      Title = "Select folder to scan"
    };

    if (dialog.ShowDialog() == true)
    {
      SelectedPath = dialog.FolderName;
    }
  }

  private bool CanStartScan() => !string.IsNullOrWhiteSpace(SelectedPath) && !IsScanning;

  [RelayCommand(CanExecute = nameof(CanStartScan))]
  private async Task StartScanAsync()
  {
    IsScanning = true;
    StatusText = "Counting files...";
    _cts = new CancellationTokenSource();

    try
    {
      var excludeExtensions = ParseCommaSeparated(ExcludeExtensionsText);
      var excludePatterns = ParseCommaSeparated(ExcludePatternsText);

      var options = new ScanOptions
      {
        Path = SelectedPath,
        Recursive = IsRecursive,
        Algorithm = SelectedAlgorithm,
        MinSizeBytes = MinSizeBytes,
        ExcludeExtensions = excludeExtensions,
        ExcludePatterns = excludePatterns
      };

      TotalFiles = _fileScanner.CountFiles(SelectedPath, IsRecursive, MinSizeBytes);
      StatusText = $"Scanning {TotalFiles:N0} files...";

      var progress = new Progress<ScanProgress>(p =>
      {
        CurrentProgress = TotalFiles > 0 ? (double)p.FilesScanned / TotalFiles * 100 : 0;
        CurrentFile = Path.GetFileName(p.CurrentFile);
        StatusText = $"Scanning... {p.FilesScanned:N0} / {TotalFiles:N0}";
      });

      var scannedFiles = new List<ScannedFile>();
      await foreach (var file in _fileScanner.ScanAsync(options, progress, _cts.Token))
      {
        scannedFiles.Add(file);
      }

      var duplicates = _duplicateDetector.FindDuplicates(scannedFiles);

      var scanResult = new ScanResult
      {
        ScannedPath = SelectedPath,
        ScanDateUtc = DateTime.UtcNow,
        HashAlgorithm = SelectedAlgorithm,
        TotalFilesScanned = scannedFiles.Count,
        TotalSizeBytes = scannedFiles.Sum(f => f.SizeBytes),
        DuplicateGroups = duplicates
      };

      var outputDir = "./fma-results/";
      var resultPath = Path.Combine(outputDir, "scan-result.json");
      await _scanResultStore.SaveAsync(scanResult, resultPath, _cts.Token);

      StatusText = $"Scan complete! Found {duplicates.Count} duplicate groups.";
      ScanCompleted?.Invoke(this, scanResult);
    }
    catch (OperationCanceledException)
    {
      StatusText = "Scan cancelled.";
    }
    finally
    {
      IsScanning = false;
      _cts?.Dispose();
      _cts = null;
    }
  }

  private static List<string> ParseCommaSeparated(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
      return [];

    return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
      .Where(s => !string.IsNullOrEmpty(s))
      .ToList();
  }

  public void Dispose()
  {
    _cts?.Dispose();
    GC.SuppressFinalize(this);
  }
}
