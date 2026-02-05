using System.Globalization;
using FileMergeAssistant.Models;
using FileMergeAssistant.Reporting;
using FileMergeAssistant.Utilities;
using Spectre.Console;

namespace FileMergeAssistant.Cli.Reporting;

public class ConsoleReportGenerator : IReportGenerator
{
  public ReportFormat Format => ReportFormat.Console;

  public Task GenerateAsync(ScanResult result, string? outputPath = null, CancellationToken cancellationToken = default)
  {
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold blue]Scan Summary[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    var summaryTable = new Table()
      .Border(TableBorder.Rounded)
      .AddColumn("Metric")
      .AddColumn("Value");

    summaryTable.AddRow("Scanned Path", result.ScannedPath);
    summaryTable.AddRow("Scan Date", result.ScanDateUtc.ToString("yyyy-MM-dd HH:mm:ss UTC", CultureInfo.InvariantCulture));
    summaryTable.AddRow("Hash Algorithm", result.HashAlgorithm);
    summaryTable.AddRow("Total Files", result.TotalFilesScanned.ToString("N0", CultureInfo.InvariantCulture));
    summaryTable.AddRow("Total Size", SizeFormatter.Format(result.TotalSizeBytes));
    summaryTable.AddRow("Duplicate Groups", result.DuplicateGroups.Count.ToString("N0", CultureInfo.InvariantCulture));
    summaryTable.AddRow("Duplicate Files", result.DuplicateFileCount.ToString("N0", CultureInfo.InvariantCulture));
    summaryTable.AddRow("Wasted Space", SizeFormatter.Format(result.WastedSpaceBytes));

    AnsiConsole.Write(summaryTable);

    if (result.DuplicateGroups.Count == 0)
    {
      AnsiConsole.MarkupLine("[green]No duplicates found![/]");
      return Task.CompletedTask;
    }

    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]Duplicate Groups[/]").RuleStyle("grey"));
    AnsiConsole.WriteLine();

    foreach (var group in result.DuplicateGroups)
    {
      var groupTable = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("File Path")
        .AddColumn("Size")
        .AddColumn("Created")
        .AddColumn("Modified");

      groupTable.Title = new TableTitle(
        $"[bold]Hash: {group.Hash[..Math.Min(16, group.Hash.Length)]}... ({group.Files.Count} files, wasted: {SizeFormatter.Format(group.WastedBytes)})[/]");

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

    return Task.CompletedTask;
  }
}
