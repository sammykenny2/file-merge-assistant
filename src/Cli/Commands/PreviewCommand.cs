using System.CommandLine;
using System.Globalization;
using FileMergeAssistant.Models;
using FileMergeAssistant.Services;
using FileMergeAssistant.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace FileMergeAssistant.Cli.Commands;

public static class PreviewCommand
{
  public static Command Create(IServiceProvider services)
  {
    var command = new Command("preview", "Preview statistics before resolving duplicates");

    var pathArgument = new Argument<FileInfo>("scan-result-path", "Path to scan result JSON file");
    var topOption = new Option<int>("--top", () => 10, "Number of top duplicate groups to display");

    command.AddArgument(pathArgument);
    command.AddOption(topOption);

    command.SetHandler(async (resultPath, top) =>
    {
      await ExecuteAsync(services, resultPath, top);
    }, pathArgument, topOption);

    return command;
  }

  private static async Task ExecuteAsync(
    IServiceProvider services,
    FileInfo resultPath,
    int top)
  {
    var store = services.GetRequiredService<IScanResultStore>();
    var previewService = services.GetRequiredService<IPreviewStatisticsService>();

    if (!resultPath.Exists)
    {
      AnsiConsole.MarkupLine($"[red]Error: File not found: {resultPath.FullName}[/]");
      return;
    }

    var scanResult = await store.LoadAsync(resultPath.FullName);
    var stats = previewService.Generate(scanResult, topGroupsCount: top);

    RenderOverview(stats);
    RenderSavingsSummary(stats);

    if (stats.ByExtension.Count > 0)
      RenderExtensionBreakdown(stats);

    if (stats.ByDirectory.Count > 0)
      RenderDirectoryBreakdown(stats);

    if (stats.TopGroups.Count > 0)
      RenderTopGroups(stats);

    RenderActionHint(resultPath);
  }

  private static void RenderOverview(PreviewStatistics stats)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Preview Statistics[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn("Metric")
      .AddColumn("Value");

    table.AddRow("Total files scanned", stats.TotalFilesScanned.ToString("N0", CultureInfo.InvariantCulture));
    table.AddRow("Total size", SizeFormatter.Format(stats.TotalSizeBytes));
    table.AddRow("Duplicate groups", stats.DuplicateGroupCount.ToString("N0", CultureInfo.InvariantCulture));
    table.AddRow("Duplicate files", stats.DuplicateFileCount.ToString("N0", CultureInfo.InvariantCulture));

    AnsiConsole.Write(table);
  }

  private static void RenderSavingsSummary(PreviewStatistics stats)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold green]Expected Savings[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn("Metric")
      .AddColumn("Value");

    table.AddRow("Files to remove", $"[bold]{stats.FilesToRemove.ToString("N0", CultureInfo.InvariantCulture)}[/]");
    table.AddRow("Unique files to keep", stats.UniqueFilesToKeep.ToString("N0", CultureInfo.InvariantCulture));
    table.AddRow("[bold]Space to recover[/]", $"[bold green]{SizeFormatter.Format(stats.WastedBytes)}[/]");
    table.AddRow("Size after cleanup", SizeFormatter.Format(stats.SizeAfterCleanup));
    table.AddRow("Savings", $"[bold]{stats.WastedPercentage:F1}%[/]");

    AnsiConsole.Write(table);
  }

  private static void RenderExtensionBreakdown(PreviewStatistics stats)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]By Extension[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn("Extension")
      .AddColumn(new TableColumn("Files").RightAligned())
      .AddColumn(new TableColumn("Groups").RightAligned())
      .AddColumn(new TableColumn("Total Size").RightAligned())
      .AddColumn(new TableColumn("Wasted").RightAligned());

    foreach (var ext in stats.ByExtension)
    {
      table.AddRow(
        ext.Extension,
        ext.FileCount.ToString("N0", CultureInfo.InvariantCulture),
        ext.DuplicateGroupCount.ToString("N0", CultureInfo.InvariantCulture),
        SizeFormatter.Format(ext.TotalBytes),
        $"[red]{SizeFormatter.Format(ext.WastedBytes)}[/]");
    }

    AnsiConsole.Write(table);
  }

  private static void RenderDirectoryBreakdown(PreviewStatistics stats)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]Top Directories with Duplicates[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var table = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn("Directory")
      .AddColumn(new TableColumn("Dup. Files").RightAligned())
      .AddColumn(new TableColumn("Groups").RightAligned())
      .AddColumn(new TableColumn("Wasted").RightAligned());

    foreach (var dir in stats.ByDirectory)
    {
      table.AddRow(
        dir.Directory,
        dir.DuplicateFileCount.ToString("N0", CultureInfo.InvariantCulture),
        dir.DuplicateGroupCount.ToString("N0", CultureInfo.InvariantCulture),
        $"[red]{SizeFormatter.Format(dir.WastedBytes)}[/]");
    }

    AnsiConsole.Write(table);
  }

  private static void RenderTopGroups(PreviewStatistics stats)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]Top Duplicate Groups by Wasted Space[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    foreach (var group in stats.TopGroups)
    {
      var groupTable = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("File Path")
        .AddColumn(new TableColumn("Size").RightAligned())
        .AddColumn("Created")
        .AddColumn("Modified");

      groupTable.Title = new TableTitle(
        $"[bold]Hash: {group.Hash[..Math.Min(16, group.Hash.Length)]}... " +
        $"({group.Files.Count} files, wasted: [red]{SizeFormatter.Format(group.WastedBytes)}[/])[/]");

      foreach (var file in group.Files)
      {
        groupTable.AddRow(
          file.FilePath,
          SizeFormatter.Format(file.SizeBytes),
          file.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
          file.ModifiedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
      }

      AnsiConsole.Write(groupTable);
      AnsiConsole.WriteLine();
    }
  }

  private static void RenderActionHint(FileInfo resultPath)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule().RuleStyle("grey"));
    AnsiConsole.MarkupLine("[grey]To resolve duplicates, run:[/]");
    AnsiConsole.MarkupLine($"[bold]  fma resolve \"{resultPath.FullName}\"[/]");
    AnsiConsole.MarkupLine("[grey]  Add --dry-run to simulate without making changes.[/]");
    AnsiConsole.WriteLine();
  }
}
