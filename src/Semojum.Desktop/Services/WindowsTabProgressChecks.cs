using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
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
            var cardLayer = Descendants(popup.Child).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "TabProgressCard");
            check(popup.Child is Grid { ActualWidth: 320, ActualHeight: 108, Margin: var m } layers && m == new Thickness(20)
                && cardLayer.Effect is DropShadowEffect { BlurRadius: 16, ShadowDepth: 8, Opacity: 0.14 }
                && layers.Children.OfType<Border>().Any(b => b.Effect is DropShadowEffect { BlurRadius: 2, ShadowDepth: 0, Opacity: 0.1 } && !b.IsHitTestVisible)
                && popup.Placement == PlacementMode.Custom,
                "Progress card retains Figma dimensions and both independent shadow layers");
            var triggerTop = firstButton.PointToScreen(new Point());
            var triggerBottom = firstButton.PointToScreen(new Point(0, firstButton.ActualHeight));
            var triggerScale = (firstButton.PointToScreen(new Point(100, 0)).X - triggerTop.X) / 100;
            var cardTop = ((FrameworkElement)popup.Child).PointToScreen(new Point());
            check(Math.Abs(cardTop.Y - triggerBottom.Y) <= 2 && Math.Abs(cardTop.X - (triggerTop.X - 48 * triggerScale)) <= 2,
                "Progress popup stays anchored below its tab at the Figma offset");
            await CheckPointerAsync(firstButton, popup, "below", check);
            if (Environment.GetEnvironmentVariable("TAB_SHADOW_CAPTURE") is { Length: > 0 } shadowCapture
                && PresentationSource.FromVisual((Visual)popup.Child)?.RootVisual is FrameworkElement popupRoot)
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(popupRoot.ActualWidth), (int)Math.Ceiling(popupRoot.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(popupRoot); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(shadowCapture); encoder.Save(output);
            }
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
            var edge = new Window { Owner = window, Width = 480, Height = 360, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            var edgeGrid = new Grid();
            var edgeProgress = new Controls.TabConversionProgress(second, () => Task.CompletedTask)
                { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(70, 0, 0, 0) };
            edgeGrid.Children.Add(edgeProgress); edge.Content = edgeGrid; edge.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                MoveToWorkBottom(edge);
                var edgeButton = Descendants(edgeProgress).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "TabConversionProgress");
                var edgePopup = edgeProgress.Children.OfType<Popup>().Single();
                edgeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var edgeCard = (FrameworkElement)edgePopup.Child;
                var buttonTop = edgeButton.PointToScreen(new Point());
                var cardBottom = edgeCard.PointToScreen(new Point(0, edgeCard.ActualHeight));
                check(edgePopup.IsOpen && Math.Abs(cardBottom.Y - buttonTop.Y) <= 2,
                    $"Progress popup flips above the tab at the screen edge without a gap (tab {buttonTop.Y:0.0}, card bottom {cardBottom.Y:0.0})");
                await CheckPointerAsync(edgeButton, edgePopup, "above", check);
            }
            finally { edgeProgress.Dispose(); edge.Close(); }
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

    private static async Task CheckPointerAsync(Button trigger, Popup popup, string placement, Action<bool, string> check)
    {
        GetCursorPos(out var cursor);
        try
        {
            var start = trigger.PointToScreen(new Point(trigger.ActualWidth / 2, trigger.ActualHeight / 2));
            var card = (FrameworkElement)popup.Child;
            var end = card.PointToScreen(new Point(card.ActualWidth / 2, card.ActualHeight / 2));
            for (var i = 0; i <= 12; i++)
            {
                SetCursorPos((int)(start.X + (end.X - start.X) * i / 12), (int)(start.Y + (end.Y - start.Y) * i / 12));
                await Task.Delay(25);
            }
            GetCursorPos(out var actual);
            if (Math.Abs(actual.X - end.X) <= 2 && Math.Abs(actual.Y - end.Y) <= 2)
                check(popup.IsOpen && card.IsMouseOver, $"Pointer can move into the progress popup placed {placement} the tab");
            else Console.WriteLine($"SKIP: {placement} progress popup pointer movement is unavailable");
        }
        finally { SetCursorPos(cursor.X, cursor.Y); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct CursorPosition { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    private static void MoveToWorkBottom(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) || !GetWindowRect(handle, out var rect)
            || !SetWindowPos(handle, IntPtr.Zero, info.Work.Left, info.Work.Bottom - (rect.Bottom - rect.Top), 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new InvalidOperationException("Cannot position the progress popup test at the work-area edge.");
        window.UpdateLayout();
    }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPosition point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
