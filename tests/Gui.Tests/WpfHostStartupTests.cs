using System.Windows;
using FileWorkbench.Localization;
using Xunit;

namespace FileWorkbench.Gui.Tests;

[Collection(DisplayLanguageTests.Name)]
public sealed class WpfHostStartupTests
{
  [Fact]
  public void Rendering_Host_Preserves_Explicit_Language_And_Does_Not_Start_Product_Windows()
  {
    AppText.Apply("zh-TW", []);
    var errors = WpfHost.Run(() =>
    {
      WpfHost.Flush();
      Assert.Equal("zh-TW", AppText.Preference);
      Assert.Empty(AppText.System);
      Assert.Empty(Application.Current.Windows.Cast<Window>());
      Assert.NotNull(Application.Current.TryFindResource("BorderBrush"));
    });
    Assert.Empty(errors);
  }
}
