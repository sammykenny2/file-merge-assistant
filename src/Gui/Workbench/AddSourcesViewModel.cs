using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Workbench;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.Workbench;

public sealed partial class LocationOption : ObservableObject
{
  [ObservableProperty]
  private bool _isChecked;

  [ObservableProperty]
  private string _description;

  /// <param name="isChosen">Picked with the folder dialog rather than suggested; its description is ours, not Core's.</param>
  public LocationOption(string path, string description, bool isChecked, bool isChosen = false)
  {
    Path = path;
    _description = description;
    IsChosen = isChosen;
    (Prefix, Tail) = Fmt.SplitPath(path);
    _isChecked = isChecked;
  }

  public string Path { get; }
  public string Prefix { get; }
  public string Tail { get; }
  public bool IsChosen { get; }
}

/// <summary>Screen 1, 「第一次開啟：加入來源」; also used to add folders later.</summary>
public sealed partial class AddSourcesViewModel : PageViewModel
{
  private string _locale;
  private IReadOnlyList<string>? _problemList;

  [ObservableProperty]
  private bool _skipRegenerable = true;

  [ObservableProperty]
  private bool _skipCloudOnly = true;

  [ObservableProperty]
  private bool _protectSuggested = true;

  [ObservableProperty]
  private bool _isBusy;

  [ObservableProperty]
  private string? _problems;

  [ObservableProperty]
  private IReadOnlyList<HowStep> _steps = [];

  public AddSourcesViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    IsFirstRun = store.Sources.Count == 0;
    _locale = AppText.Locale;
    var suggestions = store.SuggestLocations();
    foreach (var location in suggestions.Where(l => !store.Sources.Any(s => string.Equals(s.RootPath, l.Path, StringComparison.OrdinalIgnoreCase))))
    {
      Add(new LocationOption(location.Path, location.Description, location.SelectedByDefault));
    }
    ProtectPath = suggestions.FirstOrDefault(l => l.Description.Contains("git", StringComparison.OrdinalIgnoreCase))?.Path;
    Refresh();
  }

  public bool IsFirstRun { get; }

  public string Title => AppText.Get(IsFirstRun ? "addSources.title.first" : "addSources.title.more");

  public string Lead => AppText.Get(IsFirstRun ? "addSources.lead.first" : "addSources.lead.more");

  public ObservableCollection<LocationOption> Locations { get; } = [];

  public string? ProtectPath { get; }

  public bool CanProtect => ProtectPath is not null;

  public string ProtectLabel => AppText.Get("addSources.options.protect", ("path", ProtectPath));

  public int SelectedCount => Locations.Count(l => l.IsChecked);

  public bool HasSelection => SelectedCount > 0;

  public string AddAndScanLabel => AppText.Plural("addSources.addAndScan", SelectedCount, ("count", SelectedCount));

  /// <summary>Redraws the text in the current display language; the user's choices stay as they are.</summary>
  public override void Refresh()
  {
    if (_locale != AppText.Locale)
    {
      _locale = AppText.Locale;
      // Core describes the suggested places in the language of the moment, so ask again for their descriptions.
      var suggestions = Store.SuggestLocations();
      foreach (var location in Locations)
      {
        location.Description = location.IsChosen
          ? AppText.Get("addSources.chosen")
          : suggestions.FirstOrDefault(s => string.Equals(s.Path, location.Path, StringComparison.OrdinalIgnoreCase))?.Description ?? location.Description;
      }
    }
    Steps =
    [
      new("1", AppText.Get("addSources.how.discover"), AppText.Get("addSources.how.discoverText")),
      new("2", AppText.Get("addSources.how.candidates"), AppText.Get("addSources.how.candidatesText")),
      new("3", AppText.Get("addSources.how.confirm"), AppText.Get("addSources.how.confirmText")),
      new("4", AppText.Get("addSources.how.workbench"), AppText.Get("addSources.how.workbenchText")),
    ];
    Problems = _problemList is null
      ? null
      : AppText.Get("addSources.problems") + Environment.NewLine + string.Join(Environment.NewLine, _problemList);
    OnPropertyChanged(nameof(Title));
    OnPropertyChanged(nameof(Lead));
    OnPropertyChanged(nameof(ProtectLabel));
    OnPropertyChanged(nameof(AddAndScanLabel));
  }

  [RelayCommand]
  private void ChooseFolder()
  {
    var dialog = new OpenFolderDialog { Title = AppText.Get("addSources.pickerTitle"), Multiselect = true };
    if (dialog.ShowDialog() != true)
    {
      return;
    }
    foreach (var path in dialog.FolderNames)
    {
      var existing = Locations.FirstOrDefault(l => string.Equals(l.Path, path, StringComparison.OrdinalIgnoreCase));
      if (existing is not null)
      {
        existing.IsChecked = true;
        continue;
      }
      Add(new LocationOption(path, AppText.Get("addSources.chosen"), true, isChosen: true));
    }
  }

  [RelayCommand]
  private Task AddAndScanAsync() => AddAsync(startScan: true);

  [RelayCommand]
  private Task AddOnlyAsync() => AddAsync(startScan: false);

  [RelayCommand]
  private void Cancel() => Navigator.ShowSources();

  private async Task AddAsync(bool startScan)
  {
    if (!HasSelection || IsBusy)
    {
      return;
    }
    IsBusy = true;
    try
    {
      var paths = Locations.Where(l => l.IsChecked).Select(l => l.Path).ToList();
      var protectedPaths = ProtectSuggested && ProtectPath is not null ? new[] { ProtectPath } : [];
      var problems = await Store.AddSourcesAsync(paths, new SourceOptions(SkipRegenerable, SkipCloudOnly, protectedPaths), startScan);
      if (problems.Count == 0)
      {
        Navigator.ShowSources();
        return;
      }
      _problemList = problems;
      Problems = AppText.Get("addSources.problems") + Environment.NewLine + string.Join(Environment.NewLine, problems);
      foreach (var location in Locations.Where(l => l.IsChecked && !problems.Any(p => p.StartsWith(l.Path, StringComparison.OrdinalIgnoreCase))).ToList())
      {
        Locations.Remove(location);
      }
    }
    finally
    {
      IsBusy = false;
    }
  }

  private void Add(LocationOption option)
  {
    option.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(LocationOption.IsChecked))
      {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(AddAndScanLabel));
      }
    };
    Locations.Add(option);
    OnPropertyChanged(nameof(SelectedCount));
    OnPropertyChanged(nameof(HasSelection));
    OnPropertyChanged(nameof(AddAndScanLabel));
  }
}
