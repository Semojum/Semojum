using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsSelectionNavigationChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var window = new MainWindow(smoke: true);
        var path = Path.Combine(directory, "semojum-navigation-" + Guid.NewGuid() + ".smj");
        window.Show();
        try
        {
            var model = window.Session;
            model.Blocks.Clear(); model.ActiveBlock = null;
            model.Layout = new(Columns: 8, Lines: 4, StartPage: 7, PageNumbers: "출력 안 함", Footer: "출력 안 함");
            model.Add(new EditorBlock { Id = 1, Page = 1, Source = "원문", Braille = string.Join("\n", Enumerable.Repeat("⠁", 16)), HasManualBraille = true });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            var grid = window.Braille.GridView;
            check(grid.Items.Count == 4 && grid.GoToFace(2), "Fixture navigates to the second of four physical braille faces");
            check(grid.CaretCoordinates is { Face: 2 }, "Caret reports the same physical face number accepted by navigation");
            await window.NavigateAsync(NavigationTarget.BraillePage, "+1", SearchPanel.Braille);
            check(grid.CaretCoordinates is { Face: 3 }, "Relative braille navigation starts at the selected face");
            check(grid.DescribeCaret().Contains("총 4면", StringComparison.Ordinal), "Caret description includes total face count");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
            model.IsDirty = false;
            await window.OpenAsync(path);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
            grid = window.Braille.GridView;
            check(window.Session.Layout.StartPage == 7 && grid.Items.Count == 4 && grid.GoToFace(3)
                && grid.CaretCoordinates is { Face: 3 }, "Saved and reopened document preserves face navigation with a non-default print number");
            await window.NavigateAsync(NavigationTarget.BraillePage, "-1", SearchPanel.Braille);
            check(grid.CaretCoordinates is { Face: 2 }, "Relative face navigation remains correct after reopening");
            window.ToggleDetached();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            var detached = window.DetachedPane!.GridView;
            check(detached.GoToFace(2) && detached.CaretCoordinates is { Face: 2 },
                "Detached braille panel uses the same physical face numbering");
            await window.NavigateAsync(NavigationTarget.BraillePage, "+1", SearchPanel.Braille);
            check(detached.CaretCoordinates is { Face: 3 }, "Relative navigation follows the detached panel caret");
            check(window.StatusPage.Text.Contains("3 / 4", StringComparison.Ordinal), "Status displays current and total faces");
            window.UpdateLayout();
            var status = window.StatusPage;
            var textWidth = new FormattedText(status.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(status.FontFamily, status.FontStyle, status.FontWeight, status.FontStretch), status.FontSize,
                status.Foreground, VisualTreeHelper.GetDpi(status).PixelsPerDip).WidthIncludingTrailingWhitespace;
            check(textWidth <= status.ActualWidth, "Current and total face numbers fit the existing status area");
            window.ToggleDetached();
        }
        finally
        {
            window.PrepareSmokeShutdown(); window.Close(); await window.ReleaseResourcesAsync();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
