using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Models;

namespace FileMergeAssistant.Gui.ViewModels;

/// <summary>
/// The previous Scan / Results / Reports screens, kept under 「舊版掃描」 until the workbench scans real folders.
/// </summary>
public partial class LegacyScanViewModel : ObservableObject
{
  private readonly Func<ScanViewModel> _scanViewModelFactory;
  private readonly Func<ResultsViewModel> _resultsViewModelFactory;
  private readonly Func<ReportsViewModel> _reportsViewModelFactory;

  private ScanViewModel? _scanViewModel;
  private ResultsViewModel? _resultsViewModel;
  private ReportsViewModel? _reportsViewModel;

  [ObservableProperty]
  private object? _currentView;

  [ObservableProperty]
  private bool _isScanSelected = true;

  [ObservableProperty]
  private bool _isResultsSelected;

  [ObservableProperty]
  private bool _isReportsSelected;

  public LegacyScanViewModel(
    Func<ScanViewModel> scanViewModelFactory,
    Func<ResultsViewModel> resultsViewModelFactory,
    Func<ReportsViewModel> reportsViewModelFactory)
  {
    _scanViewModelFactory = scanViewModelFactory;
    _resultsViewModelFactory = resultsViewModelFactory;
    _reportsViewModelFactory = reportsViewModelFactory;

    _scanViewModel = _scanViewModelFactory();
    _scanViewModel.ScanCompleted += OnScanCompleted;
    CurrentView = _scanViewModel;
  }

  [RelayCommand]
  private void Navigate(string target)
  {
    switch (target)
    {
      case "Scan":
        _scanViewModel ??= _scanViewModelFactory();
        _scanViewModel.ScanCompleted -= OnScanCompleted;
        _scanViewModel.ScanCompleted += OnScanCompleted;
        CurrentView = _scanViewModel;
        IsScanSelected = true;
        IsResultsSelected = false;
        IsReportsSelected = false;
        break;

      case "Results":
        CurrentView = _resultsViewModel ?? _resultsViewModelFactory();
        IsScanSelected = false;
        IsResultsSelected = true;
        IsReportsSelected = false;
        break;

      case "Reports":
        if (_reportsViewModel != null)
        {
          _reportsViewModel.ScanResult = _resultsViewModel?.ScanResult;
        }
        CurrentView = _reportsViewModel ?? _reportsViewModelFactory();
        IsScanSelected = false;
        IsResultsSelected = false;
        IsReportsSelected = true;
        break;
    }
  }

  private void OnScanCompleted(object? sender, ScanResult result)
  {
    _resultsViewModel = _resultsViewModelFactory();
    _resultsViewModel.LoadScanResult(result);

    _reportsViewModel = _reportsViewModelFactory();
    _reportsViewModel.ScanResult = result;

    Navigate("Results");
  }
}
