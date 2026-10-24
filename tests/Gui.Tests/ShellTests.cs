using System.IO;
using FileWorkbench.Gui.Localization;
using FileWorkbench.Gui.Settings;
using FileWorkbench.Gui.Shell;
using FileWorkbench.Gui.Updates;
using FileWorkbench.Gui.ViewModels;
using FileWorkbench.Infrastructure;
using FileWorkbench.Localization;
using FileWorkbench.Updates;
using FileWorkbench.Workbench;
using Xunit;

namespace FileWorkbench.Gui.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DisplayLanguageTests
{
  public const string Name = "Display language";
}

/// <summary>ADR-014: the tray menu, the startup switches, the update states and the display language.</summary>
[Collection(DisplayLanguageTests.Name)]
public sealed class ShellTests : IDisposable
{
  [Fact]
  public void Product_Name_And_Tagline_Follow_The_Display_Language()
  {
    WpfHost.Run(() =>
    {
      AppText.Apply("zh-TW", []);
      var window = new MainWindow();
      Assert.Equal("貓檔家", window.Title);
      Assert.Equal("整理檔案的專家", Loc.Instance["header.productTagline"]);
      AppText.Apply("en", []);
      window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
      Assert.Equal("File Workbench", window.Title);
      Assert.Equal("Your file organization expert", Loc.Instance["header.productTagline"]);
      Assert.StartsWith("Open File Workbench", AppText.Get("tray.open"), StringComparison.Ordinal);
      window.Close();
    });
  }

  private readonly string _directory = Path.Combine(Path.GetTempPath(), "file-workbench-gui-shell-" + Guid.NewGuid().ToString("N"));

  public void Dispose()
  {
    AppText.Apply("zh-TW", []);
    if (Directory.Exists(_directory))
    {
      Directory.Delete(_directory, recursive: true);
    }
  }

  [Fact]
  public void Tray_Menu_Opens_First_Offers_Hiding_Only_When_Shown_And_Exits_Last()
  {
    Assert.Equal(
      ["show", "hide", "", "update", "", "exit"],
      TrayMenu.Entries(mainVisible: true, updates: true).Select(e => e.Action));
    Assert.Equal(["show", "", "exit"], TrayMenu.Entries(mainVisible: false, updates: false).Select(e => e.Action));
    var open = TrayMenu.Entries(true, true)[0];
    Assert.True(open.Default);
    Assert.All(TrayMenu.Entries(true, true).Where(e => !e.IsSeparator), e => Assert.True(AppText.Has(e.Text), e.Text));
  }

  [Fact]
  public void Startup_Switches_Default_On_On_Off_And_Show_What_Windows_Does()
  {
    var keys = new FakeStartupKeys();
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, keys);
      var page = new SettingsViewModel(new DemoWorkbenchStore(), new MainViewModel(new DemoWorkbenchStore()), context);
      // A new installation: the preference is on, but nothing is in Windows until the first start writes it.
      Assert.False(page.StartAtLogin);
      context.Shell.SyncStartup();
      page.Refresh();
      Assert.True(page.StartAtLogin);
      Assert.True(page.StartAtLoginEnabled);
      Assert.True(page.CloseToTray);
      Assert.False(page.OpenMainAtLogin);
      Assert.True(keys.Run.ContainsKey(StartupRegistration.ValueName));

      page.StartAtLogin = false;
      Assert.Empty(keys.Run);
      Assert.False(HostPreferences.Read(_directory).StartAtLogin);

      page.CloseToTray = false;
      page.OpenMainAtLogin = true;
      var stored = HostPreferences.Read(_directory);
      Assert.False(stored.CloseToTray);
      Assert.True(stored.OpenMainAtLogin);

