using System.CommandLine;
using FileMergeAssistant.Cli.Commands;
using FileMergeAssistant.Cli.Reporting;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Reporting;
using FileMergeAssistant.Analysis;
using Microsoft.Extensions.DependencyInjection;

namespace FileMergeAssistant.Cli;

static class Program
{
  static async Task<int> Main(string[] args)
  {
    var logger = LoggingSetup.CreateLogger();

    try
    {
      var services = new ServiceCollection()
        .RegisterCoreServices(logger)
        .AddSingleton<IReportGenerator, ConsoleReportGenerator>()
        .AddSingleton(_ => AnalysisEngineRegistry.CreateDefault())
        .BuildServiceProvider();

      var rootCommand = new RootCommand("File Merge Assistant - Deduplicate and organize files");
      rootCommand.AddCommand(ScanCommand.Create(services));
      rootCommand.AddCommand(ResolveCommand.Create(services));
      rootCommand.AddCommand(ReportCommand.Create(services));
      rootCommand.AddCommand(PreviewCommand.Create(services));
      rootCommand.AddCommand(AnalysisCommands.Engines(services));
      rootCommand.AddCommand(AnalysisCommands.Analyze(services));
      rootCommand.AddCommand(AnalysisCommands.Compare(services));

      return await rootCommand.InvokeAsync(args);
    }
    catch (Exception ex)
    {
      logger.Fatal(ex, "Unhandled exception");
      throw;
    }
    finally
    {
      await Serilog.Log.CloseAndFlushAsync();
    }
  }
}
