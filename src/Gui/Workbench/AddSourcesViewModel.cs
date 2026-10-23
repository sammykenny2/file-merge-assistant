using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Workbench;
using Microsoft.Win32;

namespace FileMergeAssistant.Gui.Workbench;

public sealed partial class LocationOption : ObservableObject
{
  [ObservableProperty]
  private bool _isChecked;

  public LocationOption(string path, string description, bool isChecked)
  {
    Path = path;
    Description = description;
    (Prefix, Tail) = Fmt.SplitPath(path);
    _isChecked = isChecked;
  }

  public string Path { get; }
  public string Prefix { get; }
  public string Tail { get; }
  public string Description { get; }
}

/// <summary>Screen 1, 「第一次開啟：加入來源」; also used to add folders later.</summary>
public sealed partial class AddSourcesViewModel : PageViewModel
{
  [ObservableProperty]
  private bool _skipRegenerable = true;

  [ObservableProperty]
  private bool _skipCloudOnly = true;

  [ObservableProperty]
  private bool _protectSuggested = true;

  [ObservableProperty]
  private bool _isBusy;

  public AddSourcesViewModel(IWorkbenchStore store, INavigator navigator)
    : base(store, navigator)
  {
    IsFirstRun = store.Sources.Count == 0;
    foreach (var location in store.SuggestLocations().Where(l => !store.Sources.Any(s => string.Equals(s.RootPath, l.Path, StringComparison.OrdinalIgnoreCase))))
    {
      Add(new LocationOption(location.Path, location.Description, location.SelectedByDefault));
    }
    ProtectPath = store.SuggestLocations().FirstOrDefault(l => l.Description.Contains("git", StringComparison.OrdinalIgnoreCase))?.Path;
  }

  public bool IsFirstRun { get; }

  public string Title => IsFirstRun ? "先加入你常放東西的地方" : "加入資料夾";

  public string Lead => IsFirstRun
    ? "不用一次掃完。加入的資料夾會在背景建立索引；同一份東西只要在兩個地方都被掃到，就會變成「一疊」出現在工作台，你可以一疊一疊整理。"
    : "加入的資料夾會排在掃描佇列最後。掃描時可以照常在工作台整理已經找到的疊。";

  public ObservableCollection<LocationOption> Locations { get; } = [];

  public string? ProtectPath { get; }

  public bool CanProtect => ProtectPath is not null;

  public string ProtectLabel => $"把 {ProtectPath} 設為受保護";

  public int SelectedCount => Locations.Count(l => l.IsChecked);

  public bool HasSelection => SelectedCount > 0;

  public string AddAndScanLabel => $"加入 {SelectedCount} 個來源並開始掃描";

  public override void Refresh()
  {
  }

  [RelayCommand]
  private void ChooseFolder()
  {
    var dialog = new OpenFolderDialog { Title = "選擇要加入的資料夾", Multiselect = true };
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
      Add(new LocationOption(path, "你選的資料夾", true));
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
      await Store.AddSourcesAsync(paths, new SourceOptions(SkipRegenerable, SkipCloudOnly, protectedPaths), startScan);
      Navigator.ShowSources();
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
