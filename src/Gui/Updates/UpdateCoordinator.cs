using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Updates;

namespace FileMergeAssistant.Gui.Updates;

public enum UpdateStatus
{
  Unavailable,
  Idle,
  Checking,
  UpToDate,
  UpdateAvailable,
  Downloading,
  Downloaded,
  Installing,
  Failed,
}

/// <summary>The one next step the settings page offers as its button.</summary>
public enum UpdateStep
{
  None,
  Check,
  Download,
  Install,
}

/// <summary>
/// ADR-014: the update state the settings page and the tray share, as desktop-work-fleet's host keeps it. One check
/// or download runs at a time and repeated requests join it; downloading checks again first, so the installer is the
/// newest one. Installing runs the setup program the way ADR-013 describes and then ends the app.
/// </summary>
public sealed partial class UpdateCoordinator : ObservableObject, IDisposable
{
  private readonly IUpdateService _service;
  private readonly Action<DateTimeOffset> _recordCheck;
  private readonly CancellationTokenSource _lifetime = new();
  private Task? _running;
  private UpdatePackage? _pending;
  private string? _downloaded;

  [ObservableProperty]
  private UpdateStatus _status;

  [ObservableProperty]
  private DateTimeOffset? _lastCheckedAt;

  [ObservableProperty]
  private int _percent;

  [ObservableProperty]
  private string? _errorMessage;

  /// <param name="installed">Whether this process runs from the copy the setup program installed.</param>
  /// <param name="repository">The configured release source.</param>
  /// <param name="recordCheck">Stores the time of the last answered check.</param>
  public UpdateCoordinator(IUpdateService service, bool installed, string? repository, DateTimeOffset? lastCheckedAt, Action<DateTimeOffset> recordCheck)
  {
    _service = service;
    _recordCheck = recordCheck;
    Repository = UpdateFeed.IsValidRepository(repository) ? repository : null;
    Capable = installed && Repository is not null;
    ReasonKey = !installed ? "settings.update.needsInstall" : Repository is null ? "settings.update.noSource" : null;
    _lastCheckedAt = lastCheckedAt;
    _status = Capable ? UpdateStatus.Idle : UpdateStatus.Unavailable;
  }

  /// <summary>Only the installed copy with a valid release source can update itself.</summary>
  public bool Capable { get; }

  /// <summary>The catalog key that says why this copy cannot update itself.</summary>
  public string? ReasonKey { get; }

  public string? Repository { get; }

  /// <summary>Whether a plan run is in progress; installing waits for it, since the setup program ends the app.</summary>
  public Func<bool>? Busy { get; set; }

  /// <summary>Ends the app once the setup program has started.</summary>
  public Action? Exit { get; set; }

  public string? Version => _pending?.Version.ToString(3);

  public string? Summary => _pending?.Summary;

  public UpdateStep NextStep => Status switch
  {
    UpdateStatus.Idle or UpdateStatus.UpToDate => UpdateStep.Check,
    UpdateStatus.UpdateAvailable => UpdateStep.Download,
    UpdateStatus.Downloaded => UpdateStep.Install,
    UpdateStatus.Failed => _pending is null ? UpdateStep.Check : _downloaded is null ? UpdateStep.Download : UpdateStep.Install,
    _ => UpdateStep.None,
  };

  partial void OnStatusChanged(UpdateStatus value)
  {
    OnPropertyChanged(nameof(NextStep));
    OnPropertyChanged(nameof(Version));
    OnPropertyChanged(nameof(Summary));
  }

  /// <summary>Checks the release source now. A request while a check or download runs joins it.</summary>
  public Task CheckAsync() => Run(CheckCoreAsync);

  /// <summary>Checks again, then downloads and verifies the installer the source names now.</summary>
  public Task DownloadAsync() => Run(async token =>
  {
    await CheckCoreAsync(token);
    if (Status == UpdateStatus.UpdateAvailable && _pending is { } package)
    {
      await DownloadCoreAsync(package, token);
    }
  });

  /// <summary>Runs the downloaded setup program, which waits for this process, updates and reopens the app.</summary>
  public void Install()
  {
    if (NextStep != UpdateStep.Install || _pending is not { } package || _downloaded is not { } installer)
    {
      return;
    }
    if (Busy?.Invoke() == true)
    {
      Fail(AppText.Get("settings.update.planRunning"));
      return;
    }
    ErrorMessage = null;
    Status = UpdateStatus.Installing;
    try
    {
      // Holding the verified file open keeps other processes from replacing it before the installer starts.
      using (UpdateService.OpenVerifiedInstaller(installer, package))
      {
        var start = new ProcessStartInfo(installer) { UseShellExecute = false };
        // installer/FileMergeAssistant.nsi: /S installs silently, /WAITPID waits for this process to exit, /RELAUNCH reopens the app.
        start.ArgumentList.Add("/S");
        start.ArgumentList.Add($"/WAITPID={Environment.ProcessId}");
        start.ArgumentList.Add("/RELAUNCH");
        Process.Start(start)?.Dispose();
      }
    }
    catch (UpdateException error)
    {
      _downloaded = null;
      Fail(error.Message);
      return;
    }
    catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
    {
      Fail(AppText.Get("settings.update.startFailed"));
      return;
    }
    Exit?.Invoke();
  }

  public void Dispose()
  {
    _lifetime.Cancel();
    _lifetime.Dispose();
  }

  private Task Run(Func<CancellationToken, Task> work)
  {
    if (!Capable)
    {
      return Task.CompletedTask;
    }
    if (_running is { IsCompleted: false })
    {
      return _running;
    }
    return _running = GuardAsync(work);
  }

  private async Task GuardAsync(Func<CancellationToken, Task> work)
  {
    ErrorMessage = null;
    try
    {
      await work(_lifetime.Token);
    }
    catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
    {
      // The app is closing.
    }
    catch (UpdateException error)
    {
      Fail(error.Message);
    }
    catch (OperationCanceledException)
    {
      Fail(AppText.Get("update.cancelled"));
    }
#pragma warning disable CA1031 // A check runs from a click; an unexpected failure is shown, not allowed to end the app.
    catch (Exception)
#pragma warning restore CA1031
    {
      Fail(AppText.Get("update.failed"));
    }
  }

  private async Task CheckCoreAsync(CancellationToken token)
  {
    Status = UpdateStatus.Checking;
    var package = await _service.CheckAsync(UpdateService.CurrentVersion, token);
    var now = DateTimeOffset.UtcNow;
    LastCheckedAt = now;
    _recordCheck(now);
    if (package is null)
    {
      _pending = null;
      _downloaded = null;
      Status = UpdateStatus.UpToDate;
      return;
    }
    // A download of another version or file is no longer the one to install.
    if (_pending is null || _pending.Version != package.Version || !string.Equals(_pending.Sha256, package.Sha256, StringComparison.OrdinalIgnoreCase))
    {
      _downloaded = null;
    }
    _pending = package;
    Status = _downloaded is not null && File.Exists(_downloaded) ? UpdateStatus.Downloaded : UpdateStatus.UpdateAvailable;
  }

  private async Task DownloadCoreAsync(UpdatePackage package, CancellationToken token)
  {
    Percent = 0;
    Status = UpdateStatus.Downloading;
    _downloaded = await _service.DownloadAsync(package, new Progress<double>(value => Percent = (int)Math.Clamp(value * 100, 0, 100)), token);
    Status = UpdateStatus.Downloaded;
  }

  private void Fail(string message)
  {
    ErrorMessage = message;
    Status = UpdateStatus.Failed;
  }
}
