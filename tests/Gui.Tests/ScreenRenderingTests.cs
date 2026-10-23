using System.IO;
using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Plans;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>
/// Walks through every workbench screen with the demo data, renders the whole window body at the mock's width and fails
/// on any WPF binding error. Set FMA_SNAPSHOT_DIR to also save each rendering as a PNG.
/// </summary>
public class ScreenRenderingTests
{
  private const double Width = 1440;
  private static readonly string? SnapshotDirectory = Environment.GetEnvironmentVariable("FMA_SNAPSHOT_DIR");

  [Fact]
  public void Every_Screen_Renders_Without_Binding_Errors()
  {
    var errors = WpfHost.Run(() =>
    {
      var store = new DemoWorkbenchStore { StepDelay = TimeSpan.Zero };
      var main = new MainViewModel(store, () => throw new InvalidOperationException(), () => throw new InvalidOperationException());
      var shell = new ShellView { DataContext = main };
      void Snap(string name)
      {
        var bitmap = WpfHost.Render(shell, Width);
        Assert.True(bitmap.PixelHeight > 200, $"{name} rendered only {bitmap.PixelHeight} px high");
        if (SnapshotDirectory is not null)
        {
          Directory.CreateDirectory(SnapshotDirectory);
          WpfHost.Save(bitmap, Path.Combine(SnapshotDirectory, name + ".png"));
        }
      }

      Assert.IsType<HubViewModel>(main.CurrentPage);
      Snap("01-hub");

      main.ShowStack("invoice");
      var stack = Assert.IsType<StackViewModel>(main.CurrentPage);
      Assert.True(stack.IsBlocked);
      Snap("02-stack-invoice");

      stack.Conflicts[0].Options[2].IsSelected = true;
      Assert.False(stack.IsBlocked);
      Snap("03-stack-invoice-both-kept");

      stack.SubmitCommand.Execute(null);
      Assert.Single(store.Plan);
      Assert.NotNull(stack.Toast);
      Snap("04-stack-next");

      main.ShowStack("thesis");
      Snap("05-stack-thesis");
      main.ShowStack("quote");
      Snap("06-stack-quote");
      main.ShowStack("oldsite");
      Snap("07-stack-offline");
      main.ShowStack("camera");
      Snap("08-stack-still-scanning");

      main.ShowSources();
      Snap("09-sources");
      main.ShowAddSources();
      Snap("10-add-sources");

      main.ShowStack("kyoto");
      ((StackViewModel)main.CurrentPage!).SubmitCommand.Execute(null);
      Assert.Equal(2, store.Plan.Count);

      main.ShowPlan();
      var plan = Assert.IsType<PlanViewModel>(main.CurrentPage);
      WpfHost.Wait(store.RecheckPlanAsync());
      Assert.Contains(store.Plan.SelectMany(e => e.Items), i => i.Check == PlanCheck.Changed);
      Snap("11-plan");

      plan.ExecuteCommand.Execute(null);
      WpfHost.Flush();
      var history = Assert.IsType<HistoryViewModel>(main.CurrentPage);
      Assert.True(history.HasFailures);
      Assert.True(history.HasSkipped);
      Snap("12-history");

      main.ShowSince();
      Snap("13-since");

      main.ShowPlan();
      Snap("14-plan-empty");
      main.ShowHub();
      Snap("15-hub-after");
    });

    Assert.Empty(errors);
  }

  [Fact]
  public void Stack_Screen_Follows_The_Keeper_And_Protection_Rules()
  {
    var errors = WpfHost.Run(() =>
    {
      var store = new DemoWorkbenchStore { StepDelay = TimeSpan.Zero };
      var stack = new StackViewModel(store, new NullNavigator(), "invoice");

      // The protected keeper may not be overwritten, so 「用其他份的版本」 is disabled (ADR-003).
      var theirs = stack.Conflicts[0].Options.Single(o => o.Choice == ConflictChoice.Theirs);
      Assert.False(theirs.IsEnabled);

      // Making the backup the keeper recomputes the comparison: the protected source copy is then kept, not recycled.
      stack.Columns.Single(c => c.Source == "舊專案").SetKeeper.Execute(null);
      Assert.Equal("舊專案", stack.Columns.Single(c => c.IsKeeper).Source);
      Assert.Equal("受保護，保留", stack.Columns.Single(c => c.Source == "開發中").Pill);
      Assert.Equal("離線，先不動", stack.Columns.Single(c => c.Source == "WD 2TB 備份").Pill);

      // 「都保留」 changes no files, so it never blocks.
      stack.Options.Single(o => o.Action == StackAction.KeepAll).IsSelected = true;
      Assert.False(stack.IsBlocked);
      Assert.Equal("標記為都保留", stack.PrimaryLabel);
    });

    Assert.Empty(errors);
  }

  private sealed class NullNavigator : INavigator
  {
    public void ShowHub()
    {
    }

    public void ShowSources()
    {
    }

    public void ShowAddSources()
    {
    }

    public void ShowStack(string stackId)
    {
    }

    public void ShowPlan()
    {
    }

    public void ShowHistory(string? batchId = null)
    {
    }

    public void ShowSince()
    {
    }
  }
}
