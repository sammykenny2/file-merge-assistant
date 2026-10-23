using FileMergeAssistant.Stacks;
using Xunit;

namespace FileMergeAssistant.Tests.Stacks;

/// <summary>Detection over an in-memory index with known ground truth.</summary>
public class StackDetectorTests
{
  private static readonly long Old = new DateTime(2024, 3, 11, 0, 0, 0, DateTimeKind.Utc).Ticks;
  private static readonly long New = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc).Ticks;

  private sealed class IndexBuilder
  {
    private long _id;
    public List<DetectionSource> Sources { get; } = [];
    public List<IndexedFile> Files { get; } = [];
    public List<IndexedFolder> Regenerable { get; } = [];
    public List<IndexedFolder> Projects { get; } = [];

    public IndexBuilder Source(string id, string root, bool isProtected = false, bool offline = false, bool scanning = false)
    {
      Sources.Add(new DetectionSource(id, "Source " + id, root, isProtected, offline, scanning));
      return this;
    }

    public IndexBuilder File(string source, string path, string content, long size = 100, long? modified = null)
    {
      Files.Add(new IndexedFile(++_id, source, path, size, modified ?? Old, content, false));
      return this;
    }

    /// <summary>A project folder of <paramref name="count"/> files whose content is shared by every copy.</summary>
    public IndexBuilder Project(string source, string folder, int count, string prefix = "p")
    {
      for (var i = 0; i < count; i++)
      {
        File(source, $@"{folder}\src\file{i:000}.cs", $"{prefix}-{i}", size: 1000 + i);
      }
      return this;
    }

    public DetectionInput Build() => new(Sources, Files, Regenerable, Projects);
  }

  private static Stack Single(IReadOnlyList<Stack> stacks, string titleFragment) =>
    Assert.Single(stacks, s => s.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase));

  [Fact]
  public void Copies_Of_A_Project_Under_Different_Roots_And_Names_Form_One_Folder_Stack()
  {
    var index = new IndexBuilder()
      .Source("dev", @"C:\Users\demo\source", isProtected: true)
      .Source("proj", @"D:\Projects")
      .Source("desk", @"C:\Users\demo\Desktop")
      .Project("dev", "invoice-tool", 40)
      .Project("proj", "invoice-tool-backup-20240311", 40)
      .Project("desk", "invoice-tool - 複製", 40)
      // Differences: the backup lacks nothing, the desktop copy has an extra file and a newer receipt.
      .File("desk", @"invoice-tool - 複製\notes.md", "notes")
      .File("dev", @"invoice-tool\receipt.html", "receipt-a", modified: Old)
      .File("proj", @"invoice-tool-backup-20240311\receipt.html", "receipt-b", modified: Old)
      .File("desk", @"invoice-tool - 複製\receipt.html", "receipt-d", modified: New)
      // Unrelated projects next to it share nothing.
      .Project("dev", "other-app", 30, prefix: "other");

    var stacks = StackDetector.Detect(index.Build());

    var stack = Single(stacks, "invoice-tool");
    Assert.Equal(StackKind.Folder, stack.Kind);
    Assert.Equal(3, stack.Copies.Count);
    Assert.Equal(40, stack.SameFileCount);
    Assert.Equal(["notes.md", "receipt.html"], stack.Rows.Select(r => r.RelativePath));
    // The protected development copy is the suggested keeper, and merging is suggested because files exist elsewhere.
    Assert.Equal(@"C:\Users\demo\source\invoice-tool", stack.SuggestedKeeperCopy.Path);
    Assert.Equal(StackAction.Merge, stack.SuggestedAction);
    Assert.DoesNotContain(stacks, s => s.Title.Contains("other-app", StringComparison.Ordinal));
  }

  [Fact]
  public void The_Project_Folder_Is_The_Copy_Not_Its_Subfolder_Or_A_Wrapper_Around_It()
  {
    var index = new IndexBuilder()
      .Source("a", @"C:\a")
      .Source("b", @"D:\b")
      .Project("a", @"work\site", 20)
      .Project("b", @"backup\site", 20)
      .File("a", @"work\site\README.md", "readme")
      .File("b", @"backup\site\README.md", "readme");

    var stack = Assert.Single(StackDetector.Detect(index.Build()));

    // site\src matches too, but site is the outer pair with the same share; work and backup only wrap site.
    Assert.Equal([@"C:\a\work\site", @"D:\b\backup\site"], stack.Copies.Select(c => c.Path));
    Assert.Equal(21, stack.SameFileCount);
  }

  [Fact]
  public void A_Larger_Folder_That_Only_Shares_Its_Name_Does_Not_Swallow_The_Real_Copy()
  {
    // E:\備份\2023 holds only the backup of invoice-tool, and the folder names match, so (src, 2023) also gets every
    // vote. The src folder mostly holds another project, so that pair is a container and the project pair wins.
    var index = new IndexBuilder()
      .Source("dev", @"C:\Users\demo")
      .Source("wd", @"E:\")
      .Project("dev", @"src\invoice-tool", 20)
      .File("dev", @"src\invoice-tool\README.md", "readme")
      .Project("wd", @"備份\2023\invoice-tool", 20)
      .File("wd", @"備份\2023\invoice-tool\README.md", "readme")
      .File("wd", @"備份\2023\notes.txt", "note")
      .Project("dev", @"src\other-app", 30, prefix: "other");

    var stack = Assert.Single(StackDetector.Detect(index.Build()));

    Assert.Equal([@"C:\Users\demo\src\invoice-tool", @"E:\備份\2023\invoice-tool"], stack.Copies.Select(c => c.Path));
  }

  [Fact]
  public void A_Subset_With_The_Same_Names_Is_A_Folder_Stack_Kept_In_The_Larger_Copy()
  {
    var index = new IndexBuilder()
      .Source("photo", @"D:\Photos")
      .Source("desk", @"C:\Desktop");
    for (var i = 0; i < 40; i++)
    {
      index.File("photo", $@"2019-kyoto\IMG_{i:0000}.jpg", $"img-{i}", size: 3_000_000 + i);
    }
    for (var i = 0; i < 10; i++)
    {
      index.File("desk", $@"kyoto-picks\IMG_{i:0000}.jpg", $"img-{i}", size: 3_000_000 + i);
    }

    var stack = Assert.Single(StackDetector.Detect(index.Build()));

    Assert.Equal(StackKind.Folder, stack.Kind);
    Assert.Equal(@"D:\Photos\2019-kyoto", stack.SuggestedKeeperCopy.Path);
    // The picks folder has nothing the keeper lacks, so recycling it loses nothing.
    Assert.Equal(StackAction.KeepKeeperOnly, stack.SuggestedAction);
    Assert.Equal(StackGroup.ReadyToConverge, StackTriage.Summarize(stack).Group);
  }

  [Fact]
  public void Folders_Sharing_Only_A_Few_Files_Are_Not_Copies()
  {
    var index = new IndexBuilder()
      .Source("a", @"C:\a")
      .Source("b", @"D:\b");
    for (var i = 0; i < 40; i++)
    {
      index.File("a", $@"left\doc{i}.md", i < 3 ? $"shared-{i}" : $"left-{i}");
      index.File("b", $@"right\doc{i}.md", i < 3 ? $"shared-{i}" : $"right-{i}");
    }

    var stacks = StackDetector.Detect(index.Build());

    Assert.DoesNotContain(stacks, s => s.Kind == StackKind.Folder);
    Assert.Equal(3, stacks.Count(s => s.Kind == StackKind.File));
  }

  [Fact]
  public void Loose_Identical_Files_Form_File_Stacks()
  {
    var index = new IndexBuilder()
      .Source("dl", @"C:\Users\demo\Downloads")
      .Source("desk", @"C:\Users\demo\Desktop")
      .File("dl", "報價單 2024Q3 (1).xlsx", "quote", size: 49_152)
      .File("dl", "報價單 2024Q3 (2).xlsx", "quote", size: 49_152)
      .File("desk", "報價單 2024Q3.xlsx", "quote", size: 49_152)
      .File("desk", "unrelated.txt", "x");

    var stack = Assert.Single(StackDetector.Detect(index.Build()));

    Assert.Equal(StackKind.File, stack.Kind);
    Assert.Equal(3, stack.Copies.Count);
    // The name without (1) or (2) is suggested.
    Assert.Equal(@"C:\Users\demo\Desktop\報價單 2024Q3.xlsx", stack.SuggestedKeeperCopy.Path);
    Assert.Equal("報價單 2024Q3.xlsx", stack.Title);
  }

  [Fact]
  public void Identical_Boilerplate_Inside_Code_Projects_Never_Forms_File_Stacks()
  {
    var index = new IndexBuilder()
      .Source("dev", @"C:\dev")
      .File("dev", @"app-one\LICENSE", "mit", size: 1070)
      .File("dev", @"app-two\LICENSE", "mit", size: 1070)
      .File("dev", @"app-one\main.cs", "one")
      .File("dev", @"app-two\main.cs", "two");
    index.Projects.Add(new IndexedFolder("dev", "app-one", ".git", 0, 0));
    index.Projects.Add(new IndexedFolder("dev", "app-two", "package.json", 0, 0));

    Assert.Empty(StackDetector.Detect(index.Build()));
  }

  [Fact]
  public void Files_Inside_A_Folder_Stack_Are_Not_Listed_Again()
  {
    var index = new IndexBuilder()
      .Source("a", @"C:\a")
      .Source("b", @"D:\b")
      .Project("a", "photos-2019", 12)
      .Project("b", "photos-2019-copy", 12)
      .File("a", "loose.jpg", "photo", size: 5000)
      .File("b", "loose (1).jpg", "photo", size: 5000);

    var stacks = StackDetector.Detect(index.Build());

    Assert.Equal(2, stacks.Count);
    Assert.Single(stacks, s => s.Kind == StackKind.Folder);
    var file = Assert.Single(stacks, s => s.Kind == StackKind.File);
    Assert.Equal(2, file.Copies.Count);
  }

  [Fact]
  public void Offline_And_Scanning_Sources_Block_The_Stack_And_Never_Supply_The_Keeper()
  {
    var index = new IndexBuilder()
      .Source("wd", @"E:\", offline: true)
      .Source("proj", @"D:\Projects")
      .Source("usb", @"F:\", scanning: true)
      .Project("wd", "old-site", 10)
      .File("wd", @"old-site\index.php", "index")
      .Project("proj", "old-site", 10)
      .File("proj", @"old-site\index.php", "index")
      .File("proj", "a.bin", "same", size: 9000)
      .File("usb", "a.bin", "same", size: 9000);

    var stacks = StackDetector.Detect(index.Build());

    var folder = Single(stacks, "old-site");
    Assert.Equal(StackBlock.SourceOffline, folder.Block);
    Assert.False(folder.SuggestedKeeperCopy.IsOffline);
    var file = Single(stacks, "a.bin");
    Assert.Equal(StackBlock.StillScanning, file.Block);
  }

  [Fact]
  public void Regenerable_Folders_Are_Summed_Per_Copy()
  {
    var index = new IndexBuilder()
      .Source("a", @"C:\a")
      .Source("b", @"D:\b")
      .Project("a", "web", 10)
      .Project("b", "web-old", 10);
    index.Regenerable.Add(new IndexedFolder("a", @"web\node_modules", "node_modules", 5_000_000, 900));
    index.Regenerable.Add(new IndexedFolder("b", @"web-old\node_modules", "node_modules", 4_000_000, 800));
    index.Regenerable.Add(new IndexedFolder("b", @"web-old\dist", "dist", 1_000_000, 10));

    var stack = Assert.Single(StackDetector.Detect(index.Build()));

    Assert.NotNull(stack.Regenerable);
    Assert.Equal("dist、node_modules", stack.Regenerable!.Names);
    Assert.Equal([5_000_000L, 5_000_000L], stack.Regenerable.BytesPerCopy);
  }

  [Fact]
  public void Stack_Ids_Are_Stable_Across_Detections()
  {
    var index = new IndexBuilder()
      .Source("a", @"C:\a")
      .Source("b", @"D:\b")
      .Project("a", "web", 10)
      .Project("b", "web-old", 10);

    var first = Assert.Single(StackDetector.Detect(index.Build()));
    var second = Assert.Single(StackDetector.Detect(index.Build()));

    Assert.Equal(first.Id, second.Id);
    Assert.StartsWith("d:", first.Id, StringComparison.Ordinal);
  }

  [Theory]
  [InlineData("invoice-tool-backup-20240311", true)]
  [InlineData("invoice-tool - 複製", true)]
  [InlineData("報價單 (1)", true)]
  [InlineData("site-old", true)]
  [InlineData("invoice-tool", false)]
  [InlineData("golden-retriever", false)]
  public void Copy_Like_Names_Are_Recognised(string name, bool expected)
  {
    Assert.Equal(expected, StackDetector.LooksLikeCopy(name));
  }
}
