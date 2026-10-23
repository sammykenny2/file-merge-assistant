using System.ComponentModel;
using System.Windows;
using FileMergeAssistant.Gui.Settings;
using FileMergeAssistant.Gui.Shell;
using FileMergeAssistant.Gui.Updates;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Services;
using FileMergeAssistant.Updates;
using FileMergeAssistant.Workbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FileMergeAssistant.Gui;

// The application lives as long as the process; OnExit disposes what it owns.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Disposed in OnExit.")]
public partial class App : Application
{
  private const string Title = "File Merge Assistant";
  private static readonly bool Installed = InstallationInfo.IsInstalledCopy();
  // Held for the life of the process; Windows releases it when the process ends.
  private static Mutex? _instance;
  private readonly IHost _host;
  private readonly HostShell _shell;
  private SingleInstance? _single;
  private NativeTray? _tray;
  private UpdateCoordinator? _updates;
  private MainWindow? _window;

  public App()
  {
    var arguments = Environment.GetCommandLineArgs();
    // Only the copy the setup program installed may start at sign-in; its Run entry passes the sign-in argument.
    _shell = new HostShell(
      AppDataPaths.Root,
      arguments.Contains(StartupRegistration.LoginArgument, StringComparer.OrdinalIgnoreCase),
      new StartupRegistration(new RegistryStartupKeys(), Installed ? Environment.ProcessPath : null));

    _host = Host.CreateDefaultBuilder()
      .ConfigureServices((_, services) =>
      {
        var logger = LoggingSetup.CreateLogger();
        services.RegisterCoreServices(logger);

        // The workbench screens read and change this store: the local index, or the GUI flow mock's simulated data
        // when started with --demo. It is created on the UI thread, so its change notifications arrive there.
        services.AddSingleton<IWorkbenchStore>(_ => UseDemoData
          ? new DemoWorkbenchStore()
          : IndexedWorkbenchStore.Open(AppDataPaths.IndexPath, detectNow: false));

        services.AddSingleton<IUpdateService>(_ => UpdateService.CreateDefault());
        services.AddSingleton(sp => new UpdateCoordinator(
          sp.GetRequiredService<IUpdateService>(), Installed, UpdateService.ConfiguredRepository, _shell.LastUpdateCheck, _shell.RecordUpdateCheck)
        {
          Busy = () => PlanViewModel.AnyRunning,
          Exit = () => Current.Shutdown(),
        });
        services.AddSingleton(sp => new SettingsContext(_shell, sp.GetRequiredService<UpdateCoordinator>(), Distribution, () => _tray?.Available == true));

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
      })
      .Build();
  }

  /// <summary>True when the app was started with <c>--demo</c>.</summary>
  public static bool UseDemoData { get; } = Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase);

  private static string Distribution =>
#if DEBUG
    Installed ? "installed" : "debug";
#else
    Installed ? "installed" : "portable";
#endif

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    // The tray, the dialogs and the window all start in the stored display language.
    AppText.Apply(_shell.Preferences.Language);
    if (!UseDemoData && !ClaimDataFolder())
    {
      Shutdown(0);
      return;
    }
    // Hiding the main window to the tray must not end the app; Exit in the tray menu or closing the window does.
    ShutdownMode = ShutdownMode.OnExplicitShutdown;
    // A failing command must not close the window with the user's work in it; report it and keep going.
    DispatcherUnhandledException += (_, args) =>
    {
      _host.Services.GetService<Serilog.ILogger>()?.Error(args.Exception, "Unhandled exception on the UI thread");
      MessageBox.Show(AppText.Get("dialog.unexpected", ("message", args.Exception.Message), ("logs", AppDataPaths.LogDirectory)),
        Title, MessageBoxButton.OK, MessageBoxImage.Warning);
      args.Handled = true;
    };

    MainViewModel viewModel;
    try
    {
      viewModel = _host.Services.GetRequiredService<MainViewModel>();
    }
#pragma warning disable CA1031 // Whatever stops the index from opening is reported; the app cannot start without it.
    catch (Exception error)
