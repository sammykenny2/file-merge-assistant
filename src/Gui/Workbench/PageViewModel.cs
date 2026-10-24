using CommunityToolkit.Mvvm.ComponentModel;
using FileWorkbench.Workbench;

namespace FileWorkbench.Gui.Workbench;

/// <summary>Moves between the workbench screens.</summary>
public interface INavigator
{
  void ShowHub();
  void ShowSources();

  /// <summary>The first-run screen; also used to add more folders later.</summary>
  void ShowAddSources();

  void ShowStack(string stackId);
  void ShowPlan();
  void ShowHistory(string? batchId = null);
  void ShowImages();

  /// <summary>「自上次以來」: what changed since the last visit.</summary>
  void ShowSince();
}

/// <summary>A screen that renders from the store and re-renders when the store changes.</summary>
public abstract class PageViewModel : ObservableObject
{
  protected PageViewModel(IWorkbenchStore store, INavigator navigator)
  {
    Store = store;
    Navigator = navigator;
  }

  protected IWorkbenchStore Store { get; }
  protected INavigator Navigator { get; }

  /// <summary>Rebuilds the screen from the store, keeping choices the user has not submitted yet.</summary>
  public abstract void Refresh();
}
