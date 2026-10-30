using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsOriginalWheelChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 700 };
        window.Show();
        try
        {
            typeof(MainWindow).GetField("autoTranslate", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            var model = window.Session;
            model.Blocks.Clear();
            foreach (var page in new[] { 1, 2, 4 }) model.Add(new EditorBlock { Id = page, Page = page, Source = $"{page}쪽 원문", Braille = "⠁" });
            foreach (var page in new[] { 1, 2, 3, 4 }) model.OriginalPages[page] = new OriginalPage("text", null, [$"{page}쪽 원본"]);
            model.PageStatuses[1] = "COMPLETED"; model.PageStatuses[2] = "COMPLETED"; model.PageStatuses[3] = "FAILED"; model.PageStatuses[4] = "COMPLETED";
            model.ActiveBlock = model.Blocks.First(b => b.Page == 1);
            window.Original.Visibility = Visibility.Collapsed; window.OriginalEmpty.Visibility = Visibility.Visible;
            window.OriginalText.Text = string.Join("\n", Enumerable.Repeat("긴 원본 미리보기", 100));
            window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var scroll = window.OriginalScroll;
            check(scroll.ScrollableHeight > 150, "Original-wheel fixture has real in-page vertical scrolling");
            scroll.ScrollToVerticalOffset(50); window.UpdateLayout();
            var middle = Wheel(scroll, -120);
            check(!middle.Handled && model.ActiveBlock?.Page == 1, "Wheel inside an original page is left to normal scrolling");

            scroll.ScrollToBottom(); window.UpdateLayout();
            var next = Wheel(scroll, -120); await SettleAsync(window);
            check(next.Handled && model.ActiveBlock?.Page == 2 && scroll.VerticalOffset < 1,
                "Wheel beyond the bottom moves to the next available original and starts at its top");
            scroll.ScrollToBottom(); window.UpdateLayout();
            var failed = Wheel(scroll, -120); await SettleAsync(window);
            check(!failed.Handled && model.ActiveBlock?.Page == 2,
                "Wheel cannot pass into a failed or missing original page");

            window.SourceToggle.IsChecked = false; window.BrailleToggle.IsChecked = false; window.UpdateLayout();
            scroll.ScrollToTop(); window.UpdateLayout();
            var previous = Wheel(scroll, 120); await SettleAsync(window);
            check(previous.Handled && model.ActiveBlock?.Page == 1 && scroll.VerticalOffset >= scroll.ScrollableHeight - 1 && window.SourceToggle.IsChecked == false && window.BrailleToggle.IsChecked == false,
                "Wheel above the top returns to the previous page bottom and preserves original-only layout");
            scroll.ScrollToTop(); window.UpdateLayout();
            var first = Wheel(scroll, 120);
            check(!first.Handled && model.ActiveBlock?.Page == 1, "Wheel at the first page does not invent a previous page");

            typeof(MainWindow).GetField("autoTranslate", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, true);
            model.ActiveBlock!.Source += " 수정";
            scroll.ScrollToBottom(); window.UpdateLayout();
            var pending = Wheel(scroll, -120);
            var heldAtOriginal = model.ActiveBlock?.Page == 1;
            // The user can independently turn the source panel on while translation awaits.
            window.SourceToggle.IsChecked = true;
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (model.ActiveBlock?.Page != 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
            await SettleAsync(window);
            check(pending.Handled && heldAtOriginal && model.ActiveBlock?.Page == 2 && window.SourceToggle.IsChecked == true && window.BrailleToggle.IsChecked == false,
                "Wheel navigation preserves a source-panel choice made during pending translation");
            model.ActiveBlock = model.Blocks.First(b => b.Page == 1);
            window.SourceToggle.IsChecked = false; window.BrailleToggle.IsChecked = false;
            model.ActiveBlock.Source += " 다시 수정";
            scroll.ScrollToBottom(); window.UpdateLayout();
            var pendingBraille = Wheel(scroll, -120);
            var heldBeforeBrailleChange = model.ActiveBlock?.Page == 1;
            window.BrailleToggle.IsChecked = true;
            deadline = DateTime.UtcNow.AddSeconds(3);
            while (model.ActiveBlock?.Page != 2 && DateTime.UtcNow < deadline) await Task.Delay(20);
            await SettleAsync(window);
            check(pendingBraille.Handled && heldBeforeBrailleChange && model.ActiveBlock?.Page == 2 &&
                  window.SourceToggle.IsChecked == false && window.BrailleToggle.IsChecked == true,
                "Wheel navigation preserves a braille-panel choice made during pending translation");
            typeof(MainWindow).GetField("autoTranslate", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            model.ActiveBlock = model.Blocks.First(b => b.Page == 1);

            model.OriginalPages.Remove(2); scroll.ScrollToBottom(); window.UpdateLayout();
            var missing = Wheel(scroll, -120); await SettleAsync(window);
            check(!missing.Handled && model.ActiveBlock?.Page == 1, "Wheel does not navigate to an original that was not received");
            model.OriginalPages[2] = new OriginalPage("text", null, ["2쪽 원본"]);
            window.ToggleOriginalPanel(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            scroll.ScrollToBottom(); window.UpdateLayout();
            var detached = Wheel(scroll, -120); await SettleAsync(window);
            check(window.DetachedOriginalWindow is { IsVisible: true } && detached.Handled && model.ActiveBlock?.Page == 2,
                "Original page wheel navigation works when the same panel is detached");
            window.ToggleOriginalPanel(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            model.IsTextJob = true; model.ActiveBlock = model.Blocks.First(b => b.Page == 1);
            scroll.ScrollToBottom(); window.UpdateLayout();
            var text = Wheel(scroll, -120);
            check(!text.Handled && model.ActiveBlock?.Page == 1, "Text-only documents do not navigate an absent original on wheel input");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
        await VerifyRealOriginalAsync(check);
    }

    private static async Task VerifyRealOriginalAsync(Action<bool, string> check)
    {
        var window = new MainWindow(testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 700 };
        window.Show();
        try
        {
            typeof(MainWindow).GetField("autoTranslate", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, false);
            var model = window.Session; model.Blocks.Clear();
            model.Add(new EditorBlock { Id = 1, Page = 1, Source = "첫째", Braille = "⠁" });
            model.Add(new EditorBlock { Id = 2, Page = 2, Source = "둘째", Braille = "⠃" });
            model.OriginalPages[1] = new OriginalPage("text", null, Enumerable.Repeat("첫째 쪽 원본", 100).ToArray());
            model.OriginalPages[2] = new OriginalPage("text", null, Enumerable.Repeat("둘째 쪽 원본", 100).ToArray());
            model.PageStatuses[1] = "COMPLETED"; model.PageStatuses[2] = "COMPLETED";
            model.ActiveBlock = model.Blocks.First(b => b.Page == 1);
            typeof(MainWindow).GetMethod("ShowOriginal", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [1]);
            await SettleAsync(window); window.OriginalScroll.ScrollToBottom(); window.UpdateLayout();
            var next = Wheel(window.OriginalScroll, -120); await SettleAsync(window);
            check(next.Handled && model.ActiveBlock?.Page == 2 && window.OriginalText.Text.StartsWith("둘째 쪽 원본", StringComparison.Ordinal)
                && window.OriginalScroll.VerticalOffset < 1,
                "Actual original rendering opens the next page at its top after wheel navigation");
            window.OriginalScroll.ScrollToTop(); window.UpdateLayout();
            var previous = Wheel(window.OriginalScroll, 120); await SettleAsync(window);
            check(previous.Handled && model.ActiveBlock?.Page == 1 && window.OriginalText.Text.StartsWith("첫째 쪽 원본", StringComparison.Ordinal)
                && window.OriginalScroll.VerticalOffset >= window.OriginalScroll.ScrollableHeight - 1,
                "Actual original rendering opens the previous page at its bottom after wheel navigation");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static MouseWheelEventArgs Wheel(ScrollViewer scroll, int delta)
    {
        var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
        scroll.RaiseEvent(e); return e;
    }

    private static async Task SettleAsync(Window window)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(50); window.UpdateLayout();
    }
}
