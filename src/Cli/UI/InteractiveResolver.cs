using System.Globalization;
using FileMergeAssistant.Models;
using FileMergeAssistant.Resolution;
using FileMergeAssistant.Utilities;
using Spectre.Console;

namespace FileMergeAssistant.Cli.UI;

public static class InteractiveResolver
{
  public static List<ResolutionDecision> Resolve(IReadOnlyList<DuplicateGroup> groups, IResolutionService resolutionService)
  {
    var decisions = new List<ResolutionDecision>();

    for (var i = 0; i < groups.Count; i++)
    {
      var group = groups[i];
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Rule($"[bold]Duplicate Group {i + 1}/{groups.Count}[/]").RuleStyle("grey"));
      AnsiConsole.MarkupLine($"[grey]Hash: {group.Hash[..Math.Min(16, group.Hash.Length)]}... | {group.Files.Count} files | Wasted: {SizeFormatter.Format(group.WastedBytes)}[/]");
      AnsiConsole.WriteLine();

      var table = new Table()
        .Border(TableBorder.Simple)
        .AddColumn("#")
        .AddColumn("File Path")
        .AddColumn("Size")
        .AddColumn("Created")
        .AddColumn("Modified");

      for (var j = 0; j < group.Files.Count; j++)
      {
        var file = group.Files[j];
        table.AddRow(
          (j + 1).ToString(CultureInfo.InvariantCulture),
          file.FilePath,
          SizeFormatter.Format(file.SizeBytes),
          file.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
          file.ModifiedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
      }

      AnsiConsole.Write(table);

      var action = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
          .Title("What would you like to do?")
          .AddChoices("Keep oldest, delete rest", "Keep newest, delete rest", "Choose which to keep", "Skip this group"));

      var decision = action switch
      {
        "Keep oldest, delete rest" => resolutionService.CreateDecision(group, 0),
        "Keep newest, delete rest" => resolutionService.CreateDecision(group, group.Files.Count - 1),
        "Choose which to keep" => CreateManualDecision(group, resolutionService),
        _ => null
      };

      if (decision != null)
        decisions.Add(decision);
    }

    return decisions;
  }

  private static ResolutionDecision CreateManualDecision(DuplicateGroup group, IResolutionService resolutionService)
  {
    var choices = group.Files.Select((f, i) => $"{i + 1}: {f.FilePath}").ToList();
    var selected = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title("Select file to [green]keep[/]:")
        .AddChoices(choices));

    var keepIndex = int.Parse(selected.Split(':')[0], CultureInfo.InvariantCulture) - 1;
    return resolutionService.CreateDecision(group, keepIndex);
  }
}
