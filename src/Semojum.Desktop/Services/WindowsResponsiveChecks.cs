using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Semojum.Desktop.Services;

internal static class WindowsResponsiveChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true);
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.MinWidth <= 480 && window.MinHeight <= 360 && window.ResizeMode == ResizeMode.CanResize,
                "Editor can resize to a 480 by 360 DIP viewport for split-screen use");
            window.WindowState = WindowState.Normal; window.Width = 960; window.Height = 600; window.UpdateLayout();
            check(window.ActualWidth <= 961 && window.ActualHeight <= 601, "Editor accepts a 960 by 600 DIP window");
            check(InBounds(window, window.OriginalPanel) && InBounds(window, window.SourcePanel) && InBounds(window, window.BrailleContainer),
                "All three panels fit in a 960 DIP viewport");
            var before = window.Session.ActiveBlock!.Source;
            window.PageEditor.Editor.Document.Insert(0, "가");
            check(window.Session.ActiveBlock.Source == "가" + before, "The source editor remains usable in a 960 DIP viewport");
            window.Width = 480; window.Height = 360; window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.ActualWidth <= 481 && window.ActualHeight <= 361, "Editor accepts a smaller 480 by 360 DIP window");
            var panelScroll = window.FindName("PanelScroll") as ScrollViewer;
            var toolScroll = window.FindName("EditorToolbarScroll") as ScrollViewer;
            var formatScroll = window.FindName("FormatToolbarScroll") as ScrollViewer;
            check(panelScroll is { ScrollableWidth: > 0 } && toolScroll is { ScrollableWidth: > 0 } && formatScroll is { ScrollableWidth: > 0 },
                "Panels and both toolbars offer horizontal scrolling in a narrow viewport");
            check(InBounds(window, window.ApplicationMenu), "The main application menu stays reachable in a narrow viewport");
            panelScroll!.ScrollToLeftEnd(); window.UpdateLayout();
            check(InViewport(panelScroll, window.OriginalHeader), "The original panel controls can be reached by scrolling left");
            panelScroll.ScrollToRightEnd(); window.UpdateLayout();
            check(InViewport(panelScroll, window.Braille.Header), "The braille panel controls can be reached by scrolling right");
            toolScroll!.ScrollToLeftEnd(); window.UpdateLayout();
            var save = Descendants(window.EditorToolbar).OfType<Button>().Single(b => Equals(b.Tag, "save"));
            check(InViewport(toolScroll, save), "The save toolbar command is reachable at the left edge");
            toolScroll.ScrollToRightEnd(); window.UpdateLayout();
            check(InViewport(toolScroll, window.RuleSearch), "The rule search remains reachable at the right edge");
            formatScroll!.ScrollToRightEnd(); window.UpdateLayout();
            var lineNumbers = Descendants(formatScroll).OfType<Button>().Single(b => Equals(b.Tag, "lineNumbers"));
            check(InViewport(formatScroll, lineNumbers), "The final formatting command is reachable at the right edge");
            check(double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight), "The editor is not permanently capped to its startup monitor size");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static bool InBounds(Window window, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var rect = element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return rect.Left >= -1 && rect.Top >= -1 && rect.Right <= window.ActualWidth + 1 && rect.Bottom <= window.ActualHeight + 1;
    }
    private static bool InViewport(ScrollViewer scroll, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var presenter = Descendants(scroll).OfType<ScrollContentPresenter>().FirstOrDefault(p => ReferenceEquals(p.TemplatedParent, scroll));
        if (presenter is null) return false;
        var rect = element.TransformToAncestor(presenter).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return rect.Left >= -1 && rect.Top >= -1 && rect.Right <= presenter.ActualWidth + 1 && rect.Bottom <= presenter.ActualHeight + 1;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
