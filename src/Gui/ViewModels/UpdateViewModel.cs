using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Gui.Updates;
using FileMergeAssistant.Updates;

namespace FileMergeAssistant.Gui.ViewModels;

public partial class UpdateViewModel : ObservableObject
{
  private readonly IUpdateService _updates;
  private readonly bool _isInstalledCopy;
  private UpdatePackage? _package;
  private string? _downloadedInstaller;
  // Cancels the running check or download; the token source itself is owned by that operation.
  private Action? _cancelWork;

  [ObservableProperty]
  private string _statusText = string.Empty;

  [ObservableProperty]
  private string _summary = string.Empty;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
  [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
  private bool _isBusy;

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(DownloadAndInstallCommand))]
  private bool _canInstall;

  [ObservableProperty]
  private bool _isDownloading;

  [ObservableProperty]
  private double _downloadProgress;

  public UpdateViewModel(IUpdateService updates)
  {
    _updates = updates;
    _isInstalledCopy = InstallationInfo.IsInstalledCopy();
  }

  public string VersionText { get; } = $"Version {UpdateService.CurrentVersion.ToString(3)}";

  public string SourceText { get; } = UpdateFeed.IsValidRepository(UpdateService.ConfiguredRepository)
    ? $"Updates come from github.com/{UpdateService.ConfiguredRepository}"
    : "No update source is configured.";

  public bool IsInstalledCopy => _isInstalledCopy;

  public bool HasSummary => Summary.Length > 0;

  /// <summary>Asked before the app closes to run the installer; returning false keeps the app open.</summary>
  public Func<UpdatePackage, bool>? ConfirmInstall { get; set; }

  partial void OnSummaryChanged(string value) => OnPropertyChanged(nameof(HasSummary));

  /// <summary>Stops a running check or download, for example when the window closes.</summary>
  public void CancelPendingWork() => _cancelWork?.Invoke();

  private bool CanCheck() => !IsBusy;

  private bool CanDownloadAndInstall() => !IsBusy && CanInstall;

  [RelayCommand(CanExecute = nameof(CanCheck))]
  private async Task CheckAsync()
  {
    IsBusy = true;
    CanInstall = false;
    Summary = string.Empty;
    _package = null;
    _downloadedInstaller = null;
    StatusText = "Checking for updates…";
    using var work = StartWork();
    try
    {
      var package = await _updates.CheckAsync(UpdateService.CurrentVersion, work.Token);
      if (package is null)
      {
        StatusText = $"You're up to date. {UpdateService.CurrentVersion.ToString(3)} is the newest version.";
        return;
      }

      _package = package;
      Summary = package.Summary;
      StatusText = _isInstalledCopy
        ? $"Version {package.Version.ToString(3)} is available."
        : $"Version {package.Version.ToString(3)} is available. This copy was not installed with the setup program, so download the new version from the releases page.";
      CanInstall = _isInstalledCopy;
    }
    catch (UpdateException ex)
    {
      StatusText = ex.Message;
    }
    catch (OperationCanceledException)
    {
      StatusText = "The check was cancelled.";
    }
#pragma warning disable CA1031 // The check runs from a window event; an unexpected failure is shown, not allowed to end the app.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      StatusText = $"Could not check for updates: {ex.Message}";
    }
    finally
    {
      EndWork();
      IsBusy = false;
    }
  }

  [RelayCommand(CanExecute = nameof(CanDownloadAndInstall))]
  private async Task DownloadAndInstallAsync()
  {
    if (_package is not { } package)
      return;
    IsBusy = true;
    using var work = StartWork();
    try
    {
      if (_downloadedInstaller is null || !File.Exists(_downloadedInstaller))
      {
        IsDownloading = true;
        DownloadProgress = 0;
        StatusText = $"Downloading version {package.Version.ToString(3)}…";
        _downloadedInstaller = await _updates.DownloadAsync(package, new Progress<double>(value => DownloadProgress = value * 100), work.Token);
        IsDownloading = false;
      }

      if (ConfirmInstall is { } confirm && !confirm(package))
      {
        DownloadProgress = 0;
        StatusText = "The update is downloaded. Select Download and install when you are ready to restart.";
        return;
      }

      // Holding the verified file open keeps other processes from replacing it before the installer starts.
      using (UpdateService.OpenVerifiedInstaller(_downloadedInstaller, package))
      {
        StatusText = "Starting the setup program. File Merge Assistant will close and reopen after the update.";
        var start = new ProcessStartInfo(_downloadedInstaller) { UseShellExecute = false };
        // installer/FileMergeAssistant.nsi: /S installs silently, /WAITPID waits for this process to exit, /RELAUNCH reopens the app.
        start.ArgumentList.Add("/S");
        start.ArgumentList.Add($"/WAITPID={Environment.ProcessId}");
        start.ArgumentList.Add("/RELAUNCH");
        Process.Start(start)?.Dispose();
      }
      Application.Current.Shutdown();
    }
    catch (UpdateException ex)
    {
      _downloadedInstaller = null;
      StatusText = ex.Message;
    }
    catch (OperationCanceledException)
    {
      StatusText = "The download was cancelled.";
    }
    catch (Win32Exception)
    {
      StatusText = "The setup program could not be started.";
    }
#pragma warning disable CA1031 // An unexpected failure is shown in the window instead of ending the app mid-update.
    catch (Exception ex)
#pragma warning restore CA1031
    {
      _downloadedInstaller = null;
      StatusText = $"The update could not be installed: {ex.Message}";
    }
    finally
    {
      EndWork();
      IsDownloading = false;
      IsBusy = false;
    }
  }

  [RelayCommand]
  private static void OpenReleasesPage()
  {
    var repository = UpdateService.ConfiguredRepository;
    if (UpdateFeed.IsValidRepository(repository))
      Process.Start(new ProcessStartInfo(UpdateFeed.ReleasesPageUrl(repository).AbsoluteUri) { UseShellExecute = true })?.Dispose();
  }

  private CancellationTokenSource StartWork()
  {
    var work = new CancellationTokenSource();
    _cancelWork = work.Cancel;
    return work;
  }

  private void EndWork() => _cancelWork = null;
}
