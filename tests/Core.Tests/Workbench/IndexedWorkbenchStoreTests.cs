using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Workbench;

/// <summary>The real store end to end: add folders, scan them, find the stack, decide, re-check. Temp folders only.</summary>
public sealed class IndexedWorkbenchStoreTests : IDisposable
{
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
  private readonly TempTree _tree = new();
  private readonly string _indexPath = Path.Combine(TestEnvironment.DataRoot, $"store-{Guid.NewGuid():N}.db");

  public void Dispose() => _tree.Dispose();

  private readonly FixedVolumes _volumes = new();

  /// <summary>Every path is on one test volume; <see cref="Serial"/> changes to simulate another disk under the same letter.</summary>
  private sealed class FixedVolumes : IVolumeInfoProvider
  {
    public string Serial { get; set; } = "1234-ABCD";

    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, Serial, "TEST", VolumeKind.Fixed);
  }

  private IndexedWorkbenchStore Open(string? indexPath = null) =>
    IndexedWorkbenchStore.Open(indexPath ?? _indexPath, _volumes, context: null, startWorker: true);

  private static async Task Rescan(IndexedWorkbenchStore store, string sourceId)
  {
    await store.ScanSourceAsync(sourceId);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
  }

  private string SourceId(IndexedWorkbenchStore store, string folder) => store.Sources.Single(s => s.RootPath == _tree.PathOf(folder)).Id;

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

  /// <summary>
  /// work\invoice-tool (protected keeper), backup\invoice-tool-backup (stale), desk\invoice-tool - 複製 (newer receipt,
  /// an extra notes file).
  /// </summary>
  private void BuildCopies()
  {
    _tree.Project(@"work\invoice-tool");
    _tree.Project(@"backup\invoice-tool-backup");
    _tree.Project(@"desk\invoice-tool - 複製");
    _tree.Write(@"work\invoice-tool\receipt.html", "<p>receipt v2</p>", new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc));
    _tree.Write(@"backup\invoice-tool-backup\receipt.html", "<p>receipt v1</p>", new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc));
    _tree.Write(@"desk\invoice-tool - 複製\receipt.html", "<p>receipt v3, newest</p>", new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc));
    _tree.Write(@"desk\invoice-tool - 複製\notes.md", "only on the desktop");
    _tree.Write(@"desk\loose\報價單.xlsx", "spreadsheet bytes");
    _tree.Write(@"backup\報價單 (1).xlsx", "spreadsheet bytes");
  }

  private async Task<IndexedWorkbenchStore> ScannedStore()
  {
    BuildCopies();
    var store = Open();
    var problems = await store.AddSourcesAsync([_tree.PathOf("work"), _tree.PathOf("backup"), _tree.PathOf("desk")],
      new SourceOptions(true, true, [_tree.PathOf("work")]), startScan: true);
    Assert.Empty(problems);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    return store;
  }

  [Fact]
  public async Task Scanning_Real_Folders_Finds_The_Folder_Stack_And_The_File_Stack()
  {
    using var store = await ScannedStore();

    Assert.Equal(3, store.Sources.Count);
    Assert.All(store.Sources, s => Assert.Equal(SourceState.Online, s.State));
    Assert.Contains(store.Sources, s => s.IsProtected);

    var folder = Assert.Single(store.Stacks, s => s.Kind == StackKind.Folder);
    Assert.Equal(3, folder.Copies.Count);
    Assert.Equal(_tree.PathOf(@"work\invoice-tool"), folder.SuggestedKeeperCopy.Path);
    Assert.Equal(14, folder.SameFileCount);
    Assert.Equal(["notes.md", "receipt.html"], folder.Rows.Select(r => r.RelativePath));
    Assert.Equal(StackAction.Merge, folder.SuggestedAction);

    var file = Assert.Single(store.Stacks, s => s.Kind == StackKind.File);
    Assert.Equal("報價單.xlsx", file.Title);
    Assert.Equal(2, file.Copies.Count);
  }

  [Fact]
  public async Task Decisions_Make_A_Plan_That_Survives_Reopening()
  {
    string stackId;
    using (var store = await ScannedStore())
    {
      var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
      stackId = folder.Id;
      store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));

      var entry = Assert.Single(store.Plan);
      Assert.Contains(entry.Items, i => i.Kind == PlanItemKind.Copy && i.Target == "notes.md");
      Assert.Contains(entry.Items, i => i.Kind == PlanItemKind.SaveAs);
      Assert.Equal(2, entry.Items.Count(i => i.Kind == PlanItemKind.Recycle));
    }

    using var reopened = Open();
    Assert.Equal(StackStatus.Pending, reopened.Stacks.Single(s => s.Id == stackId).Status);
    Assert.Single(reopened.Plan);
    Assert.NotNull(reopened.DecisionFor(stackId));
  }

  [Fact]
  public async Task Recheck_Confirms_Unchanged_Items_And_Stops_Changed_Ones()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Keeper));

    await store.RecheckPlanAsync();
    Assert.All(store.Plan[0].Items, i => Assert.Equal(PlanCheck.Confirmed, i.Check));
    Assert.NotNull(store.PlanCheckedAt);

    _tree.Write(@"backup\invoice-tool-backup\src\module03.ts", "edited after the decision", new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc));
    await store.RecheckPlanAsync();

    var changed = Assert.Single(store.Plan[0].Items, i => i.Check == PlanCheck.Changed);
    Assert.Equal(PlanItemKind.Recycle, changed.Kind);
    Assert.Contains("module03.ts", changed.ChangeDetail, StringComparison.Ordinal);
  }

  [Fact]
  public async Task A_New_Copy_Reopens_A_Decided_Stack_With_The_Earlier_Choices()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    Assert.Single(store.Plan);

    // Another copy turns up on the desktop; the stack's identity changes with its copies.
    _tree.Project(@"desk\invoice-tool-main (1)");
    await store.ScanSourceAsync(store.Sources.Single(s => s.RootPath == _tree.PathOf("desk")).Id);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();

    var reopened = Assert.Single(store.Stacks, s => s.Kind == StackKind.Folder);
    Assert.NotEqual(folder.Id, reopened.Id);
    Assert.Equal(4, reopened.Copies.Count);
    Assert.Equal(StackStatus.Open, reopened.Status);
    Assert.Equal([_tree.PathOf(@"desk\invoice-tool-main (1)")], reopened.NewCopyIds.Select(id => reopened.Copies.Single(c => c.Id == id).Path));
    Assert.Empty(store.Plan);
    var carried = store.DecisionFor(reopened.Id);
    Assert.NotNull(carried);
    Assert.Equal(ConflictChoice.Both, carried!.ChoiceFor("receipt.html"));

    // Deciding again records the new membership; the stack is pending without the "new copy" mark.
    store.Decide(carried);
    var pending = store.Stacks.Single(s => s.Id == reopened.Id);
    Assert.Equal(StackStatus.Pending, pending.Status);
    Assert.Empty(pending.NewCopyIds);
  }

  [Fact]
  public async Task Running_The_Plan_Consolidates_The_Stack_And_Restoring_Brings_It_Back()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));

    var batch = await store.ExecutePlanAsync(CancellationToken.None);

    Assert.All(batch.Items, i => Assert.Equal(BatchItemState.Done, i.State));
    Assert.True(File.Exists(_tree.PathOf(@"work\invoice-tool\notes.md")));
    Assert.Single(Directory.EnumerateFiles(_tree.PathOf(@"work\invoice-tool"), "receipt.from-*.html"));
    Assert.False(Directory.Exists(_tree.PathOf(@"backup\invoice-tool-backup")));
    Assert.False(Directory.Exists(_tree.PathOf(@"desk\invoice-tool - 複製")));
    Assert.Empty(store.Plan);
    Assert.Equal(1, store.ConvergedCount);
    Assert.Equal(batch.Id, store.History[0].Id);
    Assert.Contains(store.RecycleAreas, a => a.Bytes > 0);

    // The index catches up with the disk: the stack dissolves once only the keeper is left.
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.DoesNotContain(store.Stacks, s => s.Kind == StackKind.Folder);
    Assert.Equal(1, store.ConvergedCount);

    await store.RestoreBatchAsync(batch.Id);
    Assert.All(store.History[0].Items, i => Assert.Equal(BatchItemState.Restored, i.State));
    Assert.True(Directory.Exists(_tree.PathOf(@"backup\invoice-tool-backup")));
    Assert.False(File.Exists(_tree.PathOf(@"work\invoice-tool\notes.md")));
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();
    Assert.Equal(StackStatus.Open, Assert.Single(store.Stacks, s => s.Kind == StackKind.Folder).Status);
    Assert.Equal(0, store.ConvergedCount);
  }

  [Fact]
  public async Task Emptying_The_Recycle_Areas_Ends_Restore_For_Earlier_Runs()
  {
    using var store = await ScannedStore();
    var file = store.Stacks.Single(s => s.Kind == StackKind.File);
    store.Decide(StackDecision.Suggested(file));
    var batch = await store.ExecutePlanAsync(CancellationToken.None);
    Assert.Contains(store.RecycleAreas, a => a.Bytes > 0);

    await store.EmptyRecycleAreasAsync();

    Assert.NotNull(store.History.Single(b => b.Id == batch.Id).EmptiedAt);
    Assert.DoesNotContain(store.RecycleAreas, a => a.Bytes > 0);
    var recycled = batch.Items.Single(i => i.Kind == PlanItemKind.Recycle).RecycledPath!;
    Assert.False(File.Exists(recycled));
  }

  [Fact]
  public async Task Nested_Or_Repeated_Folders_Are_Not_Added_Twice()
  {
    _tree.Write(@"root\inner\a.txt", "a");
    using var store = Open();
    await store.AddSourcesAsync([_tree.PathOf("root")], new SourceOptions(true, true, []), startScan: false);

    var problems = await store.AddSourcesAsync([_tree.PathOf("root"), _tree.PathOf(@"root\inner"), _tree.PathOf("missing")],
      new SourceOptions(true, true, []), startScan: false);

    Assert.Equal(3, problems.Count);
    Assert.Single(store.Sources);
  }

  [Fact]
  public async Task A_Copy_Whose_Files_Changed_After_The_Decision_Goes_Back_For_Another_Look()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    Assert.Single(store.Plan);

    // The same copies, so the same stack, but the backup now has work the user has not seen.
    _tree.Write(@"backup\invoice-tool-backup\src\module05.ts", "edited after the decision", new DateTime(2026, 10, 23, 9, 0, 0, DateTimeKind.Utc));
    await Rescan(store, SourceId(store, "backup"));

    var stack = store.Stacks.Single(s => s.Id == folder.Id);
    Assert.Equal(StackStatus.Open, stack.Status);
    Assert.Equal([_tree.PathOf(@"backup\invoice-tool-backup")], stack.ChangedCopyIds.Select(id => stack.Copies.Single(c => c.Id == id).Path));
    Assert.Empty(store.Plan);
    Assert.NotNull(store.DecisionFor(folder.Id));

    // Reopening the app does not bring the old plan back.
    store.Dispose();
    using var reopened = Open();
    Assert.Empty(reopened.Plan);
  }

  [Fact]
  public async Task A_Keeper_Emptied_After_The_Decision_Stops_The_Whole_Stack()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    // The index has not seen this yet: the user cleared the keeper folder.
    foreach (var file in Directory.EnumerateFiles(_tree.PathOf(@"work\invoice-tool"), "*", SearchOption.AllDirectories))
    {
      File.Delete(file);
    }

    var batch = await store.ExecutePlanAsync(CancellationToken.None);

    Assert.All(batch.Items, i => Assert.Equal(BatchItemState.Skipped, i.State));
    Assert.Contains("正本", batch.Items[0].FailureReason, StringComparison.Ordinal);
    Assert.True(Directory.Exists(_tree.PathOf(@"backup\invoice-tool-backup\src")));
    Assert.True(Directory.Exists(_tree.PathOf(@"desk\invoice-tool - 複製\src")));
    Assert.Equal(0, store.ConvergedCount);
  }

  [Fact]
  public async Task A_Copy_Edited_Right_Before_The_Run_Is_Not_Recycled_Even_If_The_Index_Is_Stale()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    _tree.Write(@"desk\invoice-tool - 複製\todo.md", "written after the decision");

    var batch = await store.ExecutePlanAsync(CancellationToken.None);

    var desk = batch.Items.Single(i => i.OriginalPath == _tree.PathOf(@"desk\invoice-tool - 複製"));
    Assert.Equal(BatchItemState.Skipped, desk.State);
    Assert.Contains("todo.md", desk.FailureReason, StringComparison.Ordinal);
    Assert.True(File.Exists(_tree.PathOf(@"desk\invoice-tool - 複製\todo.md")));
    Assert.Equal(0, store.ConvergedCount);
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Kind == StackKind.Folder).Status);
  }

  [Fact]
  public async Task A_Stack_Converges_Only_When_Every_Item_Finished()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    ExecutionBatch batch;
    using (new FileStream(_tree.PathOf(@"desk\invoice-tool - 複製\src\module01.ts"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
      batch = await store.ExecutePlanAsync(CancellationToken.None);
    }

    Assert.Equal(1, batch.Failed);
    Assert.Equal(0, store.ConvergedCount);
    // Let the update scans the run queued finish, so none of them is reading the file the retry moves.
    await store.WaitForScansAsync(Timeout);

    await store.RetryFailedAsync(batch.Id);

    Assert.All(store.History.Single(b => b.Id == batch.Id).Items, i => Assert.Equal(BatchItemState.Done, i.State));
    Assert.Equal(1, store.ConvergedCount);
    Assert.False(Directory.Exists(_tree.PathOf(@"desk\invoice-tool - 複製")));
  }

  [Fact]
  public async Task A_Pending_Decision_Whose_Keeper_Is_Offline_Waits_Without_Errors()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));

    Directory.Move(_tree.PathOf("work"), _tree.PathOf("work-unplugged"));
    store.RefreshNow();

    Assert.Equal(SourceState.Offline, store.Sources.Single(s => s.RootPath == _tree.PathOf("work")).State);
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == folder.Id).Status);
    Assert.Empty(store.Plan);

    Directory.Move(_tree.PathOf("work-unplugged"), _tree.PathOf("work"));
    store.RefreshNow();

    Assert.Equal(StackStatus.Pending, store.Stacks.Single(s => s.Id == folder.Id).Status);
    Assert.Single(store.Plan);
  }

  [Fact]
  public async Task Another_Disk_Under_The_Same_Letter_Is_Not_Scanned_As_The_Source()
  {
    _tree.Write(@"usb\a.txt", "a");
    using var store = Open();
    await store.AddSourcesAsync([_tree.PathOf("usb")], new SourceOptions(true, true, []), startScan: false);

    _volumes.Serial = "9999-0000";
    await Rescan(store, SourceId(store, "usb"));

    Assert.Equal(ScanJobState.WaitingForSource, Assert.Single(store.Jobs).State);
    Assert.Equal(SourceState.Offline, Assert.Single(store.Sources).State);
  }

  [Fact]
  public async Task A_Folder_Can_Be_Added_While_Another_Source_Is_Offline()
  {
    _tree.Write(@"usb\a.txt", "a");
    _tree.Write(@"docs\b.txt", "b");
    using var store = Open();
    await store.AddSourcesAsync([_tree.PathOf("usb")], new SourceOptions(true, true, []), startScan: false);
    Directory.Move(_tree.PathOf("usb"), _tree.PathOf("usb-unplugged"));

    var problems = await store.AddSourcesAsync([_tree.PathOf("docs")], new SourceOptions(true, true, []), startScan: false);

    Assert.Empty(problems);
    Assert.Equal(2, store.Sources.Count);
  }

  [Fact]
  public async Task A_Junction_To_A_Source_Is_Not_Added_As_Another_Source()
  {
    _tree.Write(@"real\a.txt", "a");
    CreateJunction(_tree.PathOf("alias"), _tree.PathOf("real"));
    using var store = Open();
    await store.AddSourcesAsync([_tree.PathOf("real")], new SourceOptions(true, true, []), startScan: false);

    var problems = await store.AddSourcesAsync([_tree.PathOf("alias")], new SourceOptions(true, true, []), startScan: false);

    Assert.Contains(_tree.PathOf("real"), Assert.Single(problems), StringComparison.OrdinalIgnoreCase);
    Assert.Single(store.Sources);
    Directory.Delete(_tree.PathOf("alias"));
  }

  [Fact]
  public async Task The_Apps_Own_Folders_And_Recycle_Areas_Are_Never_Scanned()
  {
    // The app's data folder sits inside the folder the user adds; so does a recycle area of another volume.
    var indexPath = _tree.PathOf(@"data\index.db");
    _tree.Project(@"docs\invoice-tool");
    _tree.Project(@"data\recycle-areas\b1\001\invoice-tool");
    _tree.Project(@".fma-recycle\b2\001\invoice-tool");
    _tree.Write(@"docs\half-copied.txt.fma-partial", "partial");
    using var store = Open(indexPath);

    var problems = await store.AddSourcesAsync([_tree.Root], new SourceOptions(true, true, []), startScan: true);
    await store.WaitForScansAsync(Timeout);
    store.RefreshNow();

    Assert.Empty(problems);
    Assert.Empty(store.Stacks);
    Assert.Equal(14, store.Sources.Single().IndexedFiles);
    Assert.NotEmpty(await store.AddSourcesAsync([_tree.PathOf(@"data\recycle-areas")], new SourceOptions(true, true, []), startScan: false));
  }

  [Fact]
  public async Task A_Run_Cut_Short_By_A_Crash_Is_Settled_From_The_Disk_On_The_Next_Start()
  {
    ExecutionBatch batch;
    using (var store = await ScannedStore())
    {
      var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
      store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
      batch = await store.ExecutePlanAsync(CancellationToken.None);
      await store.WaitForScansAsync(Timeout);
    }
    // As if the app died mid-run: every item is still journalled as running, and one copy never reached the keeper.
    var copy = batch.Items.First(i => i.Kind == PlanItemKind.Copy);
    File.Delete(copy.CreatedPath!);
    var database = FileMergeAssistant.Index.IndexDatabase.OpenOrCreate(_indexPath);
    using (var connection = database.Open())
    {
      FileMergeAssistant.Index.IndexDatabase.Execute(connection, "UPDATE batch_items SET state = 'Running' WHERE batch_id = $b", ("$b", batch.Id));
    }

    using var reopened = Open();

    var items = reopened.History.Single(b => b.Id == batch.Id).Items;
    Assert.Equal(BatchItemState.Failed, items.Single(i => i.Id == copy.Id).State);
    Assert.All(items.Where(i => i.Kind == PlanItemKind.Recycle), i => Assert.Equal(BatchItemState.Done, i.State));
  }

  [Fact]
  public async Task A_Copy_Is_Not_Undone_While_Its_Source_Is_Still_In_The_Recycle_Area()
  {
    using var store = await ScannedStore();
    var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
    store.Decide(StackDecision.Suggested(folder).WithChoice("receipt.html", ConflictChoice.Both));
    var batch = await store.ExecutePlanAsync(CancellationToken.None);
    await store.WaitForScansAsync(Timeout);
    var notes = batch.Items.Single(i => i.Kind == PlanItemKind.Copy && i.Target == "notes.md");

    await store.RestoreItemAsync(batch.Id, notes.Id);

    var after = store.History.Single(b => b.Id == batch.Id).Items.Single(i => i.Id == notes.Id);
    Assert.Equal(BatchItemState.Done, after.State);
    Assert.NotNull(after.FailureReason);
    Assert.True(File.Exists(_tree.PathOf(@"work\invoice-tool\notes.md")));
  }

  [Fact]
  public async Task Keep_All_And_Later_Change_The_Status_Without_A_Plan()
  {
    using var store = await ScannedStore();
    var file = store.Stacks.Single(s => s.Kind == StackKind.File);

    store.Decide(StackDecision.Suggested(file).WithAction(StackAction.KeepAll));
    Assert.Equal(StackStatus.KeptAll, store.Stacks.Single(s => s.Id == file.Id).Status);
    Assert.Empty(store.Plan);

    store.Decide(StackDecision.Suggested(file).WithAction(StackAction.Later));
    Assert.Equal(StackStatus.Open, store.Stacks.Single(s => s.Id == file.Id).Status);
    Assert.Null(store.DecisionFor(file.Id));
  }
}
