namespace FileMergeAssistant.Models;

public record ResolutionDecision
{
  public required DuplicateGroup Group { get; init; }
  public required string KeptFilePath { get; init; }
  public required IReadOnlyList<FileAction> Actions { get; init; }
}

public record FileAction
{
  public required string FilePath { get; init; }
  public required ResolveAction Action { get; init; }
  public string? MoveDestination { get; init; }
}
