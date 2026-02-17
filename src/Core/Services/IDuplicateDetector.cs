using FileMergeAssistant.Models;

namespace FileMergeAssistant.Services;

public interface IDuplicateDetector
{
  IReadOnlyList<DuplicateGroup> FindDuplicates(IReadOnlyList<ScannedFile> files);
  IReadOnlyList<DuplicateGroup> FindDuplicatesBySize(IReadOnlyList<ScannedFile> files);
  IReadOnlyList<DuplicateGroup> FindDuplicatesByName(IReadOnlyList<ScannedFile> files);
  IReadOnlyList<DuplicateGroup> FindDuplicatesByNameAndSize(IReadOnlyList<ScannedFile> files);
  Task<IReadOnlyList<DuplicateGroup>> VerifyGroupsByteByByteAsync(
    IReadOnlyList<DuplicateGroup> groups,
    IByteByByteVerifier verifier,
    CancellationToken cancellationToken = default);
  IReadOnlyList<DuplicateGroup> FilterHardlinks(
    IReadOnlyList<DuplicateGroup> groups,
    IHardlinkDetector hardlinkDetector);
}
