using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Dialogs;

public static class FooterDesign
{
    public static ControlTemplate AddRowTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        var outline = new FrameworkElementFactory(typeof(Rectangle), "Outline");
        outline.SetValue(Shape.StrokeProperty, EditorModalDesign.Color("#EAEAEA"));
        outline.SetValue(Shape.StrokeThicknessProperty, 1d);
        outline.SetValue(Shape.StrokeDashArrayProperty, new DoubleCollection([2d, 2d]));
        outline.SetValue(Rectangle.RadiusXProperty, 4d); outline.SetValue(Rectangle.RadiusYProperty, 4d);
        outline.SetValue(Shape.FillProperty, Brushes.Transparent);
        root.AppendChild(outline);
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        root.AppendChild(content);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Shape.StrokeProperty, EditorModalDesign.Color("#005FB8"), "Outline"));
        focus.Setters.Add(new Setter(Shape.StrokeThicknessProperty, 2d, "Outline")); template.Triggers.Add(focus);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Shape.FillProperty, EditorModalDesign.Color("#EFF6FC"), "Outline")); template.Triggers.Add(hover);
        return template;
    }
    public static void StyleAlignment(SegmentedChoice choice)
    {
        choice.Width = 288; choice.Height = 32; choice.FontSize = 12;
        var style = new Style(typeof(ListBoxItem));
        var surface = new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        surface.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); surface.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = surface }));
        style.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB"))); style.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA"))); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#EFF6FC"))); selected.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#005FB8"))); selected.Setters.Add(new Setter(Control.ForegroundProperty, EditorModalDesign.Color("#003E92"))); style.Triggers.Add(selected);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); style.Triggers.Add(disabled); choice.ItemContainerStyle = style;
    }
    public static FrameworkElement Spinner(TextBox field)
    {
        EditorModalDesign.Input(field); field.Padding = new Thickness(11, 0, 0, 0); field.MinHeight = 0;
        var grid = new Grid { Width = 96, Height = 32, HorizontalAlignment = HorizontalAlignment.Left }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
        Grid.SetColumnSpan(field, 2); grid.Children.Add(field); var arrows = new StackPanel(); Grid.SetColumn(arrows, 1); grid.Children.Add(arrows);
        foreach (var up in new[] { true, false })
        {
            var icon = up ? EditorModalDesign.Icon("footer-up", 9.60063, 5.19934) : EditorModalDesign.Icon("footer-down");
            if (up) { icon.RenderTransform = new ScaleTransform(1, -1); icon.RenderTransformOrigin = new Point(.5, .5); }
            var button = new Button { Content = icon, Height = 16, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = EditorModalDesign.Color("#F0F0F0"), Focusable = false, Template = EditorModalDesign.ButtonTemplate(typeof(Button)) };
            System.Windows.Automation.AutomationProperties.SetName(button, up ? "시작 면 번호 늘리기" : "시작 면 번호 줄이기");
            button.Click += (_, _) => { if (int.TryParse(field.Text, out var n)) field.Text = (n + (up ? 1 : -1)).ToString(); }; arrows.Children.Add(button);
        }
        return grid;
    }
    public static void Row(StackPanel panel, string label, UIElement value)
    {
        if (panel.Children.Count > 0) ((FrameworkElement)panel.Children[panel.Children.Count - 1]).Margin = new Thickness(0, 0, 0, 4);
        var row = new DockPanel { Height = 32, LastChildFill = false }; row.Children.Add(new TextBlock { Text = label, Width = 134, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(value); panel.Children.Add(row);
    }
}
