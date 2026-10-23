using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Gui.Settings;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Sources;
using FileMergeAssistant.Stacks;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.ViewModels;

public enum NavSection
{
  Hub,
  Sources,
  Plan,
  History,
  Settings,
}

/// <summary>How an item of the status strip under the header is drawn.</summary>
public enum StripTone
{
  Plain,
  Busy,
  Offline,
}

public sealed record StripItem(string Text, StripTone Tone);

/// <summary>The window shell: header navigation, the index status strip and the current screen.</summary>
public partial class MainViewModel : ObservableObject, INavigator
{
  private readonly IWorkbenchStore _store;
  private readonly SettingsContext? _settings;

  [ObservableProperty]
  private object? _currentPage;

  [ObservableProperty]
  private NavSection _section;

  [ObservableProperty]
  private int _pendingCount;

  /// <param name="settings">What the settings page changes; without it the page is not offered.</param>
  public MainViewModel(IWorkbenchStore store, SettingsContext? settings = null)
  {
    ArgumentNullException.ThrowIfNull(store);
    _store = store;
    _settings = settings;
    _store.Changed += (_, e) => OnStoreChanged(e);
    AppText.Changed += (_, _) => OnLanguageChanged();

    RefreshHeader();
    if (_store.Sources.Count == 0)
    {
      ShowAddSources();
    }
    else
    {
      ShowHub();
    }
  }

  public bool IsDemo => _store.IsDemo;

  public bool HasSettings => _settings is not null;

  public ObservableCollection<StripItem> Strip { get; } = [];

  public bool IsHubSelected => Section == NavSection.Hub;

  public bool IsSourcesSelected => Section == NavSection.Sources;

  public bool IsPlanSelected => Section == NavSection.Plan;

  public bool IsHistorySelected => Section == NavSection.History;

  public bool IsSettingsSelected => Section == NavSection.Settings;


  public bool HasPending => PendingCount > 0;

  public void ShowHub() => Show(NavSection.Hub, new HubViewModel(_store, this));

  public void ShowSources() => Show(NavSection.Sources, new SourcesViewModel(_store, this));

  public void ShowAddSources() => Show(NavSection.Sources, new AddSourcesViewModel(_store, this));

  public void ShowStack(string stackId) => Show(NavSection.Hub, new StackViewModel(_store, this, stackId));

  public void ShowPlan() => Show(NavSection.Plan, new PlanViewModel(_store, this));

  public void ShowHistory(string? batchId = null) => Show(NavSection.History, new HistoryViewModel(_store, this, batchId));

  public void ShowSince() => Show(NavSection.Hub, new SinceViewModel(_store, this));

  public void ShowSettings()
  {
    if (_settings is not null)
    {
      Show(NavSection.Settings, new SettingsViewModel(_store, this, _settings));
    }
  }

  [RelayCommand]
  private void Navigate(NavSection section)
  {
    switch (section)
    {
      case NavSection.Sources:
        ShowSources();
        break;
      case NavSection.Plan:
        ShowPlan();
        break;
      case NavSection.History:
        ShowHistory();
        break;
      case NavSection.Settings:
        ShowSettings();
        break;
      default:
        ShowHub();
        break;
    }
  }

  partial void OnSectionChanged(NavSection value)
  {
    OnPropertyChanged(nameof(IsHubSelected));
    OnPropertyChanged(nameof(IsSourcesSelected));
    OnPropertyChanged(nameof(IsPlanSelected));
    OnPropertyChanged(nameof(IsHistorySelected));
    OnPropertyChanged(nameof(IsSettingsSelected));
  }

  partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(HasPending));

  private void Show(NavSection section, object page)
  {
    (CurrentPage as IDisposable)?.Dispose();
    Section = section;
    CurrentPage = page;
  }

  /// <summary>Text built in code redraws in the new display language; bound catalog text follows by itself.</summary>
  private void OnLanguageChanged()
  {
    var dispatcher = Application.Current?.Dispatcher;
    if (dispatcher is not null && !dispatcher.CheckAccess())
    {
      dispatcher.BeginInvoke(OnLanguageChanged);
      return;
    }
    RefreshHeader();
    (CurrentPage as PageViewModel)?.Refresh();
  }

  private void OnStoreChanged(WorkbenchChangedEventArgs change)
  {
    RefreshHeader();
    // Scan progress only redraws the page that shows it; other pages keep their state (open panels, focus).
    if (!change.OnlyProgress || CurrentPage is SourcesViewModel)
    {
      (CurrentPage as PageViewModel)?.Refresh();
    }
  }

  private void RefreshHeader()
  {
    PendingCount = _store.Plan.Count;
    Strip.Clear();
    if (_store.Sources.Count == 0)
    {
      Strip.Add(new StripItem(AppText.Get("header.strip.noSources"), StripTone.Plain));
      return;
    }

    var indexed = _store.Sources.Sum(s => s.IndexedFiles);
    Strip.Add(new StripItem(AppText.Plural("header.strip.indexed", indexed, ("count", Fmt.Count(indexed))), StripTone.Plain));
    var active = _store.Jobs.FirstOrDefault(j => j.IsActive);
    if (active is not null)
    {
      Strip.Add(new StripItem(AppText.Get("header.strip.job", ("title", active.Title), ("progress", JobProgress(active))), StripTone.Busy));
    }
    var next = _store.Jobs.FirstOrDefault(j => j.State == ScanJobState.Queued);
    if (next is not null)
    {
      Strip.Add(new StripItem(AppText.Get("header.strip.next", ("title", next.Title)), StripTone.Plain));
    }
    foreach (var source in _store.Sources.Where(s => s.State == SourceState.Offline))
    {
      Strip.Add(new StripItem(AppText.Get("header.strip.offline", ("path", source.RootPath), ("name", source.Name)), StripTone.Offline));
    }
    var open = _store.Stacks.Count(s => s.Status == StackStatus.Open);
    if (open > 0)
    {
      Strip.Add(new StripItem(AppText.Plural("header.strip.open", open, ("count", Fmt.Count(open))), StripTone.Plain));
    }
  }

  internal static string JobProgress(ScanJobInfo job) => job.State switch
  {
    ScanJobState.Pausing => AppText.Get("header.job.pausing"),
    ScanJobState.Paused => AppText.Get("header.job.paused"),
    _ => job.Phase == ScanPhase.Discover
      ? AppText.Plural("header.job.discovering", job.Discovered, ("count", Fmt.Count(job.Discovered)))
      : AppText.Get("header.job.checking", ("count", Fmt.Count(job.Hashed))),
  };
}
