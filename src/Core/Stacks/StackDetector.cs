using System.Globalization;
using System.IO.Hashing;
using System.Text;
using System.Text.RegularExpressions;

namespace FileMergeAssistant.Stacks;

/// <summary>A file as the index knows it. <see cref="RelativePath"/> uses backslashes and is relative to the source root.</summary>
public sealed record IndexedFile(long Id, string SourceId, string RelativePath, long Size, long ModifiedTicks, string? Hash, bool CloudOnly);

/// <summary>A folder recorded as a whole: a regenerable folder (size only) or a code project root.</summary>
public sealed record IndexedFolder(string SourceId, string RelativePath, string Name, long Bytes, long Files);

public sealed record DetectionSource(string Id, string Name, string Root, bool IsProtected, bool IsOffline, bool IsScanning)
{
  public DateTimeOffset? IndexedAt { get; init; }
}

public sealed record DetectionInput(
  IReadOnlyList<DetectionSource> Sources,
  IReadOnlyList<IndexedFile> Files,
  IReadOnlyList<IndexedFolder> RegenerableFolders,
  IReadOnlyList<IndexedFolder> ProjectFolders);

public sealed record DetectionOptions
{
  /// <summary>Share of the larger folder's files that must match by relative path and content.</summary>
  public double FolderThreshold { get; init; } = 0.5;

  /// <summary>A folder pair needs at least this many matching files; one matching file is a file stack, not a folder.</summary>
  public int MinimumMatchedFiles { get; init; } = 2;

  /// <summary>
  /// Share of the smaller folder's files that must match for it to count as a subset copy of the larger one (for example
  /// 48 picked photos out of 436), even when the larger folder is mostly different.
  /// </summary>
  public double SubsetThreshold { get; init; } = 0.9;

  /// <summary>A subset copy needs at least this many matching files.</summary>
  public int MinimumSubsetFiles { get; init; } = 5;

  /// <summary>Content groups larger than this do not vote for folder pairs (they are boilerplate, not copies).</summary>
  public int MaxGroupForPairs { get; init; } = 50;

  /// <summary>How many folder levels above a file a pair may be.</summary>
  public int MaxLevels { get; init; } = 12;
}

/// <summary>
/// Finds stacks in the index (ADR-001). Folder stacks: every pair of files with the same content votes for the folder
/// pairs under which their relative paths match. A pair becomes a candidate when the matching files make up at least
/// <see cref="DetectionOptions.FolderThreshold"/> of the larger folder, or nearly all of the smaller one (a subset copy).
/// Candidates are taken best share first and, on a tie, outermost first; a pair nested in or containing a taken one is
/// dropped, and so is a pair whose folder only wraps a single subfolder. Pairs that share a folder join into one stack.
/// File stacks: files with the same content that are not in
/// a folder stack. Files inside code projects never form file stacks, because identical boilerplate (LICENSE, config)
/// in two projects is not a copy to consolidate.
/// </summary>
public static partial class StackDetector
{
  public static IReadOnlyList<Stack> Detect(DetectionInput input, DetectionOptions? options = null)
  {
    ArgumentNullException.ThrowIfNull(input);
    options ??= new DetectionOptions();
    var context = new Context(input);

    var folderGroups = FindFolderGroups(context, options);
    var stacks = new List<Stack>();
    var covered = new HashSet<long>();
    foreach (var group in folderGroups)
    {
      var stack = BuildFolderStack(context, group, covered);
      if (stack is not null)
      {
        stacks.Add(stack);
      }
    }
    stacks.AddRange(BuildFileStacks(context, covered));
    return stacks;
  }

