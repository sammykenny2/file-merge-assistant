using System.Globalization;
using System.Windows.Data;
using FileMergeAssistant.Utilities;

namespace FileMergeAssistant.Gui.Converters;

public class BytesToStringConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
  {
    if (value is long bytes)
      return SizeFormatter.Format(bytes);
    return "0 B";
  }

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}
