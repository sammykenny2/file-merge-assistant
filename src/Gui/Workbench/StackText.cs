using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>Plain-language descriptions of a stack for the workbench list and the suggested next stack.</summary>
public static class StackText
{
  public static string Kind(Stack stack) =>
    (stack.Kind == StackKind.Folder ? "資料夾複本" : "單檔複本") + $"，{stack.Copies.Count} 份";

  /// <summary>Where the copies are, for example 「開發中、舊專案、WD 2TB 備份（離線）、桌面」.</summary>
  public static string Where(Stack stack) =>
    string.Join("、", stack.Copies
      .GroupBy(c => c.SourceId, StringComparer.Ordinal)
      .Select(g => g.First().SourceName + (g.All(c => c.IsOffline) ? "（離線）" : string.Empty)));

  public static IReadOnlyList<(string Name, bool Offline)> Places(Stack stack) =>
    stack.Copies
      .GroupBy(c => c.SourceId, StringComparer.Ordinal)
      .Select(g => (g.First().SourceName, g.All(c => c.IsOffline)))
      .ToList();

  /// <summary>One line under the title in the list.</summary>
  public static string Detail(StackSummary summary)
  {
    ArgumentNullException.ThrowIfNull(summary);
    var detail = PlainDetail(summary);
    return summary.Stack.ChangedCopyIds.Count > 0 ? $"你決定之後內容有變動，請再確認一次。{detail}"
      : summary.Stack.NewCopyIds.Count > 0 ? $"出現新的複本，請再確認一次。{detail}"
      : detail;
  }

  private static string PlainDetail(StackSummary summary)
  {
    var stack = summary.Stack;
    if (summary.Group == StackGroup.StillComparing)
    {
      return stack.SameLabel ?? $"目前比對了 {Fmt.Count(stack.SameFileCount)} 個檔";
    }
    if (summary.LooksDeliberate)
    {
      return $"{CopiesWord(stack.Copies.Count)}各自改過，可能是刻意分開的版本";
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      return $"{Fmt.Count(summary.OnlyElsewhereFiles)} 個檔只在部分複本"
        + (summary.ConflictFiles > 0 ? $"，{Fmt.Count(summary.ConflictFiles)} 個檔需要你選版本" : string.Empty);
    }
    if (summary.DifferingFiles > 0)
    {
      var regenerable = stack.Regenerable is { } r && r.BytesPerCopy.Count > 0
        ? $"；{Fmt.Size(r.BytesPerCopy.Max())} 是可重建的 {r.Names}"
        : string.Empty;
      return $"{Fmt.Count(summary.DifferingFiles)} 個檔內容不同{regenerable}";
    }
    if (stack.Kind == StackKind.File)
    {
      return $"{stack.Copies.Count} 個檔內容完全相同";
    }
    var keeper = stack.SuggestedKeeperCopy;
    var smaller = stack.Copies.Where(c => c.Id != keeper.Id && c.FileCount < keeper.FileCount).ToList();
    if (smaller.Count > 0)
    {
      return $"{string.Join("、", smaller.Select(c => c.SourceName).Distinct())}的 {Fmt.Count(smaller.Max(c => c.FileCount))} 個檔都在{keeper.SourceName}裡"
        + (stack.Rows.Any(r => r.Description?.Contains("檔名不同", StringComparison.Ordinal) == true) ? "，只是檔名不同" : string.Empty);
    }
    return stack.Copies.Count == 2 ? "兩份完全相同" : $"{stack.Copies.Count} 份完全相同";
  }

  /// <summary>Why the stack is suggested next.</summary>
  public static string Reason(StackSummary summary)
  {
    var stack = summary.Stack;
    if (summary.LooksDeliberate)
    {
      return $"值得看一下：{CopiesWord(stack.Copies.Count)}各自改過，可能要都保留。";
    }
    if (summary.ConflictFiles > 0)
    {
      return $"建議先做：可移入回收區約 {Fmt.Size(summary.ReclaimableBytes)}，只有 {Fmt.Count(summary.ConflictFiles)} 個檔需要你選版本。";
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      return $"建議先做：可移入回收區約 {Fmt.Size(summary.ReclaimableBytes)}，只在部分複本的檔會先合併進正本。";
    }
    if (summary.DifferingFiles > 0)
    {
      return $"可移入回收區約 {Fmt.Size(summary.ReclaimableBytes)}；其他份只有比較舊的版本。";
    }
    return stack.Kind == StackKind.File
      ? $"很快：{stack.Copies.Count} 個檔內容完全相同。"
      : "很快：其中一份已包含其他份的全部內容。";
  }

  /// <summary>The longer explanation under the suggested next stack.</summary>
  public static string Explanation(StackSummary summary)
  {
    var stack = summary.Stack;
    var parts = new List<string>();
    if (stack.SameFileCount > 0)
    {
      parts.Add($"{Fmt.Count(stack.SameFileCount)} 個檔在 {stack.Copies.Count} 份裡都相同。");
    }
    if (summary.OnlyElsewhereFiles > 0)
    {
      parts.Add($"{Fmt.Count(summary.OnlyElsewhereFiles)} 個檔只在部分複本裡，可以合併進正本。");
    }
    if (summary.ConflictFiles > 0)
    {
      var conflicts = new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.Merge)).Conflicts;
      parts.AddRange(conflicts.Select(c => $"{c.Row.RelativePath} 在{c.Source!.SourceName}那份比較新，需要你決定用哪一版。"));
    }
    if (stack.Kind == StackKind.File && summary.DifferingFiles == 0)
    {
      parts.Add($"同一個檔在{Where(stack)}各有一份。");
    }
    parts.AddRange(stack.Copies.Where(c => c.IsOffline).Select(c => $"{c.SourceName}那份離線，這次先不動。"));
    if (stack.Note is not null)
    {
      parts.Add(stack.Note);
    }
    parts.AddRange(stack.RelatedOutside.Select(r => $"{System.IO.Path.GetFileName(r.Path)} 檔名相近但內容不同，不在這疊裡。"));
    return string.Join(string.Empty, parts);
  }

  /// <summary>Why a stack cannot be consolidated yet, or null.</summary>
  public static string? Blocked(Stack stack)
  {
    switch (stack.Block)
    {
      case StackBlock.SourceOffline:
        var offline = stack.Copies.First(c => c.IsOffline);
        return $"{System.IO.Path.GetPathRoot(offline.Path)} 離線，無法確認那份還在，接上後才能整理";
      case StackBlock.StillScanning:
        var scanning = stack.Copies[^1];
        return $"{System.IO.Path.GetPathRoot(scanning.Path)} 還在掃描，結果可能會變；掃完才能整理";
      default:
        return null;
    }
  }

  public static string GroupTitle(StackGroup group) => group switch
  {
    StackGroup.ReadyToConverge => "可以直接收斂",
    StackGroup.NeedsMerge => "需要合併",
    StackGroup.VersionDifferences => "有版本差異",
    _ => "還在比對",
  };

  public static string GroupDescription(StackGroup group) => group switch
  {
    StackGroup.ReadyToConverge => "其中一份已經包含其他份的全部內容",
    StackGroup.NeedsMerge => "每一份都有別份沒有的檔",
    StackGroup.VersionDifferences => "同一個路徑的檔，內容不同",
    _ => "有一份還沒掃完，結果可能會變",
  };

  private static string CopiesWord(int count) => count switch
  {
    2 => "兩份",
    3 => "三份",
    4 => "四份",
    _ => $"{count} 份",
  };
}
