using System.Text;
using FileMergeAssistant.Infrastructure;
using Xunit;

namespace FileMergeAssistant.Tests.Infrastructure;

public sealed class HostPreferencesTests : IDisposable
{
  private readonly string _directory = Path.Combine(Path.GetTempPath(), "fma-host-preferences-" + Guid.NewGuid().ToString("N"));

  public void Dispose()
  {
    if (Directory.Exists(_directory))
    {
      Directory.Delete(_directory, recursive: true);
    }
  }

  [Fact]
  public void Defaults_Match_The_Reference_Startup_Behaviour()
  {
    var preferences = HostPreferences.Read(_directory);
    Assert.True(preferences.StartAtLogin);
    Assert.True(preferences.CloseToTray);
    Assert.False(preferences.OpenMainAtLogin);
    Assert.Equal("auto", preferences.Language);
    Assert.False(preferences.StartupApplied);
    Assert.Null(preferences.LastUpdateCheck);
    Assert.False(HostPreferences.Exists(_directory));
  }

  [Fact]
  public void Written_Preferences_Read_Back()
  {
    var written = new HostPreferences(false, false, true, "2026-10-23T01:02:03.0000000+00:00", "en", true);
    written.Write(_directory);
    Assert.True(HostPreferences.Exists(_directory));
    Assert.Equal(written, HostPreferences.Read(_directory));
    Assert.Single(Directory.GetFiles(_directory));
  }

  [Theory]
  [InlineData("")]
  [InlineData("not json")]
  [InlineData("[]")]
  [InlineData("{\"startAtLogin\":false}")]
  [InlineData("{\"version\":2,\"startAtLogin\":false}")]
  [InlineData("{\"version\":\"1\",\"startAtLogin\":false}")]
  public void Unreadable_Or_Unknown_Files_Give_The_Defaults(string content) =>
    Assert.Equal(new HostPreferences(), HostPreferences.Parse(Encoding.UTF8.GetBytes(content)));

  [Fact]
  public void An_Invalid_Value_Falls_Back_On_Its_Own()
  {
    var parsed = HostPreferences.Parse("{\"version\":1,\"startAtLogin\":false,\"closeToTray\":\"no\",\"language\":\"fr\",\"lastUpdateCheck\":\"soon\"}"u8);
    Assert.False(parsed.StartAtLogin);
    Assert.True(parsed.CloseToTray);
    Assert.Equal("auto", parsed.Language);
    Assert.Null(parsed.LastUpdateCheck);
  }

  [Fact]
  public void Only_Known_Switches_And_Languages_Are_Accepted()
  {
    var preferences = new HostPreferences();
    Assert.False(preferences.With("startAtLogin", false).StartAtLogin);
    Assert.False(preferences.With("closeToTray", false).CloseToTray);
    Assert.True(preferences.With("openMainAtLogin", true).OpenMainAtLogin);
    Assert.Equal("zh-TW", preferences.WithLanguage("zh-TW").Language);
    Assert.Throws<ArgumentException>(() => preferences.With("startHidden", true));
    Assert.Throws<ArgumentException>(() => preferences.WithLanguage("zh-CN"));
  }
}
