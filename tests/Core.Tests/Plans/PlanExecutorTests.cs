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

  private ExecutionBatch Run(PlanEntry entry, Func<PlanEntry, PlanItem, string?>? check = null, Action<BatchItem>? record = null) =>
    _executor.Execute("b1", Now, [entry], check ?? ((_, _) => null), record ?? (_ => { }), CancellationToken.None);

  private PlanEntry KeepKeeperOnly()
  {
    var stack = BuildStack();
    return Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));
  }

  private static BatchItem Desk(ExecutionBatch batch) =>
    batch.Items.Single(i => i.OriginalPath?.EndsWith("desk", StringComparison.Ordinal) == true);

  private static void CreateJunction(string junctionPath, string targetPath)
  {
    using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
      FileName = "cmd.exe",
      ArgumentList = { "/c", "mklink", "/J", junctionPath, targetPath },
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    })!;
    process.WaitForExit();
    Assert.True(process.ExitCode == 0 && Directory.Exists(junctionPath), "Could not create a test junction with mklink /J.");
  }

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
  public void Every_Item_Is_Journalled_With_Its_Paths_Before_It_Acts()
  {
    var entry = Plan(BuildStack());
    var journal = new List<(BatchItem Item, bool OriginalThere, bool CreatedThere)>();

    var batch = Run(entry, record: item => journal.Add((item,
      item.OriginalPath is { } o && (File.Exists(o) || Directory.Exists(o)),
      item.CreatedPath is { } c && File.Exists(c))));

    foreach (var done in batch.Items)
    {
      // The first record of every item that ran is "Running", written while nothing has moved yet.
      var first = journal.First(j => j.Item.Id == done.Id);
      Assert.Equal(BatchItemState.Running, first.Item.State);
      if (done.Kind == PlanItemKind.Recycle)
      {
        Assert.True(first.OriginalThere);
        Assert.Equal(done.RecycledPath, first.Item.RecycledPath);
      }
      else
      {
        Assert.False(first.CreatedThere);
        Assert.Equal(done.CreatedPath, first.Item.CreatedPath);
        Assert.NotNull(first.Item.SourceStamp);
      }
      Assert.Equal(BatchItemState.Done, journal.Last(j => j.Item.Id == done.Id).Item.State);
    }
  }

  [Fact]
  public void A_Partly_Moved_Folder_Is_Never_Emptied_And_Can_Be_Put_Back()
  {
    var entry = KeepKeeperOnly();
    BatchItem desk;
    ExecutionBatch batch;
    using (new FileStream(_tree.PathOf(@"desk\sub\b.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
      batch = Run(entry);
      desk = Desk(batch);
    }
    Assert.Equal(BatchItemState.Failed, desk.State);
    Assert.True(desk.CanRestore);
    var moved = Path.Combine(desk.RecycledPath!, "a.txt");
    Assert.True(File.Exists(moved));

    var (emptied, complete) = _executor.Empty(batch);

    Assert.False(complete);
    Assert.DoesNotContain(desk.Id, emptied);
    Assert.True(File.Exists(moved));

    var restored = _executor.Restore("b1", desk, Now);

    Assert.Equal(BatchItemState.Restored, restored.Item.State);
    Assert.Equal("same a", File.ReadAllText(_tree.PathOf(@"desk\a.txt")));
    Assert.True(File.Exists(_tree.PathOf(@"desk\sub\b.txt")));
  }

  [Fact]
  public void Retry_Moves_Only_Files_That_Are_Still_Exactly_As_They_Were()
  {
    var entry = KeepKeeperOnly();
    BatchItem desk;
    using (new FileStream(_tree.PathOf(@"desk\sub\b.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
      desk = Desk(Run(entry));
    }
    // After the run, the user edits the file that stayed behind and saves a new one next to it.
    _tree.Write(@"desk\sub\b.txt", "edited after the run", new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc));
    _tree.Write(@"desk\new.txt", "made after the run");

    var retried = PlanExecutor.Retry(desk);

    Assert.Equal(BatchItemState.Failed, retried.State);
    Assert.Equal(2, retried.FailedFiles);
    Assert.Equal("edited after the run", File.ReadAllText(_tree.PathOf(@"desk\sub\b.txt")));
    Assert.True(File.Exists(_tree.PathOf(@"desk\new.txt")));
    Assert.False(File.Exists(Path.Combine(desk.RecycledPath!, "new.txt")));
  }

  [Fact]
  public void Retry_Of_A_Copy_Refuses_A_Source_Edited_Since()
  {
    var entry = Plan(BuildStack());
    _tree.Write(@"keeper\only-backup.txt", "in the way");
    var copy = Run(entry).Items.Single(i => i.Kind == PlanItemKind.Copy);
    Assert.Equal(BatchItemState.Failed, copy.State);
    File.Delete(_tree.PathOf(@"keeper\only-backup.txt"));
    _tree.Write(@"backup\only-backup.txt", "edited after the run", new DateTime(2026, 10, 23, 8, 0, 0, DateTimeKind.Utc));

    var retried = PlanExecutor.Retry(copy);

    Assert.Equal(BatchItemState.Failed, retried.State);
    Assert.False(File.Exists(_tree.PathOf(@"keeper\only-backup.txt")));
  }

  [Fact]
  public void Restore_Leaves_A_Copied_File_The_User_Edited_Afterwards()
  {
    var batch = Run(Plan(BuildStack()));
    var copy = batch.Items.Single(i => i.Kind == PlanItemKind.Copy);
    _tree.Write(@"keeper\only-backup.txt", "the user kept working on it", new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc));

    var outcome = _executor.Restore("b1", copy, Now);

    Assert.Equal(BatchItemState.Done, outcome.Item.State);
    Assert.NotNull(outcome.Item.FailureReason);
    Assert.Equal("the user kept working on it", File.ReadAllText(_tree.PathOf(@"keeper\only-backup.txt")));
  }

  [Fact]
  public void Emptying_Removes_A_Junction_Without_Touching_What_It_Points_To()
  {
    _tree.Write(@"elsewhere\precious.txt", "not part of any copy");
    var entry = KeepKeeperOnly();
    CreateJunction(_tree.PathOf(@"desk\link"), _tree.PathOf("elsewhere"));
    var batch = Run(entry);
    Assert.Equal(BatchItemState.Done, Desk(batch).State);

    var (_, complete) = _executor.Empty(batch);

    Assert.True(complete);
    Assert.Equal("not part of any copy", File.ReadAllText(_tree.PathOf(@"elsewhere\precious.txt")));
    Assert.False(Directory.Exists(Desk(batch).RecycledPath));
  }

  [Fact]
  public void A_Folder_That_Vanished_Before_Its_Move_Fails_Without_Moving_Anything()
  {
    var entry = KeepKeeperOnly();

    var batch = Run(entry, (_, item) =>
    {
      if (item.SourcePath.EndsWith("desk", StringComparison.Ordinal) && Directory.Exists(item.SourcePath))
      {
        Directory.Move(item.SourcePath, item.SourcePath + "-renamed");
      }
      return null;
    });

    var desk = Desk(batch);
    Assert.Equal(BatchItemState.Failed, desk.State);
    Assert.False(desk.CanRestore);
    Assert.True(File.Exists(_tree.PathOf(@"desk-renamed\a.txt")));
  }

  [Fact]
  public void Replace_Keeps_The_Keepers_Version_In_The_Recycle_Area_And_Restore_Brings_It_Back()
  {
    foreach (var copy in new[] { "keeper", "backup" })
    {
      _tree.Write($@"{copy}\same.txt", "same");
    }
    _tree.Write(@"keeper\report.txt", "keeper's older report", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    _tree.Write(@"backup\report.txt", "backup's newer report", new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
    var stack = new Stack("stack", "project", StackKind.Folder,
      [Copy("keeper", _tree.PathOf("keeper")), Copy("backup", _tree.PathOf("backup"))],
      [new StackRow("report.txt", [new FileVersion("k", Now.AddDays(-30), 21), new FileVersion("b", Now, 21)])],
      0, StackAction.Merge) { SameFileCount = 1 };
    var entry = Plan(stack, StackDecision.Suggested(stack).WithChoice("report.txt", ConflictChoice.Theirs));
    Assert.Contains(entry.Items, i => i.Kind == PlanItemKind.Replace);

    var batch = Run(entry);

    var replace = batch.Items.Single(i => i.Kind == PlanItemKind.Replace);
    Assert.Equal(BatchItemState.Done, replace.State);
    Assert.Equal("backup's newer report", File.ReadAllText(_tree.PathOf(@"keeper\report.txt")));
    Assert.Equal("keeper's older report", File.ReadAllText(replace.RecycledPath!));

    var restored = _executor.Restore("b1", replace, Now);

    Assert.Equal(BatchItemState.Restored, restored.Item.State);
    Assert.Equal("keeper's older report", File.ReadAllText(_tree.PathOf(@"keeper\report.txt")));
    Assert.Equal("backup's newer report", File.ReadAllText(restored.Item.UndonePath!));
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
