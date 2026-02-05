using FileMergeAssistant.Models;
using FileMergeAssistant.Resolution;
using Xunit;

namespace FileMergeAssistant.Tests.Resolution;

public class ResolutionServiceTests
{
  private readonly ResolutionService _sut = new();

  [Fact]
  public void AutoResolveKeepOldest_Returns_Decision_Per_Group()
  {
    var groups = CreateGroups(3);

    var decisions = _sut.AutoResolveKeepOldest(groups);

    Assert.Equal(3, decisions.Count);
  }

  [Fact]
  public void AutoResolveKeepOldest_Keeps_First_File()
  {
    var groups = CreateGroups(1);

    var decisions = _sut.AutoResolveKeepOldest(groups);

    Assert.Equal(groups[0].Files[0].FilePath, decisions[0].KeptFilePath);
    Assert.Equal(ResolveAction.Keep, decisions[0].Actions[0].Action);
    Assert.Equal(ResolveAction.Delete, decisions[0].Actions[1].Action);
  }

  [Fact]
  public void AutoResolveKeepNewest_Keeps_Last_File()
  {
    var groups = CreateGroups(1);

    var decisions = _sut.AutoResolveKeepNewest(groups);

    Assert.Equal(groups[0].Files[^1].FilePath, decisions[0].KeptFilePath);
    Assert.Equal(ResolveAction.Delete, decisions[0].Actions[0].Action);
    Assert.Equal(ResolveAction.Keep, decisions[0].Actions[^1].Action);
  }

  [Fact]
  public void AutoResolveKeepNewest_Returns_Decision_Per_Group()
  {
    var groups = CreateGroups(2);

    var decisions = _sut.AutoResolveKeepNewest(groups);

    Assert.Equal(2, decisions.Count);
  }

  [Fact]
  public void CreateDecision_Keeps_Specified_Index()
  {
    var group = CreateGroup("hash1", 3);

    var decision = _sut.CreateDecision(group, 1);

    Assert.Equal(group.Files[1].FilePath, decision.KeptFilePath);
    Assert.Equal(ResolveAction.Delete, decision.Actions[0].Action);
    Assert.Equal(ResolveAction.Keep, decision.Actions[1].Action);
    Assert.Equal(ResolveAction.Delete, decision.Actions[2].Action);
  }

  [Fact]
  public void CreateDecision_Actions_Count_Matches_Files()
  {
    var group = CreateGroup("hash1", 4);

    var decision = _sut.CreateDecision(group, 0);

    Assert.Equal(4, decision.Actions.Count);
  }

  [Fact]
  public void CreateDecision_Sets_Group_Reference()
  {
    var group = CreateGroup("hash1", 2);

    var decision = _sut.CreateDecision(group, 0);

    Assert.Same(group, decision.Group);
  }

  [Fact]
  public void AutoResolveKeepOldest_Empty_Groups_Returns_Empty()
  {
    var decisions = _sut.AutoResolveKeepOldest([]);

    Assert.Empty(decisions);
  }

  [Fact]
  public void AutoResolveKeepNewest_Empty_Groups_Returns_Empty()
  {
    var decisions = _sut.AutoResolveKeepNewest([]);

    Assert.Empty(decisions);
  }

  private static List<DuplicateGroup> CreateGroups(int count)
  {
    return Enumerable.Range(0, count)
      .Select(i => CreateGroup($"hash_{i}", 2))
      .ToList();
  }

  private static DuplicateGroup CreateGroup(string hash, int fileCount)
  {
    var now = DateTime.UtcNow;
    var files = Enumerable.Range(0, fileCount)
      .Select(i => new ScannedFile
      {
        FilePath = $"file_{hash}_{i}.txt",
        SizeBytes = 1000,
        Hash = hash,
        HashAlgorithm = "sha256",
        CreatedUtc = now.AddDays(-fileCount + i),
        ModifiedUtc = now
      })
      .ToList();

    return new DuplicateGroup
    {
      Hash = hash,
      Files = files
    };
  }
}
