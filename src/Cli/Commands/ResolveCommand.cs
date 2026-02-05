using System.CommandLine;
using FileMergeAssistant.Cli.UI;
using FileMergeAssistant.Models;
using FileMergeAssistant.Resolution;
using FileMergeAssistant.Services;
using FileMergeAssistant.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Spectre.Console;

namespace FileMergeAssistant.Cli.Commands;

public static class ResolveCommand
{
  public static Command Create(IServiceProvider services)
  {
    var command = new Command("resolve", "Interactively resolve duplicate files");

    var pathArgument = new Argument<FileInfo>("scan-result-path", "Path to scan result JSON file");
    var dryRunOption = new Option<bool>("--dry-run", () => false, "Preview changes without executing");
    var autoOption = new Option<bool>("--auto", () => false, "Auto-resolve: keep oldest, delete rest");

    command.AddArgument(pathArgument);
    command.AddOption(dryRunOption);
    command.AddOption(autoOption);

    command.SetHandler(async (resultPath, dryRun, auto) =>
    {
      await ExecuteAsync(services, resultPath, dryRun, auto);
    }, pathArgument, dryRunOption, autoOption);

    return command;
  }

  private static async Task ExecuteAsync(
    IServiceProvider services,
    FileInfo resultPath,
    bool dryRun,
    bool auto)
  {
    var logger = services.GetRequiredService<ILogger>();
    var store = services.GetRequiredService<IScanResultStore>();
    var fileOps = services.GetRequiredService<IFileOperationService>();
    var resolutionService = services.GetRequiredService<IResolutionService>();

    if (!resultPath.Exists)
    {
      AnsiConsole.MarkupLine($"[red]Error: File not found: {resultPath.FullName}[/]");
      return;
    }

    var scanResult = await store.LoadAsync(resultPath.FullName);

    if (scanResult.DuplicateGroups.Count == 0)
    {
      AnsiConsole.MarkupLine("[green]No duplicates to resolve.[/]");
      return;
    }

    AnsiConsole.MarkupLine($"[bold]Resolving duplicates from:[/] {resultPath.FullName}");
    AnsiConsole.MarkupLine($"[grey]{scanResult.DuplicateGroups.Count} duplicate groups | {scanResult.DuplicateFileCount} duplicate files[/]");

    if (dryRun)
      AnsiConsole.MarkupLine("[yellow]DRY RUN mode - no files will be modified[/]");

    AnsiConsole.WriteLine();

    var decisions = auto
      ? resolutionService.AutoResolveKeepOldest(scanResult.DuplicateGroups)
      : InteractiveResolver.Resolve(scanResult.DuplicateGroups, resolutionService);

    var deletedCount = 0;
    var movedCount = 0;
    long freedBytes = 0;

    foreach (var decision in decisions)
    {
      foreach (var action in decision.Actions)
      {
        try
        {
          switch (action.Action)
          {
            case ResolveAction.Delete:
              if (!File.Exists(action.FilePath))
              {
                logger.Warning("File not found, skipping delete: {FilePath}", action.FilePath);
                break;
              }
              var fileSize = new FileInfo(action.FilePath).Length;
              await fileOps.DeleteAsync(action.FilePath, dryRun);
              deletedCount++;
              freedBytes += fileSize;
              break;

            case ResolveAction.Move when action.MoveDestination != null:
              if (!File.Exists(action.FilePath))
              {
                logger.Warning("File not found, skipping move: {FilePath}", action.FilePath);
                break;
              }
              await fileOps.MoveAsync(action.FilePath, action.MoveDestination, dryRun);
              movedCount++;
              break;
          }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
          logger.Warning(ex, "Failed to process file: {FilePath}", action.FilePath);
        }
      }
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold]Resolution Summary[/]").RuleStyle("grey"));
    AnsiConsole.MarkupLine($"  Files deleted: [bold]{deletedCount}[/]");
    AnsiConsole.MarkupLine($"  Files moved: [bold]{movedCount}[/]");
    AnsiConsole.MarkupLine($"  Space freed: [bold]{SizeFormatter.Format(freedBytes)}[/]");

    if (dryRun)
      AnsiConsole.MarkupLine("[yellow]  (DRY RUN - no changes were made)[/]");

    logger.Information("Resolution complete: {Deleted} deleted, {Moved} moved, {Freed} bytes freed (dry-run: {DryRun})",
      deletedCount, movedCount, freedBytes, dryRun);
  }
}
