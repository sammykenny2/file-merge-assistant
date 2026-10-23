using System.Text.RegularExpressions;
using FileMergeAssistant.Localization;
using Xunit;

namespace FileMergeAssistant.Tests.Localization;

public class AppTextTests
{
  [Fact]
  public void Every_Area_Has_Both_Languages_With_The_Same_Keys_And_Placeholders()
  {
    var areas = AppText.Areas();
    Assert.NotEmpty(areas);
    foreach (var (area, (english, chinese)) in areas)
    {
      Assert.True(chinese is not null, $"{area} has no Chinese table");
      Assert.Equal(english.Keys.Order(StringComparer.Ordinal), chinese!.Keys.Order(StringComparer.Ordinal));
      var prefix = char.ToLowerInvariant(area[0]) + area[1..] + ".";
      foreach (var (key, text) in english)
      {
        Assert.True(key.StartsWith(prefix, StringComparison.Ordinal), $"{key} is not in area {area}");
        Assert.False(string.IsNullOrWhiteSpace(text), $"{key} is empty in English");
        Assert.False(string.IsNullOrWhiteSpace(chinese[key]), $"{key} is empty in Chinese");
        Assert.Equal(Placeholders(text), Placeholders(chinese[key]));
      }
    }
  }

  [Fact]
  public void No_Key_Is_Defined_In_Two_Areas()
  {
    var total = AppText.Areas().Values.Sum(tables => tables.English.Count);
    Assert.Equal(total, AppText.Table("en").Count);
    Assert.Equal(total, AppText.Table("zh-TW").Count);
  }

  [Theory]
  [InlineData("en", "en")]
  [InlineData("en-GB", "en")]
  [InlineData("EN_us", "en")]
  [InlineData("zh", "zh-TW")]
  [InlineData("zh-TW", "zh-TW")]
  [InlineData("zh-Hant", "zh-TW")]
  [InlineData("zh-Hant-TW", "zh-TW")]
  [InlineData("zh-HK", "zh-TW")]
  [InlineData("zh-MO", "zh-TW")]
  [InlineData("zh_TW.UTF-8", "zh-TW")]
  [InlineData("zh-CN", null)]
  [InlineData("zh-Hans", null)]
  [InlineData("ja-JP", null)]
  [InlineData(null, null)]
  public void Match_Follows_The_Display_Language_Rules(string? tag, string? expected) =>
    Assert.Equal(expected, AppText.Match(tag));

  [Fact]
  public void Auto_Takes_The_First_Matching_System_Language_Else_English()
  {
    Assert.Equal("zh-TW", AppText.Resolve("auto", ["ja-JP", "zh-Hant-TW", "en-US"]));
    Assert.Equal("en", AppText.Resolve("auto", ["zh-CN", "en-US", "zh-TW"]));
    Assert.Equal("en", AppText.Resolve("auto", ["ja-JP"]));
    Assert.Equal("en", AppText.Resolve("auto", []));
    Assert.Equal("zh-TW", AppText.Resolve("zh-TW", ["en-US"]));
    Assert.Equal("en", AppText.Resolve("en", ["zh-TW"]));
    Assert.Equal("en", AppText.Resolve("fr", ["fr-FR"]));
  }

  [Fact]
  public void Language_Choices_Use_The_Reference_Wording()
  {
    // desktop-work-fleet names each option in the current display language and never names what Auto resolved to.
    Assert.Equal(["auto", "en", "zh-TW"], AppText.Preferences);
    Assert.Equal("Auto-detect", AppText.Format("en", "settings.general.languageAuto"));
    Assert.Equal("English", AppText.Format("en", "settings.general.languageEnglish"));
    Assert.Equal("Traditional Chinese", AppText.Format("en", "settings.general.languageTraditionalChinese"));
    Assert.Equal("自動偵測", AppText.Format("zh-TW", "settings.general.languageAuto"));
    Assert.Equal("英文", AppText.Format("zh-TW", "settings.general.languageEnglish"));
    Assert.Equal("繁體中文", AppText.Format("zh-TW", "settings.general.languageTraditionalChinese"));
  }

  [Fact]
  public void Format_Fills_Placeholders_And_Falls_Back_To_English_Then_The_Key()
  {
    Assert.Equal("Version 1.2.3 is available", AppText.Format("en", "settings.update.available", ("version", "1.2.3")));
    Assert.Equal("有新版本 1.2.3", AppText.Format("zh-TW", "settings.update.available", ("version", "1.2.3")));
    Assert.Equal("Version {version} is available", AppText.Format("en", "settings.update.available"));
    Assert.Equal("no.such.key", AppText.Format("zh-TW", "no.such.key"));
  }

  private static string[] Placeholders(string text) =>
    [.. AppText.Placeholder().Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal)];
}
