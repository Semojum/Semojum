using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsOwnedModalChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var input = new TextBox { Text = "편집 내용", Width = 180, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(20) };
        var ownerContentRoot = new Grid { Background = Brushes.White }; ownerContentRoot.Children.Add(input);
        var owner = new Window { Width = 960, Height = 600, Left = 40, Top = 40, WindowStartupLocation = WindowStartupLocation.Manual, Content = ownerContentRoot };
        Trace("owner-show"); owner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); owner.Activate(); input.Focus(); Trace("owner-ready");
            check(IsWindowEnabled(new WindowInteropHelper(owner).Handle), "The native owner is enabled before the modal opens");
            var initialFocus = FocusManager.GetFocusedElement(owner);
            var range = new ExportRangeDialog([1, 2], null) { Owner = owner }; var seen = false; var nestedSeen = false; var restored = false; var followed = false; var resized = false; var manualMove = false; var timedOut = false;
            NoticeDialog? nested = null;
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            watchdog.Tick += (_, _) => { timedOut = true; watchdog.Stop(); if (nested?.IsVisible == true) nested.Close(); if (range.IsVisible) range.Close(); };
            range.Loaded += (_, _) => range.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Trace("range-loaded");
                    var ownerContent = (UIElement)owner.Content; var layer = AdornerLayer.GetAdornerLayer(ownerContent);
                    seen = layer?.GetAdorners(ownerContent)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") == true;
                    var expectedX = owner.Left + (owner.ActualWidth - range.ActualWidth) / 2;
                    var expectedY = owner.Top + (owner.ActualHeight - range.ActualHeight) * .45;
                    check(Math.Abs(range.Left - expectedX) < 3 && Math.Abs(range.Top - expectedY) < 3, "An owned modal uses the Figma horizontal center and 45-percent vertical placement");
                    if (Environment.GetEnvironmentVariable("MODAL_COMPOSITE_CAPTURE") is { Length: > 0 } capture) CaptureComposite(owner, range, capture); Trace("composite-done");
                    var beforeX = range.Left; var beforeY = range.Top; owner.Left += 20; owner.Top += 10;
                    followed = Math.Abs(range.Left - beforeX - 20) < 3 && Math.Abs(range.Top - beforeY - 10) < 3;
                    Trace("owner-move-done"); owner.Width = 900; owner.Height = 550; owner.UpdateLayout(); Trace("owner-resize-done");
                    resized = Math.Abs(range.Left - (owner.Left + (owner.ActualWidth - range.ActualWidth) / 2)) < 3 && Math.Abs(range.Top - (owner.Top + (owner.ActualHeight - range.ActualHeight) * .45)) < 3;
                    range.Left += 15; range.Top += 10; var movedX = range.Left; var movedY = range.Top; owner.Left += 10; Trace("manual-move-done");
                    manualMove = Math.Abs(range.Left - movedX) < 3 && Math.Abs(range.Top - movedY) < 3;
                    nested = new NoticeDialog("검증", "중첩 대화상자", ["확인"]) { Owner = range }; Trace("nested-created");
                    nested.Loaded += (_, _) => nested.Dispatcher.BeginInvoke(() =>
                    {
                        try { var childContent = (UIElement)range.Content; var childLayer = AdornerLayer.GetAdornerLayer(childContent);
                            nestedSeen = childLayer?.GetAdorners(childContent)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") == true; }
                        catch (Exception ex) { check(false, "Nested backdrop inspection: " + ex.Message); }
                        finally { Trace("nested-close"); nested.Close(); }
                    });
                    nested.ShowDialog(); Trace("nested-returned");
                    restored = layer?.GetAdorners(ownerContent)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") == true;
                }
                catch (Exception ex) { check(false, "Modal inspection: " + ex.Message); }
                finally { Trace("range-closing"); watchdog.Stop(); if (range.IsVisible) range.Close(); }
            });
            watchdog.Start(); range.ShowDialog(); watchdog.Stop(); Trace("range-returned"); check(!timedOut, "Modal inspection closes within its time limit");
            check(IsWindowEnabled(new WindowInteropHelper(owner).Handle), "The native owner is re-enabled after closing the nested modal sequence");
            var content = (UIElement)owner.Content; var ownerLayer = AdornerLayer.GetAdornerLayer(content);
            check(seen, "The owner displays the Figma backdrop while its modal is open");
            check(followed, "An unmoved modal follows owner movement");
            check(resized, "An unmoved modal follows owner resizing");
            check(manualMove, "A manually moved modal is not repositioned when its owner moves");
            check(nestedSeen && restored, "A nested modal dims its own owner without removing the outer backdrop");
            check(ownerLayer?.GetAdorners(content)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") != true, "Closing the modal removes its owner backdrop");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Trace("focus-yield-returned");
            check(initialFocus == input && FocusManager.GetFocusedElement(owner) == input, "Closing a modal preserves the owner's keyboard focus");

            var escaped = false;
            try { OwnedModal.Show(new ExportRangeDialog([1], null) { Owner = owner }, () => throw new InvalidOperationException("fixture")); }
            catch (InvalidOperationException ex) when (ex.Message == "fixture") { escaped = true; }
            check(escaped && ownerLayer?.GetAdorners(content)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") != true, "An exception removes the backdrop and propagates to the caller"); Trace("exception-check-done");

            var picker = new FormPickerDialog(Array.Empty<BrailleForm>(), null) { Owner = owner }; var pickerUndimmed = false;
            picker.Loaded += (_, _) => picker.Dispatcher.BeginInvoke(() => { Trace("picker-loaded"); pickerUndimmed = ownerLayer?.GetAdorners(content)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") != true; picker.Close(); });
            var pickerWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; pickerWatchdog.Tick += (_, _) => { Trace("picker-timeout"); pickerWatchdog.Stop(); picker.Close(); };
            Trace("picker-show"); pickerWatchdog.Start(); picker.ShowDialog(); pickerWatchdog.Stop(); Trace("picker-returned"); check(pickerUndimmed, "The separately designed form picker is not dimmed as an editor modal");
            var modeless = new ExportRangeDialog([1], null) { Owner = owner }; modeless.Show(); Trace("modeless-shown");
            try { check(ownerLayer?.GetAdorners(content)?.Any(a => AutomationProperties.GetAutomationId(a) == "FigmaModalBackdrop") != true, "A modeless window does not add a modal backdrop"); }
            finally { modeless.Close(); }
        }
        finally { Trace("owner-closing"); owner.Close(); }
    }

    private static void Trace(string stage)
    { if (Environment.GetEnvironmentVariable("MODAL_TRACE") is { Length: > 0 } path) File.AppendAllText(path, stage + Environment.NewLine); }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr hwnd);
    private static void CaptureComposite(Window owner, Window child, string path)
    {
        owner.UpdateLayout(); child.UpdateLayout();
        var ownerBitmap = new RenderTargetBitmap((int)Math.Ceiling(owner.ActualWidth), (int)Math.Ceiling(owner.ActualHeight), 96, 96, PixelFormats.Pbgra32); ownerBitmap.Render(owner);
        var childBitmap = new RenderTargetBitmap((int)Math.Ceiling(child.ActualWidth), (int)Math.Ceiling(child.ActualHeight), 96, 96, PixelFormats.Pbgra32); childBitmap.Render(child);
        var composite = new DrawingVisual(); using (var dc = composite.RenderOpen())
        {
            dc.DrawImage(ownerBitmap, new Rect(0, 0, owner.ActualWidth, owner.ActualHeight));
            dc.DrawImage(childBitmap, new Rect(child.Left - owner.Left, child.Top - owner.Top, child.ActualWidth, child.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap(ownerBitmap.PixelWidth, ownerBitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(composite);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
}
