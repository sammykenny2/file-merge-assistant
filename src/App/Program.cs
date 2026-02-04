using System.CommandLine;
using FileMergeAssistant.Commands;
using FileMergeAssistant.Infrastructure;

namespace FileMergeAssistant;

static class Program
{
  static async Task<int> Main(string[] args)
  {
    var logger = LoggingSetup.CreateLogger();

    try
    {
      var services = ServiceRegistration.BuildServiceProvider(logger);

      var rootCommand = new RootCommand("File Merge Assistant - Deduplicate and organize files");
      rootCommand.AddCommand(ScanCommand.Create(services));
      rootCommand.AddCommand(ResolveCommand.Create(services));
      rootCommand.AddCommand(ReportCommand.Create(services));

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
