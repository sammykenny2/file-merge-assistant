using System.Diagnostics;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Tests.Scanning;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Tests.Localization;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DisplayLanguageTests
{
  public const string Name = "Display language";
}

/// <summary>
/// Core text in English and after a change of display language (docs/i18n.md). These tests switch the language of the
/// whole process, so they run on their own and switch back to Traditional Chinese afterwards.
/// </summary>
[Collection(DisplayLanguageTests.Name)]
public sealed class CoreTextTests : IDisposable
{
  private static readonly long Ticks = new DateTime(2026, 9, 30, 8, 20, 0, DateTimeKind.Utc).Ticks;

  public void Dispose() => AppText.Apply("zh-TW", []);

  [Fact]
  public void Drives_And_Restored_Names_Follow_The_Display_Language()
  {
    var drive = new VolumeInfo(@"E:\", "7C1E-09AB", null, VolumeKind.Removable);
    var now = new DateTimeOffset(2026, 10, 23, 9, 0, 0, TimeSpan.FromHours(8));
    Assert.Equal("外接硬碟，磁碟區序號 7C1E-09AB", drive.Describe());
    Assert.Equal("(還原 10-23)", PlanExecutor.RestoredSuffix(now));

    AppText.Apply("en", []);

    Assert.Equal("External drive, volume serial 7C1E-09AB", drive.Describe());
    Assert.Equal("Internal drive", (drive with { Serial = null, Kind = VolumeKind.Fixed }).Describe());
    Assert.Equal("(restored 10-23)", PlanExecutor.RestoredSuffix(now));
  }

  [Fact]
  public void Keeper_Reasons_Use_The_Separator_And_Closing_Punctuation_Of_Each_Language()
  {
    var input = new DetectionInput(
      [
        new DetectionSource("dl", "下載", @"C:\Users\demo\Downloads", false, false, false),
        new DetectionSource("desk", "桌面", @"C:\Users\demo\Desktop", true, false, false),
      ],
      [
        new IndexedFile(1, "dl", "quote (1).xlsx", 49_152, Ticks, "same", false),
        new IndexedFile(2, "desk", "quote.xlsx", 49_152, Ticks, "same", false),
      ],
      [],
      []);

    var chinese = Assert.Single(StackDetector.Detect(input));
    AppText.Apply("en", []);
    var english = Assert.Single(StackDetector.Detect(input));

    Assert.Equal("在受保護的位置，名稱沒有備份或複製的字樣。", chinese.SuggestedKeeperCopy.SuggestReason);
    Assert.Equal("In a protected location. No backup or copy marker in the name.", english.SuggestedKeeperCopy.SuggestReason);
    Assert.Equal("檔案內容", chinese.SameLabel);
    Assert.Equal("File content", english.SameLabel);
    Assert.Equal("比對方式：內容指紋相同（每份 48 KB）。檔名不同不影響判定。", chinese.Evidence);
    Assert.Equal("Compared by content fingerprint: identical (48 KB each). Different file names don't affect the result.", english.Evidence);
  }

  [Fact]
  public async Task The_Demo_Store_Follows_The_Display_Language_But_Keeps_What_Actions_Wrote()
  {
    var store = new DemoWorkbenchStore { StepDelay = TimeSpan.Zero };
    var invoice = store.Stacks.Single(s => s.Id == "invoice");
    store.Decide(StackDecision.Suggested(invoice).WithChoice("src/templates/receipt.html", ConflictChoice.Both));
    await store.RecheckPlanAsync();
    await store.RescanFailedAsync("job-dev");
    var retry = store.Jobs.Single(j => j.Id == "job-dev-retry").Reason;
    Assert.Equal("重新掃描上次失敗的 2 個檔", retry);
    Assert.StartsWith("比對方式：", invoice.Evidence, StringComparison.Ordinal);

    AppText.Apply("en", []);

    var stack = store.Stacks.Single(s => s.Id == "invoice");
    Assert.Equal(StackStatus.Pending, stack.Status);
    Assert.StartsWith("Compared by", stack.Evidence, StringComparison.Ordinal);
    var entry = Assert.Single(store.Plan);
    Assert.StartsWith("Compared by", entry.Stack.Evidence, StringComparison.Ordinal);
    Assert.Equal("The keeper is still there and unchanged since you decided; it's protected, so files are only added", entry.KeeperCheck);
    var changed = Assert.Single(entry.Items, i => i.Check == PlanCheck.Changed);
    Assert.StartsWith("You decided at", changed.ChangeDetail, StringComparison.Ordinal);
    Assert.Equal("Internal drive, volume serial 5E90-11F2", store.Sources.Single(s => s.Id == "proj").VolumeDescription);
    Assert.Equal("46 more files since the last scan", store.Jobs.Single(j => j.Id == "job-dl").Reason);
    Assert.Equal("Another copy of invoice-tool appeared", store.SinceLastVisit!.Notices.Single(n => n.Id == "invoice").Title);
    // What an action wrote keeps the language it was written in, as it would in the real index.
    Assert.Equal(retry, store.Jobs.Single(j => j.Id == "job-dev-retry").Reason);
    Assert.Equal("失敗的項目已重新排入佇列", store.Jobs.Single(j => j.Id == "job-dev").Reason);
  }

  [Fact]
  public async Task The_Real_Store_Reads_The_Index_Again_In_The_New_Language()
  {
    using var tree = new TempTree();
    tree.Write(@"dl\quote (1).xlsx", "spreadsheet bytes");
    tree.Write(@"desk\quote.xlsx", "spreadsheet bytes");
    using var store = IndexedWorkbenchStore.Open(Path.Combine(TestEnvironment.DataRoot, $"store-{Guid.NewGuid():N}.db"), new OneVolume(),
      context: null, startWorker: true);
    Assert.Empty(await store.AddSourcesAsync([tree.PathOf("dl"), tree.PathOf("desk")], new SourceOptions(true, true, []), startScan: true));
    await store.WaitForScansAsync(TimeSpan.FromSeconds(60));
    store.RefreshNow();
    Assert.Equal("檔案內容", Assert.Single(store.Stacks).SameLabel);

    AppText.Apply("en", []);

    var waited = Stopwatch.StartNew();
    while (Assert.Single(store.Stacks).SameLabel != "File content" && waited.Elapsed < TimeSpan.FromSeconds(30))
    {
      await Task.Delay(50);
    }
    Assert.Equal("File content", Assert.Single(store.Stacks).SameLabel);
    // The drive's description was written into the index when the source was added and keeps that language.
    Assert.All(store.Sources, s => Assert.Equal("內建硬碟，磁碟區序號 1234-ABCD", s.VolumeDescription));
  }

  private sealed class OneVolume : IVolumeInfoProvider
  {
    public VolumeInfo? Describe(string path) => new(Path.GetPathRoot(path)!, "1234-ABCD", null, VolumeKind.Fixed);
  }
}
