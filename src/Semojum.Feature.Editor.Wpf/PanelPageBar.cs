using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SharpVectors.Converters;

namespace Semojum.Feature.Editor.Wpf;

/// <summary>Navigation for actual page identities; missing and unavailable pages are never invented.</summary>
public sealed class PanelPageBar : UserControl
{
    private readonly Button previous = new(), next = new();
    private readonly TextBox input = new();
    private readonly TextBlock total = new();
    private readonly StackPanel numbers = new() { Orientation = Orientation.Horizontal };
    private int[] pages = [];
    private HashSet<int> available = [];
    private int? current;
    private string stateKey = "";
    public event Action<int>? PageRequested;
    public event Action? InvalidPageRequested;
    public PanelPageBar()
    {
        Height = 32;
        var border = new Border { Background = Brush("#FBFBFB"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new Thickness(0, 1, 0, 0) };
        var layout = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        border.Child = layout; Content = border;
        ConfigureButton(previous); ConfigureButton(next);
        System.Windows.Automation.AutomationProperties.SetName(previous, "이전 쪽");
        System.Windows.Automation.AutomationProperties.SetName(next, "다음 쪽");
        previous.Click += (_, _) => Step(-1); next.Click += (_, _) => Step(1);
        layout.Children.Add(previous);
        input.Width = 44; input.Height = 24; input.MinHeight = 24; input.Padding = new Thickness(2, 0, 2, 0);
        input.Margin = new Thickness(4, 0, 8, 0); input.FontSize = 12; input.TextAlignment = TextAlignment.Center;
        input.VerticalContentAlignment = VerticalAlignment.Center; input.BorderBrush = Brush("#EAEAEA");
        System.Windows.Automation.AutomationProperties.SetName(input, "이동할 쪽 번호");
        input.KeyDown += (_, e) => { if (e.Key != Key.Enter) return; e.Handled = true; if (int.TryParse(input.Text, out var page) && available.Contains(page)) PageRequested?.Invoke(page); else { InvalidPageRequested?.Invoke(); input.Text = current?.ToString() ?? "—"; } };
        input.LostKeyboardFocus += (_, _) => input.Text = current?.ToString() ?? "—";
        layout.Children.Add(input);
        total.FontSize = 12; total.Foreground = Brush("#5F5F5F"); total.VerticalAlignment = VerticalAlignment.Center; total.Margin = new Thickness(0, 0, 12, 0);
        layout.Children.Add(total); layout.Children.Add(numbers); layout.Children.Add(next);
        SizeChanged += (_, _) => numbers.Visibility = ActualWidth >= 350 ? Visibility.Visible : Visibility.Collapsed;
    }
    public void SetIcons(string left, string right)
    {
        previous.Content = Icon(left); next.Content = Icon(right);
    }
    public void SetPages(IEnumerable<int> pageNumbers, IEnumerable<int> navigable, int? selected)
    {
        var actual = pageNumbers.Distinct().Order().ToArray(); var allowed = navigable.ToHashSet();
        var key = string.Join(",", actual) + "|" + string.Join(",", allowed.Order()) + "|" + selected;
        if (key == stateKey) return;
        stateKey = key; pages = actual; available = allowed; current = selected is { } p && actual.Contains(p) ? p : null;
        if (!input.IsKeyboardFocusWithin) input.Text = current?.ToString() ?? "—";
        input.IsEnabled = allowed.Count > 0;
        var continuous = pages.SequenceEqual(Enumerable.Range(1, pages.Length));
        total.Text = continuous && pages.Length > 0 ? $"/ {pages.Length}" : "/ —";
        total.ToolTip = !continuous && pages.Length > 0 ? $"총 {pages.Length}쪽 (쪽 번호가 연속적이지 않음)" : null;
        var index = current is null ? -1 : Array.IndexOf(pages, current.Value);
        previous.IsEnabled = index > 0 && available.Contains(pages[index - 1]);
        next.IsEnabled = index >= 0 && index + 1 < pages.Length && available.Contains(pages[index + 1]);
        numbers.Children.Clear();
        if (index < 0) return;
        var first = Math.Max(0, Math.Min(index - 2, pages.Length - 5));
        if (first > 0) AddEllipsis();
        foreach (var page in pages.Skip(first).Take(5))
        {
            var button = new Button { Content = page.ToString(), Width = 26, MinWidth = 26, Height = 24, MinHeight = 24, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0), FontSize = 12,
                IsEnabled = available.Contains(page), Background = page == current ? Brush("#005FB8") : Brushes.Transparent, Foreground = page == current ? Brushes.White : Brush("#5F5F5F"), BorderBrush = Brushes.Transparent };
            button.ToolTip = available.Contains(page) ? $"{page}쪽으로 이동" : $"{page}쪽은 편집할 수 없습니다";
            System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
            button.Click += (_, _) => PageRequested?.Invoke(page); numbers.Children.Add(button);
        }
        if (first + 5 < pages.Length) AddEllipsis();
    }
    private void AddEllipsis() => numbers.Children.Add(new TextBlock { Text = "⋯", Width = 18, TextAlignment = TextAlignment.Center,
        Foreground = Brush("#8A8A8A"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
    private void Step(int delta)
    {
        if (current is null) return;
        var index = Array.IndexOf(pages, current.Value) + delta;
        if (index >= 0 && index < pages.Length && available.Contains(pages[index])) PageRequested?.Invoke(pages[index]);
    }
    private static void ConfigureButton(Button button)
    {
        button.Width = button.MinWidth = 24; button.Height = button.MinHeight = 24; button.Margin = new Thickness(0); button.Padding = new Thickness(0);
        button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
    }
    private static SvgViewbox Icon(string file) => new() { Source = new Uri($"pack://application:,,,/Semojum.Feature.Editor.Wpf;component/Assets/Figma/{file}"), Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false };
    private static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;
}
