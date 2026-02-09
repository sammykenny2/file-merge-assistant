using FileMergeAssistant.Models;

namespace FileMergeAssistant.Resolution;

public interface IResolutionService
{
  List<ResolutionDecision> AutoResolveKeepOldest(IReadOnlyList<DuplicateGroup> groups);
  List<ResolutionDecision> AutoResolveKeepNewest(IReadOnlyList<DuplicateGroup> groups);
  List<ResolutionDecision> AutoResolveByMetadata(IReadOnlyList<DuplicateGroup> groups);
  List<ResolutionDecision> AutoResolveByFolderPriority(IReadOnlyList<DuplicateGroup> groups, IReadOnlyList<FolderPriority> folderPriorities);
  ResolutionDecision CreateDecision(DuplicateGroup group, int keepIndex);
}
