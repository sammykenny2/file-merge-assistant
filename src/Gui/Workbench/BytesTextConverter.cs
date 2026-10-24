using System.Globalization;
using System.Windows.Data;

namespace FileWorkbench.Gui.Workbench;

/// <summary>Formats a byte count the way the workbench does (2.2 GB, 38 MB, 48 KB).</summary>
public sealed class BytesTextConverter : IValueConverter
{
  public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
    value is long bytes ? Fmt.Size(bytes) : string.Empty;

  public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
