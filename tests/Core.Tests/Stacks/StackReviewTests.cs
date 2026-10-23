using FileMergeAssistant.Stacks;
using Xunit;
using static FileMergeAssistant.Tests.Stacks.StackFixtures;

namespace FileMergeAssistant.Tests.Stacks;

public class StackReviewTests
{
  private static StackReview Review(StackAction action, Func<StackDecision, StackDecision>? change = null)
  {
    var stack = Project();
    var decision = StackDecision.Suggested(stack).WithAction(action);
    return new StackReview(stack, change is null ? decision : change(decision));
  }

  private static RowReview Row(StackReview review, string path) => review.Rows.Single(r => r.Row.RelativePath == path);

  [Fact]
  public void Roles_Follow_Keeper_Offline_Protection_And_Also_Keep()
  {
    var review = Review(StackAction.Merge);
    var copies = review.Stack.Copies;

    Assert.Equal(CopyRole.Keeper, review.RoleOf(copies[0]));
    Assert.Equal(CopyRole.Recycle, review.RoleOf(copies[1]));
    Assert.Equal(CopyRole.Offline, review.RoleOf(copies[2]));
    Assert.Equal(CopyRole.Recycle, review.RoleOf(copies[3]));

    var alsoKept = Review(StackAction.Merge, d => d.WithAlsoKeep("desktop", true));
    Assert.Equal(CopyRole.AlsoKeep, alsoKept.RoleOf(copies[3]));
  }

  [Fact]
  public void A_Protected_Copy_Is_Kept_When_It_Is_Not_The_Keeper()
  {
    var review = Review(StackAction.KeepKeeperOnly, d => d.WithKeeper("backup"));

    Assert.Equal(CopyRole.Protected, review.RoleOf(review.Stack.Copies[0]));
    Assert.DoesNotContain(review.Recycled, c => c.Id == "keeper");
  }

  [Fact]
  public void Actions_That_Change_No_Files_Leave_Every_Copy_Unchanged()
  {
    foreach (var action in new[] { StackAction.KeepAll, StackAction.Later })
    {
      var review = Review(action);
      Assert.False(review.ChangesFiles);
      Assert.Equal(CopyRole.Unchanged, review.RoleOf(review.Stack.Copies[1]));
      Assert.Equal(CopyRole.Offline, review.RoleOf(review.Stack.Copies[2]));
      Assert.Equal(ReviewBlock.None, review.Block);
      Assert.Equal(0, review.RecycleBytes);
    }
  }

  [Fact]
  public void Rows_Are_Classified_Against_The_Keeper()
  {
    var review = Review(StackAction.Merge);

    var receipt = Row(review, "src/receipt.html");
    Assert.Equal(RowKind.Conflict, receipt.Kind);
    Assert.Equal("desktop", receipt.Source!.Id);

    Assert.Equal(RowKind.Older, Row(review, "src/pdf.ts").Kind);

    var spec = Row(review, "docs/old-spec.md");
    Assert.Equal(RowKind.OnlyElsewhere, spec.Kind);
    Assert.Equal("backup", spec.Source!.Id);

    Assert.Equal(RowKind.OnlyElsewhere, Row(review, "notes.md").Kind);
    Assert.Equal(RowKind.OnlyOffline, Row(review, "offline.txt").Kind);

    Assert.Equal(2, review.OnlyElsewhereFileCount);
    Assert.Equal(1, review.ConflictCount);
    Assert.Equal(3, review.LossFileCount);
  }

  [Fact]
  public void Changing_The_Keeper_Recomputes_The_Comparison()
  {
    var review = Review(StackAction.Merge, d => d.WithKeeper("desktop"));

    // The desktop copy has the newest receipt, so nothing is newer than the keeper any more.
    Assert.Equal(RowKind.Older, Row(review, "src/receipt.html").Kind);
    // The old keeper is protected and stays; only the backup is recycled and only it can supply files.
    Assert.Equal(CopyRole.Protected, review.RoleOf(review.Stack.Copies[0]));
    Assert.Equal("backup", Row(review, "docs/old-spec.md").Source!.Id);
    Assert.Equal(RowKind.Same, Row(review, "notes.md").Kind);
    Assert.Equal(RowKind.OnlyOffline, Row(review, "offline.txt").Kind);
  }