  private static List<FolderGroup> FindFolderGroups(Context context, DetectionOptions options)
  {
    // Votes per folder pair: the number of relative paths whose content matches under both folders.
    var votes = new Dictionary<(string A, string B), int>();
    foreach (var group in context.Duplicates)
    {
      if (group.Count > options.MaxGroupForPairs)
      {
        continue;
      }
      for (var i = 0; i < group.Count; i++)
      {
        for (var j = i + 1; j < group.Count; j++)
        {
          Vote(context, group[i], group[j], options.MaxLevels, votes);
        }
      }
    }

    var candidates = new List<(string A, string B, int Votes, double Score, int Depth, bool Strong)>();
    foreach (var ((a, b), count) in votes)
    {
      if (count < options.MinimumMatchedFiles || context.IsWrapper(a) || context.IsWrapper(b) || IsContainer(context, votes, a, b, count))
      {
        continue;
      }
      var larger = Math.Max(context.FileCount(a), context.FileCount(b));
      var smaller = Math.Min(context.FileCount(a), context.FileCount(b));
      var score = larger == 0 ? 0 : Math.Min(1.0, (double)count / larger);
      var subset = smaller == 0 ? 0 : Math.Min(1.0, (double)count / smaller);
      var strong = score >= options.FolderThreshold;
      if (strong || (subset >= options.SubsetThreshold && count >= options.MinimumSubsetFiles))
      {
        candidates.Add((a, b, count, score, Depth(a) + Depth(b), strong));
      }
    }

    var accepted = new List<string>();
    var parent = new Dictionary<string, string>(StringComparer.Ordinal);
    var strength = new Dictionary<string, int>(StringComparer.Ordinal);
    string Find(string x)
    {
      while (parent[x] != x)
      {
        x = parent[x] = parent[parent[x]];
      }
      return x;
    }

    // Pairs where most of the larger folder matches come first, outermost first, so a project folder with a few
    // differing files wins over its identical src subfolder. Subset-only pairs come after and never swallow them.
    var ordered = candidates
      .OrderByDescending(c => c.Strong)
      .ThenBy(c => c.Depth)
      .ThenByDescending(c => c.Score)
      .ThenByDescending(c => c.Votes);
    foreach (var (a, b, count, _, _, _) in ordered)
    {
      if (accepted.Any(r => IsInside(a, r) || IsInside(b, r) || IsInside(r, a) || IsInside(r, b)))
      {
        continue;
      }
      foreach (var root in new[] { a, b })
      {
        if (!parent.ContainsKey(root))
        {
          parent[root] = root;
          accepted.Add(root);
        }
      }
      var ra = Find(a);
      var rb = Find(b);
      if (ra != rb)
      {
        parent[ra] = rb;
      }
      strength[a + "\0" + b] = count;
    }

    return accepted
      .GroupBy(Find, StringComparer.Ordinal)
      .Select(g =>
      {
        var members = g.ToList();
        var matched = strength.Where(kv => members.Contains(kv.Key.Split('\0')[0], StringComparer.Ordinal)).Select(kv => kv.Value).DefaultIfEmpty(0).Max();
        return new FolderGroup(members, matched);
      })
      .Where(g => g.Members.Count >= 2)
      .ToList();
  }

  /// <summary>
  /// True when a folder pair only matches because one same-named subfolder pair does, and that subfolder is the real
  /// unit: it is a code project, or the rest of a folder is mostly other subfolders (a folder of projects whose names
  /// happen to line up, such as <c>C:\src</c> and <c>E:\備份\2023</c> both holding <c>invoice-tool</c>).
  /// </summary>
  private static bool IsContainer(Context context, Dictionary<(string A, string B), int> votes, string a, string b, int count)
  {
    var best = 0;
    string? childA = null;
    string? childB = null;
    foreach (var child in context.ChildrenOf(a))
    {
      var other = b + child[a.Length..];
      var key = string.CompareOrdinal(child, other) < 0 ? (child, other) : (other, child);
      if (votes.TryGetValue(key, out var childVotes) && childVotes > best)
      {
        (best, childA, childB) = (childVotes, child, other);
      }
    }
    if (childA is null || best < 0.9 * count)
    {
      return false;
    }
    if (context.IsProjectRoot(childA) || context.IsProjectRoot(childB!))
    {
      return true;
    }
    double Foreign(string folder, string child)
    {
      var total = context.FileCount(folder);
      return total == 0 ? 0 : (double)(total - context.FileCount(child) - context.DirectFileCount(folder)) / total;
    }
    return Foreign(a, childA) >= 0.5 || Foreign(b, childB!) >= 0.5;
  }

