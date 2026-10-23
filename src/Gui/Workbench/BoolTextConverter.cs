using System.Globalization;
using System.Windows.Data;

namespace FileMergeAssistant.Gui.Workbench;

/// <summary>Shows one of two texts for a boolean.</summary>
public sealed class BoolTextConverter : IValueConverter
{
  public string TrueText { get; set; } = string.Empty;
  public string FalseText { get; set; } = string.Empty;

  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? TrueText : FalseText;

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
