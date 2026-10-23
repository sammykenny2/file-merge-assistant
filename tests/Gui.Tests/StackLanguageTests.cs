using FileMergeAssistant.Gui.ViewModels;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;
using Xunit;

namespace FileMergeAssistant.Gui.Tests;

/// <summary>The stack screen redraws in a new display language without losing what the user has not submitted yet.</summary>
[Collection(DisplayLanguageTests.Name)]
public sealed class StackLanguageTests : IDisposable
{
  public void Dispose() => AppText.Apply("zh-TW", []);

  [Fact]
  public void Switching_The_Language_Redraws_The_Stack_Screen_And_Keeps_The_Choices()
  {
    var errors = WpfHost.Run(() =>
    {
      var store = new DemoWorkbenchStore { StepDelay = TimeSpan.Zero };
      var main = new MainViewModel(store);
      var shell = new ShellView { DataContext = main };
      main.ShowStack("invoice");
      var stack = Assert.IsType<StackViewModel>(main.CurrentPage);
      var conflict = stack.Conflicts[0];
      conflict.Options.Single(o => o.Choice == ConflictChoice.Both).IsSelected = true;
      Assert.False(stack.IsBlocked);
      Assert.Equal("合併到正本", stack.Options[0].Label);
      WpfHost.Render(shell, 1440);

      AppText.Apply("en", []);
      WpfHost.Flush();
      Assert.Same(stack, main.CurrentPage);
      // The conflict choice and the action are kept; the conflict keeps its view model, only its text changes.
      Assert.Same(conflict, stack.Conflicts[0]);
      Assert.True(conflict.Options.Single(o => o.Choice == ConflictChoice.Both).IsChosen);
      Assert.True(stack.Options.Single(o => o.Action == StackAction.Merge).IsChosen);
      Assert.False(stack.IsBlocked);
      Assert.Equal("Merge into the keeper", stack.Options[0].Label);
      Assert.Equal("Keeper", stack.Columns.Single(c => c.IsKeeper).Pill);
      Assert.Equal("To recycle area", stack.Columns.Single(c => c.Source == "舊專案").Pill);
      Assert.Equal("Offline, left as is", stack.Columns.Single(c => c.Source == "WD 2TB 備份").Pill);
      Assert.StartsWith("Use the keeper's version", conflict.Options[0].Label, StringComparison.Ordinal);
      Assert.StartsWith("The keeper's version is from", conflict.Compare, StringComparison.Ordinal);
      Assert.Equal("Add to Pending", stack.PrimaryLabel);
      Assert.Equal("Folder, 4 copies", stack.KindLine);
      WpfHost.Render(shell, 1440);

      stack.SubmitCommand.Execute(null);
      Assert.Equal("Added to Pending: invoice-tool 專案", stack.Toast);
      AppText.Apply("zh-TW", []);
      WpfHost.Flush();
      Assert.Equal("已加入待執行：invoice-tool 專案", stack.Toast);
      Assert.Equal("合併到正本", stack.Options[0].Label);
      WpfHost.Render(shell, 1440);
    });

    Assert.Empty(errors);
  }
}