  private static void Vote(Context context, IndexedFile a, IndexedFile b, int maxLevels, Dictionary<(string A, string B), int> votes)
  {
    var partsA = a.RelativePath.Split('\\');
    var partsB = b.RelativePath.Split('\\');
    var common = 0;
    while (common < partsA.Length && common < partsB.Length
      && string.Equals(partsA[^(common + 1)], partsB[^(common + 1)], StringComparison.OrdinalIgnoreCase))
    {
      common++;
    }
    // A folder copy never is a whole source: the pair stays strictly below both source roots.
    var levels = Math.Min(Math.Min(common, maxLevels), Math.Min(partsA.Length - 1, partsB.Length - 1));
    for (var level = 1; level <= levels; level++)
    {
      var folderA = context.Canonical(a.SourceId, string.Join('\\', partsA, 0, partsA.Length - level));
      var folderB = context.Canonical(b.SourceId, string.Join('\\', partsB, 0, partsB.Length - level));
      if (folderA == folderB || IsInside(folderA, folderB) || IsInside(folderB, folderA))
      {
        continue;
      }
      var key = string.CompareOrdinal(folderA, folderB) < 0 ? (folderA, folderB) : (folderB, folderA);
      votes[key] = votes.GetValueOrDefault(key) + 1;
    }
  }

