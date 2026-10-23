using System.CommandLine;
using System.CommandLine.IO;
using System.Data.Common;
using System.Text.Json;
using FileMergeAssistant.Analysis;
using FileMergeAssistant.Scanning;
using Microsoft.Extensions.DependencyInjection;

namespace FileMergeAssistant.Cli.Commands;

/// <summary>Explicit versioned analysis commands. The legacy scan/resolve/report contracts stay separate.</summary>
public static class AnalysisCommands
{
  public static Command Engines(IServiceProvider services)
  {
    var command = new Command("engines", "List analysis engine versions, capabilities, maturity and availability as JSON");
    command.SetHandler(context => context.Console.Out.WriteLine(services.GetRequiredService<AnalysisEngineRegistry>().ToJson()));
    return command;
  }

  public static Command Analyze(IServiceProvider services, string? dataRoot = null)
  {
    var command = new Command("analyze", "Read-only exact-content analysis using the workbench engine contract; outputs versioned JSON");
    var paths = new Argument<string[]>("paths", "Source folders; applies workbench exclusions, cloud and link policies") { Arity = ArgumentArity.OneOrMore };
    var engine = new Option<string>("--engine", () => ManagedAnalysisEngine.EngineId, "Analysis engine ID; see engines");
    var algorithm = new Option<string?>("--algorithm", "Versioned algorithm ID; defaults to the selected engine's first exact-content algorithm");
    command.AddArgument(paths);
    command.AddOption(engine);
    command.AddOption(algorithm);
    command.SetHandler(async context =>
    {
      try
      {
        var registry = services.GetRequiredService<AnalysisEngineRegistry>();
        var id = context.ParseResult.GetValueForOption(engine)!;
        var descriptor = registry.Descriptors.SingleOrDefault(item => item.Id == id)
          ?? throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, $"Unknown analysis engine: {id}");
        var selectedAlgorithm = context.ParseResult.GetValueForOption(algorithm)
          ?? descriptor.Capabilities.FirstOrDefault(item => item.Capability == AnalysisCapability.ExactDuplicates)?.Algorithms.FirstOrDefault()
          ?? throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, "The selected engine has no exact-content capability.");
        var snapshot = registry.Snapshot(id, AnalysisCapability.ExactDuplicates, selectedAlgorithm);
        var report = await new ReadOnlyAnalysisSession(registry, dataRoot).RunAsync(context.ParseResult.GetValueForArgument(paths), snapshot,
          cancellationToken: context.GetCancellationToken());
        context.Console.Out.WriteLine(report.ToJson());
        context.ExitCode = report.IsComplete ? 0 : report.Outcome == "Canceled" ? 130 : 2;
      }
      catch (OperationCanceledException)
      {
        context.Console.Out.WriteLine(JsonSerializer.Serialize(new { formatVersion = 1, isComplete = false, outcome = "Canceled" }));
        context.ExitCode = 130;
      }
      catch (Exception error) when (error is AnalysisEngineUnavailableException or ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or DbException)
      {
        var availability = error is AnalysisEngineUnavailableException unavailable ? unavailable.Availability.ToString() : "Failed";
        context.Console.Out.WriteLine(JsonSerializer.Serialize(new { formatVersion = 1, isComplete = false, outcome = availability, error = error.Message }));
        context.ExitCode = 2;
      }
    });
    return command;
  }
}
