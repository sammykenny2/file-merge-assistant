using FileMergeAssistant.Stacks;

namespace FileMergeAssistant.Tests.Stacks;

/// <summary>Small hand-built stacks for the review and plan tests.</summary>
internal static class StackFixtures
{
  public static readonly DateTimeOffset Old = new(2024, 3, 11, 0, 0, 0, TimeSpan.Zero);
  public static readonly DateTimeOffset Mid = new(2026, 10, 15, 0, 0, 0, TimeSpan.Zero);
  public static readonly DateTimeOffset New = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

  public static StackCopy Copy(string id, string path, bool isProtected = false, bool offline = false, long bytes = 1000, long regenerable = 0) =>
    new(id, "src-" + id, "Source " + id, path, id, isProtected, offline, bytes, regenerable, 10, New);

  public static FileVersion V(string content, DateTimeOffset modified, long bytes = 100) => new(content, modified, bytes);

  /// <summary>
  /// Keeper (protected), a stale backup, an offline copy and a desktop copy:
  /// <list type="bullet">
  /// <item>receipt.html: the desktop copy is newer than the keeper (conflict).</item>
  /// <item>pdf.ts: the backup is older (older).</item>
  /// <item>old-spec.md: only the backup and the offline copy have it (only elsewhere, source = backup).</item>
  /// <item>notes.md: only the desktop copy has it (only elsewhere, source = desktop).</item>
  /// <item>offline.txt: only the offline copy has it.</item>
  /// </list>
  /// </summary>
  public static Stack Project() => new("project", "project", StackKind.Folder,
    [
      Copy("keeper", @"C:\source\project", isProtected: true, regenerable: 500),
      Copy("backup", @"D:\backup\project", bytes: 900, regenerable: 400),
      Copy("offline", @"E:\project", offline: true),
      Copy("desktop", @"C:\Desktop\project - copy", bytes: 1100, regenerable: 500),
    ],
    [
      new StackRow("src/receipt.html", [V("a", Mid), V("b", Old), V("c", Old), V("d", New)]),
      new StackRow("src/pdf.ts", [V("a", Mid), V("b", Old), null, V("a", Mid)]),
      new StackRow("docs/old-spec.md", [null, V("s", Old), V("s", Old), null]),
      new StackRow("notes.md", [null, null, null, V("n", New)]),
      new StackRow("offline.txt", [null, null, V("o", Old), null]),
    ],
    SuggestedKeeper: 0,
    SuggestedAction: StackAction.Merge)
  {
    SameFileCount = 100,
    Regenerable = new RegenerableSummary("node_modules", [500, 400, 0, 500]),
  };

  /// <summary>Three identical files.</summary>
  public static Stack Identical() => new("identical", "a.xlsx", StackKind.File,
    [
      Copy("one", @"C:\one\a.xlsx"),
      Copy("two", @"C:\two\a (1).xlsx"),
      Copy("three", @"C:\three\a (2).xlsx"),
    ],
    [],
    SuggestedKeeper: 0,
    SuggestedAction: StackAction.KeepKeeperOnly);
}
