using System.Diagnostics;
using FileWorkbench.Stacks;
using FileWorkbench.Tests.Scanning;
using Xunit;

namespace FileWorkbench.Tests.Stacks;

/// <summary>Git facts from throwaway repositories under %TEMP%. Skipped when git is not installed.</summary>
public sealed class GitStatusReaderTests : IDisposable
{
  private readonly TempTree _tree = new();

  public void Dispose()
  {
    // git marks object files read-only; clear that so the temp tree can be deleted.
    foreach (var file in Directory.EnumerateFiles(_tree.Root, "*", SearchOption.AllDirectories))
    {
      File.SetAttributes(file, FileAttributes.Normal);
    }
    _tree.Dispose();
  }

  private static string _lastError = string.Empty;

  private static bool Git(string folder, string arguments)
  {
    try
    {
      using var process = Process.Start(new ProcessStartInfo("git",
        $"-c user.name=test -c user.email=test@example.invalid -c commit.gpgsign=false -C \"{folder}\" {arguments}")
      {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      })!;
      process.StandardOutput.ReadToEnd();
      _lastError = process.StandardError.ReadToEnd();
      process.WaitForExit();
      return process.ExitCode == 0;
    }
    catch (System.ComponentModel.Win32Exception)
    {
      return false;
    }
  }

  private string Repo(string name, int commits)
  {
    var folder = _tree.PathOf(name);
    Directory.CreateDirectory(folder);
    Assert.True(Git(folder, "init -q -b main"));
    for (var i = 0; i < commits; i++)
    {
      // Same size and an unchanged time would look untouched to git (as to the scanner), so each write gets its own time.
      _tree.Write(Path.Combine(name, "log.txt"), $"commit {i}", DateTime.UtcNow.AddMinutes(i));
      Assert.True(Git(folder, "add -A"));
      Assert.True(Git(folder, $"commit -q -m c{i}"), _lastError);
    }
    return folder;
  }

  [Fact]
  public void Reads_Branch_Head_And_Uncommitted_Changes_Without_Writing_The_Index()
  {
    if (!Git(_tree.Root, "--version"))
    {
      return;
    }
    var repo = Repo("app", 2);
    _tree.Write(@"app\log.txt", "edited", DateTime.UtcNow.AddHours(1));
    _tree.Write(@"app\new.txt", "untracked");
    var index = Path.Combine(repo, ".git", "index");
    var before = File.GetLastWriteTimeUtc(index);

    var status = GitStatusReader.Read(repo);

    Assert.NotNull(status);
    Assert.Equal("main", status!.Branch);
    Assert.NotNull(status.Head);
    Assert.Equal(2, status.UncommittedChanges);
    Assert.Equal(before, File.GetLastWriteTimeUtc(index));
  }

  [Fact]
  public void A_Folder_That_Is_Not_A_Repository_Has_No_Git_Facts()
  {
    _tree.Write(@"plain\a.txt", "a");

    Assert.Null(GitStatusReader.Read(_tree.PathOf("plain")));
  }

  [Fact]
  public void A_Copy_Cloned_Earlier_Is_Behind_The_Keeper()
  {
    if (!Git(_tree.Root, "--version"))
    {
      return;
    }
    var keeper = Repo("keeper", 1);
    Assert.True(Git(_tree.Root, $"clone -q \"{keeper}\" \"{_tree.PathOf("backup")}\""));
    for (var i = 0; i < 3; i++)
    {
      _tree.Write(@"keeper\more.txt", $"more {i}", DateTime.UtcNow.AddMinutes(i + 1));
      Assert.True(Git(keeper, "add -A"));
      Assert.True(Git(keeper, $"commit -q -m more{i}"));
    }
    var stack = new Stack("s", "app", StackKind.Folder,
      [StackFixtures.Copy("keeper", keeper), StackFixtures.Copy("backup", _tree.PathOf("backup"))], [], 0, StackAction.KeepKeeperOnly);

    var enriched = GitStatusReader.Enrich(stack);

    var backup = enriched.Copies[1].Git;
    Assert.NotNull(backup);
    Assert.Equal(3, backup!.BehindKeeper);
    Assert.Equal(0, backup.AheadOfKeeper);
  }
}