#pragma warning restore CA1031
    {
      _host.Services.GetService<Serilog.ILogger>()?.Error(error, "Could not open the index");
      MessageBox.Show(AppText.Get("dialog.indexFailed", ("message", error.Message), ("path", AppDataPaths.IndexPath)), Title,
        MessageBoxButton.OK, MessageBoxImage.Error);
      Shutdown(1);
      return;
    }

    var window = _host.Services.GetRequiredService<MainWindow>();
    _window = window;
    window.DataContext = viewModel;
    MainWindow = window;
    window.CloseToTray = () => _shell.Preferences.CloseToTray && _tray?.Available == true;
    window.Closed += (_, _) => Shutdown();

    var updates = _host.Services.GetRequiredService<UpdateCoordinator>();
    _updates = updates;
    updates.PropertyChanged += OnUpdateChanged;
    AppText.Changed += (_, _) => Dispatcher.BeginInvoke(ShowUpdateTip);
    _tray = new NativeTray(OnTrayAction, () => TrayMenu.Entries(window.IsVisible, updates.Capable));
    // Without a tray icon a hidden window could not be reached again.
    _tray.Changed += (_, _) =>
    {
      if (_tray?.Available == false)
      {
        ShowMain();
      }
    };

    if (_shell.StartHidden)
    {
      if (!_tray.Available)
      {
        _ = ShowWithoutTrayAsync();
      }
    }
    else
    {
      window.Show();
    }
    _single?.Listen(() => Dispatcher.BeginInvoke(ShowMain));
    if (!UseDemoData)
    {
      _shell.SyncStartup();
    }
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    _tray?.Dispose();
    _single?.Dispose();
    await _host.StopAsync();
    // Disposing the host disposes the store, which stops the scan worker before the next instance may start.
    _host.Dispose();
    _instance?.Dispose();
    base.OnExit(e);
  }

  /// <summary>
  /// One app per data folder: a running one is asked to show its window, and this launch ends. The mutex is the last
  /// safeguard when the running one cannot answer, for example while it is still starting.
  /// </summary>
  private bool ClaimDataFolder()
  {
    if (SingleInstance.Forward(AppDataPaths.Root))
    {
      return false;
    }
    // Two windows over one index would run plans and scans side by side; the second one stops here.
    _instance = new Mutex(initiallyOwned: true, @"Local\FileMergeAssistant.Workbench", out var first);
    if (!first)
    {
      _instance.Dispose();
      _instance = null;
      MessageBox.Show(AppText.Get("dialog.alreadyOpen"), Title, MessageBoxButton.OK, MessageBoxImage.Information);
      return false;
    }
    _single = SingleInstance.Serve(AppDataPaths.Root);
    return true;
  }

  private void ShowMain()
  {
    if (_window is not { } window)
    {
      return;
    }
    if (!window.IsVisible)
    {
      window.Show();
    }
    if (window.WindowState == WindowState.Minimized)
    {
      window.WindowState = WindowState.Normal;
    }
    window.Activate();
  }

  /// <summary>A hidden sign-in start shows the window when no tray icon arrived in time.</summary>
  private async Task ShowWithoutTrayAsync()
  {
    await Task.Delay(HostShell.TrayWait);
    if (_tray?.Available != true && _window?.IsVisible == false)
    {
      ShowMain();
    }
  }

  private void OnTrayAction(string action)
  {
    switch (action)
    {
      case TrayMenu.Show:
        ShowMain();
        break;
      case TrayMenu.Hide:
        _window?.Hide();
        break;
      case TrayMenu.Update:
        _ = CheckFromTrayAsync();
        break;
      case TrayMenu.Exit:
        Shutdown();
        break;
      default:
        break;
    }
  }

  /// <summary>The tray asks once: a fresh check, then a native answer; updating restarts like the settings page.</summary>
  private async Task CheckFromTrayAsync()
  {
    if (_updates is not { } updates)
    {
      return;
    }
    var current = UpdateService.CurrentVersion.ToString(3);
    await updates.CheckAsync();
    if (updates.Status is UpdateStatus.UpdateAvailable or UpdateStatus.Downloaded)
    {
      var text = string.IsNullOrWhiteSpace(updates.Summary)
        ? AppText.Get("dialog.updateAvailable", ("version", updates.Version), ("current", current))
        : AppText.Get("dialog.updateAvailableSummary", ("version", updates.Version), ("current", current), ("summary", updates.Summary));
      if (MessageBox.Show(text, AppText.Get("dialog.updateTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
      {
        return;
      }
      if (updates.Status != UpdateStatus.Downloaded)
      {
        await updates.DownloadAsync();
      }
      if (updates.Status == UpdateStatus.Downloaded)
      {
        updates.Install();
      }
      if (updates.Status == UpdateStatus.Failed)
      {
        MessageBox.Show(AppText.Get("dialog.installFailed", ("detail", updates.ErrorMessage)), AppText.Get("dialog.updateTitle"),
          MessageBoxButton.OK, MessageBoxImage.Warning);
      }
      return;
    }
    var upToDate = updates.Status == UpdateStatus.UpToDate;
    MessageBox.Show(upToDate ? AppText.Get("dialog.upToDate", ("version", current)) : updates.ErrorMessage ?? AppText.Get("update.failed"),
      AppText.Get("dialog.updateTitle"), MessageBoxButton.OK, upToDate ? MessageBoxImage.Information : MessageBoxImage.Warning);
  }

  private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName is nameof(UpdateCoordinator.Status) or nameof(UpdateCoordinator.Percent))
    {
      ShowUpdateTip();
    }
  }

  /// <summary>An open menu does not repaint, so download progress shows in the tray tooltip.</summary>
  private void ShowUpdateTip() =>
    _tray?.SetTip(_updates?.Status == UpdateStatus.Downloading
      ? AppText.Get("tray.tipDownloading", ("percent", _updates.Percent))
      : null);
}
