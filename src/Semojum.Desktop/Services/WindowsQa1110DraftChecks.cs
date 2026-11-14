using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsQa1110DraftChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true) { Width = 1440, Height = 1000, ShowInTaskbar = false }; window.Show();
        try
        {
            var block = window.Session.ActiveBlock!;
            block.Metadata = new(Type: "image", SelectedIndex: 5, Alternatives: Enumerable.Range(1, 6)
                .Select(i => new AlternativeDraft("초안 " + i, "설명 " + i, null, "⠁", null)).ToArray());
            await window.ExecuteAsync("drafts");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var choices = window.DraftChoiceGrid.Children.OfType<RadioButton>().ToArray();
            check(choices.Length == 6 && choices.Select(c => Math.Round(c.TransformToAncestor(window.DraftChoiceGrid).Transform(new Point()).Y, 1)).Distinct().Count() == 1,
                "QA1110: six draft choices stay on a single row");
            check(window.DraftDock.ActualHeight >= 320, $"QA1110: draft preview opens taller than 212px (actual={window.DraftDock.ActualHeight})");
            var last = choices[^1].TransformToAncestor(window.DraftChoiceScroll).TransformBounds(new Rect(choices[^1].RenderSize));
            check(last.Left >= -1 && last.Right <= window.DraftChoiceScroll.ViewportWidth + 1 && window.DraftChoiceScroll.ScrollableHeight == 0,
                "QA1110: selected later draft is horizontally revealed without vertical wrapping");
            var handle = window.FindName("DraftDockResize") as Thumb;
            check(handle is { IsVisible: true, Focusable: true }, "QA1110: draft dock has a focusable vertical resize handle");
            if (handle is not null)
            {
                var before = window.DraftDock.ActualHeight;
                handle.RaiseEvent(new DragDeltaEventArgs(0, -80) { RoutedEvent = Thumb.DragDeltaEvent });
                window.UpdateLayout();
                check(window.DraftDock.ActualHeight >= before + 75, "QA1110: dragging draft top edge upward grows preview");
                handle.RaiseEvent(new DragDeltaEventArgs(0, 5000) { RoutedEvent = Thumb.DragDeltaEvent }); window.UpdateLayout();
                check(window.DraftDock.ActualHeight >= 240 && window.DraftDock.ActualHeight <= window.PanelGrid.ActualHeight,
                    "QA1110: draft resizing preserves minimum content and stays inside editor");
            }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
