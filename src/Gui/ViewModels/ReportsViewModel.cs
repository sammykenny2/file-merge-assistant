using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Models;
using FileMergeAssistant.Reporting;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
  private readonly IEnumerable<IReportGenerator> _reportGenerators;

  [ObservableProperty]
  private ScanResult? _scanResult;

  [ObservableProperty]
  private string _selectedFormat = "Json";

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
  private bool _isExporting;

  [ObservableProperty]
  private string _statusText = string.Empty;

  public ObservableCollection<string> Formats { get; } = ["Json", "Csv", "PowerShell"];

  public ReportsViewModel(IEnumerable<IReportGenerator> reportGenerators)
  {
    _reportGenerators = reportGenerators;
  }

  private bool CanExportReport() => !IsExporting && ScanResult != null;

  [RelayCommand(CanExecute = nameof(CanExportReport))]
  private async Task ExportReportAsync()
  {
    if (ScanResult == null) return;

    if (!Enum.TryParse<ReportFormat>(SelectedFormat, ignoreCase: true, out var format))
      return;

    var extension = format switch
    {
      ReportFormat.Json => "json",
      ReportFormat.Csv => "csv",
      ReportFormat.PowerShell => "ps1",
      _ => "json"
    };

    var dialog = new SaveFileDialog
    {
      Title = "Export Report",
      Filter = $"{SelectedFormat} files (*.{extension})|*.{extension}|All files (*.*)|*.*",
      FileName = format == ReportFormat.PowerShell ? "resolve-duplicates.ps1" : $"scan-report.{extension}"
    };

    if (dialog.ShowDialog() != true)
      return;

    IsExporting = true;
    StatusText = "Exporting...";

    try
    {
      var outputDir = System.IO.Path.GetDirectoryName(dialog.FileName);
      foreach (var generator in _reportGenerators)
      {
        if (generator.Format == format)
        {
          await generator.GenerateAsync(ScanResult, outputDir);
          break;
        }
      }

      StatusText = $"Report exported to: {dialog.FileName}";
    }
    finally
    {
      IsExporting = false;
    }
  }
}
