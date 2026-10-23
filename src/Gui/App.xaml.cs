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
          : IndexedWorkbenchStore.Open(AppDataPaths.IndexPath));

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
    catch (Exception error) when (error is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException
      or Microsoft.Data.Sqlite.SqliteException)
    {
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
    _host.Dispose();
    base.OnExit(e);
  }
}
