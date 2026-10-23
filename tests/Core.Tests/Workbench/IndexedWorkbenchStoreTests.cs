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

  private sealed class FixedVolumes : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "1234-ABCD", "TEST", VolumeKind.Fixed);
  }

  private IndexedWorkbenchStore Open() => IndexedWorkbenchStore.Open(_indexPath, new FixedVolumes(), context: null, startWorker: true);

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
      Assert.False(store.CanExecute);
      await Assert.ThrowsAsync<NotSupportedException>(() => store.ExecutePlanAsync(CancellationToken.None));
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
