using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
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

  /// <summary>The same temp path through the administrative share, or null when that share is not reachable here.</summary>
  private static string? Unc(string path)
  {
    var unc = $@"\\localhost\{path[0]}${path[2..]}";
    return Directory.Exists(Path.GetDirectoryName(unc)) ? unc : null;
  }

  private static PlanEntry Only(PlanEntry entry, Func<PlanItem, bool> enabled) =>
    entry with { Items = entry.Items.Select(i => i with { Enabled = enabled(i) }).ToList() };

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

    var retried = _executor.Retry("b1", desk);

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

    // While the backup (the file's source) is in the recycle area, the copy is not undone: that would leave the file
    // only in a recycle area.
    var copy = batch.Items.Single(i => i.Kind == PlanItemKind.Copy);
    var refused = _executor.Restore("b1", copy, Now);
    Assert.Equal(BatchItemState.Done, refused.Item.State);
    Assert.NotNull(refused.Item.FailureReason);
    Assert.True(File.Exists(_tree.PathOf(@"keeper\only-backup.txt")));

    foreach (var item in batch.Items.OrderBy(i => i.Kind == PlanItemKind.Recycle ? 0 : 1))
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

    var retried = _executor.Retry("b1", desk);

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

    var retried = _executor.Retry("b1", copy);

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

    // The backup's version only exists in the keeper while the backup is recycled, so it comes back first.
    Assert.Equal(BatchItemState.Done, _executor.Restore("b1", replace, Now).Item.State);
    Assert.Equal(BatchItemState.Restored, _executor.Restore("b1", batch.Items.Single(i => i.Kind == PlanItemKind.Recycle), Now).Item.State);
    var restored = _executor.Restore("b1", replace, Now);

    Assert.Equal(BatchItemState.Restored, restored.Item.State);
    Assert.Equal("keeper's older report", File.ReadAllText(_tree.PathOf(@"keeper\report.txt")));
    Assert.Equal("backup's newer report", File.ReadAllText(restored.Item.UndonePath!));
  }

  [Fact]
  public void A_Copy_That_Is_The_Keeper_Under_Another_Name_Is_Never_Recycled()
  {
    _tree.Write(@"keeper\a.txt", "a");
    _tree.Write(@"keeper\sub\b.txt", "b");
    CreateJunction(_tree.PathOf("alias"), _tree.PathOf("keeper"));
    var stack = new Stack("stack", "project", StackKind.Folder,
      [Copy("keeper", _tree.PathOf("keeper")), Copy("alias", _tree.PathOf("alias"))], [], 0, StackAction.KeepKeeperOnly) { SameFileCount = 2 };

    var batch = Run(Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly)));

    Assert.Equal(BatchItemState.Failed, Assert.Single(batch.Items).State);
    Assert.Null(batch.Items[0].RecycledPath);
    Assert.True(File.Exists(_tree.PathOf(@"keeper\sub\b.txt")));
    Directory.Delete(_tree.PathOf("alias"));
  }

  [Fact]
  public void A_Copy_That_Is_The_Keeper_Through_A_Network_Path_Is_Never_Recycled()
  {
    _tree.Write(@"keeper\a.txt", "a");
    if (Unc(_tree.PathOf("keeper")) is not { } alias || Unc(_appData) is not { } appData)
    {
      return; // The administrative share is turned off on this machine; the junction test covers the same check.
    }
    Directory.CreateDirectory(_appData);
    var executor = new PlanExecutor(new RecycleAreaLocator(appData));
    var stack = new Stack("stack", "project", StackKind.Folder,
      [Copy("keeper", _tree.PathOf("keeper")), Copy("alias", alias)], [], 0, StackAction.KeepKeeperOnly) { SameFileCount = 1 };
    var entry = Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));

    var batch = executor.Execute("b1", Now, [entry], (_, _) => null, _ => { }, CancellationToken.None);

    Assert.Equal(BatchItemState.Failed, Assert.Single(batch.Items).State);
    Assert.True(File.Exists(_tree.PathOf(@"keeper\a.txt")));
  }

  [Fact]
  public void A_Copy_Holding_The_Apps_Own_Folder_Is_Never_Recycled()
  {
    var entry = KeepKeeperOnly();
    var executor = new PlanExecutor(new RecycleAreaLocator(_tree.PathOf(@"desk\app-data")));

    var batch = executor.Execute("b1", Now, [entry], (_, _) => null, _ => { }, CancellationToken.None);

    Assert.Equal(BatchItemState.Failed, Desk(batch).State);
    Assert.True(File.Exists(_tree.PathOf(@"desk\a.txt")));
  }

  [Fact]
  public void Undoing_A_Copy_Waits_Until_Its_Source_Is_Back_Even_When_Another_Run_Recycled_It()
  {
    var entry = Plan(BuildStack());
    // Run 1 copies the backup's only file into the keeper but keeps the backup; run 2 recycles the backup.
    var copy = Run(Only(entry, i => i.Kind != PlanItemKind.Recycle)).Items.Single(i => i.Kind == PlanItemKind.Copy);
    var second = _executor.Execute("b2", Now, [Only(entry, i => i.Kind == PlanItemKind.Recycle && i.SourcePath.EndsWith("backup", StringComparison.Ordinal))],
      (_, _) => null, _ => { }, CancellationToken.None);
    Assert.Equal(BatchItemState.Done, second.Items.Single(i => i.State != BatchItemState.Skipped).State);

    var outcome = _executor.Restore("b1", copy, Now);

    Assert.Equal(BatchItemState.Done, outcome.Item.State);
    Assert.Contains("先還原", outcome.Item.FailureReason, StringComparison.Ordinal);
    Assert.Equal("only here", File.ReadAllText(_tree.PathOf(@"keeper\only-backup.txt")));
  }

  [Fact]
  public void Emptying_Keeps_What_It_Cannot_Reach_And_It_Stays_Restorable()
  {
    var batch = Run(KeepKeeperOnly());
    var area = Path.Combine(_appData, "recycle-areas", "b1");
    // As if the drive holding the recycle area were unplugged.
    Directory.Move(area, area + "-away");

    var (emptied, complete) = _executor.Empty(batch);

    Assert.Empty(emptied);
    Assert.False(complete);
    Directory.Move(area + "-away", area);
    Assert.Equal(BatchItemState.Restored, _executor.Restore("b1", Desk(batch), Now).Item.State);
    Assert.True(File.Exists(_tree.PathOf(@"desk\sub\b.txt")));
  }

  [Fact]
  public void Unchecked_And_Cancelled_Items_Are_Recorded_As_Skipped()
  {
    var entry = Only(KeepKeeperOnly(), i => !i.SourcePath.EndsWith("desk", StringComparison.Ordinal));

    var batch = Run(entry);

    Assert.Equal(BatchItemState.Skipped, Desk(batch).State);
    Assert.True(Directory.Exists(_tree.PathOf("desk")));
    using var cancel = new CancellationTokenSource();
    cancel.Cancel();
    var cancelled = _executor.Execute("b2", Now, [KeepKeeperOnly()], (_, _) => null, _ => { }, cancel.Token);
    Assert.All(cancelled.Items, i => Assert.Equal(BatchItemState.Skipped, i.State));
  }

  [Fact]
  public void A_Locked_Single_File_Moves_On_Retry_Only_While_Unchanged()
  {
    _tree.Write(@"keeper\報價單.xlsx", "sheet");
    _tree.Write(@"desk\報價單 (1).xlsx", "sheet");
    var stack = new Stack("file", "報價單.xlsx", StackKind.File,
      [Copy("keeper", _tree.PathOf(@"keeper\報價單.xlsx")), Copy("desk", _tree.PathOf(@"desk\報價單 (1).xlsx"))], [], 0, StackAction.KeepKeeperOnly);
    var entry = Plan(stack, StackDecision.Suggested(stack).WithAction(StackAction.KeepKeeperOnly));
    BatchItem desk;
    using (new FileStream(_tree.PathOf(@"desk\報價單 (1).xlsx"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
      desk = Assert.Single(Run(entry).Items);
    }
    Assert.Equal(BatchItemState.Failed, desk.State);
    Assert.False(desk.CanRestore);

    var retried = _executor.Retry("b1", desk);

    Assert.Equal(BatchItemState.Done, retried.State);
    Assert.False(File.Exists(_tree.PathOf(@"desk\報價單 (1).xlsx")));
    Assert.Equal("sheet", File.ReadAllText(retried.RecycledPath!));
    Assert.True(File.Exists(_tree.PathOf(@"keeper\報價單.xlsx")));
  }

  [Fact]
  public void A_Leftover_Partial_File_Does_Not_Block_A_Copy()
  {
    var source = _tree.Write("s.txt", "source");
    var destination = _tree.PathOf(@"keeper\d.txt");
    _tree.Write(@"keeper\d.txt" + ".fma-partial", "left by a crash");

    PlanExecutor.CopyVerified(source, destination);

    Assert.Equal("source", File.ReadAllText(destination));
    Assert.False(File.Exists(destination + ".fma-partial"));
  }

  [Fact]
  public void Undoing_A_Copy_Is_Refused_When_Another_File_Now_Sits_Where_Its_Source_Was()
  {
    var entry = Plan(BuildStack());
    var copy = Run(Only(entry, i => i.Kind != PlanItemKind.Recycle)).Items.Single(i => i.Kind == PlanItemKind.Copy);
    _executor.Execute("b2", Now, [Only(entry, i => i.Kind == PlanItemKind.Recycle && i.SourcePath.EndsWith("backup", StringComparison.Ordinal))],
      (_, _) => null, _ => { }, CancellationToken.None);
    // A different file with the same name appears where the recycled source was.
    _tree.Write(@"backup\only-backup.txt", "an unrelated file", new DateTime(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc));

    var outcome = _executor.Restore("b1", copy, Now);

    Assert.Equal(BatchItemState.Done, outcome.Item.State);
    Assert.Equal("only here", File.ReadAllText(_tree.PathOf(@"keeper\only-backup.txt")));
  }

  [Fact]
  public void A_Probe_Shows_Up_Under_Every_Name_Of_Its_Folder_And_Is_Gone_Afterwards()
  {
    _tree.Write(@"real\sub\a.txt", "a");
    _tree.Write(@"other\b.txt", "b");
    CreateJunction(_tree.PathOf("alias"), _tree.PathOf("real"));

    Assert.Equal(0, PathIdentity.ProbeSeenAt(_tree.PathOf("real"), [_tree.PathOf("alias")]));
    Assert.Equal(1, PathIdentity.ProbeSeenAt(_tree.PathOf("real"), PathIdentity.SelfAndAncestors(_tree.PathOf(@"alias\sub"))));
    Assert.Equal(-1, PathIdentity.ProbeSeenAt(_tree.PathOf("real"), PathIdentity.SelfAndAncestors(_tree.PathOf("other"))));
    Assert.Empty(Directory.EnumerateFiles(_tree.PathOf("real"), PathIdentity.ProbePrefix + "*"));
    Directory.Delete(_tree.PathOf("alias"));
  }

  [Fact]
  public void A_Cloned_Drive_Is_Not_The_Same_Volume_Even_With_The_Same_Serial_And_Ids()
  {
    var original = new FileIdentity(0x1234ABCD, 42, "Volume{11111111-1111-1111-1111-111111111111}");
    var clone = original with { VolumeGuid = "Volume{22222222-2222-2222-2222-222222222222}" };
    var throughShare = original with { VolumeGuid = null };

    Assert.False(original.SameAs(clone));
    Assert.True(original.SameAs(throughShare));
    Assert.True(PathIdentity.Identify(_tree.Root) is { VolumeGuid: not null });
  }

  [Fact]
  public void A_Snapshot_Taken_While_The_Copy_Changes_Is_Not_A_Baseline()
  {
    _tree.Write(@"copy\a.txt", "a");
    _tree.Write(@"copy\.git\refs\heads\main", "old");
    // The folders above were just created; the decision comes after them.
    var decided = DateTime.UtcNow.AddSeconds(1);
    Assert.DoesNotContain(CopySnapshot.ChangedPrefix, CopySnapshot.Of(_tree.PathOf("copy"), _ => false, decided), StringComparison.Ordinal);
    // Written after the decision (while the snapshot would have been walking).
    _tree.Write(@"copy\.git\refs\heads\unpushed", "new", DateTime.UtcNow.AddSeconds(2));

    Assert.StartsWith(CopySnapshot.ChangedPrefix, CopySnapshot.Of(_tree.PathOf("copy"), _ => false, decided), StringComparison.Ordinal);
  }

  [Fact]
  public void A_File_Dated_In_The_Future_Does_Not_Block_Its_Copy_Forever()
  {
    _tree.Write(@"copy\a.txt", "a");
    // A camera with a wrong clock: the file was there long before the decision, but its time is years ahead.
    _tree.Write(@"copy\photo.jpg", "jpeg", new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    var decided = DateTime.UtcNow.AddSeconds(1);

    var snapshot = CopySnapshot.Of(_tree.PathOf("copy"), _ => false, decided);

    Assert.DoesNotContain(CopySnapshot.ChangedPrefix, snapshot, StringComparison.Ordinal);
    Assert.Equal(snapshot, CopySnapshot.Of(_tree.PathOf("copy"), _ => false));
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
