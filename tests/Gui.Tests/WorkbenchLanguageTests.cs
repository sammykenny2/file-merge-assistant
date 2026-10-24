using System.IO;
using FileWorkbench.Gui.ViewModels;
using FileWorkbench.Gui.Workbench;
using FileWorkbench.Localization;
using FileWorkbench.Workbench;
using Xunit;

namespace FileWorkbench.Gui.Tests;

/// <summary>The header, hub, sources, add-sources and since screens switch language in place and render in English.</summary>
[Collection(DisplayLanguageTests.Name)]
public sealed class WorkbenchLanguageTests : IDisposable
{
  private static readonly string? SnapshotDirectory = Environment.GetEnvironmentVariable("FWB_SNAPSHOT_DIR");

  public void Dispose() => AppText.Apply("zh-TW", []);

  [Fact]
  public void Fmt_Follows_The_Display_Language()
  {
    var now = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    AppText.Apply("zh-TW", []);
    Assert.Equal("今天 09:12", Fmt.When(now.AddHours(-2).AddMinutes(-48), now));
    Assert.Equal("昨天 21:40", Fmt.When(new DateTimeOffset(2026, 10, 21, 21, 40, 0, TimeSpan.Zero), now));
    Assert.Equal("10/20", Fmt.When(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), now));
    Assert.Equal("2024/03/11", Fmt.Day(new DateTimeOffset(2024, 3, 11, 8, 0, 0, TimeSpan.Zero), now));
    Assert.Equal("9/28 22:14", Fmt.WhenWithTime(new DateTimeOffset(2026, 9, 28, 22, 14, 0, TimeSpan.Zero), now));
    Assert.Equal("a、b", Fmt.List(["a", "b"]));
    Assert.Equal("一。二。", Fmt.Sentences("一。", null, "二。"));
    Assert.Equal("甲；乙", Fmt.Clauses("甲", "", "乙"));

    AppText.Apply("en", []);
    Assert.Equal("Today 09:12", Fmt.When(now.AddHours(-2).AddMinutes(-48), now));
    Assert.Equal("Yesterday 21:40", Fmt.When(new DateTimeOffset(2026, 10, 21, 21, 40, 0, TimeSpan.Zero), now));
    Assert.Equal("Oct 20", Fmt.When(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero), now));
    Assert.Equal("Mar 11, 2024", Fmt.Day(new DateTimeOffset(2024, 3, 11, 8, 0, 0, TimeSpan.Zero), now));
    Assert.Equal("Sep 28, 22:14", Fmt.WhenWithTime(new DateTimeOffset(2026, 9, 28, 22, 14, 0, TimeSpan.Zero), now));
    Assert.Equal("a, b", Fmt.List(["a", "b"]));
    Assert.Equal("One. Two.", Fmt.Sentences("One.", null, "Two."));
    Assert.Equal("a; b", Fmt.Clauses("a", "", "b"));
  }

  [Fact]
  public void Header_Hub_Sources_And_Since_Switch_Language_In_Place()
  {
    var errors = WpfHost.Run(() =>
    {
      AppText.Apply("zh-TW", []);
      var store = new DemoWorkbenchStore { StepDelay = TimeSpan.Zero };
      var main = new MainViewModel(store, TestSettings.Create());
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

      var hub = Assert.IsType<HubViewModel>(main.CurrentPage);
      Assert.StartsWith("找到 ", hub.Summary, StringComparison.Ordinal);
      Assert.Equal("建議順序：省空間又少決定的先", hub.Sort.Label);
      Assert.All(main.Strip, i => Assert.DoesNotContain("{", i.Text, StringComparison.Ordinal));
      Snap("c-zh-hub");

      AppText.Apply("en", []);
      WpfHost.Flush();
      Assert.StartsWith("Found ", hub.Summary, StringComparison.Ordinal);
      Assert.Equal("Suggested: biggest wins first", hub.Sort.Label);
      Assert.Contains(main.Strip, i => i.Text.EndsWith("files indexed", StringComparison.Ordinal));
      Assert.Equal("Load 20 more", hub.LoadMoreLabel);
      Assert.DoesNotContain(hub.Pairs, p => p.Label.Contains('與', StringComparison.Ordinal));
      Snap("c-en-hub");

      main.ShowSources();
      var sources = Assert.IsType<SourcesViewModel>(main.CurrentPage);
      Assert.Contains(" sources, ", sources.Summary, StringComparison.Ordinal);
      Snap("c-en-sources");
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      Assert.Contains(" 個來源，已索引 ", sources.Summary, StringComparison.Ordinal);
      AppText.Apply("en", []);
      WpfHost.Flush();

      main.ShowAddSources();
      var add = Assert.IsType<AddSourcesViewModel>(main.CurrentPage);
      Assert.Equal("Discover", add.Steps[0].Title);
      Snap("c-en-add");
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      Assert.Equal("探索", add.Steps[0].Title);
      AppText.Apply("en", []);
      WpfHost.Flush();

      main.ShowSince();
      var since = Assert.IsType<SinceViewModel>(main.CurrentPage);
      Assert.StartsWith("Since ", since.Title, StringComparison.Ordinal);
      Assert.Equal("Needs a look", since.Sections[0].Title);
      Snap("c-en-since");

      main.ShowHub();
      Snap("c-en-hub-again");
    });

    Assert.Empty(errors);
  }
}
