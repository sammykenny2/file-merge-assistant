using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Workbench;

public class DemoWorkbenchStoreTests
{
  private static DemoWorkbenchStore NewStore() => new() { StepDelay = TimeSpan.Zero };

  private static StackDecision InvoiceMerge(DemoWorkbenchStore store)
  {
    var invoice = store.Stacks.Single(s => s.Id == "invoice");
    return StackDecision.Suggested(invoice).WithChoice("src/templates/receipt.html", ConflictChoice.Both);
  }

  [Fact]
  public void Starts_With_The_Mock_Content()
  {
    var store = NewStore();

    Assert.True(store.IsDemo);
    Assert.Equal(8, store.Sources.Count);
    Assert.Contains(store.Sources, s => s.State == SourceState.Offline);
    Assert.Contains(store.Jobs, j => j.State == ScanJobState.Running);
    Assert.Contains(store.Stacks, s => s.Id == "invoice" && s.Status == StackStatus.Open);
    Assert.Empty(store.Plan);
    Assert.NotNull(store.SinceLastVisit);
  }

  [Fact]
  public void Every_Demo_Stack_Can_Be_Reviewed_With_Its_Suggestion()
  {
    foreach (var stack in NewStore().Stacks)
    {
      Assert.Equal(stack.Copies.Count, stack.Rows.Count == 0 ? stack.Copies.Count : stack.Rows[0].Cells.Count);
      var review = new StackReview(stack, StackDecision.Suggested(stack));
      Assert.NotNull(review.Outcome);
      Assert.False(stack.SuggestedKeeperCopy.IsOffline);
    }
  }

  [Fact]
  public void Deciding_Adds_To_The_Plan_Without_Changing_Anything_Else()
  {
    var store = NewStore();
    var changes = 0;
    store.Changed += (_, _) => changes++;

    store.Decide(InvoiceMerge(store));

    var entry = Assert.Single(store.Plan);
    Assert.Equal("invoice", entry.Stack.Id);
    Assert.Equal(StackStatus.Pending, store.Stacks.Single(s => s.Id == "invoice").Status);
    Assert.NotNull(store.DecisionFor("invoice"));
    Assert.True(changes > 0);
    Assert.Null(store.PlanCheckedAt);
  }

  [Fact]
  public void A_Blocked_Decision_Is_Refused()
  {
    var store = NewStore();
    var invoice = store.Stacks.Single(s => s.Id == "invoice");

    Assert.Throws<InvalidOperationException>(() => store.Decide(StackDecision.Suggested(invoice)));
    Assert.Empty(store.Plan);
  }

