using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Semojum.Desktop.Controls;

/// <summary>Reserve the system scrollbar height only when a toolbar needs horizontal scrolling.</summary>
public sealed class ToolbarHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var height = double.Parse((string)parameter, CultureInfo.InvariantCulture);
        return height + (value is Visibility.Visible ? SystemParameters.HorizontalScrollBarHeight : 0);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
