using System.CommandLine;
using FileMergeAssistant.Cli.UI;
using FileMergeAssistant.Models;
using FileMergeAssistant.Reporting;
using FileMergeAssistant.Services;
using FileMergeAssistant.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;

namespace FileMergeAssistant.Cli.Commands;

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
    var maxSizeOption = new Option<long>("--max-size", () => 0, "Maximum file size in bytes (0 = no limit)");
    var matchModeOption = new Option<string>("--match-mode", () => "content", "Match mode: content, name, name-size");
    var additionalPathsOption = new Option<string[]>("--also", "Additional directories to scan") { AllowMultipleArgumentsPerToken = true };
    var protectedPathsOption = new Option<string[]>("--protect", "Protected directories (files here will not be deleted)") { AllowMultipleArgumentsPerToken = true };
    var skipHardlinksOption = new Option<bool>("--skip-hardlinks", () => false, "Skip files that are hardlinked to each other");
    var verifyOption = new Option<bool>("--verify", () => false, "Byte-by-byte verification after hash match (paranoid mode)");
    var cleanupEmptyOption = new Option<bool>("--cleanup-empty", () => false, "Remove empty directories after scan");

    command.AddArgument(pathArgument);
    command.AddOption(recursiveOption);
    command.AddOption(algorithmOption);
    command.AddOption(outputOption);
    command.AddOption(formatOption);
    command.AddOption(minSizeOption);
    command.AddOption(maxSizeOption);
    command.AddOption(matchModeOption);
    command.AddOption(additionalPathsOption);
    command.AddOption(protectedPathsOption);
    command.AddOption(skipHardlinksOption);
    command.AddOption(verifyOption);
    command.AddOption(cleanupEmptyOption);

    command.SetHandler(async context =>
    {
      var path = context.ParseResult.GetValueForArgument(pathArgument);
      var recursive = context.ParseResult.GetValueForOption(recursiveOption);
      var algorithm = context.ParseResult.GetValueForOption(algorithmOption)!;
      var output = context.ParseResult.GetValueForOption(outputOption)!;
      var format = context.ParseResult.GetValueForOption(formatOption)!;
      var minSize = context.ParseResult.GetValueForOption(minSizeOption);
      var maxSize = context.ParseResult.GetValueForOption(maxSizeOption);
      var matchMode = context.ParseResult.GetValueForOption(matchModeOption)!;
      var additionalPaths = context.ParseResult.GetValueForOption(additionalPathsOption) ?? [];
      var protectedPaths = context.ParseResult.GetValueForOption(protectedPathsOption) ?? [];
      var skipHardlinks = context.ParseResult.GetValueForOption(skipHardlinksOption);
      var verify = context.ParseResult.GetValueForOption(verifyOption);
      var cleanupEmpty = context.ParseResult.GetValueForOption(cleanupEmptyOption);

      await ExecuteAsync(services, path!, recursive, algorithm, output, format,
        minSize, maxSize, matchMode, additionalPaths, protectedPaths,
        skipHardlinks, verify, cleanupEmpty);
    });

    return command;
  }

  private static async Task ExecuteAsync(
    IServiceProvider services,
    DirectoryInfo path,
    bool recursive,
    string algorithm,
    string output,
    string format,
    long minSize,
    long maxSize,
    string matchMode,
    string[] additionalPaths,
    string[] protectedPaths,
    bool skipHardlinks,
    bool verify,
    bool cleanupEmpty)
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
      AdditionalPaths = additionalPaths.ToList(),
      Recursive = recursive,
      Algorithm = algorithm,
      MatchMode = matchMode,
      OutputDirectory = output,
      MinSizeBytes = minSize,
      MaxSizeBytes = maxSize,
      ProtectedPaths = protectedPaths.ToList(),
      SkipHardlinks = skipHardlinks,
      ByteByByteVerification = verify,
      CleanupEmptyDirectories = cleanupEmpty
    };

    AnsiConsole.MarkupLine($"[bold]Scanning:[/] {path.FullName}");
    if (additionalPaths.Length > 0)
      AnsiConsole.MarkupLine($"[bold]Also scanning:[/] {string.Join(", ", additionalPaths)}");
    AnsiConsole.MarkupLine($"[grey]Algorithm: {algorithm} | Mode: {matchMode} | Recursive: {recursive}[/]");
    if (minSize > 0 || maxSize > 0)
      AnsiConsole.MarkupLine($"[grey]Size filter: {SizeFormatter.Format(minSize)} - {(maxSize > 0 ? SizeFormatter.Format(maxSize) : "no limit")}[/]");
    if (verify)
      AnsiConsole.MarkupLine("[grey]Byte-by-byte verification enabled (paranoid mode)[/]");
    if (skipHardlinks)
      AnsiConsole.MarkupLine("[grey]Hardlink detection enabled[/]");
    if (protectedPaths.Length > 0)
      AnsiConsole.MarkupLine($"[grey]Protected paths: {string.Join(", ", protectedPaths)}[/]");
    AnsiConsole.WriteLine();

    logger.Information("Starting scan of {Path} with algorithm {Algorithm}, mode {MatchMode}", path.FullName, algorithm, matchMode);

    var totalFiles = scanner.CountFiles(path.FullName, recursive, minSize);
    AnsiConsole.MarkupLine($"[grey]Found {totalFiles:N0} files to scan[/]");

    var scannedFiles = await ProgressReporter.RunWithProgressAsync(
      "Scanning files",
      scanner.ScanAsync(options, cancellationToken: CancellationToken.None),
      totalFiles,
      f => Path.GetFileName(f.FilePath));

    IReadOnlyList<DuplicateGroup> duplicates = matchMode.ToLowerInvariant() switch
    {
      "name" => detector.FindDuplicatesByName(scannedFiles),
      "name-size" => detector.FindDuplicatesByNameAndSize(scannedFiles),
      _ => detector.FindDuplicatesBySize(scannedFiles)
    };

    if (skipHardlinks && duplicates.Count > 0)
    {
      var hardlinkDetector = services.GetRequiredService<IHardlinkDetector>();
      var before = duplicates.Count;
      duplicates = detector.FilterHardlinks(duplicates, hardlinkDetector);
      var filtered = before - duplicates.Count;
      if (filtered > 0)
        AnsiConsole.MarkupLine($"[grey]Filtered {filtered} hardlinked group(s)[/]");
    }

    if (verify && duplicates.Count > 0)
    {
      AnsiConsole.MarkupLine("[grey]Verifying duplicates byte-by-byte...[/]");
      var verifier = services.GetRequiredService<IByteByByteVerifier>();
      var before = duplicates.Sum(g => g.Files.Count);
      duplicates = await detector.VerifyGroupsByteByByteAsync(duplicates, verifier);
      var after = duplicates.Sum(g => g.Files.Count);
      if (before != after)
        AnsiConsole.MarkupLine($"[grey]Verification removed {before - after} false positive(s)[/]");
    }

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

    if (cleanupEmpty)
    {
      var emptyDirService = services.GetRequiredService<IEmptyDirectoryService>();
      var removed = emptyDirService.Cleanup(path.FullName);
      if (removed > 0)
        AnsiConsole.MarkupLine($"[grey]Cleaned up {removed} empty director(ies)[/]");
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[green]Scan complete. Results saved to: {resultPath}[/]");
  }
}
