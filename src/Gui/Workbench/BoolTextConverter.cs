using System.Globalization;
using System.Windows.Data;
using FileMergeAssistant.Localization;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>
/// Shows the catalog text of one of two keys for a boolean, in the display language at the time of binding. Use it
/// inside items that the screen recreates in <c>Refresh()</c>, so a language change binds it again.
/// </summary>
public sealed class BoolTextConverter : IValueConverter
{
  public string TrueKey { get; set; } = string.Empty;
  public string FalseKey { get; set; } = string.Empty;

  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => AppText.Get(value is true ? TrueKey : FalseKey);

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
