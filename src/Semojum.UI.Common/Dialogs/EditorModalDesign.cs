using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public static class EditorModalDesign
{
    public static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    public static SvgViewbox Icon(string name, double width = 16, double height = 16, string assemblyName = "Semojum.Mockup") => new()
    {
        Source = new Uri($"pack://application:,,,/{assemblyName};component/Assets/Figma/{name}.svg"),
        Width = width, Height = height, Stretch = Stretch.None
    };
    public static ControlTemplate ButtonTemplate(Type type)
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty })
            surface.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); surface.AppendChild(content);
        var template = new ControlTemplate(type) { VisualTree = surface };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#005FB8"), "Surface")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Surface")); template.Triggers.Add(disabled);
        return template;
    }
    public static void Input(TextBox box)
    {
        box.BorderBrush = Color("#EAEAEA"); box.BorderThickness = new Thickness(1); box.FontSize = 12;
        var root = new FrameworkElementFactory(typeof(Grid));
        var surface = new FrameworkElementFactory(typeof(Border), "Surface"); surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            surface.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        surface.SetBinding(Border.BackgroundProperty, Semojum.Desktop.Controls.FigmaInputFocusBrush.Binding());
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"); surface.AppendChild(host);
        root.AppendChild(surface);
        var underline = new FrameworkElementFactory(typeof(Border), "Underline"); underline.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); underline.SetValue(FrameworkElement.HeightProperty, 1d); underline.SetValue(Border.BackgroundProperty, Color("#D1D1D1")); underline.SetValue(UIElement.IsHitTestVisibleProperty, false); root.AppendChild(underline);
        var focusLine = new FrameworkElementFactory(typeof(Border), "FocusUnderline"); focusLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); focusLine.SetValue(FrameworkElement.HeightProperty, 2d); focusLine.SetValue(Border.BackgroundProperty, Color("#005FB8")); focusLine.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); focusLine.SetValue(UIElement.IsHitTestVisibleProperty, false); root.AppendChild(focusLine);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = root };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusUnderline")); template.Triggers.Add(focus);
        var borderless = new Trigger { Property = Control.BorderThicknessProperty, Value = new Thickness(0) }; borderless.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Underline")); borderless.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "FocusUnderline")); template.Triggers.Add(borderless);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5, "Surface")); template.Triggers.Add(disabled);
        box.Template = template;
    }
    public static void Frame(Window window, DockPanel root, WrapPanel actions)
    {
        if (window.Content is Grid frame && frame.Children[0] is Border titleBar && titleBar.Child is DockPanel heading)
        {
            frame.RowDefinitions[0].Height = new GridLength(55);
            WindowChrome.GetWindowChrome(window).CaptionHeight = 55;
            titleBar.Background = Color("#F3F3F3"); titleBar.BorderThickness = new Thickness(0);
            var title = heading.Children.OfType<TextBlock>().Single();
            title.Margin = new Thickness(24, 0, 0, 0); title.FontSize = 16; title.FontWeight = FontWeights.Bold; title.VerticalAlignment = VerticalAlignment.Center;
            var buttons = heading.Children.OfType<StackPanel>().Single(); buttons.Margin = new Thickness(0, 0, 24, 0);
            var close = buttons.Children.OfType<Button>().Single(); close.Content = Icon("modal-close");
            close.Width = close.Height = 28; close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0); close.Template = ButtonTemplate(typeof(Button)); close.VerticalAlignment = VerticalAlignment.Center;
            foreach (var outline in frame.Children.OfType<Border>().Where(x => x != titleBar)) outline.Visibility = Visibility.Collapsed;
        }
        if (root.Children.OfType<Border>().FirstOrDefault() is { } footer) footer.BorderThickness = new Thickness(0);
        actions.Margin = new Thickness(24, 16, 24, 16);
        foreach (var button in actions.Children.OfType<Button>())
        {
            button.Height = button.MinHeight = 32; button.MinWidth = 0; button.Width = 88; button.FontSize = 12;
            button.Template = ButtonTemplate(typeof(Button)); button.BorderThickness = new Thickness(1);
            if (!button.IsDefault) button.BorderBrush = Color("#EAEAEA");
        }
    }
    public static Border Card(UIElement child, Thickness margin) => new()
    { Background = Color("#F9F9F9"), CornerRadius = new CornerRadius(4), Padding = new Thickness(16), Margin = margin, Child = child };
}
