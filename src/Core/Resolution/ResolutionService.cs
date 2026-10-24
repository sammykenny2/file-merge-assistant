using FileWorkbench.Models;

namespace FileWorkbench.Resolution;

public class ResolutionService : IResolutionService
{
  public List<ResolutionDecision> AutoResolveKeepOldest(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g => CreateDecision(g, 0)).ToList();
  }

  public List<ResolutionDecision> AutoResolveKeepNewest(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g => CreateDecision(g, g.Files.Count - 1)).ToList();
  }

  public List<ResolutionDecision> AutoResolveKeepLargest(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g =>
    {
      var maxSize = g.Files.Max(f => f.SizeBytes);
      var bestIndex = g.Files.Select((f, i) => (f, i))
        .First(x => x.f.SizeBytes == maxSize).i;
      return CreateDecision(g, bestIndex);
    }).ToList();
  }

  public List<ResolutionDecision> AutoResolveKeepSmallest(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g =>
    {
      var minSize = g.Files.Min(f => f.SizeBytes);
      var bestIndex = g.Files.Select((f, i) => (f, i))
        .First(x => x.f.SizeBytes == minSize).i;
      return CreateDecision(g, bestIndex);
    }).ToList();
  }

  public List<ResolutionDecision> AutoResolveByMetadata(IReadOnlyList<DuplicateGroup> groups)
  {
    return groups.Select(g =>
    {
      var bestIndex = FindBestFileByMetadata(g);
      return CreateDecision(g, bestIndex);
    }).ToList();
  }

  public List<ResolutionDecision> AutoResolveByFolderPriority(
    IReadOnlyList<DuplicateGroup> groups,
    IReadOnlyList<FolderPriority> folderPriorities)
  {
    return groups.Select(g =>
    {
      var bestIndex = FindBestFileByFolderPriority(g, folderPriorities);
      return CreateDecision(g, bestIndex);
    }).ToList();
  }

  public ResolutionDecision CreateDecision(DuplicateGroup group, int keepIndex)
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

  public List<ResolutionDecision> ApplyProtectedPaths(
    List<ResolutionDecision> decisions,
    IReadOnlyList<string> protectedPaths)
  {
    if (protectedPaths.Count == 0)
      return decisions;

    return decisions.Select(d =>
    {
      var newActions = d.Actions.Select(a =>
      {
        if (a.Action == ResolveAction.Delete && IsProtected(a.FilePath, protectedPaths))
          return a with { Action = ResolveAction.Skip };
        return a;
      }).ToList();

      return new ResolutionDecision
      {
        Group = d.Group,
        KeptFilePath = d.KeptFilePath,
        Actions = newActions
      };
    }).ToList();
  }

  private static bool IsProtected(string filePath, IReadOnlyList<string> protectedPaths)
  {
    var normalizedFile = Path.GetFullPath(filePath).Replace('\\', '/');
    return protectedPaths.Any(pp =>
    {
      var normalizedProtected = Path.GetFullPath(pp).Replace('\\', '/');
      return normalizedFile.StartsWith(normalizedProtected, StringComparison.OrdinalIgnoreCase);
    });
  }

  private static int FindBestFileByMetadata(DuplicateGroup group)
  {
    var bestIndex = 0;
    var bestScore = 0;

    for (var i = 0; i < group.Files.Count; i++)
    {
      var file = group.Files[i];
      var score = 0;

      if (file.Metadata != null)
      {
        score += 10;

        if (!string.IsNullOrEmpty(file.Metadata.CameraModel))
          score += 5;

        if (file.Metadata.Width.HasValue && file.Metadata.Height.HasValue)
          score += (file.Metadata.Width.Value * file.Metadata.Height.Value) > 0 ? 3 : 0;

        if (file.Metadata.DateTaken.HasValue)
          score += 5;
      }

      if (file.SizeBytes >= group.Files.Max(f => f.SizeBytes))
        score += 2;

      if (file.CreatedUtc <= group.Files.Min(f => f.CreatedUtc))
        score += 3;

      if (score > bestScore)
      {
        bestScore = score;
        bestIndex = i;
      }
    }

    return bestIndex;
  }

  private static int FindBestFileByFolderPriority(
    DuplicateGroup group,
    IReadOnlyList<FolderPriority> folderPriorities)
  {
    var bestIndex = 0;
    var bestPriority = int.MinValue;

    for (var i = 0; i < group.Files.Count; i++)
    {
      var filePath = group.Files[i].FilePath.Replace('\\', '/');
      var priority = 0;

      foreach (var fp in folderPriorities)
      {
        var normalizedFolder = fp.FolderPath.Replace('\\', '/');
        if (filePath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
        {
          priority = fp.Priority;
          break;
        }
      }

      if (priority > bestPriority)
      {
        bestPriority = priority;
        bestIndex = i;
      }
    }

    return bestIndex;
  }
}
