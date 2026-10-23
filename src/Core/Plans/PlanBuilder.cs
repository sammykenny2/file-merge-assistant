using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Plans;

/// <summary>Turns a reviewed stack decision into pending-plan items (ADR-002). No file is touched.</summary>
public static class PlanBuilder
{
  private static readonly string[] SecretPatterns = [".env", ".pem", ".key", ".pfx", "id_rsa", "secrets", "credentials"];

  /// <summary>Builds the plan items; returns an empty list for decisions that change no files.</summary>
  /// <exception cref="InvalidOperationException">The decision is blocked (see <see cref="StackReview.Block"/>).</exception>
  public static PlanEntry Build(StackReview review, DateTimeOffset decidedAt)
  {
    ArgumentNullException.ThrowIfNull(review);
    if (review.Block != ReviewBlock.None)
    {
      throw new InvalidOperationException($"Stack '{review.Stack.Id}' cannot be added to the plan: {review.Block}.");
    }

    var items = new List<PlanItem>();
    var stackId = review.Stack.Id;
    var keeper = review.Keeper;
    var number = 0;
    string NextId() => $"{stackId}#{++number}";

    if (review.ChangesFiles)
    {
      if (review.Action == StackAction.Merge)
      {
        foreach (var row in review.OnlyElsewhere)
        {
          var source = row.Source!;
          items.Add(new PlanItem(NextId(), stackId, PlanItemKind.Copy, row.Row.RelativePath,
            Combine(source, row.Row.RelativePath), Combine(keeper, row.Row.RelativePath))
          {
            CopyId = source.Id,
            CopyName = source.SourceName,
            Bytes = row.SourceVersion?.SizeBytes ?? 0,
            FileCount = row.Row.Count,
            LooksLikeSecret = LooksLikeSecret(row.Row.RelativePath),
          });
        }

        foreach (var row in review.Conflicts)
        {
          var source = row.Source!;
          var path = row.Row.RelativePath;
          if (row.Choice == ConflictChoice.Both)
          {
            var newPath = StackReview.SaveAsPath(path, source.Tag);
            items.Add(new PlanItem(NextId(), stackId, PlanItemKind.SaveAs, newPath, Combine(source, path), Combine(keeper, newPath))
            {
              CopyId = source.Id,
              CopyName = source.SourceName,
              Bytes = row.SourceVersion?.SizeBytes ?? 0,
            });
          }
          else if (row.Choice == ConflictChoice.Theirs)
          {
            items.Add(new PlanItem(NextId(), stackId, PlanItemKind.Replace, path, Combine(source, path), Combine(keeper, path))
            {
              CopyId = source.Id,
              CopyName = source.SourceName,
              Bytes = row.SourceVersion?.SizeBytes ?? 0,
            });
          }
        }
      }

      foreach (var copy in review.Recycled)
      {
        items.Add(new PlanItem(NextId(), stackId, PlanItemKind.Recycle, copy.Path, copy.Path, string.Empty)
        {
          CopyId = copy.Id,
          CopyName = copy.SourceName,
          Bytes = copy.TotalBytes,
          FileCount = copy.FileCount,
          IncludesRegenerable = copy.RegenerableBytes > 0 ? review.Stack.Regenerable?.Names : null,
        });
      }
    }

    return new PlanEntry(review.Stack, review.Decision, keeper, items, decidedAt)
    {
      OfflineCopies = review.ChangesFiles ? review.OfflineCopies : [],
      KeptCopies = review.ChangesFiles ? review.KeptOthers : [],
    };
  }

  /// <summary>
  /// The recycle item a copy item protects: if the copy is not made, the file only exists in that folder and would be
  /// recycled with it.
  /// </summary>
  public static PlanItem? ProtectedRecycle(PlanEntry stack, PlanItem item)
  {
    ArgumentNullException.ThrowIfNull(stack);
    ArgumentNullException.ThrowIfNull(item);
    return item.IsCopyLike
      ? stack.Items.FirstOrDefault(i => i.Kind == PlanItemKind.Recycle && i.CopyId == item.CopyId)
      : null;
  }

  /// <summary>True when a disabled copy item leaves its only source to be recycled.</summary>
  public static bool HasDependencyWarning(PlanEntry stack, PlanItem item)
  {
    if (item.Enabled || !item.IsCopyLike)
    {
      return false;
    }
    var recycle = ProtectedRecycle(stack, item);
    return recycle is not null && recycle.WillRun;
  }

  public static bool LooksLikeSecret(string relativePath)
  {
    ArgumentNullException.ThrowIfNull(relativePath);
    var name = relativePath[(relativePath.LastIndexOf('/') + 1)..];
    return SecretPatterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>Full path of a relative path inside a folder copy. Only folder stacks have rows to copy.</summary>
  private static string Combine(StackCopy copy, string relativePath) =>
    Path.Combine(copy.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
