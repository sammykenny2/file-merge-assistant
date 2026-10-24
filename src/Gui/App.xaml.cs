using System.ComponentModel;
using System.Windows;
using FileWorkbench.Gui.Settings;
using FileWorkbench.Gui.Shell;
using FileWorkbench.Gui.Updates;
using FileWorkbench.Gui.ViewModels;
using FileWorkbench.Gui.Workbench;
using FileWorkbench.Infrastructure;
using FileWorkbench.Localization;
using FileWorkbench.Services;
using FileWorkbench.Updates;
using FileWorkbench.Workbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FileWorkbench.Gui;

// The application lives as long as the process; OnExit disposes what it owns.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Disposed in OnExit.")]
public partial class App : Application
{
  private static string Title => AppText.Get("header.productName");
  private static readonly bool Installed = InstallationInfo.IsInstalledCopy();
  // Held for the life of the process; Windows releases it when the process ends.
  private static Mutex? _instance;
  private readonly IHost _host;
  private readonly HostShell _shell;
  private SingleInstance? _single;
  private NativeTray? _tray;
  private UpdateCoordinator? _updates;
  private MainWindow? _window;
  private bool _exiting;

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
          Busy = () => FileChanges.InProgress,
          Exit = RequestExit,
        });
        services.AddSingleton(sp => new SettingsContext(_shell, sp.GetRequiredService<UpdateCoordinator>(), Distribution,
          () => _tray?.Available == true, UseDemoData));

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
    // Signing out or shutting down waits while files are being moved: Windows then lists the app as blocking it.
    // Otherwise the app ends without asking.
    SessionEnding += (_, args) =>
    {
      if (FileChanges.InProgress)
      {
        args.Cancel = true;
      }
      else
      {
        _exiting = true;
      }
    };
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
    // A later launch shows this window from now on, whatever happens with the tray below.
    _single?.Listen(() => Dispatcher.BeginInvoke(ShowMain));
    window.CloseToTray = () => _shell.Preferences.CloseToTray && _tray?.Available == true;
    window.Closing += (_, args) =>
    {
      // Closing the window to quit waits for file changes, as Exit in the tray does.
      if (!_exiting && FileChanges.InProgress)
      {
        args.Cancel = true;
        ShowBusy();
      }
    };
    window.Closed += (_, _) => Shutdown();

    var updates = _host.Services.GetRequiredService<UpdateCoordinator>();
    _updates = updates;
    updates.PropertyChanged += OnUpdateChanged;
    AppText.Changed += (_, _) => Dispatcher.BeginInvoke(ShowUpdateTip);
    try
    {
      var tray = new NativeTray(OnTrayAction, () => TrayMenu.Entries(window.IsVisible, updates.Capable));
      _tray = tray;
      // Without a tray icon a hidden window could not be reached again.
      tray.Changed += (_, _) =>
      {
        if (!tray.Available)
        {
          ShowMain();
        }
      };
    }
#pragma warning disable CA1031 // Without a tray the app still works; the window then always shows and closing it quits.
    catch (Exception error)
#pragma warning restore CA1031
    {
      _host.Services.GetService<Serilog.ILogger>()?.Error(error, "Could not create the tray icon");
      _tray = null;
    }

    if (_shell.StartHidden && _tray is not null)
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
        RequestExit();
        break;
      default:
        break;
    }
  }

  /// <summary>
  /// Ends the app the way closing the window does, unless a run, retry, restore or emptying is still moving files:
  /// ending it then would leave the history behind the disk.
  /// </summary>
  private void RequestExit()
  {
    if (FileChanges.InProgress)
    {
      ShowBusy();
      return;
    }
    _exiting = true;
    Shutdown();
  }

  private static void ShowBusy() =>
    MessageBox.Show(AppText.Get("dialog.busy"), Title, MessageBoxButton.OK, MessageBoxImage.Information);

  /// <summary>
  /// The tray asks once: a fresh check, then a native answer; updating restarts like the settings page. Every path
  /// ends with an answer, unless the setup program has started and the app is closing.
  /// </summary>
  private async Task CheckFromTrayAsync()
  {
    if (_updates is not { } updates)
    {
      return;
    }
    var current = UpdateService.CurrentVersion.ToString(3);
    var title = AppText.Get("dialog.updateTitle");
    await updates.CheckAsync();
    if (updates.Status is UpdateStatus.UpdateAvailable or UpdateStatus.Downloaded)
    {
      var text = string.IsNullOrWhiteSpace(updates.Summary)
        ? AppText.Get("dialog.updateAvailable", ("version", updates.Version), ("current", current))
        : AppText.Get("dialog.updateAvailableSummary", ("version", updates.Version), ("current", current), ("summary", updates.Summary));
      if (MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
      {
        return;
      }
      // A download that only joined a check already running is started once more.
      for (var attempt = 0; attempt < 2 && updates.Status is UpdateStatus.UpdateAvailable or UpdateStatus.Checking; attempt++)
      {
        await updates.DownloadAsync();
      }
      if (updates.Status == UpdateStatus.Downloaded)
      {
        updates.Install();
        if (updates.Status == UpdateStatus.Installing)
        {
          return;
        }
      }
    }
    var (message, icon) = updates.Status switch
    {
      UpdateStatus.UpToDate => (AppText.Get("dialog.upToDate", ("version", current)), MessageBoxImage.Information),
      UpdateStatus.Failed => (AppText.Get("dialog.installFailed", ("detail", updates.ErrorMessage ?? AppText.Get("update.failed"))), MessageBoxImage.Warning),
      UpdateStatus.Downloading => (AppText.Get("settings.update.downloading", ("version", updates.Version)), MessageBoxImage.Information),
      UpdateStatus.Downloaded => (AppText.Get("settings.update.downloaded", ("version", updates.Version)), MessageBoxImage.Information),
      _ => (AppText.Get("update.failed"), MessageBoxImage.Warning),
    };
    MessageBox.Show(message, title, MessageBoxButton.OK, icon);
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
