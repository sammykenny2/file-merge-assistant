using FileMergeAssistant.Stacks;
using Xunit;
using static FileMergeAssistant.Tests.Stacks.StackFixtures;

namespace FileMergeAssistant.Tests.Stacks;

public class StackTriageTests
{
  [Fact]
  public void Identical_Copies_Are_Ready_To_Converge()
  {
    var summary = StackTriage.Summarize(Identical());

    Assert.Equal(StackGroup.ReadyToConverge, summary.Group);
    Assert.Equal(2000, summary.ReclaimableBytes);
    Assert.False(summary.LooksDeliberate);
  }

  [Fact]
  public void Files_Only_Some_Copies_Have_Mean_The_Stack_Needs_A_Merge()
  {
    var summary = StackTriage.Summarize(Project());

    Assert.Equal(StackGroup.NeedsMerge, summary.Group);
    // old-spec.md, notes.md and offline.txt are missing from the keeper; receipt.html is newer elsewhere.
    Assert.Equal(3, summary.OnlyElsewhereFiles);
    Assert.Equal(1, summary.ConflictFiles);
    Assert.Equal(2, summary.DifferingFiles);
  }

  [Fact]
  public void Differences_In_An_Offline_Copy_Still_Count()
  {
    var stack = new Stack("s", "s", StackKind.Folder,
      [Copy("a", @"C:\a"), Copy("b", @"E:\b", offline: true)],
      [new StackRow("x.cs", [V("1", New), V("2", Old)])],
      0, StackAction.KeepKeeperOnly)
    {
      Block = StackBlock.SourceOffline,
    };

    var summary = StackTriage.Summarize(stack);

    Assert.Equal(StackGroup.VersionDifferences, summary.Group);
    Assert.Equal(0, summary.ReclaimableBytes);
    Assert.Equal(1000, summary.DuplicateBytes);
    Assert.True(summary.IsBlocked);
  }

  [Fact]
  public void Stacks_Still_Being_Scanned_Are_Listed_Separately()
  {
    var summary = StackTriage.Summarize(Identical() with { Block = StackBlock.StillScanning });

    Assert.Equal(StackGroup.StillComparing, summary.Group);
  }

  [Fact]
  public void Copies_Edited_On_Both_Sides_Look_Deliberate()
  {
    var stack = new Stack("thesis", "thesis", StackKind.Folder,
      [Copy("a", @"C:\a"), Copy("b", @"D:\b")],
      [
        new StackRow("main.tex", [V("1", Mid), V("2", New)]),
        new StackRow("refs.bib", [V("3", New), V("4", Old)]),
      ],
      0, StackAction.KeepAll);

    Assert.True(StackTriage.Summarize(stack).LooksDeliberate);
    Assert.False(StackTriage.Summarize(Project()).LooksDeliberate);
  }

  [Fact]
  public void Suggested_Order_Puts_Space_Per_Decision_First_And_Blocked_Last()
  {
    var big = Identical() with { Id = "big", Copies = Identical().Copies.Select(c => c with { SizeBytes = 50_000 }).ToList() };
    var blocked = big with { Id = "blocked", Block = StackBlock.SourceOffline };
    var small = Identical();
    var conflicted = Project();

    var order = StackTriage.Order(new[] { blocked, small, conflicted, big }.Select(StackTriage.Summarize), StackSort.Suggested)
      .Select(s => s.Stack.Id)
      .ToList();

    Assert.Equal(["big", "identical", "project", "blocked"], order);
  }

  [Fact]
  public void Other_Sorts_Use_Space_Recency_And_Copy_Count()
  {
    var many = Identical() with { Id = "many", UpdatedAt = New };
    var project = Project() with { UpdatedAt = Old };
    var summaries = new[] { many, project }.Select(StackTriage.Summarize).ToList();

    Assert.Equal("project", StackTriage.Order(summaries, StackSort.MostSpace)[0].Stack.Id);
    Assert.Equal("many", StackTriage.Order(summaries, StackSort.RecentlyChanged)[0].Stack.Id);
    Assert.Equal("project", StackTriage.Order(summaries, StackSort.MostCopies)[0].Stack.Id);
  }
}
