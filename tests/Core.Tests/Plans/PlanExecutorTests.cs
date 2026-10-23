using FileMergeAssistant.Plans;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Tests.Scanning;
using Xunit;

namespace FileMergeAssistant.Tests.Plans;

/// <summary>The executor against throwaway folders under %TEMP%; its recycle area lives under the test data folder.</summary>
public sealed class PlanExecutorTests : IDisposable
{
  private static readonly DateTimeOffset Now = new(2026, 10, 23, 14, 36, 0, TimeSpan.FromHours(8));
  private readonly TempTree _tree = new();
  private readonly string _appData = Path.Combine(TestEnvironment.DataRoot, "executor-" + Guid.NewGuid().ToString("N")[..8]);
  private readonly PlanExecutor _executor;

  public PlanExecutorTests()
  {
    _executor = new PlanExecutor(new RecycleAreaLocator(_appData));
  }

  public void Dispose()
  {
    _tree.Dispose();
    try
    {
      Directory.Delete(_appData, recursive: true);
    }
    catch (IOException)
    {
      // Best effort.
    }
    catch (UnauthorizedAccessException)
    {
      // Best effort.
    }
  }

  private static StackCopy Copy(string id, string path, bool isProtected = false) =>
    new(id, "s-" + id, "Source " + id, path, id, isProtected, false, 100, 0, 3, Now);

  private static FileVersion V(string content) => new(content, Now, 10);

  /// <summary>keeper (a, b), backup (a, b, only-backup.txt), desk (a, b).</summary>
  private Stack BuildStack(bool protectBackup = false)
  {
    foreach (var copy in new[] { "keeper", "backup", "desk" })
    {
      _tree.Write($@"{copy}\a.txt", "same a");
      _tree.Write($@"{copy}\sub\b.txt", "same b");
    }
    _tree.Write(@"backup\only-backup.txt", "only here");
    return new Stack("stack", "project", StackKind.Folder,
      [Copy("keeper", _tree.PathOf("keeper")), Copy("backup", _tree.PathOf("backup"), protectBackup), Copy("desk", _tree.PathOf("desk"))],
      [new StackRow("only-backup.txt", [null, V("only"), null])],
      0, StackAction.Merge) { SameFileCount = 2 };
  }

  private static PlanEntry Plan(Stack stack, StackDecision? decision = null) =>
    PlanBuilder.Build(new StackReview(stack, decision ?? StackDecision.Suggested(stack)), Now);

  private ExecutionBatch Run(PlanEntry entry, Func<PlanEntry, PlanItem, string?>? check = null) =>
    _executor.Execute("b1", Now, [entry], check ?? ((_, _) => null), CancellationToken.None);

  [Fact]
  public void Merge_Copies_Into_The_Keeper_Then_Moves_The_Other_Copies_To_The_Recycle_Area()
  {
    var entry = Plan(BuildStack());

    var batch = Run(entry);

    Assert.All(batch.Items, i => Assert.Equal(BatchItemState.Done, i.State));
    Assert.Equal("only here", File.ReadAllText(_tree.PathOf(@"keeper\only-backup.txt")));
    Assert.False(Directory.Exists(_tree.PathOf("backup")));
    Assert.False(Directory.Exists(_tree.PathOf("desk")));
    foreach (var recycled in batch.Items.Where(i => i.Kind == PlanItemKind.Recycle))
    {
      Assert.StartsWith(Path.Combine(_appData, "recycle-areas", "b1"), recycled.RecycledPath, StringComparison.OrdinalIgnoreCase);
      Assert.True(File.Exists(Path.Combine(recycled.RecycledPath!, "sub", "b.txt")));
    }
    // The copied file keeps the source's modification time (TempTree writes 2024-03-11) and no partial file is left.
    Assert.Equal(new DateTime(2024, 3, 11, 12, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(_tree.PathOf(@"keeper\only-backup.txt")));
    Assert.Empty(Directory.EnumerateFiles(_tree.PathOf("keeper"), "*.fma-partial", SearchOption.AllDirectories));
  }

  [Fact]
  public void An_Existing_File_In_The_Keeper_Is_Never_Overwritten_And_The_Stack_Is_Not_Recycled()
  {
    var entry = Plan(BuildStack());
    _tree.Write(@"keeper\only-backup.txt", "the keeper's own, created after the decision");

    var batch = Run(entry);

    var copy = Assert.Single(batch.Items, i => i.Kind == PlanItemKind.Copy);
    Assert.Equal(BatchItemState.Failed, copy.State);
    Assert.Equal("the keeper's own, created after the decision", File.ReadAllText(_tree.PathOf(@"keeper\only-backup.txt")));
    Assert.All(batch.Items.Where(i => i.Kind == PlanItemKind.Recycle), i => Assert.Equal(BatchItemState.Skipped, i.State));
    Assert.True(Directory.Exists(_tree.PathOf("backup")));
    Assert.True(Directory.Exists(_tree.PathOf("desk")));
  }

  [Fact]
  public void An_Item_That_Changed_Right_Before_It_Runs_Is_Skipped()
  {
    var entry = Plan(BuildStack());

    var batch = Run(entry, (_, item) => item.Kind == PlanItemKind.Recycle && item.SourcePath.EndsWith("desk", StringComparison.Ordinal) ? "changed" : null);

    var desk = Assert.Single(batch.Items, i => i.OriginalPath?.EndsWith("desk", StringComparison.Ordinal) == true);
    Assert.Equal(BatchItemState.Skipped, desk.State);
    Assert.True(Directory.Exists(_tree.PathOf("desk")));
    Assert.False(Directory.Exists(_tree.PathOf("backup")));
  }

  [Fact]
  public void A_Protected_Copy_Is_Never_Recycled_Even_If_The_Plan_Says_So()
  {
    var stack = BuildStack();
    var entry = Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));
    // Simulate a stale plan: the backup became protected after the plan was built.
    var protectedStack = stack with { Copies = stack.Copies.Select(c => c.Id == "backup" ? c with { IsProtected = true } : c).ToList() };
    entry = entry with { Stack = protectedStack };

