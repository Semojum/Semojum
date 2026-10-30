using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using Semojum.Core;
using Semojum.Core.Integration;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsTabProgressChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture();
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api);
        window.Show();
        try
        {
            window.UpdateLayout();
            check(window.DocumentTabs.Items.Count == 1 && window.DocumentTabs.IsVisible && window.DocumentTabs.ActualHeight == 34,
                "A single open document keeps the Figma 34px document tab row visible");
            var first = window.Session;
            first.Title = "첫 문서.smj";
            first.JobId = "first-job"; first.JobStatus = "IN_PROGRESS";
            first.ActiveBlock!.Source = "첫 문서";
            first.Progress = new ConversionProgress(128, 46, 1, 1, "잔여 약 12분");
            await window.ExecuteAsync("new");
            var second = window.Session;
            second.JobId = "second-job"; second.JobStatus = "IN_PROGRESS";
            second.Progress = new ConversionProgress(10, 2, 0, 1, "남은 시간 계산 중");
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            window.UpdateLayout();
            var firstTab = (TabItem)window.DocumentTabs.Items[0];
            var secondTab = (TabItem)window.DocumentTabs.Items[1];
            var activeLine = secondTab.Template.FindName("ActiveLine", secondTab) as FrameworkElement;
            var inactiveLine = firstTab.Template.FindName("ActiveLine", firstTab) as FrameworkElement;
            check(activeLine is { Visibility: Visibility.Visible, Height: 2 } && inactiveLine?.Visibility == Visibility.Collapsed
                && secondTab.FontWeight == FontWeights.Bold && firstTab.FontWeight != FontWeights.Bold,
                "The selected document tab has the Figma blue stripe and stronger title");
            Button? ProgressButton(TabItem tab) => Descendants((DependencyObject)tab.Header).OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "TabConversionProgress");
            var firstButton = ProgressButton(firstTab);
            var secondButton = ProgressButton(secondTab);
            var closeButton = Descendants((DependencyObject)secondTab.Header).OfType<Button>()
                .FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "DocumentTabClose");
            // The SVG uses a 16×16 viewBox with a 9.6×9.6 painted cross. Stretch=None
            // sizes SharpVectors' view to the painted bounds; DPI rounding can shift it by a pixel.
            var iconMatches = closeButton?.Content is SvgViewbox { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false } inspected
                && Math.Abs(inspected.Drawings.Bounds.Width - 9.6) < .05 && Math.Abs(inspected.Drawings.Bounds.Height - 9.6) < .05
                && Math.Abs(inspected.ActualWidth - 9.6) < 1 && Math.Abs(inspected.ActualHeight - 9.6) < 1;
            check(iconMatches && closeButton!.IsVisible && AutomationProperties.GetName(closeButton).Contains(second.Title, StringComparison.Ordinal),
                "Each document tab exposes the Figma close icon with its document name to assistive technology");
            if (Environment.GetEnvironmentVariable("TAB_HEADER_CAPTURE") is { Length: > 0 } headerCapture)
                await WindowsSmoke.RenderElementAsync(closeButton!, headerCapture);
            check(firstButton is { IsVisible: true } && secondButton is { IsVisible: true },
                "Every converting document shows progress in its own tab, including an inactive tab");
            if (firstButton is null || secondButton is null) return;
            check(AutomationProperties.GetName(firstButton).Contains("47", StringComparison.Ordinal)
                && AutomationProperties.GetName(firstButton).Contains("128", StringComparison.Ordinal),
                "Inactive tab exposes its own finished and total page counts to assistive technology");
            var popup = ((Grid)VisualTreeHelper.GetParent(firstButton)).Children.OfType<Popup>().Single();
            firstButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(popup.IsOpen && Descendants(popup.Child).OfType<TextBlock>().Any(t => t.Text.Contains("47쪽 / 128쪽 처리 (37%)", StringComparison.Ordinal)),
                "The inactive tab opens its own progress details with the correct percentage");
            if (Environment.GetEnvironmentVariable("TAB_CAPTURE") is { Length: > 0 } capture)
            {
                var card = (FrameworkElement)popup.Child; card.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(card); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(capture); encoder.Save(output);
            }
            var cancel = Descendants(popup.Child).OfType<Button>().Single(b => Equals(b.Content, "취소"));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(20);
            check(fixture.CancelPath?.Contains("first-job", StringComparison.Ordinal) == true && ReferenceEquals(window.Session, second)
                && first.CancellationRequested && !second.CancellationRequested,
                "Cancel from an inactive tab sends only that document's request and preserves the selected document");
            first.Progress = null;
            check(firstButton.Visibility == Visibility.Collapsed && secondButton.Visibility == Visibility.Visible,
                "Finishing one job removes only that tab's progress indicator");
            if (closeButton is not null)
            {
                closeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(window.DocumentTabs.Items.Count == 1 && ReferenceEquals(window.Session, first),
                    "The tab close icon closes the chosen document and selects the remaining tab");
            }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public string? CancelPath;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/cancel", StringComparison.Ordinal)) CancelPath = path;
            var result = path.EndsWith("/login", StringComparison.Ordinal)
                ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}"
                : path.EndsWith("/cancel", StringComparison.Ordinal)
                    ? "{\"canceled\":true,\"status\":\"IN_PROGRESS\",\"inFlightPages\":1}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
