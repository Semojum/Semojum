using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Semojum.Desktop.Services;

internal static class WindowsStatusZoomChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var buttons = Children(window.StatusZoom).OfType<Button>().ToArray();
            var minus = buttons.Single(b => AutomationProperties.GetName(b) == "축소");
            var plus = buttons.Single(b => AutomationProperties.GetName(b) == "확대");
            check(window.StatusBar.ActualHeight == 28 && window.ZoomSlider.Value == 50 && window.ZoomPercent.Text == "100%" && window.ZoomSlider.ActualWidth == 106,
                "Status zoom starts at the Figma 100-percent midpoint with a 106-pixel track");
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.SourceEditor.FontSize == 16 && Math.Abs(window.Braille.GridView.FontSize - 16 * 22.0 / 14) < 0.001 && window.ZoomPercent.Text == "114%",
                "Zoom-in updates the source, braille grid, and displayed percentage together");
            minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.SourceEditor.FontSize == 14 && window.ZoomSlider.Value == 50 && window.ZoomPercent.Text == "100%",
                "Zoom-out returns to the original size and synchronizes the slider");
            window.ZoomSlider.Value = 75;
            check(window.SourceEditor.FontSize == 23 && window.ZoomPercent.Text == "164%", "Moving the slider changes the actual editor size");
            window.ZoomPicker.SelectedItem = "125%";
            check(window.SourceEditor.FontSize == 17.5 && Math.Abs(window.ZoomSlider.Value - (50 + 3.5 * 50 / 18)) < 0.001,
                "Existing zoom selection remains synchronized with the status slider");
            window.ZoomSlider.Value = 0;
            check(window.SourceEditor.FontSize == 10, "Slider retains the existing minimum font size");
            window.ZoomSlider.Value = 100;
            check(window.SourceEditor.FontSize == 32, "Slider retains the existing maximum font size");
            check(window.ZoomSlider.Focusable && minus.Focusable && plus.Focusable && AutomationProperties.GetName(window.ZoomSlider) == "편집기 확대 비율",
                "Zoom slider and both actions are keyboard focusable and named for assistive technology");
            foreach (var width in new[] { 960.0, 480.0 })
            {
                window.Width = width;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var position = window.StatusZoom.TranslatePoint(new Point(), window);
                check(window.StatusZoom.IsVisible && window.ZoomSlider.ActualWidth == 106 && position.X >= 0 && position.X + window.StatusZoom.ActualWidth <= window.ActualWidth,
                    $"Status zoom remains visible and reachable at {width:0}px window width");
            }
            if (Environment.GetEnvironmentVariable("STATUS_ZOOM_CAPTURE") is { Length: > 0 } capture)
                await WindowsSmoke.RenderElementAsync(window.StatusBar, capture);
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