    var batch = Run(entry);

    var backup = Assert.Single(batch.Items, i => i.OriginalPath?.EndsWith("backup", StringComparison.Ordinal) == true);
    Assert.Equal(BatchItemState.Failed, backup.State);
    Assert.True(Directory.Exists(_tree.PathOf("backup")));
  }

  [Fact]
  public void A_File_In_Use_Stays_Behind_And_Moves_On_Retry()
  {
    var stack = BuildStack();
    var entry = Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));
    BatchItem desk;
    using (new FileStream(_tree.PathOf(@"desk\sub\b.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
      var batch = Run(entry);
      desk = Assert.Single(batch.Items, i => i.OriginalPath?.EndsWith("desk", StringComparison.Ordinal) == true);
      Assert.Equal(BatchItemState.Failed, desk.State);
      Assert.Equal(1, desk.FailedFiles);
      Assert.Contains("b.txt", desk.FailureReason, StringComparison.Ordinal);
      Assert.True(File.Exists(_tree.PathOf(@"desk\sub\b.txt")));
      Assert.False(File.Exists(_tree.PathOf(@"desk\a.txt")));
    }

    var retried = PlanExecutor.Retry(desk);

    Assert.Equal(BatchItemState.Done, retried.State);
    Assert.True(retried.Retried);
    Assert.False(Directory.Exists(_tree.PathOf("desk")));
    Assert.True(File.Exists(Path.Combine(retried.RecycledPath!, "sub", "b.txt")));
  }

  [Fact]
  public void Restore_Puts_Copies_Back_And_Recycles_What_Was_Copied_In()
  {
    var entry = Plan(BuildStack());
    var batch = Run(entry);

    foreach (var item in batch.Items)
    {
      var outcome = _executor.Restore("b1", item, Now);
      Assert.Null(outcome.Conflict);
      Assert.Equal(BatchItemState.Restored, outcome.Item.State);
    }

    Assert.True(File.Exists(_tree.PathOf(@"backup\only-backup.txt")));
    Assert.True(File.Exists(_tree.PathOf(@"desk\sub\b.txt")));
    Assert.False(File.Exists(_tree.PathOf(@"keeper\only-backup.txt")));
  }

  [Fact]
  public void Restore_Never_Overwrites_And_A_Conflict_Can_Restore_Under_Another_Name()
  {
    var stack = BuildStack();
    var batch = Run(Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly)));
    var desk = batch.Items.Single(i => i.OriginalPath?.EndsWith("desk", StringComparison.Ordinal) == true);
    _tree.Write(@"desk\new.txt", "a new folder was made where the copy was");

    var outcome = _executor.Restore("b1", desk, Now);

    Assert.NotNull(outcome.Conflict);
    Assert.Equal(BatchItemState.Done, outcome.Item.State);
    Assert.False(File.Exists(_tree.PathOf(@"desk\a.txt")));

    var placed = PlanExecutor.Resolve(outcome.Conflict!, desk, RestoreConflictChoice.RenameAtOriginal, null, Now);

    Assert.Equal(_tree.PathOf("desk (還原 10-23)"), placed);
    Assert.True(File.Exists(_tree.PathOf(@"desk (還原 10-23)\a.txt")));
    Assert.True(File.Exists(_tree.PathOf(@"desk\new.txt")));
  }

  [Fact]
  public void Emptying_Deletes_Only_What_The_Batch_Put_In_The_Recycle_Area()
  {
    var stack = BuildStack();
    var batch = Run(Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly)));

    _executor.Empty(batch);

    Assert.False(Directory.Exists(Path.Combine(_appData, "recycle-areas", "b1")));
    Assert.True(File.Exists(_tree.PathOf(@"keeper\a.txt")));
  }

  [Fact]
  public void Copy_Verified_Refuses_To_Overwrite()
  {
    var source = _tree.Write("s.txt", "source");
    var destination = _tree.Write("d.txt", "already here");

    Assert.Throws<IOException>(() => PlanExecutor.CopyVerified(source, destination));
    Assert.Equal("already here", File.ReadAllText(destination));
  }
}
