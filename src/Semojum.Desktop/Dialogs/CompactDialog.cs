using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

/// <summary>Shared compact desktop dialog geometry from the Figma D6/D7 and board screens.</summary>
public class CompactDialog : Window
{
    protected readonly DockPanel Root = new();
    protected readonly StackPanel Actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 8, 12, 8) };
    protected readonly TextBlock Status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(16, 4, 16, 4), Foreground = Brushes.Firebrick };
    protected readonly CancellationTokenSource Lifetime = new();
    private bool busy;
    public CompactDialog(string title, double width, double height)
    {
        Title = title; Width = width; Height = height; MinWidth = Math.Min(width, 420); MinHeight = 260;
        FontSize = 12; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)); Content = Root;
        var footer = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)), BorderThickness = new(0, 1, 0, 0), Child = Actions };
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
        var b = new Button { Content = label, MinWidth = 76, Height = 24, MinHeight = 24, Padding = new(8, 0, 8, 0), Margin = new(4, 0, 4, 0), FontSize = 12 };
        if (primary) { b.Background = new SolidColorBrush(Color.FromRgb(0, 95, 184)); b.Foreground = Brushes.White; }
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
    internal static ComboBox Choices(IEnumerable<string> choices, string? selected = null) => new()
    { ItemsSource = choices, SelectedItem = selected, MinHeight = 24, Height = 24, FontSize = 12, Padding = new(6, 0, 6, 0) };
    internal static void Row(Panel panel, string label, UIElement value, double labelWidth = 164)
    {
        var dock = new DockPanel { Margin = new(0, 8, 0, 8) };
        dock.Children.Add(new TextBlock { Text = label, Width = labelWidth, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        dock.Children.Add(value);
        panel.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(214, 214, 214)), BorderThickness = new(0, 0, 0, 1), Child = dock });
    }
}
