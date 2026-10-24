using FileWorkbench.Models;
using FileWorkbench.Resolution;
using Xunit;

namespace FileWorkbench.Tests.Resolution;

public class ResolutionServiceExtendedTests
{
  private readonly ResolutionService _sut = new();

  [Fact]
  public void AutoResolveKeepLargest_Keeps_Largest_File()
  {
    var groups = new List<DuplicateGroup>
    {
      CreateGroupWithVariedSizes("hash1", [500, 1000, 750])
    };

    var decisions = _sut.AutoResolveKeepLargest(groups);

    Assert.Single(decisions);
    Assert.Equal(groups[0].Files[1].FilePath, decisions[0].KeptFilePath); // 1000 bytes
  }

  [Fact]
  public void AutoResolveKeepSmallest_Keeps_Smallest_File()
  {
    var groups = new List<DuplicateGroup>
    {
      CreateGroupWithVariedSizes("hash1", [500, 1000, 750])
    };

    var decisions = _sut.AutoResolveKeepSmallest(groups);

    Assert.Single(decisions);
    Assert.Equal(groups[0].Files[0].FilePath, decisions[0].KeptFilePath); // 500 bytes
  }

  [Fact]
  public void AutoResolveKeepLargest_Empty_Groups_Returns_Empty()
  {
    var decisions = _sut.AutoResolveKeepLargest([]);
    Assert.Empty(decisions);
  }

  [Fact]
  public void AutoResolveKeepSmallest_Empty_Groups_Returns_Empty()
  {
    var decisions = _sut.AutoResolveKeepSmallest([]);
    Assert.Empty(decisions);
  }

  [Fact]
  public void ApplyProtectedPaths_Skips_Protected_Files()
  {
    var group = CreateGroupWithPaths("hash1", ["/protected/file1.txt", "/other/file2.txt"]);
    var decisions = _sut.AutoResolveKeepNewest(new[] { group });

    var result = _sut.ApplyProtectedPaths(decisions, ["/protected"]);

    var protectedAction = result[0].Actions.First(a => a.FilePath == "/protected/file1.txt");
    Assert.Equal(ResolveAction.Skip, protectedAction.Action);
  }

  [Fact]
  public void ApplyProtectedPaths_Does_Not_Skip_Unprotected()
  {
    var group = CreateGroupWithPaths("hash1", ["/dir1/file1.txt", "/dir2/file2.txt"]);
    var decisions = _sut.AutoResolveKeepOldest(new[] { group });

    var result = _sut.ApplyProtectedPaths(decisions, ["/protected"]);

    var deleteActions = result[0].Actions.Where(a => a.Action == ResolveAction.Delete).ToList();
    Assert.Single(deleteActions);
  }

  [Fact]
  public void ApplyProtectedPaths_Empty_Paths_Returns_Unchanged()
  {
    var group = CreateGroupWithPaths("hash1", ["/a/file.txt", "/b/file.txt"]);
    var decisions = _sut.AutoResolveKeepOldest(new[] { group });

    var result = _sut.ApplyProtectedPaths(decisions, []);

    Assert.Equal(decisions, result);
  }

  [Fact]
  public void ApplyProtectedPaths_Multiple_Protected_Dirs()
  {
    var group = CreateGroupWithPaths("hash1", ["/a/file1.txt", "/b/file2.txt", "/c/file3.txt"]);
    var decisions = _sut.AutoResolveKeepOldest(new[] { group });

    var result = _sut.ApplyProtectedPaths(decisions, ["/b", "/c"]);

    var skipped = result[0].Actions.Count(a => a.Action == ResolveAction.Skip);
    Assert.Equal(2, skipped);
  }

  private static DuplicateGroup CreateGroupWithVariedSizes(string hash, long[] sizes)
  {
    var now = DateTime.UtcNow;
    var files = sizes.Select((size, i) => new ScannedFile
    {
      FilePath = $"/folder{i}/file{i}.txt",
      SizeBytes = size,
      Hash = hash,
      HashAlgorithm = "sha256",
      CreatedUtc = now.AddDays(-sizes.Length + i),
      ModifiedUtc = now
    }).ToList();

    return new DuplicateGroup { Hash = hash, Files = files };
  }

  private static DuplicateGroup CreateGroupWithPaths(string hash, string[] paths)
  {
    var now = DateTime.UtcNow;
    var files = paths.Select((path, i) => new ScannedFile
    {
      FilePath = path,
      SizeBytes = 1000,
      Hash = hash,
      HashAlgorithm = "sha256",
      CreatedUtc = now.AddDays(-paths.Length + i),
      ModifiedUtc = now
    }).ToList();

    return new DuplicateGroup { Hash = hash, Files = files };
  }
}
