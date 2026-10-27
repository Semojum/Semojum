using System.Globalization;
using System.Windows.Data;

namespace Semojum.Desktop.Controls;

public sealed class InterfaceLineHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is double size
        ? size switch { <= 12 => 16d, <= 14 => 20d, <= 20 => 28d, _ => Math.Ceiling(size * 1.3) } : 16d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
