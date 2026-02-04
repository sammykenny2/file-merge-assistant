using System.CommandLine;
using FileMergeAssistant.Models;
using FileMergeAssistant.Reporting;
using FileMergeAssistant.Services;
using FileMergeAssistant.UI;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;

namespace FileMergeAssistant.Commands;

public static class ScanCommand
{
  public static Command Create(IServiceProvider services)
  {
    var command = new Command("scan", "Scan directories for duplicate files");

    var pathArgument = new Argument<DirectoryInfo>("path", "Directory to scan");
    var recursiveOption = new Option<bool>(["--recursive", "-r"], () => true, "Scan subdirectories recursively");
    var algorithmOption = new Option<string>(["--algorithm", "-a"], () => "sha256", "Hash algorithm: sha256, xxhash, perceptual");
    var outputOption = new Option<string>(["--output", "-o"], () => "./fma-results/", "Output directory for results");
    var formatOption = new Option<string>(["--format", "-f"], () => "all", "Report format: console, json, csv, all");
    var minSizeOption = new Option<long>("--min-size", () => 0, "Minimum file size in bytes");

    command.AddArgument(pathArgument);
    command.AddOption(recursiveOption);
    command.AddOption(algorithmOption);
    command.AddOption(outputOption);
    command.AddOption(formatOption);
    command.AddOption(minSizeOption);

    command.SetHandler(async (path, recursive, algorithm, output, format, minSize) =>
    {
      await ExecuteAsync(services, path, recursive, algorithm, output, format, minSize);
    }, pathArgument, recursiveOption, algorithmOption, outputOption, formatOption, minSizeOption);

    return command;
  }

  private static async Task ExecuteAsync(
    IServiceProvider services,
    DirectoryInfo path,
    bool recursive,
    string algorithm,
    string output,
    string format,
    long minSize)
  {
    var logger = services.GetRequiredService<ILogger>();
    var scanner = services.GetRequiredService<IFileScanner>();
    var detector = services.GetRequiredService<IDuplicateDetector>();
    var store = services.GetRequiredService<IScanResultStore>();
    var reportGenerators = services.GetServices<IReportGenerator>();

    if (!path.Exists)
    {
      AnsiConsole.MarkupLine($"[red]Error: Directory not found: {path.FullName}[/]");
      return;
    }

    var options = new ScanOptions
    {
      Path = path.FullName,
      Recursive = recursive,
      Algorithm = algorithm,
      OutputDirectory = output,
      MinSizeBytes = minSize
    };

    AnsiConsole.MarkupLine($"[bold]Scanning:[/] {path.FullName}");
    AnsiConsole.MarkupLine($"[grey]Algorithm: {algorithm} | Recursive: {recursive} | Min size: {minSize} bytes[/]");
    AnsiConsole.WriteLine();

    logger.Information("Starting scan of {Path} with algorithm {Algorithm}", path.FullName, algorithm);

    var totalFiles = scanner.CountFiles(path.FullName, recursive, minSize);
    AnsiConsole.MarkupLine($"[grey]Found {totalFiles:N0} files to scan[/]");

    var scannedFiles = await ProgressReporter.RunWithProgressAsync(
      "Scanning files",
      scanner.ScanAsync(options, cancellationToken: CancellationToken.None),
      totalFiles,
      f => Path.GetFileName(f.FilePath));

    var duplicates = detector.FindDuplicates(scannedFiles);

    var scanResult = new ScanResult
    {
      ScannedPath = path.FullName,
      ScanDateUtc = DateTime.UtcNow,
      HashAlgorithm = algorithm,
      TotalFilesScanned = scannedFiles.Count,
      TotalSizeBytes = scannedFiles.Sum(f => f.SizeBytes),
      DuplicateGroups = duplicates
    };

    var resultPath = Path.Combine(output, "scan-result.json");
    await store.SaveAsync(scanResult, resultPath);
    logger.Information("Scan results saved to {Path}", resultPath);

    var reportFormat = Enum.Parse<ReportFormat>(format, ignoreCase: true);
    foreach (var generator in reportGenerators)
    {
      if (reportFormat == ReportFormat.All || generator.Format == reportFormat)
      {
        await generator.GenerateAsync(scanResult, output);
      }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[green]Scan complete. Results saved to: {resultPath}[/]");
  }
}
