using System.CommandLine;
using FileWorkbench.Reporting;
using FileWorkbench.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace FileWorkbench.Cli.Commands;

public static class ReportCommand
{
  public static Command Create(IServiceProvider services)
  {
    var command = new Command("report", "Generate reports from scan results");

    var pathArgument = new Argument<FileInfo>("scan-result-path", "Path to scan result JSON file");
    var formatOption = new Option<string>(["--format", "-f"], () => "console", "Report format: console, json, csv, all");
    var outputOption = new Option<string?>(["--output", "-o"], "Output directory for report files");

    command.AddArgument(pathArgument);
    command.AddOption(formatOption);
    command.AddOption(outputOption);

    command.SetHandler(async (resultPath, format, output) =>
    {
      await ExecuteAsync(services, resultPath, format, output);
    }, pathArgument, formatOption, outputOption);

    return command;
  }

  private static async Task ExecuteAsync(
    IServiceProvider services,
    FileInfo resultPath,
    string format,
    string? output)
  {
    var store = services.GetRequiredService<IScanResultStore>();
    var reportGenerators = services.GetServices<IReportGenerator>();

    if (!resultPath.Exists)
    {
      AnsiConsole.MarkupLine($"[red]Error: File not found: {resultPath.FullName}[/]");
      return;
    }

    var scanResult = await store.LoadAsync(resultPath.FullName);
    if (!Enum.TryParse<ReportFormat>(format, ignoreCase: true, out var reportFormat))
    {
      AnsiConsole.MarkupLine($"[red]Error: Invalid report format '{format}'. Supported: console, json, csv, all[/]");
      return;
    }

    foreach (var generator in reportGenerators)
    {
      if (reportFormat == ReportFormat.All || generator.Format == reportFormat)
      {
        await generator.GenerateAsync(scanResult, output);
      }
    }

    AnsiConsole.MarkupLine("[green]Report generation complete.[/]");
  }
}
