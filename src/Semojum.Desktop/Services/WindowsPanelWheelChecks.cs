using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsPanelWheelChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { ShowInTaskbar = false };
        window.Show();
        try
        {
            // This suite isolates wheel routing; asynchronous translation is covered separately.
            typeof(MainWindow).GetField("autoTranslate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(window, false);
            var model = window.Session;
            model.Blocks.Clear(); model.ActiveBlock = null;
            var first = new EditorBlock { Id = 1, Page = 1, Source = string.Join("\n", Enumerable.Repeat("첫 페이지", 100)), Braille = "⠁" };
            var second = new EditorBlock { Id = 2, Page = 2, Source = "둘째 페이지", Braille = "⠃" };
            model.Add(first); model.Add(second); model.ActiveBlock = first;
            var editor = window.PageEditor.Editor;
            await Settle();
            var scroll = Descendants(editor).OfType<ScrollViewer>().First();
            scroll.ScrollToVerticalOffset(100); window.UpdateLayout();
            var middle = Wheel(-120); await Settle();
            check(!middle.Handled && model.ActiveBlock == first, "QA1110: source wheel inside a page preserves ordinary scrolling");
            scroll.ScrollToBottom(); window.UpdateLayout();
            var next = Wheel(-120);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (model.ActiveBlock != second && DateTime.UtcNow < deadline) await Task.Delay(20);
            await Settle();
            check(next.Handled && model.ActiveBlock == second && scroll.VerticalOffset < 1,
                $"QA1110: source wheel at bottom opens next page at top (handled={next.Handled}, page={model.ActiveBlock?.Page}, offset={scroll.VerticalOffset}, extent={scroll.ScrollableHeight}, status={model.Status})");
            var previous = Wheel(120); await Settle();
            check(previous.Handled && model.ActiveBlock == first && scroll.VerticalOffset >= scroll.ScrollableHeight - 1,
                "QA1110: source wheel at top opens previous page at bottom");
            scroll.ScrollToTop(); window.UpdateLayout();
            var boundary = Wheel(120); await Settle();
            check(!boundary.Handled && model.ActiveBlock == first, "QA1110: source wheel respects first-page boundary");

            model.ActiveBlock = second; await Settle();
            var end = Wheel(-120); await Settle();
            check(!end.Handled && model.ActiveBlock == second, "QA1110: source wheel respects last-page boundary");
            var grid = window.Braille.GridView;
            first.Braille = string.Join("\n", Enumerable.Repeat("⠁", 100));
            await Settle();
            var brailleScroll = Descendants(grid).OfType<ScrollViewer>().First();
            brailleScroll.ScrollToTop(); window.UpdateLayout();
            void BrailleWheel(int delta) => brailleScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = Mouse.MouseWheelEvent, Source = brailleScroll });
            BrailleWheel(-120); await Settle();
            var step = brailleScroll.VerticalOffset;
            check(step > 0 && step < brailleScroll.ViewportHeight,
                $"QA1110: one braille wheel notch moves less than one viewport (step={step}, viewport={brailleScroll.ViewportHeight})");
            var start = brailleScroll.VerticalOffset;
            for (var i = 0; i < 20; i++) { BrailleWheel(-120); window.UpdateLayout(); }
            await Settle();
            check(brailleScroll.VerticalOffset > start + 26 * 28,
                "QA1110: repeated braille wheel input crosses physical face boundaries");
            var forward = brailleScroll.VerticalOffset;
            BrailleWheel(120); await Settle();
            check(brailleScroll.VerticalOffset < forward, "QA1110: braille wheel can scroll back toward previous faces");
            MouseWheelEventArgs Wheel(int delta)
            {
                var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
                { RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = editor };
                editor.RaiseEvent(args); return args;
            }
            async Task Settle()
            {
                await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            }
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}