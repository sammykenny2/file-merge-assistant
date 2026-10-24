using FileWorkbench.Plans;
using FileWorkbench.Stacks;
using Xunit;
using static FileWorkbench.Tests.Stacks.StackFixtures;

namespace FileWorkbench.Tests.Stacks;

public class PlanBuilderTests
{
  private static readonly DateTimeOffset DecidedAt = new(2026, 10, 23, 14, 5, 0, TimeSpan.Zero);

  [Fact]
  public void Merge_Copies_Unique_Files_Saves_Both_Versions_Then_Recycles()
  {
    var stack = Project();
    var decision = StackDecision.Suggested(stack).WithChoice("src/receipt.html", ConflictChoice.Both);
    var entry = PlanBuilder.Build(new StackReview(stack, decision), DecidedAt);

    var copies = entry.Items.Where(i => i.Kind == PlanItemKind.Copy).ToList();
    Assert.Equal(["docs/old-spec.md", "notes.md"], copies.Select(i => i.Target));
    Assert.Equal(@"D:\backup\project\docs\old-spec.md", copies[0].SourcePath);
    Assert.Equal(@"C:\source\project\docs\old-spec.md", copies[0].DestinationPath);

    var saveAs = Assert.Single(entry.Items, i => i.Kind == PlanItemKind.SaveAs);
    Assert.Equal(@"C:\Desktop\project - copy\src\receipt.html", saveAs.SourcePath);
    Assert.Equal(@"C:\source\project\src\receipt.from-desktop.html", saveAs.DestinationPath);

    var recycled = entry.Items.Where(i => i.Kind == PlanItemKind.Recycle).Select(i => i.SourcePath).ToList();
    Assert.Equal([@"D:\backup\project", @"C:\Desktop\project - copy"], recycled);
    Assert.Single(entry.OfflineCopies);
    Assert.Equal(DecidedAt, entry.DecidedAt);
  }

  [Fact]
  public void Copies_Come_Before_Recycling()
  {
    var stack = Project();
    var decision = StackDecision.Suggested(stack).WithKeeper("backup")
      .WithChoice("src/receipt.html", ConflictChoice.Theirs)
      .WithChoice("src/pdf.ts", ConflictChoice.Both);
    var entry = PlanBuilder.Build(new StackReview(stack, decision), DecidedAt);

    var firstRecycle = entry.Items.ToList().FindIndex(i => i.Kind == PlanItemKind.Recycle);
    Assert.True(firstRecycle > 0);
    Assert.All(entry.Items.Take(firstRecycle), i => Assert.True(i.IsCopyLike));
    Assert.All(entry.Items.Skip(firstRecycle), i => Assert.Equal(PlanItemKind.Recycle, i.Kind));
  }

  [Fact]
  public void Keeping_The_Keeper_Version_Adds_No_Copy()
  {
    var stack = Project();
    var decision = StackDecision.Suggested(stack).WithChoice("src/receipt.html", ConflictChoice.Keeper);
    var entry = PlanBuilder.Build(new StackReview(stack, decision), DecidedAt);

    Assert.DoesNotContain(entry.Items, i => i.Target.Contains("receipt", StringComparison.Ordinal));
  }

  [Fact]
  public void Keep_Keeper_Only_Recycles_Without_Copying()
  {
    var stack = Project();
    var entry = PlanBuilder.Build(new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly)), DecidedAt);

    Assert.All(entry.Items, i => Assert.Equal(PlanItemKind.Recycle, i.Kind));
    Assert.Equal(2, entry.Items.Count);
  }

  [Fact]
  public void Decisions_That_Change_No_Files_Produce_No_Items()
  {
    var stack = Project();
    var entry = PlanBuilder.Build(new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepAll)), DecidedAt);

    Assert.Empty(entry.Items);
    Assert.Empty(entry.OfflineCopies);
  }

  [Fact]
  public void Blocked_Decisions_Are_Refused()
  {
    var stack = Project();

    Assert.Throws<InvalidOperationException>(() => PlanBuilder.Build(new StackReview(stack, StackDecision.Suggested(stack)), DecidedAt));
  }

  [Fact]
  public void Unchecking_A_Copy_Warns_When_Its_Source_Will_Still_Be_Recycled()
  {
    var stack = Project();
    var decision = StackDecision.Suggested(stack).WithChoice("src/receipt.html", ConflictChoice.Keeper);
    var entry = PlanBuilder.Build(new StackReview(stack, decision), DecidedAt);
    var notes = entry.Items.Single(i => i.Target == "notes.md");

    Assert.False(PlanBuilder.HasDependencyWarning(entry, notes));

    var disabled = entry with { Items = entry.Items.Select(i => i.Id == notes.Id ? i with { Enabled = false } : i).ToList() };
    Assert.True(PlanBuilder.HasDependencyWarning(disabled, disabled.Items.Single(i => i.Id == notes.Id)));

    // If the copy it comes from is not recycled either, nothing is lost.
    var both = disabled with
    {
      Items = disabled.Items.Select(i => i.Kind == PlanItemKind.Recycle && i.CopyId == notes.CopyId ? i with { Enabled = false } : i).ToList(),
    };
    Assert.False(PlanBuilder.HasDependencyWarning(both, both.Items.Single(i => i.Id == notes.Id)));
  }

  [Theory]
  [InlineData(".env.local", true)]
  [InlineData("config/secrets.json", true)]
  [InlineData("certs/server.pem", true)]
  [InlineData("src/app.ts", false)]
  [InlineData("environment.md", false)]
  public void Secret_Looking_Files_Are_Flagged(string path, bool expected)
  {
    Assert.Equal(expected, PlanBuilder.LooksLikeSecret(path));
  }
}
