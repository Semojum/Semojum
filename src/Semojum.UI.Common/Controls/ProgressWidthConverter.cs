using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Semojum.Desktop.Controls;

public sealed class ProgressWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 4 || values.Any(v => v == DependencyProperty.UnsetValue)) return 0d;
        var width = System.Convert.ToDouble(values[0], culture); var value = System.Convert.ToDouble(values[1], culture);
        var min = System.Convert.ToDouble(values[2], culture); var max = System.Convert.ToDouble(values[3], culture);
        return max > min ? Math.Max(0, width) * Math.Clamp((value - min) / (max - min), 0, 1) : 0d;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
