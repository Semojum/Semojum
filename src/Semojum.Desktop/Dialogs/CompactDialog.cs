using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

/// <summary>Shared compact desktop dialog geometry from the Figma D6/D7 and board screens.</summary>
public class CompactDialog : Window
{
    protected readonly DockPanel Root = new();
    protected readonly WrapPanel Actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 7, 12, 8) };
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(16, 4, 16, 4), Foreground = Brushes.Firebrick };
    protected readonly CancellationTokenSource Lifetime = new();
    private bool busy;
    public CompactDialog(string title, double width, double height, bool popover = false, bool editor = false, bool editorActions = false)
    {
        Title = title; Width = width; Height = height; MinWidth = Math.Min(width, 420); MinHeight = Math.Min(height, 170);
        ResizeMode = editor ? ResizeMode.CanResize : ResizeMode.NoResize;
        FontSize = 12; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
        Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)); Content = Root;
        if (popover)
        {
            Content = null;
            Style = (Style)FindResource(typeof(Window)); WindowStyle = WindowStyle.None;
            Background = Brushes.White;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new() { CaptionHeight = 0, ResizeBorderThickness = new(0), GlassFrameThickness = new(0) });
            Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(95, 100, 109)), BorderThickness = new(1), Child = Root };
            PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Close(); e.Handled = true; } };
        }
        else Services.WindowFrame.Apply(this, editor);
        Status.Visibility = Visibility.Collapsed;
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(Status, (_, _) => Status.Visibility = string.IsNullOrEmpty(Status.Text) ? Visibility.Collapsed : Visibility.Visible);
        var footer = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)), BorderThickness = new(0, 1, 0, 0), Child = Actions };
        if (popover || editor && !editorActions) footer.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(footer, Dock.Bottom); Root.Children.Add(footer);
        DockPanel.SetDock(Status, Dock.Bottom); Root.Children.Add(Status);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) { new HelpWindow(Title) { Owner = this }.Show(); e.Handled = true; } };
        Closed += (_, _) => Lifetime.Cancel();
        Closing += (_, e) => { if (busy && !DialogResult.HasValue) e.Cancel = true; };
    }
    protected Button Action(string label, Action action, bool primary = false)
    {
        var button = Button(label, () => { try { action(); } catch (Exception ex) { Status.Text = ex.Message; } return Task.CompletedTask; }, primary);
        Actions.Children.Add(button); return button;
    }
    protected Button Action(string label, Func<Task> action, bool primary = false)
    {
        var button = Button(label, async () => await RunAsync(action), primary); Actions.Children.Add(button); return button;
    }
    internal static Button Button(string label, Func<Task> action, bool primary = false)
    {
        var b = new Button { Content = label, MinWidth = 76, Height = 24, MinHeight = 24, Padding = new(8, 0, 8, 0), Margin = new(8, 0, 0, 0), FontSize = 12 };
        b.IsDefault = primary;
        b.IsCancel = label == "취소";
        if (primary) { b.Background = b.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 95, 184)); b.Foreground = Brushes.White; }
        b.Click += async (_, _) => await action(); return b;
    }
    protected async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true; Status.Text = ""; Root.IsEnabled = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally { Root.IsEnabled = true; busy = false; }
    }
    protected void OnLoad(Func<Task> action) => Loaded += async (_, _) => await RunAsync(action);
    protected void Body(UIElement element) => Root.Children.Add(element);
    /// <summary>The default ListBox template pads its border by 1px; Figma lists start flush with their margins.</summary>
    internal static void StripListPadding(ListBox list)
    {
        var template = new ControlTemplate(typeof(ListBox));
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.FocusableProperty, false);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        template.VisualTree = scroll; list.Template = template;
    }
    internal static ComboBox Choices(IEnumerable<string> choices, string? selected = null) => new()
    { ItemsSource = choices, SelectedItem = selected, MinHeight = 24, Height = 24, FontSize = 12, Padding = new(6, 0, 6, 0) };
    internal static void Row(Panel panel, string label, UIElement value, double labelWidth = 164)
    {
        var dock = new DockPanel { Margin = new(0, 8, 0, 8) };
        dock.Children.Add(new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        dock.Children.Add(value);
        panel.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)), BorderThickness = new(0, 0, 0, 1), Child = dock });
    }
    internal static void SettingRow(Panel panel, string label, FrameworkElement value, double labelWidth = 164, double fieldWidth = 280)
    {
        var row = new Grid { Height = 41 };
        row.ColumnDefinitions.Add(new() { Width = new(labelWidth) }); row.ColumnDefinitions.Add(new());
        row.Children.Add(new TextBlock { Text = label, FontSize = 14, Height = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 13, 0, 0) });
        value.Width = fieldWidth; value.Height = 24; value.VerticalAlignment = VerticalAlignment.Top; value.HorizontalAlignment = HorizontalAlignment.Left; value.Margin = new(0, 9, 0, 0);
        if (value is Control control) { control.MinHeight = 24; control.FontSize = 12; }
        System.Windows.Automation.AutomationProperties.SetName(value, label);
        Grid.SetColumn(value, 1); row.Children.Add(value);
        panel.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)), BorderThickness = new(0, 0, 0, 1), Child = row });
    }
}
