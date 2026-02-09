namespace FileMergeAssistant.Models;

public record FolderPriority
{
  public required string FolderPath { get; init; }
  public required int Priority { get; init; }
}
