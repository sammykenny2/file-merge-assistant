using System.Diagnostics;
using System.IO;
using System.Security;
using FileMergeAssistant.Infrastructure;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Gui.Shell;

/// <summary>
/// ADR-014: what the app does around its main window, from <c>host-preferences.json</c> in the data folder: starting at
/// sign-in, staying in the tray when the window closes, showing the window after a sign-in start, and the display
/// language. Changes are written at once; a failure is reported in the display language and changes nothing.
/// </summary>
public sealed class HostShell
{
  /// <summary>
  /// How long a hidden sign-in start waits for the notification area before it shows the main window instead, so the
  /// app is never left running where the user cannot reach it. At sign-in the taskbar may come up after the app.
  /// </summary>
  public static readonly TimeSpan TrayWait = TimeSpan.FromSeconds(30);

  public HostShell(string directory, bool launchedAtLogin, StartupRegistration startup)
  {
    Directory = Path.GetFullPath(directory);
    LaunchedAtLogin = launchedAtLogin;
    Startup = startup;
    Preferences = HostPreferences.Read(Directory);
  }

  public string Directory { get; }

  /// <summary>Windows started the app at sign-in (<see cref="StartupRegistration.LoginArgument"/>).</summary>
  public bool LaunchedAtLogin { get; }

  public StartupRegistration Startup { get; }

  public HostPreferences Preferences { get; private set; }

  /// <summary>Hidden at sign-in unless the user asked for the main window; a launch by the user always shows it.</summary>
  public bool StartHidden => LaunchedAtLogin && !Preferences.OpenMainAtLogin;

  public event EventHandler? Changed;

  /// <summary>Writes the start-at-sign-in preference to Windows the first time an installation starts.</summary>
  public void SyncStartup() => Startup.Sync(Preferences.StartAtLogin);

  /// <summary>One switch from the settings page: startAtLogin, closeToTray or openMainAtLogin.</summary>
  /// <exception cref="InvalidOperationException">The change could not be saved; the message says so in the display language.</exception>
  public void Set(string name, bool value)
  {
    var next = Preferences.With(name, value);
    if (name == "startAtLogin")
    {
      try
      {
        Startup.Apply(value);
      }
      catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
      {
        throw new InvalidOperationException(AppText.Get("settings.startup.registryFailed", ("detail", error.Message)), error);
      }
    }
    Write(next, "settings.startup.saveFailed");
  }

  /// <summary>Stores the display language and switches the app to it at once.</summary>
  /// <exception cref="InvalidOperationException">The language could not be saved.</exception>
  public void SetLanguage(string preference)
  {
    Write(Preferences.WithLanguage(preference), "settings.general.languageFailed");
    AppText.Apply(preference);
  }

  /// <summary>Remembers the last update check; losing it only changes the "Last checked" time shown.</summary>
  public void RecordUpdateCheck(DateTimeOffset time) =>
    TryWrite(Preferences with { LastUpdateCheck = time.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture) });

  public DateTimeOffset? LastUpdateCheck =>
    DateTimeOffset.TryParse(Preferences.LastUpdateCheck, System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.AssumeUniversal, out var time) ? time : null;

  /// <summary>Opens Settings > Apps > Startup, where the user turns the entry back on.</summary>
  /// <exception cref="InvalidOperationException">Windows could not open it.</exception>
  public static void OpenSystemSettings()
  {
    try
    {
      Process.Start(new ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true })?.Dispose();
    }
    catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
    {
      throw new InvalidOperationException(AppText.Get("settings.startup.systemFailed"), error);
    }
  }

  private void Write(HostPreferences next, string failureKey)
  {
    try
    {
      next.Write(Directory);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      throw new InvalidOperationException(AppText.Get(failureKey), error);
    }
    Preferences = next;
    Changed?.Invoke(this, EventArgs.Empty);
  }

  private void TryWrite(HostPreferences next)
  {
    try
    {
      next.Write(Directory);
      Preferences = next;
      Changed?.Invoke(this, EventArgs.Empty);
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
    {
      // Only bookkeeping is lost; the next start or check writes it again.
    }
  }
}
