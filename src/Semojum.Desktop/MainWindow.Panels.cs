using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Semojum.Core;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<FrameworkElement, Window> detachedPanels = [];
    internal Window? DetachedSourceWindow => detachedPanels.GetValueOrDefault(SourcePanel);
    internal Window? DetachedOriginalWindow => detachedPanels.GetValueOrDefault(OriginalPanel);

    private void BindCommonKeys(Window target)
    {
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("help")), new KeyGesture(Key.F1)));
        for (var i = 0; i < 10; i++)
        {
            var slot = i;
            target.InputBindings.Add(new KeyBinding(new RelayCommand(() => InsertSnippet(slot, SearchPanel.Text)), new KeyGesture(Key.D0 + i, ModifierKeys.Control)));
            target.InputBindings.Add(new KeyBinding(new RelayCommand(() => InsertSnippet(slot, SearchPanel.Braille)), new KeyGesture(Key.D0 + i, ModifierKeys.Alt)));
        }
        foreach (var (key, action) in new[] { (Key.N, "new"), (Key.O, "open"), (Key.S, "save"), (Key.F, "find"), (Key.H, "replace"), (Key.G, "goto"), (Key.W, "closeDocument"), (Key.C, "copy"), (Key.X, "cut"), (Key.V, "paste") })
            target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync(action)), new KeyGesture(key, ModifierKeys.Control)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("appearance")), new KeyGesture(Key.S, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("colors")), new KeyGesture(Key.C, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("defaults")), new KeyGesture(Key.G, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("sixdot")), new KeyGesture(Key.K, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("layout")), new KeyGesture(Key.Y, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("optimize")), new KeyGesture(Key.P, ModifierKeys.Alt)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("optimize")), new KeyGesture(Key.O, ModifierKeys.Control | ModifierKeys.Shift)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("symbol")), new KeyGesture(Key.F4)));
        target.InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync("findNext")), new KeyGesture(Key.F3)));
    }

    internal void ToggleSourcePanel() { SourceToggle.IsChecked = true; TogglePanel(SourcePanel, "텍스트"); }
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
            (Panel: (FrameworkElement)OriginalPanel, Visible: OriginalToggle.IsChecked && !detachedPanels.ContainsKey(OriginalPanel), Weight: 380d, Minimum: 260d),
            (Panel: (FrameworkElement)SourcePanel, Visible: SourceToggle.IsChecked && !detachedPanels.ContainsKey(SourcePanel), Weight: 484d, Minimum: 300d),
            (Panel: (FrameworkElement)BrailleContainer, Visible: BrailleToggle.IsChecked && detachedWindow is null, Weight: 576d, Minimum: 300d)
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
                PanelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
                var splitter = new GridSplitter { ResizeBehavior = GridResizeBehavior.PreviousAndNext, ShowsPreview = true };
                Grid.SetColumn(splitter, column++); PanelGrid.Children.Add(splitter);
            }
            PanelGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.Weight, GridUnitType.Star), MinWidth = item.Minimum });
            Grid.SetColumn(item.Panel, column++); item.Panel.Visibility = Visibility.Visible;
        }
    }
}
