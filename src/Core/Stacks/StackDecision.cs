namespace FileWorkbench.Stacks;

/// <summary>What to do with a stack (ADR-002).</summary>
public enum StackAction
{
  /// <summary>Copy what only the other copies have into the keeper, then recycle the other copies.</summary>
  Merge,

  /// <summary>Recycle the other copies as they are.</summary>
  KeepKeeperOnly,

  /// <summary>The copies are deliberate versions; change nothing.</summary>
  KeepAll,

  /// <summary>Put the stack back in the list; change nothing.</summary>
  Later,
}

/// <summary>How to resolve a path whose content differs and where another copy is newer (ADR-002). There is no default.</summary>
public enum ConflictChoice
{
  None,

  /// <summary>Keep the keeper's version; the newer one is recycled with its copy.</summary>
  Keeper,

  /// <summary>Replace the keeper's version with the newer one; not allowed when the keeper is protected (ADR-003).</summary>
  Theirs,

  /// <summary>Keep both: save the newer one next to the keeper's as <c>name.from-tag.ext</c>.</summary>
  Both,
}

/// <summary>
/// The stack a decision was made for is no longer in the index in that form, for example because scanning found another
/// copy. The user has to look at the stack again.
/// </summary>
public sealed class StackChangedException : InvalidOperationException
{
  public StackChangedException()
  {
  }

  public StackChangedException(string stackId)
    : base($"Stack '{stackId}' changed after it was shown; review it again.")
  {
    StackId = stackId;
  }

  public StackChangedException(string message, Exception innerException)
    : base(message, innerException)
  {
  }

  public string? StackId { get; }
}

/// <summary>The user's decision for one stack. Copy ids refer to <see cref="Stack.Copies"/>.</summary>
public sealed record StackDecision(string StackId, string KeeperCopyId, StackAction Action)
{
  /// <summary>Non-keeper copies the user chose to keep as well.</summary>
  public IReadOnlySet<string> AlsoKeep { get; init; } = new HashSet<string>(StringComparer.Ordinal);

  /// <summary>Copies the user said do not belong to this stack (ADR-006).</summary>
  public IReadOnlySet<string> Removed { get; init; } = new HashSet<string>(StringComparer.Ordinal);

  /// <summary>Choices for conflicting paths, by relative path.</summary>
  public IReadOnlyDictionary<string, ConflictChoice> Choices { get; init; } = new Dictionary<string, ConflictChoice>(StringComparer.Ordinal);

  /// <summary>True when the decision was added in bulk from the suggestion rather than reviewed one by one.</summary>
  public bool FromSuggestion { get; init; }

  public static StackDecision Suggested(Stack stack) =>
    new(stack.Id, stack.SuggestedKeeperCopy.Id, stack.SuggestedAction);

  public ConflictChoice ChoiceFor(string relativePath) =>
    Choices.TryGetValue(relativePath, out var choice) ? choice : ConflictChoice.None;

  public StackDecision WithKeeper(string copyId) => this with { KeeperCopyId = copyId };

  public StackDecision WithAction(StackAction action) => this with { Action = action };

  public StackDecision WithAlsoKeep(string copyId, bool keep)
  {
    var set = new HashSet<string>(AlsoKeep, StringComparer.Ordinal);
    if (keep)
    {
      set.Add(copyId);
    }
    else
    {
      set.Remove(copyId);
    }
    return this with { AlsoKeep = set };
  }

  public StackDecision WithRemoved(string copyId)
  {
    var set = new HashSet<string>(Removed, StringComparer.Ordinal) { copyId };
    return this with { Removed = set };
  }

  public StackDecision WithoutRemoved() => this with { Removed = new HashSet<string>(StringComparer.Ordinal) };

  public StackDecision WithChoice(string relativePath, ConflictChoice choice)
  {
    var map = new Dictionary<string, ConflictChoice>(Choices, StringComparer.Ordinal) { [relativePath] = choice };
    return this with { Choices = map };
  }
}
