using FileMergeAssistant.Models;

namespace FileMergeAssistant.Resolution;

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
}
