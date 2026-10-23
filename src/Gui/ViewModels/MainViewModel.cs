using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Models;
using FileMergeAssistant.Updates;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class MainViewModel : ObservableObject
{
  private readonly Func<ScanViewModel> _scanViewModelFactory;
  private readonly Func<ResultsViewModel> _resultsViewModelFactory;
  private readonly Func<ReportsViewModel> _reportsViewModelFactory;
  private readonly Func<UpdateWindow> _updateWindowFactory;

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

  public MainViewModel(
    Func<ScanViewModel> scanViewModelFactory,
    Func<ResultsViewModel> resultsViewModelFactory,
    Func<ReportsViewModel> reportsViewModelFactory,
    Func<UpdateWindow> updateWindowFactory)
  {
    _scanViewModelFactory = scanViewModelFactory;
    _resultsViewModelFactory = resultsViewModelFactory;
    _reportsViewModelFactory = reportsViewModelFactory;
    _updateWindowFactory = updateWindowFactory;

    _scanViewModel = _scanViewModelFactory();
    _scanViewModel.ScanCompleted += OnScanCompleted;
    CurrentView = _scanViewModel;
  }

  public string VersionText { get; } = $"Version {UpdateService.CurrentVersion.ToString(3)}";

  [RelayCommand]
  private void CheckForUpdates()
  {
    var window = _updateWindowFactory();
    window.Owner = Application.Current.MainWindow;
    window.ShowDialog();
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