  [Fact]
  public void Files_Only_A_Kept_Copy_Has_Are_Not_Copied()
  {
    var review = Review(StackAction.Merge, d => d.WithKeeper("backup"));

    // notes.md is only in the desktop copy, which is recycled, so it is copied into the backup keeper.
    Assert.Equal(RowKind.OnlyElsewhere, Row(review, "notes.md").Kind);
    // The protected copy stays, so a path only it and recycled copies lack is reported as kept elsewhere.
    var keepDesktop = Review(StackAction.Merge, d => d.WithKeeper("backup").WithAlsoKeep("desktop", true));
    Assert.Equal(RowKind.KeptElsewhere, Row(keepDesktop, "notes.md").Kind);
  }

  [Fact]
  public void Cells_Describe_Each_Copy_Relative_To_The_Keeper()
  {
    var review = Review(StackAction.Merge);
    var copies = review.Stack.Copies;

    var receipt = Row(review, "src/receipt.html");
    Assert.Equal(CellKind.KeeperHas, review.CellOf(receipt, copies[0]));
    Assert.Equal(CellKind.Older, review.CellOf(receipt, copies[1]));
    Assert.Equal(CellKind.Newer, review.CellOf(receipt, copies[3]));

    var pdf = Row(review, "src/pdf.ts");
    Assert.Equal(CellKind.Missing, review.CellOf(pdf, copies[2]));
    Assert.Equal(CellKind.SameAsKeeper, review.CellOf(pdf, copies[3]));

    var spec = Row(review, "docs/old-spec.md");
    Assert.Equal(CellKind.KeeperMissing, review.CellOf(spec, copies[0]));
    Assert.Equal(CellKind.KeeperLacks, review.CellOf(spec, copies[1]));
  }

  [Fact]
  public void Merge_Is_Blocked_Until_Every_Conflict_Has_A_Choice()
  {
    var review = Review(StackAction.Merge);
    Assert.Equal(ReviewBlock.UnresolvedConflicts, review.Block);
    Assert.Equal(1, review.UnresolvedCount);

    var chosen = Review(StackAction.Merge, d => d.WithChoice("src/receipt.html", ConflictChoice.Both));
    Assert.Equal(ReviewBlock.None, chosen.Block);
  }

  [Fact]
  public void A_Protected_Keeper_Cannot_Take_The_Other_Version()
  {
    var review = Review(StackAction.Merge, d => d.WithChoice("src/receipt.html", ConflictChoice.Theirs));

    Assert.False(review.IsChoiceAllowed(ConflictChoice.Theirs));
    Assert.True(review.IsUnresolved(review.Conflicts[0]));
    Assert.Equal(ReviewBlock.UnresolvedConflicts, review.Block);

    var unprotectedKeeper = Review(StackAction.Merge, d => d.WithKeeper("backup").WithChoice("src/receipt.html", ConflictChoice.Theirs));
    Assert.True(unprotectedKeeper.IsChoiceAllowed(ConflictChoice.Theirs));
  }

  [Fact]
  public void Keep_Keeper_Only_Is_Not_Blocked_By_Conflicts_But_Reports_The_Loss()
  {
    var review = Review(StackAction.KeepKeeperOnly);

    Assert.Equal(ReviewBlock.None, review.Block);
    var loss = Assert.Single(review.Outcome, o => o.Kind == OutcomeKind.RecyclesUniqueFiles);
    Assert.Equal(3, loss.FileCount);
  }