  private static Stack? BuildFolderStack(Context context, FolderGroup group, HashSet<long> covered)
  {
    var members = group.Members
      .Select(canonical => context.Folders[canonical])
      .OrderBy(m => context.SourceOrder(m.SourceId))
      .ThenBy(m => m.RelativePath, StringComparer.OrdinalIgnoreCase)
      .ToList();

    var contents = members.Select(m => new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase)).ToList();
    var regenerable = new long[members.Count];
    var regenerableNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < members.Count; i++)
    {
      var member = members[i];
      var prefix = member.RelativePath + "\\";
      foreach (var file in context.FilesUnder(member.SourceId, prefix))
      {
        contents[i][file.RelativePath[prefix.Length..].Replace('\\', '/')] = file;
        covered.Add(file.Id);
      }
      foreach (var folder in context.Input.RegenerableFolders)
      {
        if (folder.SourceId == member.SourceId && folder.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
          regenerable[i] += folder.Bytes;
          regenerableNames.Add(folder.Name);
        }
      }
    }

    var paths = contents.SelectMany(c => c.Keys).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal).ToList();
    var rows = new List<StackRow>();
    long same = 0;
    foreach (var path in paths)
    {
      var cells = contents.Select(c => c.TryGetValue(path, out var f) ? Version(f) : null).ToList();
      if (cells.All(c => c is not null) && cells.Select(c => c!.ContentId).Distinct(StringComparer.Ordinal).Count() == 1)
      {
        same++;
        continue;
      }
      rows.Add(new StackRow(path, cells));
    }

    var tags = UniqueTags(members.Select(m => TagFor(context.FullPath(m.SourceId, m.RelativePath))).ToList());
    var copies = members.Select((m, i) =>
    {
      var source = context.Sources[m.SourceId];
      var files = contents[i].Values;
      return new StackCopy(
        $"{m.SourceId}:{m.RelativePath}",
        m.SourceId,
        source.Name,
        context.FullPath(m.SourceId, m.RelativePath),
        tags[i],
        source.IsProtected,
        source.IsOffline,
        files.Sum(f => f.Size),
        regenerable[i],
        files.Count,
        files.Count == 0 ? null : Local(files.Max(f => f.ModifiedTicks)))
      {
        IndexedAt = source.IsOffline ? source.IndexedAt : null,
        Notes = context.IsProject(m.SourceId, m.RelativePath) ? ["程式碼專案"] : [],
        Digest = CopyDigest.Of(contents[i].Select(kv => (kv.Key, kv.Value.Size, kv.Value.ModifiedTicks))),
      };
    }).ToList();

    var (keeper, reason) = SuggestKeeper(copies, c => Path.GetFileName(c.Path));
    if (keeper < 0)
    {
      return null;
    }
    copies[keeper] = copies[keeper] with { SuggestReason = reason };

    var stack = new Stack(StackId("d", copies), Path.GetFileName(copies[keeper].Path), StackKind.Folder, copies, rows, keeper, StackAction.KeepKeeperOnly)
    {
      SameFileCount = same,
      Regenerable = regenerableNames.Count == 0 ? null : new RegenerableSummary(string.Join("、", regenerableNames), regenerable),
      Block = BlockOf(context, copies),
      Evidence = $"比對方式：{group.Matched.ToString("N0", CultureInfo.InvariantCulture)} 個檔的相對路徑與內容指紋都相同，才判定為同一份資料夾。"
        + (regenerableNames.Count > 0 ? "可重建的資料夾只比大小，不逐檔比對。" : string.Empty),
      UpdatedAt = copies.Max(c => c.LastModified) ?? DateTimeOffset.MinValue,
    };
    return stack with { SuggestedAction = SuggestAction(stack) };
  }

  private static IEnumerable<Stack> BuildFileStacks(Context context, HashSet<long> covered)
  {
    foreach (var group in context.Duplicates)
    {
      var files = group
        .Where(f => !covered.Contains(f.Id) && !context.IsInProject(f))
        .OrderBy(f => context.SourceOrder(f.SourceId))
        .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
        .ToList();
      if (files.Count < 2)
      {
        continue;
      }
      var tags = UniqueTags(files.Select(f => TagFor(context.FullPath(f.SourceId, f.RelativePath))).ToList());
      var copies = files.Select((f, i) =>
      {
        var source = context.Sources[f.SourceId];
        return new StackCopy($"{f.SourceId}:{f.RelativePath}", f.SourceId, source.Name, context.FullPath(f.SourceId, f.RelativePath), tags[i],
          source.IsProtected, source.IsOffline, f.Size, 0, 1, Local(f.ModifiedTicks))
        {
          IndexedAt = source.IsOffline ? source.IndexedAt : null,
          Digest = CopyDigest.Of([(Path.GetFileName(f.RelativePath), f.Size, f.ModifiedTicks)]),
        };
      }).ToList();
      var (keeper, reason) = SuggestKeeper(copies, c => Path.GetFileNameWithoutExtension(c.Path));
      if (keeper < 0)
      {
        continue;
      }
      copies[keeper] = copies[keeper] with { SuggestReason = reason };
      yield return new Stack(StackId("f", copies), Path.GetFileName(copies[keeper].Path), StackKind.File, copies, [], keeper, StackAction.KeepKeeperOnly)
      {
        SameLabel = "檔案內容",
        Block = BlockOf(context, copies),
        Evidence = $"比對方式：內容指紋相同（每份 {SizeText(files[0].Size)}）。檔名不同不影響判定。",
        UpdatedAt = copies.Max(c => c.LastModified) ?? DateTimeOffset.MinValue,
      };
    }
  }

  /// <summary>
  /// The suggested keeper: never an offline copy; protected first, then names without backup or copy markers, then the
  /// most complete, then the most recently changed.
  /// </summary>
  private static (int Index, string Reason) SuggestKeeper(List<StackCopy> copies, Func<StackCopy, string> name)
  {
    var online = copies.Select((c, i) => (Copy: c, Index: i)).Where(x => !x.Copy.IsOffline).ToList();
    if (online.Count == 0)
    {
      return (-1, string.Empty);
    }
    var best = online
      .OrderByDescending(x => x.Copy.IsProtected)
      .ThenBy(x => LooksLikeCopy(name(x.Copy)))
      .ThenByDescending(x => x.Copy.FileCount)
      .ThenByDescending(x => x.Copy.LastModified)
      .ThenBy(x => x.Copy.Path.Length)
      .First();

    var reasons = new List<string>();
    if (best.Copy.IsProtected)
    {
      reasons.Add("在受保護的位置");
    }
    if (!LooksLikeCopy(name(best.Copy)) && copies.Any(c => LooksLikeCopy(name(c))))
    {
      reasons.Add("名稱沒有備份或複製的字樣");
    }
    if (copies.Count(c => c.FileCount == best.Copy.FileCount) == 1 && copies.All(c => c.FileCount <= best.Copy.FileCount))
    {
      reasons.Add("檔案最完整");
    }
    if (copies.All(c => c.LastModified <= best.Copy.LastModified) && copies.Any(c => c.LastModified < best.Copy.LastModified))
    {
      reasons.Add("最近修改");
    }
    if (reasons.Count == 0)
    {
      reasons.Add("排在第一個來源，內容和其他份相同");
    }
    return (best.Index, string.Join("，", reasons) + "。");
  }

  private static StackAction SuggestAction(Stack stack)
  {
    var summary = StackTriage.Summarize(stack);
    if (summary.LooksDeliberate)
    {
      return StackAction.KeepAll;
    }
    return summary.OnlyElsewhereFiles > 0 || summary.ConflictFiles > 0 ? StackAction.Merge : StackAction.KeepKeeperOnly;
  }

  private static StackBlock BlockOf(Context context, List<StackCopy> copies)
  {
    if (copies.Any(c => c.IsOffline))
    {
      return StackBlock.SourceOffline;
    }
    return copies.Any(c => context.Sources[c.SourceId].IsScanning) ? StackBlock.StillScanning : StackBlock.None;
  }

  public static bool LooksLikeCopy(string name)
  {
    ArgumentNullException.ThrowIfNull(name);
    return CopyName().IsMatch(name);
  }

  [GeneratedRegex(@"(backup|\bbak\b|\bold\b|copy|複製|副本|備份|\(\d+\)|-\d{8}$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
  private static partial Regex CopyName();

  private static FileVersion Version(IndexedFile file) =>
    new(file.Hash ?? $"unhashed:{file.Id}", Local(file.ModifiedTicks), file.Size);

  private static DateTimeOffset Local(long utcTicks) => new DateTimeOffset(utcTicks, TimeSpan.Zero).ToLocalTime();

  private static string StackId(string kind, IEnumerable<StackCopy> copies)
  {
    var identity = string.Join('\n', copies.Select(c => c.Id.ToUpperInvariant()).Order(StringComparer.Ordinal));
    return kind + ":" + Convert.ToHexStringLower(XxHash64.Hash(Encoding.UTF8.GetBytes(identity)));
  }

  /// <summary>An ASCII tag for <c>name.from-tag.ext</c>: the parent folder name, for example <c>desktop</c>.</summary>
  private static string TagFor(string path)
  {
    var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty);
    var tag = new string(parent.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    return tag.Length == 0 ? "copy" : tag.Length > 16 ? tag[..16] : tag;
  }

  private static List<string> UniqueTags(List<string> tags)
  {
    var result = new List<string>(tags.Count);
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var tag in tags)
    {
      var candidate = tag;
      for (var n = 2; !seen.Add(candidate); n++)
      {
        candidate = tag + n.ToString(CultureInfo.InvariantCulture);
      }
      result.Add(candidate);
    }
    return result;
  }

  private static string SizeText(long bytes)
  {
    var mb = bytes / 1024.0 / 1024.0;
    return mb >= 1024 ? (mb / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
      : mb >= 1 ? Math.Round(mb).ToString("0", CultureInfo.InvariantCulture) + " MB"
      : Math.Max(1, Math.Round(bytes / 1024.0)).ToString("0", CultureInfo.InvariantCulture) + " KB";
  }

  private static bool IsInside(string child, string parent) =>
    child.Length > parent.Length && child.StartsWith(parent, StringComparison.Ordinal) && child[parent.Length] == '\\';

  private static int Depth(string canonical) => canonical.Count(c => c == '\\');

  private sealed record FolderGroup(List<string> Members, int Matched);

  private sealed record FolderRef(string SourceId, string RelativePath);

  private sealed class Context
  {
    private readonly Dictionary<string, IndexedFile[]> _sorted;
    private readonly Dictionary<string, int> _fileCounts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hasDirectFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _directFileCounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _children = new(StringComparer.Ordinal);
    private readonly HashSet<string> _projects = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _order;

    public Context(DetectionInput input)
    {
      Input = input;
      Sources = input.Sources.ToDictionary(s => s.Id, StringComparer.Ordinal);
      _order = input.Sources.Select((s, i) => (s.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
      var known = input.Files.Where(f => Sources.ContainsKey(f.SourceId)).ToList();
      _sorted = known.GroupBy(f => f.SourceId, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.Ordinal);

      foreach (var file in known)
      {
        var rel = file.RelativePath;
        string? previous = null;
        for (var slash = rel.IndexOf('\\', StringComparison.Ordinal); slash > 0; slash = rel.IndexOf('\\', slash + 1))
        {
          var folder = Canonical(file.SourceId, rel[..slash]);
          _fileCounts[folder] = _fileCounts.GetValueOrDefault(folder) + 1;
          if (!Folders.ContainsKey(folder))
          {
            Folders[folder] = new FolderRef(file.SourceId, rel[..slash]);
          }
          if (previous is not null)
          {
            if (!_children.TryGetValue(previous, out var children))
            {
              _children[previous] = children = new HashSet<string>(StringComparer.Ordinal);
            }
            children.Add(folder);
          }
          previous = folder;
        }
        if (previous is not null)
        {
          _hasDirectFiles.Add(previous);
          _directFileCounts[previous] = _directFileCounts.GetValueOrDefault(previous) + 1;
        }
      }
      foreach (var project in input.ProjectFolders.Where(p => Sources.ContainsKey(p.SourceId)))
      {
        _projects.Add(Canonical(project.SourceId, project.RelativePath));
        // A project root is a meaningful unit even when its files sit in one subfolder.
        _hasDirectFiles.Add(Canonical(project.SourceId, project.RelativePath));
      }
      foreach (var folder in input.RegenerableFolders.Where(r => Sources.ContainsKey(r.SourceId)))
      {
        // node_modules next to src means the parent is a project, not a wrapper around src.
        var slash = folder.RelativePath.LastIndexOf('\\');
        if (slash > 0)
        {
          _hasDirectFiles.Add(Canonical(folder.SourceId, folder.RelativePath[..slash]));
        }
      }

      Duplicates = known
        .Where(f => f.Hash is not null && f.Size > 0 && !f.CloudOnly)
        .GroupBy(f => f.Hash!, StringComparer.Ordinal)
        .Where(g => g.Count() > 1)
        .Select(g => g.ToList())
        .ToList();
    }

    public DetectionInput Input { get; }
    public Dictionary<string, DetectionSource> Sources { get; }
    public Dictionary<string, FolderRef> Folders { get; } = new(StringComparer.Ordinal);
    public List<List<IndexedFile>> Duplicates { get; }

    /// <summary>Files of a source whose relative path starts with <paramref name="prefix"/>, by binary search.</summary>
    public IEnumerable<IndexedFile> FilesUnder(string sourceId, string prefix)
    {
      if (!_sorted.TryGetValue(sourceId, out var files))
      {
        yield break;
      }
      int low = 0, high = files.Length;
      while (low < high)
      {
        var middle = (low + high) / 2;
        if (StringComparer.OrdinalIgnoreCase.Compare(files[middle].RelativePath, prefix) < 0)
        {
          low = middle + 1;
        }
        else
        {
          high = middle;
        }
      }
      for (var i = low; i < files.Length && files[i].RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase); i++)
      {
        yield return files[i];
      }
    }

    public int FileCount(string canonicalFolder) => _fileCounts.GetValueOrDefault(canonicalFolder);

    public int DirectFileCount(string canonicalFolder) => _directFileCounts.GetValueOrDefault(canonicalFolder);

    public HashSet<string> ChildrenOf(string canonicalFolder) =>
      _children.TryGetValue(canonicalFolder, out var children) ? children : [];

    public bool IsProjectRoot(string canonicalFolder) => _projects.Contains(canonicalFolder);

    /// <summary>A folder with no files of its own and exactly one subfolder: the subfolder is the meaningful unit.</summary>
    public bool IsWrapper(string canonicalFolder) =>
      !_hasDirectFiles.Contains(canonicalFolder) && _children.TryGetValue(canonicalFolder, out var children) && children.Count == 1;

    public int SourceOrder(string sourceId) => _order.GetValueOrDefault(sourceId, int.MaxValue);

    public string FullPath(string sourceId, string relative)
    {
      var root = Sources[sourceId].Root.TrimEnd('\\');
      return relative.Length == 0 ? Sources[sourceId].Root : root + "\\" + relative;
    }

    /// <summary>Upper-cased full path, so folders compare the way Windows does.</summary>
    public string Canonical(string sourceId, string relative) => FullPath(sourceId, relative).ToUpperInvariant();

    public bool IsProject(string sourceId, string relative) => _projects.Contains(Canonical(sourceId, relative));

    /// <summary>True when the file's folder or any folder above it (up to the source root) is a code project.</summary>
    public bool IsInProject(IndexedFile file)
    {
      var rel = file.RelativePath;
      if (_projects.Contains(Canonical(file.SourceId, string.Empty)))
      {
        return true;
      }
      for (var slash = rel.IndexOf('\\', StringComparison.Ordinal); slash > 0; slash = rel.IndexOf('\\', slash + 1))
      {
        if (_projects.Contains(Canonical(file.SourceId, rel[..slash])))
        {
          return true;
        }
      }
      return false;
    }
  }
}
