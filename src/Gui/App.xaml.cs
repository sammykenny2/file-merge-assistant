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

        services.AddTransient<ScanViewModel>();
        services.AddTransient<ResultsViewModel>();
        services.AddTransient<ReportsViewModel>();

        services.AddSingleton<Func<ScanViewModel>>(sp => sp.GetRequiredService<ScanViewModel>);
        services.AddSingleton<Func<ResultsViewModel>>(sp => sp.GetRequiredService<ResultsViewModel>);
        services.AddSingleton<Func<ReportsViewModel>>(sp => sp.GetRequiredService<ReportsViewModel>);
        services.AddTransient<LegacyScanViewModel>();
        services.AddSingleton<Func<LegacyScanViewModel>>(sp => sp.GetRequiredService<LegacyScanViewModel>);

        // The workbench screens read and change this store. Until the real index lands it holds the mock's demo data.
        services.AddSingleton<IWorkbenchStore, DemoWorkbenchStore>();

        services.AddSingleton<IUpdateService>(_ => UpdateService.CreateDefault());
        services.AddTransient<UpdateViewModel>();
        services.AddTransient<UpdateWindow>();
        services.AddSingleton<Func<UpdateWindow>>(sp => sp.GetRequiredService<UpdateWindow>);

        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
      })
      .Build();
  }

  protected override void OnStartup(StartupEventArgs e)
  {
    base.OnStartup(e);

    var mainWindow = _host.Services.GetRequiredService<MainWindow>();
    mainWindow.DataContext = _host.Services.GetRequiredService<MainViewModel>();
    mainWindow.Show();
  }

  protected override async void OnExit(ExitEventArgs e)
  {
    await _host.StopAsync();
    _host.Dispose();
    base.OnExit(e);
  }
}
