using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Semojum.Desktop.Controls;

// Preserve custom button colors while giving the three design-system colors their states.
public sealed class FigmaButtonStateBrush : IMultiValueConverter
{
    internal static MultiBinding Binding(bool border = false)
    {
        var binding = new MultiBinding { Converter = new FigmaButtonStateBrush(), ConverterParameter = border };
        foreach (var name in new[] { nameof(Control.Background), nameof(Control.BorderBrush), nameof(UIElement.IsMouseOver), nameof(ButtonBase.IsPressed), nameof(UIElement.IsEnabled) })
            binding.Bindings.Add(new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        return binding;
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var border = parameter is true || Equals(parameter, "genericBorder");
        if (values.Length < 5 || values[0] is not SolidColorBrush background) return values.Length > (border ? 1 : 0) ? values[border ? 1 : 0] : DependencyProperty.UnsetValue;
        var original = border ? values[1] : values[0];
        if (parameter is string && values[4] is false) return original;
        var color = background.Color;
        if (border && (original is not SolidColorBrush originalBorder || originalBorder.Color != color)) return original;
        if (color.A == 0 && !border && values[4] is true)
        {
            var ghost = values[3] is true ? "#EAEAEA" : values[2] is true ? "#F0F0F0" : null;
            return ghost is null ? original : (Brush)new BrushConverter().ConvertFromString(ghost)!;
        }
        if (color.A != 255) return original;
        var hex = color.ToString().ToUpperInvariant();
        var enabled = values[4] is true;
        var pressed = enabled && values[3] is true;
        var hover = enabled && values[2] is true;
        string? replacement = hex switch
        {
            "#FF005FB8" => !enabled ? "#BFBFBF" : pressed ? "#004A91" : hover ? "#1A6FC0" : null,
            "#FFC42B1C" => !enabled ? "#E3BDB9" : pressed ? "#A82318" : hover ? "#CF3B2C" : null,
            "#FFFBFBFB" or "#FFFFFFFF" => border ? null : !enabled ? "#F5F5F5" : pressed ? "#F0F0F0" : hover ? "#F5F5F5" : null,
            _ => null
        };
        return replacement is null ? original : (Brush)new BrushConverter().ConvertFromString(replacement)!;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class FigmaButtonForegroundBrush : IMultiValueConverter
{
    internal static MultiBinding Binding()
    {
        var binding = new MultiBinding { Converter = new FigmaButtonForegroundBrush() };
        foreach (var name in new[] { nameof(Control.Foreground), nameof(Control.Background), nameof(UIElement.IsEnabled) })
            binding.Bindings.Add(new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        return binding;
    }
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 3 && values[2] is false && values[1] is SolidColorBrush bg && (bg.Color.ToString() == "#FFFBFBFB" || bg.Color.ToString() == "#FFFFFFFF"))
            return (Brush)new BrushConverter().ConvertFromString("#9F9F9F")!;
        return values.Length > 0 ? values[0] : DependencyProperty.UnsetValue;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class FigmaDisabledIconOpacity : IMultiValueConverter
{
    internal static MultiBinding Binding()
    {
        var binding = new MultiBinding { Converter = new FigmaDisabledIconOpacity() };
        foreach (var name in new[] { nameof(Control.Background), nameof(UIElement.IsEnabled) })
            binding.Bindings.Add(new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        return binding;
    }
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[1] is false && values[0] is SolidColorBrush bg && bg.Color.A == 0 ? .4 : 1d;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

internal sealed class FigmaInputFocusBrush : IMultiValueConverter
{
    internal static MultiBinding Binding()
    {
        var binding = new MultiBinding { Converter = new FigmaInputFocusBrush() };
        foreach (var name in new[] { nameof(Control.Background), nameof(UIElement.IsKeyboardFocusWithin) })
            binding.Bindings.Add(new Binding(name) { RelativeSource = RelativeSource.TemplatedParent });
        return binding;
    }
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length >= 2 && values[1] is true && values[0] is SolidColorBrush bg && bg.Color.ToString() == "#FFFBFBFB"
            ? Brushes.White : values.Length > 0 ? values[0] : DependencyProperty.UnsetValue;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
