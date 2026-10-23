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
        var snapshot = Select(registry, id, context.ParseResult.GetValueForOption(algorithm));
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

  public static Command Compare(IServiceProvider services, string? dataRoot = null)
  {
    var command = new Command("compare-engines", "Run two independent read-only analyses and output a versioned comparison JSON report");
    var paths = new Argument<string[]>("paths", "Source folders") { Arity = ArgumentArity.OneOrMore };
    var first = new Option<string>("--first", () => ManagedAnalysisEngine.EngineId, "First engine ID");
    var second = new Option<string>("--second", () => NativeAnalysisEngine.EngineId, "Second engine ID");
    var firstAlgorithm = new Option<string?>("--first-algorithm", "Versioned first algorithm; defaults to its first exact algorithm");
    var secondAlgorithm = new Option<string?>("--second-algorithm", "Versioned second algorithm; defaults to its first exact algorithm");
    command.AddArgument(paths);
    command.AddOption(first);
    command.AddOption(second);
    command.AddOption(firstAlgorithm);
    command.AddOption(secondAlgorithm);
    command.SetHandler(async context =>
    {
      try
      {
        var registry = services.GetRequiredService<AnalysisEngineRegistry>();
        var a = Select(registry, context.ParseResult.GetValueForOption(first)!, context.ParseResult.GetValueForOption(firstAlgorithm));
        var b = Select(registry, context.ParseResult.GetValueForOption(second)!, context.ParseResult.GetValueForOption(secondAlgorithm));
        var report = await new AnalysisEngineComparison(registry, dataRoot).RunAsync(context.ParseResult.GetValueForArgument(paths), a, b,
          cancellationToken: context.GetCancellationToken());
        context.Console.Out.WriteLine(report.ToJson());
        context.ExitCode = report.IsComplete ? 0 : report.Outcome == "Canceled" ? 130 : 2;
      }
      catch (OperationCanceledException)
      {
        context.Console.Out.WriteLine(JsonSerializer.Serialize(new { formatVersion = 1, isComplete = false, outcome = "Canceled" }));
        context.ExitCode = 130;
      }
      catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException or DbException)
      {
        var availability = error is AnalysisEngineUnavailableException unavailable ? unavailable.Availability.ToString() : "Failed";
        context.Console.Out.WriteLine(JsonSerializer.Serialize(new { formatVersion = 1, isComplete = false, outcome = availability, error = error.Message }));
        context.ExitCode = 2;
      }
    });
    return command;
  }

  private static AnalysisEngineSnapshot Select(AnalysisEngineRegistry registry, string id, string? algorithm)
  {
    var descriptor = registry.Descriptors.SingleOrDefault(item => item.Id == id)
      ?? throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, $"Unknown analysis engine: {id}");
    algorithm ??= descriptor.Capabilities.FirstOrDefault(item => item.Capability == AnalysisCapability.ExactDuplicates)?.Algorithms.FirstOrDefault()
      ?? throw new AnalysisEngineUnavailableException(AnalysisAvailability.Unsupported, "The selected engine has no exact-content capability.");
    return registry.Snapshot(id, AnalysisCapability.ExactDuplicates, algorithm);
  }
}
