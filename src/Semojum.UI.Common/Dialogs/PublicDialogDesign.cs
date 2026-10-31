using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using SharpVectors.Converters;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public static class PublicDialogDesign
{
    public static Grid Frame(CompactDialog window, string heading, string icon, string automationId, out StackPanel actions, string assemblyName = "Semojum.Mockup")
    {
        if (window.Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#EAEAEA"); frame.Background = Brush("#F3F3F3"); }
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = new(55) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) });
        var title = new Grid { Margin = new(24, 0, 24, 0) };
        title.Children.Add(new TextBlock { Text = heading, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = DesignIcon(icon, 16, 16, assemblyName), Template = SurfaceTemplate(0), ToolTip = "닫기" };
        AutomationProperties.SetAutomationId(close, automationId); AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => window.Close(); title.Children.Add(close);
        layout.Children.Add(title);
        actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 16) };
        var footer = new Border { BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0), Child = actions };
        Grid.SetRow(footer, 2); layout.Children.Add(footer); return layout;
    }
    public static void StyleAction(Button button, bool primary = false)
    {
        button.Width = 88; button.Height = 32; button.MinWidth = button.MinHeight = 0; button.Padding = new(0); button.Template = SurfaceTemplate(4);
        button.Background = Brush(primary ? "#005FB8" : "#FBFBFB"); button.BorderBrush = Brush(primary ? "#005FB8" : "#EAEAEA");
        button.Foreground = primary ? Brushes.White : Brush("#1A1A1A");
    }
    public static FrameworkElement TextField(TextBox input, string label, string hint)
    {
        AutomationProperties.SetName(input, label); input.Background = Brushes.Transparent; input.BorderThickness = new(0);
        input.Padding = new(11, 5, 11, 5); input.FontSize = 12; input.VerticalContentAlignment = VerticalAlignment.Center;
        var holder = new Grid(); holder.Children.Add(input);
        var placeholder = new TextBlock { Text = hint, Foreground = Brush("#8A8A8A"), Margin = new(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
        void Update() => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) => Update(); Update(); holder.Children.Add(placeholder);
        var underline = new Border { Height = 1, Background = Brush("#D1D1D1"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        holder.Children.Add(underline);
        input.GotKeyboardFocus += (_, _) => { underline.Height = 2; underline.Background = Brush("#005FB8"); };
        input.LostKeyboardFocus += (_, _) => { underline.Height = 1; underline.Background = Brush("#D1D1D1"); };
        return new Border { Background = Brush("#FBFBFB"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(4), Child = holder };
    }

    public static Style ConsentStyle(string assemblyName)
    {
        var style = new Style(typeof(CheckBox), Application.Current.FindResource(typeof(CheckBox)) as Style);
        var template = new ControlTemplate(typeof(CheckBox));
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var square = new FrameworkElementFactory(typeof(Border), "Square"); square.SetValue(FrameworkElement.WidthProperty, 20d); square.SetValue(FrameworkElement.HeightProperty, 20d);
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); square.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        square.SetValue(Border.BorderBrushProperty, Brush("#A0A0A0")); square.SetValue(Border.BackgroundProperty, Brushes.White);
        var icon = new FrameworkElementFactory(typeof(SvgViewbox), "Tick"); icon.SetValue(SvgViewbox.SourceProperty, new Uri($"pack://application:,,,/{assemblyName};component/Assets/Figma/public-inquiry-check.svg"));
        icon.SetValue(FrameworkElement.WidthProperty, 16d); icon.SetValue(FrameworkElement.HeightProperty, 16d); icon.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); square.AppendChild(icon); panel.AppendChild(square);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(content);
        template.VisualTree = panel;
        var selected = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#005FB8"), "Square")); selected.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#005FB8"), "Square"));
        selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Tick")); template.Triggers.Add(selected);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Square")); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
}
