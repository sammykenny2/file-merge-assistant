using System.Security;
using Microsoft.Win32;

namespace FileWorkbench.Infrastructure;

/// <summary>The per-user registry places that decide whether the app starts at sign-in.</summary>
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

  /// <summary>Whether this installation has written its start-at-sign-in setting to Windows before.</summary>
  bool ReadApplied();

  void WriteApplied();
}

/// <summary>The real keys of the signed-in user.</summary>
public sealed class RegistryStartupKeys : IStartupKeys
{
  private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
  private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

  // The installation's own key (installer/FileWorkbench.nsi, REG_APP). Uninstalling deletes it, so a new
  // installation counts as a first start again.
  private const string AppKey = @"Software\FileMergeAssistant";
  private const string AppliedValue = "StartupApplied";

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

  public bool ReadApplied()
  {
    using var key = Registry.CurrentUser.OpenSubKey(AppKey);
    return key?.GetValue(AppliedValue) is int applied && applied == 1;
  }

  public void WriteApplied()
  {
    using var key = Registry.CurrentUser.CreateSubKey(AppKey);
    key.SetValue(AppliedValue, 1, RegistryValueKind.DWord);
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
  // Keep the Windows identity so an upgrade respects the user's existing Startup apps switch.
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

  /// <summary>
  /// Adds or removes the Run value, the user's own choice from the settings page. Windows' own switch for it stays as
  /// the user left it.
  /// </summary>
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
    _keys.WriteApplied();
  }

  /// <summary>
  /// Brings Windows in line with the preference when an installed copy starts. The first start of an installation
  /// writes the preference (on by default); later starts only point an existing entry that names another copy of the
  /// app at this one, so an entry the user deleted is not created again. A damaged preferences file therefore cannot
  /// bring an entry back either.
  /// </summary>
  public void Sync(bool startAtLogin)
  {
    if (!Supported)
    {
      return;
    }
    try
    {
      if (!_keys.ReadApplied())
      {
        Apply(startAtLogin);
        return;
      }
      if (startAtLogin && _keys.ReadRun(ValueName) is { } command
          && !string.Equals(command, Command, StringComparison.OrdinalIgnoreCase))
      {
        _keys.WriteRun(ValueName, Command!);
      }
    }
    catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
    {
      // The settings page shows what Windows has; the next start tries again.
    }
  }
}
