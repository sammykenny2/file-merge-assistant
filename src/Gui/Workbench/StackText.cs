using FileMergeAssistant.Localization;
using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>
/// Plain-language descriptions of a stack for the workbench list, the suggested next stack and the stack screen. Every
/// call reads the catalog (keys <c>stack.*</c>), so callers get the current display language.
/// </summary>
public static class StackText
{
  public static string Kind(Stack stack) => Kind(stack.Kind, stack.Copies.Count);

  /// <summary>「資料夾複本，3 份」: what kind of copies and how many.</summary>
  public static string Kind(StackKind kind, int copies) =>
    AppText.Plural(kind == StackKind.Folder ? "stack.kind.folder" : "stack.kind.file", copies, ("count", copies));

  /// <summary>Where the copies are, for example 「開發中、舊專案、WD 2TB 備份（離線）、桌面」.</summary>
  public static string Where(Stack stack) =>
    JoinList(stack.Copies
      .GroupBy(c => c.SourceId, StringComparer.Ordinal)
      .Select(g => PlaceName(g.First().SourceName, g.All(c => c.IsOffline))));

  public static IReadOnlyList<(string Name, bool Offline)> Places(Stack stack) =>
    stack.Copies
      .GroupBy(c => c.SourceId, StringComparer.Ordinal)
      .Select(g => (g.First().SourceName, g.All(c => c.IsOffline)))
      .ToList();

  /// <summary>Whether a row's description is the demo data's note on renamed files, in either language.</summary>
  private static bool IsRenamedRow(string? description) =>
    description is not null && AppText.Locales.Any(locale =>
    {
      var text = AppText.Format(locale, "demo.stack.renamedRow");
      var fixedPart = text.IndexOf('{', StringComparison.Ordinal) is var cut and > 0 ? text[..cut] : text;
      return description.StartsWith(fixedPart, StringComparison.Ordinal);
    });

  /// <summary>A source's name, marked when its copies are offline: 「WD 2TB 備份（離線）」.</summary>
  public static string PlaceName(string name, bool offline) =>
    offline ? AppText.Get("stack.place.offline", ("name", name)) : name;

  /// <summary>Items as a list in the display language: 「a、b」 or "a, b".</summary>
  public static string JoinList(IEnumerable<string> items) => string.Join(AppText.Get("stack.list.separator"), items);

  /// <summary>Whole sentences one after the other: Chinese runs them together, English puts a space between them.</summary>
  public static string JoinSentences(IEnumerable<string> sentences) =>
    sentences.Aggregate(string.Empty, (text, next) => text.Length == 0 ? next : AppText.Get("stack.sentences", ("first", text), ("next", next)));

  /// <summary>「3 個檔」: a file count to put inside a longer sentence.</summary>
  public static string Files(long count) => AppText.Plural("stack.files", count, ("count", Fmt.Count(count)));

  /// <summary>One line under the title in the list.</summary>
  public static string Detail(StackSummary summary)
  {
    ArgumentNullException.ThrowIfNull(summary);
    var detail = PlainDetail(summary);
    return summary.Stack.ChangedCopyIds.Count > 0 ? AppText.Get("stack.detail.changed", ("detail", detail))
      : summary.Stack.NewCopyIds.Count > 0 ? AppText.Get("stack.detail.newCopies", ("detail", detail))
      : detail;
  }

  private static string PlainDetail(StackSummary summary)
  {
    var stack = summary.Stack;
    if (summary.Group == StackGroup.StillComparing)
    {
      return stack.SameLabel ?? AppText.Plural("stack.detail.compared", stack.SameFileCount, ("count", Fmt.Count(stack.SameFileCount)));
    }
    if (summary.LooksDeliberate)
    {
      return ByCopies("stack.detail.deliberate", stack.Copies.Count);
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      return summary.ConflictFiles > 0
        ? AppText.Plural("stack.detail.onlySomeConflicts", summary.ConflictFiles,
          ("files", Files(summary.OnlyElsewhereFiles)), ("count", Fmt.Count(summary.ConflictFiles)))
        : AppText.Plural("stack.detail.onlySome", summary.OnlyElsewhereFiles, ("count", Fmt.Count(summary.OnlyElsewhereFiles)));
    }
    if (summary.DifferingFiles > 0)
    {
      return stack.Regenerable is { } r && r.BytesPerCopy.Count > 0
        ? AppText.Plural("stack.detail.differRegenerable", summary.DifferingFiles,
          ("count", Fmt.Count(summary.DifferingFiles)), ("size", Fmt.Size(r.BytesPerCopy.Max())), ("names", r.Names))
        : AppText.Plural("stack.detail.differ", summary.DifferingFiles, ("count", Fmt.Count(summary.DifferingFiles)));
    }
    if (stack.Kind == StackKind.File)
    {
      return AppText.Plural("stack.detail.identicalFiles", stack.Copies.Count, ("count", stack.Copies.Count));
    }
    var keeper = stack.SuggestedKeeperCopy;
    var smaller = stack.Copies.Where(c => c.Id != keeper.Id && c.FileCount < keeper.FileCount).ToList();
    if (smaller.Count > 0)
    {
      // Only the demo data describes renamed files on a row (demo.stack.renamedRow), in the language it was made in.
      var renamed = stack.Rows.Any(r => IsRenamedRow(r.Description));
      var files = smaller.Max(c => c.FileCount);
      return AppText.Plural(renamed ? "stack.detail.containedRenamed" : "stack.detail.contained", files,
        ("sources", JoinList(smaller.Select(c => c.SourceName).Distinct())), ("count", Fmt.Count(files)), ("keeper", keeper.SourceName));
    }
    return stack.Copies.Count == 2
      ? AppText.Get("stack.detail.identicalTwo")
      : AppText.Get("stack.detail.identicalMany", ("count", stack.Copies.Count));
  }

