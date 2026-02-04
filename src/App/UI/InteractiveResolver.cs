using System.Globalization;
using FileMergeAssistant.Models;
using Spectre.Console;

namespace FileMergeAssistant.UI;

public static class InteractiveResolver
{
  public static List<ResolutionDecision> Resolve(IReadOnlyList<DuplicateGroup> groups)
  {
    var decisions = new List<ResolutionDecision>();

    for (var i = 0; i < groups.Count; i++)
    {
      var group = groups[i];
      AnsiConsole.WriteLine();
      AnsiConsole.Write(new Rule($"[bold]Duplicate Group {i + 1}/{groups.Count}[/]").RuleStyle("grey"));
      AnsiConsole.MarkupLine($"[grey]Hash: {group.Hash[..Math.Min(16, group.Hash.Length)]}... | {group.Files.Count} files | Wasted: {FormatSize(group.WastedBytes)}[/]");
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
          FormatSize(file.SizeBytes),
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
        "Keep oldest, delete rest" => CreateDecision(group, 0),
        "Keep newest, delete rest" => CreateDecision(group, group.Files.Count - 1),
        "Choose which to keep" => CreateManualDecision(group),
        _ => null
      };

      if (decision != null)
        decisions.Add(decision);
    }

    return decisions;
  }

  public static List<ResolutionDecision> AutoResolve(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g => CreateDecision(g, 0)).ToList();
  }

  private static ResolutionDecision CreateDecision(DuplicateGroup group, int keepIndex)
  {
    var actions = group.Files.Select((file, index) => new FileAction
    {
      FilePath = file.FilePath,
      Action = index == keepIndex ? ResolveAction.Keep : ResolveAction.Delete
    }).ToList();

    return new ResolutionDecision
    {
      Group = group,
      KeptFilePath = group.Files[keepIndex].FilePath,
      Actions = actions
    };
  }

  private static ResolutionDecision CreateManualDecision(DuplicateGroup group)
  {
    var choices = group.Files.Select((f, i) => $"{i + 1}: {f.FilePath}").ToList();
    var selected = AnsiConsole.Prompt(
      new SelectionPrompt<string>()
        .Title("Select file to [green]keep[/]:")
        .AddChoices(choices));

    var keepIndex = int.Parse(selected.Split(':')[0], CultureInfo.InvariantCulture) - 1;
    return CreateDecision(group, keepIndex);
  }

  private static string FormatSize(long bytes)
  {
    string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
    var index = 0;
    var size = (double)bytes;
    while (size >= 1024 && index < suffixes.Length - 1)
    {
      size /= 1024;
      index++;
    }
    return $"{size:F2} {suffixes[index]}";
  }
}
