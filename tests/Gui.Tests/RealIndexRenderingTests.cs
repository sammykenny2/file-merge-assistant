using System.IO;
using System.Text;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>
/// The screens over the real index: scan a throwaway tree under %TEMP%, then render the workbench, a stack, the sources
/// and the plan. Fails on any binding error. Never touches the user's folders.
/// </summary>
public sealed class RealIndexRenderingTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), "fma-gui-tree-" + Guid.NewGuid().ToString("N")[..8]);
  private static readonly string? SnapshotDirectory = Environment.GetEnvironmentVariable("FMA_SNAPSHOT_DIR");

  public void Dispose()
  {
    try
    {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException)
    {
      // Best effort.
    }
  }

  private void Write(string relative, string content, int year = 2024)
  {
    var path = Path.Combine(_root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, content, Encoding.UTF8);
    File.SetLastWriteTimeUtc(path, new DateTime(year, 3, 11, 12, 0, 0, DateTimeKind.Utc));
  }

  private void Project(string folder)
  {
    Write(Path.Combine(folder, "package.json"), "{ \"name\": \"invoice-tool\" }");
    for (var i = 0; i < 15; i++)
    {
      Write(Path.Combine(folder, "src", $"module{i:00}.ts"), $"export const v{i} = {i};" + new string('x', 30 + i));
    }
  }

  [Fact]
  public void Screens_Render_The_Real_Index()
  {
    Project(@"source\invoice-tool");
    Project(@"backup\invoice-tool-backup");
    Write(@"backup\invoice-tool-backup\old-notes.md", "only in the backup");
    Write(@"source\invoice-tool\receipt.html", "v2", 2026);
    Write(@"backup\invoice-tool-backup\receipt.html", "v1 is older");
    Write(@"backup\photo.jpg", "jpeg bytes here");
    Write(@"source\photo (1).jpg", "jpeg bytes here");

    var errors = WpfHost.Run(() =>
    {
      using var store = IndexedWorkbenchStore.Open(Path.Combine(_root, "index.db"));
      var problems = WpfHost.Wait(store.AddSourcesAsync([Path.Combine(_root, "source"), Path.Combine(_root, "backup")],
        new SourceOptions(true, true, [Path.Combine(_root, "source")]), startScan: true));
      Assert.Empty(problems);
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(60)));
      store.RefreshNow();

      var main = new MainViewModel(store, () => throw new InvalidOperationException());
      var shell = new ShellView { DataContext = main };
      void Snap(string name)
      {
        var bitmap = WpfHost.Render(shell, 1440);
        if (SnapshotDirectory is not null)
        {
          Directory.CreateDirectory(SnapshotDirectory);
          WpfHost.Save(bitmap, Path.Combine(SnapshotDirectory, name + ".png"));
        }
      }

      Assert.False(main.IsDemo);
      Assert.IsType<HubViewModel>(main.CurrentPage);
      Snap("r1-hub");

      var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
      Assert.Single(store.Stacks, s => s.Kind == StackKind.File);
      main.ShowStack(folder.Id);
      var stack = Assert.IsType<StackViewModel>(main.CurrentPage);
      Assert.Equal(2, stack.Columns.Count);
      Snap("r2-stack");

      stack.SubmitCommand.Execute(null);
      Assert.Single(store.Plan);

      main.ShowSources();
      Snap("r3-sources");

      main.ShowPlan();
      var plan = Assert.IsType<PlanViewModel>(main.CurrentPage);
      // Opening the plan starts a re-check against the disk; wait for it before running.
      for (var i = 0; i < 200 && (plan.IsChecking || !plan.CanExecute); i++)
      {
        WpfHost.Wait(Task.Delay(25));
      }
      Assert.True(plan.CanExecute);
      Snap("r4-plan");

      plan.ExecuteCommand.Execute(null);
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(60)));
      for (var i = 0; i < 100 && main.CurrentPage is not HistoryViewModel; i++)
      {
        WpfHost.Wait(Task.Delay(50));
      }
      var history = Assert.IsType<HistoryViewModel>(main.CurrentPage);
      Assert.True(history.HasBatch);
      Snap("r5-history");
    });

    Assert.Empty(errors);
  }
}
