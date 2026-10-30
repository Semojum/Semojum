using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsRangeDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var countCalls = 0;
        var dialog = new ExportRangeDialog([3, 4, 6], () => { countCalls++; return Task.FromResult(7); }, allowFormatChange: true); dialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var scope = Descendants(dialog).OfType<SegmentedChoice>().Single(); var format = Descendants(dialog).OfType<ComboBox>().Single();
            var start = Descendants(dialog).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "시작 페이지 번호");
            var end = Descendants(dialog).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "끝 페이지 번호");
            check(dialog.Width == 520 && dialog.Height == 324 && scope.SelectedIndex == 0 && start.Text == "3" && end.Text == "6", "Source range opens with Figma dimensions and real original page bounds");
            var card = (Border)((Grid)((Grid)format.Parent).Parent).Parent;
            var formatRect = format.TransformToAncestor(card).TransformBounds(new Rect(0, 0, format.ActualWidth, format.ActualHeight));
            check(format.ActualHeight == 32 && formatRect.Bottom <= card.ActualHeight, "The entire format selector fits inside the range card");
            foreach (var asset in new[] { "range-close.svg", "range-spinner-up.svg", "range-spinner-down.svg", "range-format-chevron.svg" })
            {
                var icons = Descendants(dialog).OfType<SvgViewbox>().Where(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true).ToArray();
                check(icons.Length > 0 && icons.All(i => i.Drawings is { Bounds.IsEmpty: false } && i.ActualWidth > 0 && i.ActualHeight > 0), $"{asset} renders its nonempty original Figma drawing");
            }
            if (Environment.GetEnvironmentVariable("RANGE_SOURCE_CAPTURE") is { Length: > 0 } sourceCapture) await WindowsSmoke.RenderElementAsync(dialog, sourceCapture);
            scope.SelectedIndex = 1; await WaitAsync(() => end.Text == "7");
            check(start.Text == "1" && countCalls == 1, "Braille range counts real pages and starts at the first physical page");
            if (Environment.GetEnvironmentVariable("RANGE_BRAILLE_CAPTURE") is { Length: > 0 } brailleCapture) await WindowsSmoke.RenderElementAsync(dialog, brailleCapture);
            format.SelectedItem = ".txt — 원문"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(scope.SelectedIndex == 0 && !((ListBoxItem)scope.Items[1]).IsEnabled && start.Text == "3" && end.Text == "6", "Source TXT returns to original pages and disables braille scope");
            if (Environment.GetEnvironmentVariable("RANGE_TEXT_CAPTURE") is { Length: > 0 } textCapture) await WindowsSmoke.RenderElementAsync(dialog, textCapture);
            format.SelectedItem = ".txt — 점자"; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(((ListBoxItem)scope.Items[1]).IsEnabled, "Braille TXT permits selecting braille pages again");
            scope.SelectedIndex = 1; await WaitAsync(() => end.Text == "7");
            check(countCalls == 1, "Switching formats preserves the computed braille page count");
        }
        finally { dialog.Close(); }

        var only = new ExportRangeDialog([], () => Task.FromResult(4), brailleOnly: true, format: ".txt — 점자"); only.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var scope = Descendants(only).OfType<SegmentedChoice>().Single();
            check(scope.SelectedIndex == 1 && !((ListBoxItem)scope.Items[0]).IsEnabled, "Braille preview limits selection to braille pages");
            if (Environment.GetEnvironmentVariable("RANGE_PREVIEW_CAPTURE") is { Length: > 0 } previewCapture) await WindowsSmoke.RenderElementAsync(only, previewCapture);
        }
        finally { only.Close(); }

        var failed = new ExportRangeDialog([1, 2], () => Task.FromException<int>(new InvalidOperationException("면 계산 실패"))); failed.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var scope = Descendants(failed).OfType<SegmentedChoice>().Single(); scope.SelectedIndex = 1;
            await WaitAsync(() => Descendants(failed).OfType<TextBlock>().Any(t => t.Text == "면 계산 실패"));
            check(scope.IsEnabled && Descendants(failed).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "면 계산 실패"), "Failed page count reports the error and allows choosing another range");
            scope.SelectedIndex = 0;
            check(Descendants(failed).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "끝 페이지 번호" && t.Text == "2"), "After a count failure the original page range remains usable");
        }
        finally { failed.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static async Task WaitAsync(Func<bool> predicate)
    { var until = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20); if (!predicate()) throw new InvalidOperationException("Range dialog state did not settle"); }
}
