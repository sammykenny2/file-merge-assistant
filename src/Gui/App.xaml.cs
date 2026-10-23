using System.Windows;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Services;
using FileMergeAssistant.Updates;
using FileMergeAssistant.Workbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FileMergeAssistant.Gui;

public partial class App : Application
{
  private readonly IHost _host;
  // Held for the life of the process; Windows releases it when the process ends.
  private static Mutex? _instance;

  public App()
  {
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
        services.AddTransient<UpdateViewModel>();
        services.AddTransient<UpdateWindow>();
        services.AddSingleton<Func<UpdateWindow>>(sp => sp.GetRequiredService<UpdateWindow>);

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
      })
      .Build();
  }

  /// <summary>True when the app was started with <c>--demo</c>.</summary>
  public static bool UseDemoData { get; } = Environment.GetCommandLineArgs().Contains("--demo", StringComparer.OrdinalIgnoreCase);

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);
    if (!UseDemoData)
    {
      // Two windows over one index would run plans and scans side by side; the second one stops here.
      _instance = new Mutex(initiallyOwned: true, @"Local\FileMergeAssistant.Workbench", out var first);
      if (!first)
      {
        _instance.Dispose();
        _instance = null;
        MessageBox.Show("File Merge Assistant 已經開著了。請切換到已開啟的視窗。", "File Merge Assistant", MessageBoxButton.OK, MessageBoxImage.Information);
        Shutdown(0);
        return;
      }
    }
    // A failing command must not close the window with the user's work in it; report it and keep going.
    DispatcherUnhandledException += (_, args) =>
    {
      _host.Services.GetService<Serilog.ILogger>()?.Error(args.Exception, "Unhandled exception on the UI thread");
      MessageBox.Show($"發生了沒有預期的錯誤，這個動作沒有完成：{Environment.NewLine}{args.Exception.Message}{Environment.NewLine}{Environment.NewLine}詳細內容寫在記錄檔：{AppDataPaths.LogDirectory}",
        "File Merge Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
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
      MessageBox.Show($"無法開啟索引：{error.Message}{Environment.NewLine}{Environment.NewLine}索引位置：{AppDataPaths.IndexPath}", "File Merge Assistant",
        MessageBoxButton.OK, MessageBoxImage.Error);
      Shutdown(1);
      return;
    }
    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
    mainWindow.DataContext = viewModel;
    mainWindow.Show();
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    await _host.StopAsync();
    // Disposing the host disposes the store, which stops the scan worker before the next instance may start.
    _host.Dispose();
    _instance?.Dispose();
    base.OnExit(e);
  }
}