  /// <summary>Why the stack is suggested next.</summary>
  public static string Reason(StackSummary summary)
  {
    var stack = summary.Stack;
    if (summary.LooksDeliberate)
    {
      return ByCopies("stack.reason.deliberate", stack.Copies.Count);
    }
    if (summary.ConflictFiles > 0)
    {
      return AppText.Plural("stack.reason.conflicts", summary.ConflictFiles,
        ("size", Fmt.Size(summary.ReclaimableBytes)), ("count", Fmt.Count(summary.ConflictFiles)));
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      return AppText.Get("stack.reason.merge", ("size", Fmt.Size(summary.ReclaimableBytes)));
    }
    if (summary.DifferingFiles > 0)
    {
      return AppText.Get("stack.reason.older", ("size", Fmt.Size(summary.ReclaimableBytes)));
    }
    return stack.Kind == StackKind.File
      ? AppText.Plural("stack.reason.quickFiles", stack.Copies.Count, ("count", stack.Copies.Count))
      : AppText.Get("stack.reason.quickContained");
  }

  /// <summary>The longer explanation under the suggested next stack.</summary>
  public static string Explanation(StackSummary summary)
  {
    var stack = summary.Stack;
    var parts = new List<string>();
    if (stack.SameFileCount > 0)
    {
      parts.Add(AppText.Plural("stack.explain.same", stack.SameFileCount,
        ("count", Fmt.Count(stack.SameFileCount)), ("copies", stack.Copies.Count)));
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      parts.Add(AppText.Plural("stack.explain.onlySome", summary.OnlyElsewhereFiles, ("count", Fmt.Count(summary.OnlyElsewhereFiles))));
    }
    if (summary.ConflictFiles > 0)
    {
      var conflicts = new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.Merge)).Conflicts;
      parts.AddRange(conflicts.Select(c => AppText.Get("stack.explain.conflict", ("path", c.Row.RelativePath), ("source", c.Source!.SourceName))));
    }
    if (stack.Kind == StackKind.File && summary.DifferingFiles == 0)
    {
      parts.Add(AppText.Get("stack.explain.fileWhere", ("where", Where(stack))));
    }
    parts.AddRange(stack.Copies.Where(c => c.IsOffline).Select(c => AppText.Get("stack.explain.offline", ("source", c.SourceName))));
    if (stack.Note is not null)
    {
      parts.Add(stack.Note);
    }
    parts.AddRange(stack.RelatedOutside.Select(r => AppText.Get("stack.explain.related", ("name", System.IO.Path.GetFileName(r.Path)))));
    return JoinSentences(parts);
  }

  /// <summary>Why a stack cannot be consolidated yet, or null.</summary>
  public static string? Blocked(Stack stack)
  {
    switch (stack.Block)
    {
      case StackBlock.SourceOffline:
        var offline = stack.Copies.First(c => c.IsOffline);
        return AppText.Get("stack.blocked.offline", ("drive", System.IO.Path.GetPathRoot(offline.Path) ?? string.Empty));
      case StackBlock.StillScanning:
        var scanning = stack.Copies[^1];
        return AppText.Get("stack.blocked.scanning", ("drive", System.IO.Path.GetPathRoot(scanning.Path) ?? string.Empty));
      default:
        return null;
    }
  }

  public static string GroupTitle(StackGroup group) => group switch
  {
    StackGroup.ReadyToConverge => AppText.Get("stack.group.ready"),
    StackGroup.NeedsMerge => AppText.Get("stack.group.needsMerge"),
    StackGroup.VersionDifferences => AppText.Get("stack.group.versions"),
    _ => AppText.Get("stack.group.comparing"),
  };

  public static string GroupDescription(StackGroup group) => group switch
  {
    StackGroup.ReadyToConverge => AppText.Get("stack.group.readyDescription"),
    StackGroup.NeedsMerge => AppText.Get("stack.group.needsMergeDescription"),
    StackGroup.VersionDifferences => AppText.Get("stack.group.versionsDescription"),
    _ => AppText.Get("stack.group.comparingDescription"),
  };

  /// <summary>A sentence about all the copies: Chinese says 「兩份」「三份」「四份」 in words, then 「5 份」.</summary>
  private static string ByCopies(string key, int count) => count switch
  {
    2 => AppText.Get(key + ".two"),
    3 => AppText.Get(key + ".three"),
    4 => AppText.Get(key + ".four"),
    _ => AppText.Get(key + ".many", ("count", count)),
  };
}
