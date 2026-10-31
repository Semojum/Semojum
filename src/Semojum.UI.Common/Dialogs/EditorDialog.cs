using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

public abstract class EditorDialog : Window
{
    public new bool? ShowDialog() => Services.OwnedModal.Show(this, base.ShowDialog);
    protected readonly StackPanel Fields = new() { Margin = new Thickness(24, 14, 24, 20) };
    protected readonly TextBlock Error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    protected EditorDialog(string title, double width = 560)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MaxHeight = SystemParameters.WorkArea.Height;
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) { Services.CommonWindowServices.ShowHelp(this, Title); e.Handled = true; } };
        var root = new DockPanel(); Content = root;
        var header = new Border { Background = Brushes.White, Padding = new Thickness(24, 18, 24, 18), BorderBrush = new SolidColorBrush(Color.FromRgb(207, 213, 220)), BorderThickness = new Thickness(0, 0, 0, 1), Child = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold } };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 12, 24, 20) };
        var ok = new Button { Content = "확인", MinWidth = 92, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        ok.Click += (_, _) => { try { if (Apply()) DialogResult = true; } catch (ArgumentException ex) { Error.Text = ex.Message; } };
        var cancel = new Button { Content = "취소", MinWidth = 92, IsCancel = true, Margin = new Thickness(10, 0, 0, 0) };
        footer.Children.Add(ok); footer.Children.Add(cancel); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    protected void Row(string label, FrameworkElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center });
        field.MinHeight = 32; row.Children.Add(field); Fields.Children.Add(row);
    }
    protected void Note(string text) => Fields.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 4), FontSize = 12 });
    protected abstract bool Apply();
}
