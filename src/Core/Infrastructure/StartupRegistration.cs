using System.Security;
using Microsoft.Win32;

namespace FileMergeAssistant.Infrastructure;

/// <summary>The two per-user registry places that decide whether a program starts at sign-in.</summary>
public interface IStartupKeys
{
  /// <summary>The command under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, or null.</summary>
  string? ReadRun(string name);

  void WriteRun(string name, string command);

  void DeleteRun(string name);

  /// <summary>
  /// The value under <c>HKCU\...\Explorer\StartupApproved\Run</c> that Task Manager and Settings > Apps > Startup
  /// write when the user turns an entry off or on, or null when the user never changed it.
  /// </summary>
  byte[]? ReadApproval(string name);
}

/// <summary>The real keys of the signed-in user.</summary>
public sealed class RegistryStartupKeys : IStartupKeys
{
  private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

  public string? ReadRun(string name)
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
    return key?.GetValue(name) as string;
  }

  public void WriteRun(string name, string command)
  {
    using var key = Registry.CurrentUser.CreateSubKey(RunKey);
    key.SetValue(name, command, RegistryValueKind.String);
  }

  public void DeleteRun(string name)
  {
    using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
    key?.DeleteValue(name, throwOnMissingValue: false);
  }

  public byte[]? ReadApproval(string name)
  {
    using var key = Registry.CurrentUser.OpenSubKey(ApprovedKey);
    return key?.GetValue(name) as byte[];
  }
}

public enum StartupState
{
  /// <summary>This copy cannot start at sign-in: only the copy the setup program installed can.</summary>
  Unavailable,

  /// <summary>Windows did not let the app read the setting.</summary>
  Unreadable,

  Enabled,
  Disabled,

  /// <summary>The entry is there, but the user turned it off in Windows; only Windows can turn it back on.</summary>
  DisabledByUser,
}

/// <summary>
/// ADR-014: starting the app at sign-in through the user's Run key, the per-user setup program's counterpart of
/// desktop-work-fleet's MSIX StartupTask. The Run value starts the app with <see cref="LoginArgument"/>; Windows'
/// own switch in Startup apps is read back and never overridden.
/// </summary>
public sealed class StartupRegistration
{
  public const string ValueName = "FileMergeAssistant";
  public const string LoginArgument = "--launch-at-login";

  private readonly IStartupKeys _keys;
  private readonly string? _executable;

  /// <param name="executable">The installed app's executable; null when this copy cannot start at sign-in.</param>
  public StartupRegistration(IStartupKeys keys, string? executable)
  {
    _keys = keys;
    _executable = executable;
  }

  public bool Supported => _executable is not null;

  public string? Command => _executable is null ? null : $"\"{_executable}\" {LoginArgument}";

  public StartupState Read()
  {
    if (!Supported)
    {
      return StartupState.Unavailable;
    }
    try
    {
      if (_keys.ReadRun(ValueName) is null)
      {
        return StartupState.Disabled;
      }
      // Windows marks an entry the user turned off with an odd first byte (03 on this machine) and one turned back
      // on with an even one (02); no value means the user never changed it.
      return _keys.ReadApproval(ValueName) is { Length: > 0 } approval && (approval[0] & 1) == 1
        ? StartupState.DisabledByUser
        : StartupState.Enabled;
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
    {
      return StartupState.Unreadable;
    }
  }

  /// <summary>Adds or removes the Run value. Windows' own switch for it stays as the user left it.</summary>
  /// <exception cref="IOException">The registry could not be changed.</exception>
  /// <exception cref="UnauthorizedAccessException">The registry could not be changed.</exception>
  /// <exception cref="SecurityException">The registry could not be changed.</exception>
  public void Apply(bool enabled)
  {
    if (!Supported)
    {
      return;
    }
    if (enabled)
    {
      _keys.WriteRun(ValueName, Command!);
    }
    else
    {
      _keys.DeleteRun(ValueName);
    }
  }

  /// <summary>
  /// Brings Windows in line with the preferences when an installed copy starts. The first time, the preference (on by
  /// default) is written; later only an existing entry that names another copy of the app is pointed at this one, so
  /// an entry the user deleted is not created again.
  /// </summary>
  /// <returns>The preferences to store, with <see cref="HostPreferences.StartupApplied"/> set once written.</returns>
  public HostPreferences Sync(HostPreferences preferences)
  {
    ArgumentNullException.ThrowIfNull(preferences);
    if (!Supported)
    {
      return preferences;
    }
    try
    {
      if (!preferences.StartupApplied)
      {
        Apply(preferences.StartAtLogin);
        return preferences with { StartupApplied = true };
      }
      if (preferences.StartAtLogin && _keys.ReadRun(ValueName) is { } command
          && !string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
      {
        _keys.WriteRun(ValueName, Command!);
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
    {
      // The settings page shows what Windows has; the next start tries again.
    }
    return preferences;
  }
}
