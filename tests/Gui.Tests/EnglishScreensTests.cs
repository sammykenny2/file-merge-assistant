using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>
/// Every screen over a real index of ASCII-named folders, in English: no Chinese may be left on screen and no catalog
/// key may show instead of its text. This catches text that was not moved into the catalogs (docs/i18n.md).
/// </summary>
[Collection(DisplayLanguageTests.Name)]
public sealed partial class EnglishScreensTests : IDisposable
{
  private static readonly string? SnapshotDirectory = Environment.GetEnvironmentVariable("FMA_SNAPSHOT_DIR");
  private readonly string _root = Path.Combine(Path.GetTempPath(), "fma-gui-english-" + Guid.NewGuid().ToString("N")[..8]);

  public void Dispose()
  {
    AppText.Apply("zh-TW", []);
    try
    {
      Directory.Delete(_root, recursive: true);
    }
    catch (IOException)
    {
      // Best effort.
    }
  }

  [Fact]
  public void Every_Screen_Is_In_English_When_English_Is_Chosen()
  {
    Project(@"source\invoice-tool");
    Project(@"backup\invoice-tool-backup");
    Write(@"backup\invoice-tool-backup\old-notes.md", "only in the backup");
    Write(@"source\invoice-tool\receipt.html", "v2", 2026);
    Write(@"backup\invoice-tool-backup\receipt.html", "v1 is older");
    Write(@"backup\photo.jpg", "jpeg bytes here");
    Write(@"source\photo (1).jpg", "jpeg bytes here");

    var problems = new List<string>();
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("en", []);
      using var store = IndexedWorkbenchStore.Open(Path.Combine(_root, "data", "index.db"));
      Assert.Empty(WpfHost.Wait(store.AddSourcesAsync([Path.Combine(_root, "source"), Path.Combine(_root, "backup")],
        new SourceOptions(true, true, [Path.Combine(_root, "source")]), startScan: true)));
      WpfHost.Wait(store.WaitForScansAsync(TimeSpan.FromSeconds(60)));
      store.RefreshNow();

      var main = new MainViewModel(store, TestSettings.Create());
      var shell = new ShellView { DataContext = main };
      void Check(string screen)
      {
        var bitmap = WpfHost.Render(shell, 1440);
        if (SnapshotDirectory is not null)
        {
          Directory.CreateDirectory(SnapshotDirectory);
          WpfHost.Save(bitmap, Path.Combine(SnapshotDirectory, "en-" + screen.Replace(' ', '-') + ".png"));
        }
        foreach (var text in Texts(shell))
        {
          if (Han().IsMatch(text))
          {
            problems.Add($"{screen}: Chinese text \"{text}\"");
          }
          else if (AppText.Table("en").ContainsKey(text.Trim()))
          {
            problems.Add($"{screen}: catalog key shown \"{text}\"");
          }
        }
      }

      Check("hub");
      var folder = store.Stacks.Single(s => s.Kind == StackKind.Folder);
      main.ShowStack(folder.Id);
      Check("stack");
      var stack = Assert.IsType<StackViewModel>(main.CurrentPage);
      stack.SubmitCommand.Execute(null);
      Check("stack after submit");

      main.ShowSources();
      Check("sources");
      // Not the add-sources screen: over the real store it suggests folders from the real user profile, which no test
      // may read.
      main.ShowSince();
      Check("since");

      main.ShowPlan();
      var plan = Assert.IsType<PlanViewModel>(main.CurrentPage);
      for (var i = 0; i < 200 && (plan.IsChecking || !plan.CanExecute); i++)
      {
        WpfHost.Wait(Task.Delay(25));
      }
      Check("plan");
      plan.ExecuteCommand.Execute(null);
      for (var i = 0; i < 100 && main.CurrentPage is not HistoryViewModel; i++)
      {
        WpfHost.Wait(Task.Delay(50));
      }
      Check("history");
      main.ShowHub();
      Check("hub after the run");
      main.ShowSettings();
      Check("settings");
      main.ShowImages();
      Check("image review");
      main.ShowSettings();

      // Switching back redraws the open screen in Chinese without reopening it.
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      WpfHost.Render(shell, 1440);
      Assert.Contains(Texts(shell), t => t == "設定");
    });

    Assert.Empty(errors);
    Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
  }

  /// <summary>Every piece of text on screen: text blocks, plain-text content, tooltips and accessible names.</summary>
  private static IEnumerable<string> Texts(DependencyObject root)
  {
    var pending = new Stack<DependencyObject>();
    pending.Push(root);
    while (pending.Count > 0)
    {
      var node = pending.Pop();
      if (node is TextBlock block && !string.IsNullOrWhiteSpace(block.Text))
      {
        yield return block.Text;
      }
      if (node is FrameworkElement element)
      {
        if (element.ToolTip is string tip && tip.Length > 0)
        {
          yield return tip;
        }
        var name = AutomationProperties.GetName(element);
        if (!string.IsNullOrWhiteSpace(name))
        {
          yield return name;
        }
      }
      for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
      {
        pending.Push(VisualTreeHelper.GetChild(node, i));
      }
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

  [GeneratedRegex(@"[　-〿一-鿿！-～]")]
  private static partial Regex Han();
}