      // Turned off in Windows' Startup apps: shown as off, locked, with the way to Windows' settings.
      page.StartAtLogin = true;
      keys.Approval[StartupRegistration.ValueName] = [3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
      page.Refresh();
      Assert.False(page.StartAtLogin);
      Assert.False(page.StartAtLoginEnabled);
      Assert.True(page.ShowOpenSystem);
      Assert.Equal(AppText.Get("settings.startup.offByUser"), page.StartAtLoginDetail);
    });
  }

  [Fact]
  public void A_Copy_Without_The_Setup_Program_Explains_Why_It_Cannot_Start_At_Sign_In_Or_Update()
  {
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, installed: false, tray: false);
      var page = new SettingsViewModel(new DemoWorkbenchStore(), new MainViewModel(new DemoWorkbenchStore()), context);
      Assert.False(page.StartAtLoginEnabled);
      Assert.False(page.OpenMainEnabled);
      Assert.Equal(AppText.Get("settings.startup.needsInstall"), page.StartAtLoginDetail);
      Assert.False(page.CloseToTrayEnabled);
      Assert.Equal(AppText.Get("settings.startup.noTray"), page.CloseToTrayDetail);
      Assert.False(page.HasUpdateAction);
      Assert.Equal(AppText.Get("settings.update.needsInstall"), page.UpdateDetailText);
      Assert.True(page.ShowReleasesLink);
      Assert.False(context.Updates.Capable);
    });
  }

  [Fact]
  public void The_Demo_Shows_The_Startup_Switches_But_Does_Not_Change_Windows()
  {
    var keys = new FakeStartupKeys();
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, keys, demo: true);
      var page = new SettingsViewModel(new DemoWorkbenchStore(), new MainViewModel(new DemoWorkbenchStore()), context);
      Assert.False(page.StartAtLoginEnabled);
      Assert.False(page.CloseToTrayEnabled);
      Assert.False(page.OpenMainEnabled);
      Assert.Equal(AppText.Get("settings.startup.demo"), page.StartAtLoginDetail);
      Assert.Empty(keys.Run);
    });
  }

  [Fact]
  public void File_Changes_Are_Counted_Until_Each_One_Ends()
  {
    Assert.False(FileChanges.InProgress);
    var first = FileChanges.Begin();
    var second = FileChanges.Begin();
    Assert.True(FileChanges.InProgress);
    first.Dispose();
    first.Dispose();
    Assert.True(FileChanges.InProgress);
    second.Dispose();
    Assert.False(FileChanges.InProgress);
  }

  [Fact]
  public void Updates_Go_Check_Download_Install_With_One_Button()
  {
    var service = new FakeUpdateService();
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, updates: service);
      var updates = context.Updates;
      var page = new SettingsViewModel(new DemoWorkbenchStore(), new MainViewModel(new DemoWorkbenchStore()), context);
      Assert.Equal(UpdateStep.Check, updates.NextStep);
      Assert.Equal(AppText.Get("settings.update.neverChecked"), page.UpdateDetailText);

      WpfHost.Wait(updates.CheckAsync());
      Assert.Equal(UpdateStatus.UpToDate, updates.Status);
      Assert.NotNull(HostPreferences.Read(_directory).LastUpdateCheck);
      Assert.StartsWith(AppText.Get("settings.update.lastChecked", ("time", string.Empty)), page.UpdateDetailText, StringComparison.Ordinal);

      service.Package = FakeUpdateService.NewPackage("9.0.0");
      page.RunUpdateActionCommand.Execute(null);
      WpfHost.Wait(updates.CheckAsync());
      Assert.Equal(UpdateStatus.UpdateAvailable, updates.Status);
      Assert.Equal(AppText.Get("settings.update.available", ("version", "9.0.0")), page.UpdateStatusText);
      Assert.Equal(AppText.Get("settings.update.download"), page.UpdateActionText);

      // Downloading checks again first, so the installer is the newest one.
      var checks = service.Checks;
      WpfHost.Wait(updates.DownloadAsync());
      Assert.Equal(checks + 1, service.Checks);
      Assert.Equal(UpdateStatus.Downloaded, updates.Status);
      Assert.Equal(AppText.Get("settings.update.install"), page.UpdateActionText);

      // A plan run keeps the setup program from ending the app mid-run.
      updates.Busy = () => true;
      updates.Install();
      Assert.Equal(UpdateStatus.Failed, updates.Status);
      Assert.Equal(AppText.Get("settings.update.planRunning"), page.UpdateDetailText);
      Assert.True(page.UpdateDetailIsError);
      Assert.Equal(UpdateStep.Install, updates.NextStep);
    });
  }

  [Fact]
  public void A_Failed_Check_Shows_Its_Reason_And_Offers_To_Check_Again()
  {
    var service = new FakeUpdateService { CheckFailure = new UpdateException("offline", AppText.Get("update.offline")) };
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, updates: service);
      WpfHost.Wait(context.Updates.CheckAsync());
      Assert.Equal(UpdateStatus.Failed, context.Updates.Status);
      Assert.Equal(AppText.Get("update.offline"), context.Updates.ErrorMessage);
      Assert.Equal(UpdateStep.Check, context.Updates.NextStep);
    });
  }

  [Fact]
  public void A_Second_Request_Joins_The_Running_Check()
  {
    var service = new FakeUpdateService { Gate = new TaskCompletionSource() };
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory, updates: service);
      var first = context.Updates.CheckAsync();
      var second = context.Updates.CheckAsync();
      Assert.Same(first, second);
      service.Gate.SetResult();
      WpfHost.Wait(first);
      Assert.Equal(1, service.Checks);
    });
  }

  [Fact]
  public void Choosing_A_Language_Stores_It_And_Switches_The_Window_At_Once()
  {
    WpfHost.Run(() =>
    {
      var context = TestSettings.Create(_directory);
      var store = new DemoWorkbenchStore();
      var main = new MainViewModel(store, context);
      main.ShowSettings();
      var page = Assert.IsType<SettingsViewModel>(main.CurrentPage);
      var shell = new ShellView { DataContext = main };
      WpfHost.Render(shell, 1440);
      Assert.Equal(["自動偵測", "英文", "繁體中文"], page.Languages.Select(o => o.Label));

      page.Language = "en";
      WpfHost.Flush();
      Assert.Equal("en", AppText.Locale);
      Assert.Equal("en", HostPreferences.Read(_directory).Language);
      Assert.Equal("Settings", Loc.Instance["settings.title"]);
      Assert.Equal(["Auto-detect", "English", "Traditional Chinese"], page.Languages.Select(o => o.Label));
      Assert.Equal("en", page.Language);
      Assert.Equal(AppText.Format("en", "settings.startup.on"), page.StartAtLoginDetail);
      WpfHost.Render(shell, 1440);

      page.Language = "zh-TW";
      WpfHost.Flush();
      Assert.Equal("zh-TW", AppText.Locale);
      Assert.Equal("設定", Loc.Instance["settings.title"]);
    });
  }
}
