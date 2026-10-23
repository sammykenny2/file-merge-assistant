using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FileMergeAssistant.Gui.Shell;
using FileMergeAssistant.Gui.Updates;
using FileMergeAssistant.Gui.Workbench;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;
using FileMergeAssistant.Updates;
using FileMergeAssistant.Workbench;

namespace FileMergeAssistant.Gui.Settings;

/// <summary>What the settings page works on, created once by the app.</summary>
/// <param name="Distribution">"installed", "portable" or "debug".</param>
/// <param name="TrayAvailable">Whether the tray icon is there right now.</param>
/// <param name="Demo">Started with --demo: the startup switches, which change Windows, are shown but cannot be changed.</param>
public sealed record SettingsContext(HostShell Shell, UpdateCoordinator Updates, string Distribution, Func<bool> TrayAvailable, bool Demo = false);

/// <summary>One choice of the display-language list; its label is in the current display language.</summary>
public sealed partial class LanguageOption(string value, string labelKey) : ObservableObject
{
  public string Value { get; } = value;

  public string LabelKey { get; } = labelKey;

  [ObservableProperty]
  private string _label = AppText.Get(labelKey);
}

/// <summary>
/// ADR-014: the settings page, laid out like desktop-work-fleet's: General (display language), Startup (three
/// switches) and Version and updates. Changes apply as soon as they are made; there is nothing to save.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel, IDisposable
{
  private readonly SettingsContext _context;
  private bool _rendering;

  [ObservableProperty]
  private string _language = "auto";

  [ObservableProperty]
  private bool _startAtLogin;

  [ObservableProperty]
  private bool _startAtLoginEnabled;

  [ObservableProperty]
  private string _startAtLoginDetail = string.Empty;

  [ObservableProperty]
  private bool _showOpenSystem;

  [ObservableProperty]
  private bool _closeToTray;

  [ObservableProperty]
  private bool _closeToTrayEnabled;

  [ObservableProperty]
  private string _closeToTrayDetail = string.Empty;

  [ObservableProperty]
  private bool _openMainAtLogin;

  [ObservableProperty]
  private bool _openMainEnabled;

  [ObservableProperty]
  private string? _message;

  [ObservableProperty]
  private string _versionText = string.Empty;

  [ObservableProperty]
  private string _distributionText = string.Empty;

  [ObservableProperty]
  private string _updateStatusText = string.Empty;

  [ObservableProperty]
  private string _updateDetailText = string.Empty;

  [ObservableProperty]
  private bool _updateDetailIsError;

  [ObservableProperty]
  private string? _updateActionText;

  [ObservableProperty]
  private bool _showReleasesLink;

  public SettingsViewModel(IWorkbenchStore store, INavigator navigator, SettingsContext context) : base(store, navigator)
  {
    ArgumentNullException.ThrowIfNull(context);
    _context = context;
    _context.Updates.PropertyChanged += OnUpdateChanged;
    Refresh();
  }

  /// <summary>As in desktop-work-fleet, each option is named in the current display language.</summary>
  public IReadOnlyList<LanguageOption> Languages { get; } =
  [
    new("auto", "settings.general.languageAuto"),
    new("en", "settings.general.languageEnglish"),
    new("zh-TW", "settings.general.languageTraditionalChinese"),
  ];

  public bool HasMessage => Message is not null;

  public bool HasUpdateAction => UpdateActionText is not null;

  partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));

  partial void OnUpdateActionTextChanged(string? value) => OnPropertyChanged(nameof(HasUpdateAction));

  public override void Refresh()
  {
    _rendering = true;
    try
    {
      foreach (var option in Languages)
      {
        option.Label = AppText.Get(option.LabelKey);
      }
      Language = _context.Shell.Preferences.Language;
      RenderAnalysis();
      RenderComparison();
      RenderStartup();
      VersionText = AppText.Get("settings.version", ("version", UpdateService.CurrentVersion.ToString(3)));
      DistributionText = AppText.Get("settings.distribution." + _context.Distribution);
      RenderUpdate();
    }
    finally
    {
      _rendering = false;
    }
  }

  public void Dispose()
  {
    CompareEnginesCommand.Cancel();
    _context.Updates.PropertyChanged -= OnUpdateChanged;
  }

  partial void OnLanguageChanged(string value)
  {
    if (_rendering || value is null || value == _context.Shell.Preferences.Language)
    {
      return;
    }
    try
    {
      Message = null;
      // The whole window, this page included, redraws in the new language through AppText.Changed.
      _context.Shell.SetLanguage(value);
    }
    catch (InvalidOperationException error)
    {
      Message = error.Message;
      Refresh();
    }
  }

  partial void OnStartAtLoginChanged(bool value) => Set("startAtLogin", value);

  partial void OnCloseToTrayChanged(bool value) => Set("closeToTray", value);

  partial void OnOpenMainAtLoginChanged(bool value) => Set("openMainAtLogin", value);

  [RelayCommand]
  private void OpenSystemSettings()
  {
    try
    {
      Message = null;
      HostShell.OpenSystemSettings();
    }
    catch (InvalidOperationException error)
    {
      Message = error.Message;
    }
  }

  [RelayCommand]
  private void RunUpdateAction()
  {
    var updates = _context.Updates;
    switch (updates.NextStep)
    {
      case UpdateStep.Check:
        _ = updates.CheckAsync();
        break;
      case UpdateStep.Download:
        _ = updates.DownloadAsync();
        break;
      case UpdateStep.Install:
        updates.Install();
        break;
      default:
        break;
    }
  }

  [RelayCommand]
  private void OpenReleasesPage()
  {
    if (_context.Updates.Repository is { } repository)
    {
      Process.Start(new ProcessStartInfo(UpdateFeed.ReleasesPageUrl(repository).AbsoluteUri) { UseShellExecute = true })?.Dispose();
    }
  }

  private void Set(string name, bool value)
  {
    if (_rendering)
    {
      return;
    }
    try
    {
      Message = null;
      _context.Shell.Set(name, value);
    }
    catch (InvalidOperationException error)
    {
      Message = error.Message;
    }
    Refresh();
  }

  /// <summary>The switches show what Windows actually does; an entry the user turned off there stays off.</summary>
  private void RenderStartup()
  {
    var shell = _context.Shell;
    var preferences = shell.Preferences;
    var state = shell.Startup.Read();
    var demo = _context.Demo;
    StartAtLogin = state == StartupState.Enabled;
    StartAtLoginEnabled = !demo && state is StartupState.Enabled or StartupState.Disabled;
    StartAtLoginDetail = AppText.Get(demo ? "settings.startup.demo" : state switch
    {
      StartupState.Unavailable => "settings.startup.needsInstall",
      StartupState.Unreadable => "settings.startup.unreadable",
      StartupState.DisabledByUser => "settings.startup.offByUser",
      _ => "settings.startup.on",
    });
    ShowOpenSystem = !demo && state == StartupState.DisabledByUser;

    var tray = _context.TrayAvailable();
    CloseToTray = preferences.CloseToTray;
    CloseToTrayEnabled = !demo && tray;
    CloseToTrayDetail = AppText.Get(tray ? "settings.startup.closeToTrayOff" : "settings.startup.noTray");

    OpenMainAtLogin = preferences.OpenMainAtLogin;
    OpenMainEnabled = !demo && state is not (StartupState.Unavailable or StartupState.Unreadable);
  }

  private void RenderUpdate()
  {
    var updates = _context.Updates;
    if (!updates.Capable)
    {
      UpdateStatusText = AppText.Get("settings.update.title");
      UpdateDetailText = updates.ReasonKey is { } reason ? AppText.Get(reason) : string.Empty;
      UpdateDetailIsError = false;
      UpdateActionText = null;
      ShowReleasesLink = updates.Repository is not null;
      return;
    }
    ShowReleasesLink = false;
    var checkedText = updates.LastCheckedAt is { } time
      ? AppText.Get("settings.update.lastChecked", ("time", time.ToLocalTime().ToString("g", AppText.Culture)))
      : AppText.Get("settings.update.neverChecked");
    var version = updates.Version;
    (UpdateStatusText, var detail) = updates.Status switch
    {
      UpdateStatus.Idle => (AppText.Get("settings.update.check"), checkedText),
      UpdateStatus.Checking => (AppText.Get("settings.update.checking"), string.Empty),
      UpdateStatus.UpToDate => (AppText.Get("settings.update.upToDate"), checkedText),
      UpdateStatus.UpdateAvailable => (AppText.Get("settings.update.available", ("version", version)), updates.Summary ?? string.Empty),
      UpdateStatus.Downloading => (AppText.Get("settings.update.downloading", ("version", version)), $"{updates.Percent}%"),
      UpdateStatus.Downloaded => (AppText.Get("settings.update.downloaded", ("version", version)), AppText.Get("settings.update.installOnRestart")),
      UpdateStatus.Installing => (AppText.Get("settings.update.installing"), AppText.Get("settings.update.reopens")),
      UpdateStatus.Failed => (AppText.Get("settings.update.failed"), string.Empty),
      _ => (AppText.Get("settings.update.title"), string.Empty),
    };
    UpdateDetailText = updates.ErrorMessage ?? detail;
    UpdateDetailIsError = updates.ErrorMessage is not null;
    UpdateActionText = updates.NextStep switch
    {
      UpdateStep.Check => AppText.Get("settings.update.check"),
      UpdateStep.Download => AppText.Get("settings.update.download"),
      UpdateStep.Install => AppText.Get("settings.update.install"),
      _ => null,
    };
  }

  private void OnUpdateChanged(object? sender, PropertyChangedEventArgs e)
  {
    _rendering = true;
    try
    {
      RenderUpdate();
    }
    finally
    {
      _rendering = false;
    }
  }
}
