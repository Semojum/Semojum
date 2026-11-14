using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Feature.Board;
using Semojum.Feature.Board.Presentation;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsQa1110BoardChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var rows = Enumerable.Range(0, 5).Select(i => new BoardRow(i + 1, "항목 " + i, 0, "작성자", false, "2026-11-14", 0, i < 3 ? "notice-" + i : null)).ToArray();
        var grid = new BoardListView((Style)Application.Current.FindResource("BoardListGrid")) { ItemsSource = rows };
        var flag = new CheckBox { Content = "비밀글", Width = 140, Height = 20, VerticalAlignment = VerticalAlignment.Center, Padding = new(0), FontSize = 14, Style = BoardDesign.CheckStyle() };
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Height = 24 }; flags.Children.Add(flag);
        var panel = new DockPanel(); DockPanel.SetDock(flags, Dock.Top); panel.Children.Add(flags); panel.Children.Add(grid);
        var host = new Window { Width = 1000, Height = 400, Content = panel, ShowInTaskbar = false }; host.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
            if (Environment.GetEnvironmentVariable("QA1110_BOARD_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(host, capture);
            var colors = Enumerable.Range(0, 5).Select(i => ((grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow)?.Background as SolidColorBrush)?.Color).ToArray();
            check(colors[0] == Color.FromRgb(239, 246, 252) && colors[1] == colors[0] && colors.Skip(2).All(c => c == Colors.White),
                "QA1110: only the top two notice rows have highlighted backgrounds (" + string.Join(",", colors) + ")");
            grid.ItemsSource = rows.Skip(3).ToArray();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
            check(Enumerable.Range(0, 2).All(i => (grid.ItemContainerGenerator.ContainerFromIndex(i) as DataGridRow)?.Background is SolidColorBrush { Color: var c } && c == Colors.White),
                "QA1110: filtering to ordinary posts clears recycled notice highlights");
            foreach (var selected in new[] { false, true })
            {
                flag.IsChecked = selected; host.UpdateLayout();
                var square = (Border)flag.Template.FindName("Square", flag);
                var clip = LayoutInformation.GetLayoutClip(flag);
                var bounds = square.TransformToAncestor(flag).TransformBounds(new Rect(square.RenderSize));
                check(clip is null || clip.Bounds.Contains(bounds),
                    $"QA1110: private checkbox border is not clipped ({selected}, square={bounds}, clip={clip?.Bounds})");
            }
        }
        finally { host.Close(); }
    }
}
