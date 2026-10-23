using FileMergeAssistant.Infrastructure;
using Xunit;

namespace FileMergeAssistant.Tests.Infrastructure;

/// <summary>Start at sign-in against in-memory keys; no test touches the real Run, StartupApproved or app keys.</summary>
public class StartupRegistrationTests
{
  private const string Exe = @"C:\Users\someone\AppData\Local\Programs\FileMergeAssistant\app\FileMergeAssistant.exe";

  [Fact]
  public void A_Copy_The_Setup_Program_Did_Not_Install_Cannot_Start_At_Sign_In()
  {
    var keys = new FakeStartupKeys();
    var startup = new StartupRegistration(keys, executable: null);
    Assert.False(startup.Supported);
    Assert.Equal(StartupState.Unavailable, startup.Read());
    startup.Apply(true);
    startup.Sync(true);
    Assert.Empty(keys.Run);
    Assert.False(keys.Applied);
  }

  [Fact]
  public void The_Run_Value_Starts_The_Installed_Copy_At_Sign_In()
  {
    var keys = new FakeStartupKeys();
    var startup = new StartupRegistration(keys, Exe);
    Assert.Equal(StartupState.Disabled, startup.Read());
    startup.Apply(true);
    Assert.Equal($"\"{Exe}\" --launch-at-login", keys.Run[StartupRegistration.ValueName]);
    Assert.Equal(StartupState.Enabled, startup.Read());
    startup.Apply(false);
    Assert.Empty(keys.Run);
    Assert.Equal(StartupState.Disabled, startup.Read());
    Assert.True(keys.Applied);
  }

  [Theory]
  [InlineData(0x03, StartupState.DisabledByUser)]
  [InlineData(0x01, StartupState.DisabledByUser)]
  [InlineData(0x02, StartupState.Enabled)]
  [InlineData(0x06, StartupState.Enabled)]
  public void Windows_Startup_Apps_Switch_Is_Read_Back(byte first, StartupState expected)
  {
    var keys = new FakeStartupKeys();
    keys.Approval[StartupRegistration.ValueName] = [first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
    var startup = new StartupRegistration(keys, Exe);
    startup.Apply(true);
    Assert.Equal(expected, startup.Read());
    // Turning it on again never touches Windows' own switch.
    Assert.Equal(first, keys.Approval[StartupRegistration.ValueName][0]);
  }

  [Fact]
  public void The_First_Start_Writes_The_Default_And_Later_Starts_Do_Not_Recreate_A_Removed_Entry()
  {
    var keys = new FakeStartupKeys();
    var startup = new StartupRegistration(keys, Exe);
    startup.Sync(new HostPreferences().StartAtLogin);
    Assert.True(keys.Applied);
    Assert.True(keys.Run.ContainsKey(StartupRegistration.ValueName));

    // The user removed the entry outside the app, or a damaged preferences file fell back to "on": it stays removed.
    keys.Run.Clear();
    startup.Sync(true);
    Assert.Empty(keys.Run);
  }

  [Fact]
  public void A_New_Installation_Writes_The_Preference_Again()
  {
    var keys = new FakeStartupKeys();
    var startup = new StartupRegistration(keys, Exe);
    startup.Sync(true);
    // Uninstalling removes the Run value and the installation's key; the preferences in the data folder stay.
    keys.Run.Clear();
    keys.Applied = false;
    startup.Sync(true);
    Assert.True(keys.Run.ContainsKey(StartupRegistration.ValueName));
  }

  [Fact]
  public void The_First_Start_Removes_The_Entry_When_The_Preference_Is_Off()
  {
    var keys = new FakeStartupKeys();
    keys.Run[StartupRegistration.ValueName] = "\"C:\\old\\FileMergeAssistant.exe\" --launch-at-login";
    var startup = new StartupRegistration(keys, Exe);
    startup.Sync(false);
    Assert.True(keys.Applied);
    Assert.Empty(keys.Run);
  }

  [Fact]
  public void An_Entry_Naming_Another_Copy_Is_Pointed_At_This_One()
  {
    var keys = new FakeStartupKeys { Applied = true };
    keys.Run[StartupRegistration.ValueName] = "\"D:\\Apps\\FileMergeAssistant\\app\\FileMergeAssistant.exe\" --launch-at-login";
    var startup = new StartupRegistration(keys, Exe);
    startup.Sync(true);
    Assert.Equal(startup.Command, keys.Run[StartupRegistration.ValueName]);
  }

  [Fact]
  public void Registry_Failures_Are_Reported_As_Unreadable_And_Do_Not_Stop_A_Start()
  {
    var keys = new FakeStartupKeys { Fail = true };
    var startup = new StartupRegistration(keys, Exe);
    Assert.Equal(StartupState.Unreadable, startup.Read());
    startup.Sync(true);
    Assert.Throws<UnauthorizedAccessException>(() => startup.Apply(true));
  }

  internal sealed class FakeStartupKeys : IStartupKeys
  {
    public Dictionary<string, string> Run { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, byte[]> Approval { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Applied { get; set; }

    public bool Fail { get; set; }

    public string? ReadRun(string name) => Check(Run.GetValueOrDefault(name));

    public void WriteRun(string name, string command) => Run[Check(name)] = command;

    public void DeleteRun(string name) => Run.Remove(Check(name));

    public byte[]? ReadApproval(string name) => Check(Approval.GetValueOrDefault(name));

    public bool ReadApplied() => Check(Applied);

    public void WriteApplied() => Applied = Check(true);

    private T Check<T>(T value) => Fail ? throw new UnauthorizedAccessException("denied") : value;
  }
}