  [Fact]
  public void Keep_All_And_Later_Do_Not_Enter_The_Plan()
  {
    var store = NewStore();
    var thesis = store.Stacks.Single(s => s.Id == "thesis");

    store.Decide(StackDecision.Suggested(thesis));
    Assert.Equal(StackStatus.KeptAll, store.Stacks.Single(s => s.Id == "thesis").Status);

    var kyoto = store.Stacks.Single(s => s.Id == "kyoto");
    store.Decide(StackDecision.Suggested(kyoto).WithAction(StackAction.Later));
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == "kyoto").Status);
    Assert.Empty(store.Plan);
  }

  [Fact]
  public void Deciding_Again_Replaces_The_Plan_Entry()
  {
    var store = NewStore();
    store.Decide(InvoiceMerge(store));
    store.Decide(InvoiceMerge(store).WithAction(StackAction.KeepKeeperOnly));

    var entry = Assert.Single(store.Plan);
    Assert.All(entry.Items, i => Assert.Equal(PlanItemKind.Recycle, i.Kind));

    store.Undecide("invoice");
    Assert.Empty(store.Plan);
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == "invoice").Status);
  }

  [Fact]
  public void Bulk_Suggestion_Skips_Blocked_And_Conflicted_Stacks()
  {
    var store = NewStore();

    var added = store.DecideSuggested(["kyoto", "quote", "oldsite", "invoice", "camera"]);

    Assert.Equal(2, added);
    Assert.All(store.Plan, e => Assert.True(e.Decision.FromSuggestion));
  }

  [Fact]
  public async Task Recheck_Stops_Items_That_Changed_And_Running_Skips_Them()
  {
    var store = NewStore();
    store.Decide(InvoiceMerge(store));
    store.Decide(StackDecision.Suggested(store.Stacks.Single(s => s.Id == "quote")));

    await store.RecheckPlanAsync();

    var items = store.Plan.SelectMany(e => e.Items).ToList();
    var changed = Assert.Single(items, i => i.Check == PlanCheck.Changed);
    Assert.False(changed.WillRun);
    Assert.All(items.Where(i => i.Id != changed.Id), i => Assert.Equal(PlanCheck.Confirmed, i.Check));

    var batch = await store.ExecutePlanAsync(CancellationToken.None);

    Assert.Equal(BatchItemState.Skipped, batch.Items.Single(i => i.Id == changed.Id).State);
    Assert.Empty(store.Plan);
    Assert.Same(batch, store.History[0]);
    // A skipped item puts its stack back in the list; the other stack converged.
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == "invoice").Status);
    Assert.Equal(StackStatus.Converged, store.Stacks.Single(s => s.Id == "quote").Status);
  }

  [Fact]
  public async Task Disabled_Items_Are_Not_Run()
  {
    var store = NewStore();
    store.Decide(StackDecision.Suggested(store.Stacks.Single(s => s.Id == "quote")));
    var first = store.Plan[0].Items[0];
    store.SetPlanItemEnabled(first.Id, false);

    var batch = await store.ExecutePlanAsync(CancellationToken.None);

    Assert.DoesNotContain(batch.Items, i => i.Id == first.Id);
  }

  [Fact]
  public async Task Failed_Items_Can_Be_Retried_And_Done_Items_Restored()
  {
    var store = NewStore();
    store.Decide(StackDecision.Suggested(store.Stacks.Single(s => s.Id == "kyoto")));
    var batch = await store.ExecutePlanAsync(CancellationToken.None);
    var failed = Assert.Single(batch.Items, i => i.State == BatchItemState.Failed);

    await store.RetryFailedAsync(batch.Id);
    var retried = store.History[0].Items.Single(i => i.Id == failed.Id);
    Assert.Equal(BatchItemState.Done, retried.State);
    Assert.True(retried.Retried);

    var recycledBefore = store.RecycleAreas.Sum(a => a.Bytes);
    await store.RestoreItemAsync(batch.Id, failed.Id);
    Assert.Equal(BatchItemState.Restored, store.History[0].Items.Single(i => i.Id == failed.Id).State);
    Assert.True(store.RecycleAreas.Sum(a => a.Bytes) < recycledBefore);
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == "kyoto").Status);
  }

  [Fact]
  public async Task Restore_Conflicts_Never_Overwrite_And_Record_The_Choice()
  {
    var store = NewStore();
    var conflict = Assert.Single(store.RestoreConflicts);

    await store.RestoreBatchAsync(conflict.BatchId);
    var conflicted = store.History.Single(b => b.Id == conflict.BatchId).Items.Single(i => i.Id == conflict.ItemId);
    Assert.Equal(BatchItemState.Done, conflicted.State);

    await store.ResolveRestoreConflictAsync(conflict.Id, RestoreConflictChoice.KeepInRecycleArea, null);
    Assert.NotNull(store.RestoreConflicts[0].Resolution);
    Assert.Equal(BatchItemState.Done, store.History.Single(b => b.Id == conflict.BatchId).Items.Single(i => i.Id == conflict.ItemId).State);
  }

  [Fact]
  public async Task Emptying_The_Recycle_Areas_Ends_Restoring()
  {
    var store = NewStore();

    await store.EmptyRecycleAreasAsync();

    Assert.All(store.RecycleAreas, a => Assert.Equal(0, a.Bytes));
    Assert.All(store.History, b => Assert.NotNull(b.EmptiedAt));
  }

  [Fact]
  public async Task Jobs_Pause_Resume_Move_And_Cancel()
  {
    var store = NewStore();
    var running = store.Jobs.Single(j => j.State == ScanJobState.Running);

    await store.PauseJobAsync(running.Id);
    Assert.Equal(ScanJobState.Paused, store.Jobs.Single(j => j.Id == running.Id).State);
    await store.ResumeJobAsync(running.Id);
    Assert.Equal(ScanJobState.Running, store.Jobs.Single(j => j.Id == running.Id).State);

    var queued = store.Jobs.Where(j => j.State == ScanJobState.Queued).Select(j => j.Id).ToList();
    store.MoveJob(queued[1], -1);
    Assert.Equal(queued[1], store.Jobs.First(j => j.State == ScanJobState.Queued).Id);

    await store.CancelJobAsync(queued[0]);
    Assert.DoesNotContain(store.Jobs, j => j.Id == queued[0]);
  }

  [Fact]
  public async Task Added_Sources_Are_Queued_Once()
  {
    var store = NewStore();
    var options = new SourceOptions(true, true, [@"X:\work"]);

    await store.AddSourcesAsync([@"X:\work", @"C:\Users\demo\Downloads"], options, startScan: true);
    await store.AddSourcesAsync([@"X:\work"], options, startScan: true);

    var added = Assert.Single(store.Sources, s => s.RootPath == @"X:\work");
    Assert.True(added.IsProtected);
    Assert.Single(store.Jobs, j => j.SourceId == added.Id && j.State == ScanJobState.Queued);
  }

  [Fact]
  public void Notices_Record_The_Response()
  {
    var store = NewStore();

    store.RespondToNotice("drive", NoticeResponse.SameDrive);

    Assert.NotNull(store.SinceLastVisit!.Notices.Single(n => n.Id == "drive").HandledText);
  }
}
