using System.ComponentModel;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Scanning;
using FileMergeAssistant.Sources;

namespace FileMergeAssistant.Gui.Settings;

public sealed partial class SettingsViewModel
{
  private string? _comparisonOutcome;
  private int _comparisonMatchingGroups;
  [ObservableProperty]
  private string _comparisonStatus = string.Empty;

  [ObservableProperty]
  private string? _comparisonReportPath;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(CompareEnginesCommand))]
  private bool _comparisonBusy;

  [ObservableProperty]
  private bool _comparisonSaving;

  public bool CanCompareEngines => !ComparisonBusy && !Store.IsDemo
    && Store.Sources.Any(source => source.State != SourceState.Offline)
    && !Store.Jobs.Any(job => job.IsActive || job.State == ScanJobState.Queued)
    && Store.Engines.Descriptors.Any(engine => engine.Id == NativeAnalysisEngine.EngineId && engine.Availability == AnalysisAvailability.Available
      && engine.Supports(AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3));

  public bool HasComparisonReport => ComparisonReportPath is not null;

  partial void OnComparisonReportPathChanged(string? value) => OnPropertyChanged(nameof(HasComparisonReport));

  private void RenderComparison()
  {
    if (!ComparisonBusy)
    {
      ComparisonStatus = _comparisonOutcome is null ? AppText.Get("analysis.compare.hint")
        : AppText.Get("analysis.compare." + _comparisonOutcome, ("groups", Fmt.Count(_comparisonMatchingGroups)));
    }
    OnPropertyChanged(nameof(CanCompareEngines));
    CompareEnginesCommand.NotifyCanExecuteChanged();
  }

  [RelayCommand(CanExecute = nameof(CanCompareEngines), IncludeCancelCommand = true)]
  private async Task CompareEnginesAsync(CancellationToken cancellationToken)
  {
    ComparisonBusy = true;
    ComparisonReportPath = null;
    _comparisonOutcome = null;
    Message = null;
    using var progress = new CoalescingProgress<AnalysisComparisonProgress>(value =>
    {
      ComparisonStatus = value.Stage == "Comparison" ? AppText.Get("analysis.compare.normalizing")
        : AppText.Get("analysis.compare.progress", ("pass", value.Pass), ("engine", value.EngineId),
        ("files", Fmt.Count(value.Jobs.Sum(job => job.Discovered))), ("confirmed", Fmt.Count(value.Jobs.Sum(job => job.Confirmed))));
    });
    try
    {
      var first = Store.Engines.Snapshot(ManagedAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.XxHash128);
      var second = Store.Engines.Snapshot(NativeAnalysisEngine.EngineId, AnalysisCapability.ExactDuplicates, ContentFingerprints.Blake3);
      var report = await Store.CompareEnginesAsync(first, second, progress, cancellationToken);
      progress.Dispose();
      ComparisonSaving = true;
      ComparisonStatus = AppText.Get("analysis.compare.saving");
      var reportPath = Path.Combine(AppDataPaths.Root, "comparisons", $"comparison-{Guid.NewGuid():N}.json");
      await Task.Run(async () =>
      {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        var pending = reportPath + ".partial";
        await File.WriteAllTextAsync(pending, report.ToJson(), CancellationToken.None).ConfigureAwait(false);
        File.Move(pending, reportPath);
      }, CancellationToken.None);
      ComparisonReportPath = reportPath;
      _comparisonMatchingGroups = report.MatchingGroups;
      _comparisonOutcome = report.Outcome == "Canceled" ? "canceled" : !report.IsComplete ? "incomplete" : report.GroupsAgree ? "agree" : "differ";
    }
    catch (OperationCanceledException) { _comparisonOutcome = "canceled"; }
    catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or DbException)
    {
      Message = AppText.Get("analysis.compare.failed", ("error", error.Message));
      _comparisonOutcome = "incomplete";
    }
    finally
    {
      progress.Dispose();
      ComparisonSaving = false;
      ComparisonBusy = false;
      RenderComparison();
    }
  }

  [RelayCommand]
  private void ShowComparisonReport()
  {
    if (ComparisonReportPath is not { } path) { return; }
    try { using var process = Process.Start(new ProcessStartInfo(Path.GetDirectoryName(path)!) { UseShellExecute = true }); }
    catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
    {
      Message = AppText.Get("analysis.compare.failed", ("error", error.Message));
    }
  }
}