  [Fact]
  public void Nothing_To_Recycle_Blocks_File_Actions()
  {
    var review = Review(StackAction.KeepKeeperOnly, d => d.WithAlsoKeep("backup", true).WithAlsoKeep("desktop", true));

    Assert.Equal(ReviewBlock.NothingToRecycle, review.Block);
  }

  [Fact]
  public void Still_Scanning_Stacks_Cannot_Change_Files()
  {
    var stack = Identical() with { Block = StackBlock.StillScanning };

    Assert.Equal(ReviewBlock.StillScanning, new StackReview(stack, StackDecision.Suggested(stack)).Block);
    Assert.Equal(ReviewBlock.None, new StackReview(stack, StackDecision.Suggested(stack).WithAction(StackAction.Later)).Block);
  }

  [Fact]
  public void Removed_Copies_Leave_The_Stack_But_The_Keeper_Stays()
  {
    var review = Review(StackAction.Merge, d => d.WithRemoved("desktop").WithRemoved("keeper"));

    Assert.Equal(3, review.ActiveCopies.Count);
    Assert.Contains(review.ActiveCopies, c => c.Id == "keeper");
    Assert.Single(review.RemovedCopies);
    // Without the desktop copy, notes.md and the newer receipt are gone from the comparison.
    Assert.Equal(RowKind.None, Row(review, "notes.md").Kind);
    Assert.Equal(RowKind.Older, Row(review, "src/receipt.html").Kind);
  }

  [Fact]
  public void A_Stack_Keeps_At_Least_Two_Copies()
  {
    var stack = Identical();
    var review = new StackReview(stack, StackDecision.Suggested(stack).WithRemoved("two"));

    Assert.False(review.CanRemove(stack.Copies[2]));
    Assert.False(review.CanRemove(stack.Copies[0]));
  }

  [Fact]
  public void Offline_Copies_Cannot_Be_The_Keeper()
  {
    var stack = Project();

    Assert.False(StackReview.CanBeKeeper(stack.Copies[2]));
    Assert.Throws<ArgumentException>(() => new StackReview(stack, StackDecision.Suggested(stack).WithKeeper("offline")));
  }

  [Fact]
  public void Space_Counts_Every_Non_Keeper_Copy_But_Recycles_Only_Online_Unkept_Ones()
  {
    var review = Review(StackAction.Merge, d => d.WithChoice("src/receipt.html", ConflictChoice.Keeper));

    Assert.Equal(900 + 400 + 1000 + 1100 + 500, review.DuplicateBytes);
    Assert.Equal(900 + 400 + 1100 + 500, review.RecycleBytes);
    Assert.Equal(400 + 500, review.RecycleRegenerableBytes);
  }

  [Fact]
  public void Merge_Outcome_Lists_Copies_Choices_Recycling_And_Offline_Copies()
  {
    var review = Review(StackAction.Merge, d => d.WithChoice("src/receipt.html", ConflictChoice.Both));
    var kinds = review.Outcome.Select(o => o.Kind).ToList();

    Assert.Equal(
      [OutcomeKind.CopyToKeeper, OutcomeKind.SaveAs, OutcomeKind.Recycle, OutcomeKind.Recycle, OutcomeKind.OfflineUntouched],
      kinds);
    Assert.Equal("src/receipt.from-desktop.html", review.Outcome[1].NewPath);
  }

  [Theory]
  [InlineData("src/receipt.html", "desk", "src/receipt.from-desk.html")]
  [InlineData("notes", "usb", "notes.from-usb")]
  [InlineData(".gitconfig", "wd", ".gitconfig.from-wd")]
  [InlineData(".env.local", "wd", ".env.from-wd.local")]
  [InlineData("a.b/readme", "x", "a.b/readme.from-x")]
  public void SaveAsPath_Inserts_The_Tag_Before_The_Extension(string path, string tag, string expected)
  {
    Assert.Equal(expected, StackReview.SaveAsPath(path, tag));
  }
}
