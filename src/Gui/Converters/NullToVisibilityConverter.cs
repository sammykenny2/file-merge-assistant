using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileMergeAssistant.Gui.Converters;

/// <summary>Collapses an element whose bound value is null or an empty string.</summary>
public class NullToVisibilityConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
  {
    return value is null || (value is string text && text.Length == 0) ? Visibility.Collapsed : Visibility.Visible;
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}
