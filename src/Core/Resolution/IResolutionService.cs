using FileMergeAssistant.Models;

namespace FileMergeAssistant.Resolution;

public interface IResolutionService
{
  List<ResolutionDecision> AutoResolveKeepOldest(IReadOnlyList<DuplicateGroup> groups);
  List<ResolutionDecision> AutoResolveKeepNewest(IReadOnlyList<DuplicateGroup> groups);
  ResolutionDecision CreateDecision(DuplicateGroup group, int keepIndex);
}
