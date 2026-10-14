using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<FrameworkElement, Window> detachedPanels = [];
    internal Window? DetachedSourceWindow => detachedPanels.GetValueOrDefault(SourcePanel);
    internal Window? DetachedOriginalWindow => detachedPanels.GetValueOrDefault(OriginalPanel);

    private void BindCommonKeys(Window target)
    {
        foreach (var (key, action) in new[] { (Key.N, "new"), (Key.O, "open"), (Key.S, "save"), (Key.F, "find"), (Key.H, "replace") })
            target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync(action)), new KeyGesture(key, ModifierKeys.Control)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("sixdot")), new KeyGesture(Key.K, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("findNext")), new KeyGesture(Key.F3)));
    }

    internal void ToggleSourcePanel() => TogglePanel(SourcePanel, "텍스트");
    internal void ToggleOriginalPanel()
    {
        OriginalToggle.IsChecked = true;
        TogglePanel(OriginalPanel, "원본");
    }

    private void TogglePanel(FrameworkElement panel, string label)
    {
        if (detachedPanels.TryGetValue(panel, out var existing)) { existing.Close(); return; }
        PanelGrid.Children.Remove(panel);
        var host = new DockPanel();
        var restore = new Button { Content = "메인 창으로 합치기", Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right };
        DockPanel.SetDock(restore, Dock.Top); host.Children.Add(restore); host.Children.Add(panel);
        panel.Visibility = Visibility.Visible;
        var window = new Window { Title = $"{label} — {session.Title}", Owner = this, Width = 680, Height = 760,
            MinWidth = 380, MinHeight = 350, Content = host, DataContext = session,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Tag = label };
        detachedPanels.Add(panel, window); BindCommonKeys(window);
        restore.Click += (_, _) => window.Close();
        window.Closed += (_, _) =>
        {
            host.Children.Remove(panel); window.Content = null;
            detachedPanels.Remove(panel); PanelGrid.Children.Add(panel); UpdatePanelLayout();
        };
        UpdatePanelLayout(); window.Show();
        if (panel == SourcePanel) SourceEditor.Focus();
    }

    private void UpdatePanelLayout()
    {
        if (PanelGrid is null) return;
        foreach (var splitter in PanelGrid.Children.OfType<GridSplitter>().ToArray()) PanelGrid.Children.Remove(splitter);
        PanelGrid.ColumnDefinitions.Clear();
        var panels = new[]
        {
            (Panel: (FrameworkElement)OriginalPanel, Visible: OriginalToggle.IsChecked && !detachedPanels.ContainsKey(OriginalPanel), Weight: .82, Minimum: 260d),
            (Panel: (FrameworkElement)SourcePanel, Visible: !detachedPanels.ContainsKey(SourcePanel), Weight: 1d, Minimum: 340d),
            (Panel: (FrameworkElement)BrailleContainer, Visible: detachedWindow is null, Weight: 1d, Minimum: 340d)
        };
        var column = 0;
        foreach (var item in panels)
        {
            if (!item.Visible)
            {
                if (!detachedPanels.ContainsKey(item.Panel)) item.Panel.Visibility = Visibility.Collapsed;
                continue;
            }
            if (column > 0)
            {
                PanelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                var splitter = new GridSplitter { ResizeBehavior = GridResizeBehavior.PreviousAndNext };
                Grid.SetColumn(splitter, column++); PanelGrid.Children.Add(splitter);
            }
            PanelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.Weight, GridUnitType.Star), MinWidth = item.Minimum });
            Grid.SetColumn(item.Panel, column++); item.Panel.Visibility = Visibility.Visible;
        }
    }
}
