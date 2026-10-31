using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using static System.Windows.Controls.Control;
using Semojum.Desktop.Controls;
using SharpVectors.Converters;
using static System.Windows.FrameworkElement;
using static System.Windows.UIElement;

namespace Semojum.Desktop.Dialogs;

// Common Figma primitives shared by dialogs; screen-specific layout remains in each dialog.
public static class FigmaDialogElements
{
    public static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    public static SvgViewbox DesignIcon(string name, double width, double height) => new()
    {
        Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}"),
        Width = width, Height = height, Stretch = Stretch.None
    };
    public static ControlTemplate SurfaceTemplate(double radius, bool stretchContent = false)
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        var outer = new FrameworkElementFactory(typeof(Border), "FocusOuter"); outer.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 3));
        outer.SetValue(Border.BorderThicknessProperty, new Thickness(2)); outer.SetValue(Border.BorderBrushProperty, Brush("#1A1A1A")); outer.SetValue(MarginProperty, new Thickness(-3)); outer.SetValue(VisibilityProperty, Visibility.Collapsed); outer.SetValue(IsHitTestVisibleProperty, false); root.AppendChild(outer);
        var inner = new FrameworkElementFactory(typeof(Border), "FocusInner"); inner.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius + 1)); inner.SetValue(Border.BorderThicknessProperty, new Thickness(1)); inner.SetValue(Border.BorderBrushProperty, Brushes.White); inner.SetValue(MarginProperty, new Thickness(-1)); inner.SetValue(VisibilityProperty, Visibility.Collapsed); inner.SetValue(IsHitTestVisibleProperty, false); root.AppendChild(inner);
        var border = new FrameworkElementFactory(typeof(Border), "Surface"); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, FigmaButtonStateBrush.Binding());
        border.SetBinding(Border.BorderBrushProperty, FigmaButtonStateBrush.Binding(true));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(HorizontalAlignmentProperty, stretchContent ? HorizontalAlignment.Stretch : HorizontalAlignment.Center);
        content.SetBinding(System.Windows.Documents.TextElement.ForegroundProperty, FigmaButtonForegroundBrush.Binding());
        content.SetBinding(OpacityProperty, FigmaDisabledIconOpacity.Binding());
        content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        root.AppendChild(border);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true };
        focus.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "FocusOuter")); focus.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "FocusInner")); template.Triggers.Add(focus);
        return template;
    }
    public static Style ScopeStyle()
    {
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var text = new FrameworkElementFactory(typeof(ContentPresenter)); text.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(text);
        var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        style.Setters.Add(new Setter(BackgroundProperty, Brush("#FBFBFB"))); style.Setters.Add(new Setter(BorderBrushProperty, Brush("#EAEAEA")));
        style.Setters.Add(new Setter(ForegroundProperty, Brush("#5F5F5F")));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, Brush("#EFF6FC"))); selected.Setters.Add(new Setter(BorderBrushProperty, Brush("#005FB8")));
        selected.Setters.Add(new Setter(ForegroundProperty, Brush("#003E92"))); style.Triggers.Add(selected);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .5)); style.Triggers.Add(disabled);
        return style;
    }
}
