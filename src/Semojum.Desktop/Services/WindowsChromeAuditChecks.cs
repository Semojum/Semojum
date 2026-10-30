using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using SharpVectors.Converters;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Read native and WPF window geometry without changing operating-system window settings.</summary>
internal static class WindowsChromeAuditChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var editor = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 600 };
        editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            check(true, $"Harness thread modal={ComponentDispatcher.IsThreadModal}; windows={string.Join(" | ", Application.Current.Windows.Cast<Window>().Select(w => $"{w.GetType().Name}:visible={w.IsVisible},owner={w.Owner?.GetType().Name ?? "none"},native-enabled={IsWindowEnabled(new WindowInteropHelper(w).Handle)}"))}");
            void NativeEnabled(string phase) => check(true, $"Native enable at {phase}: {IsWindowEnabled(new WindowInteropHelper(editor).Handle)}");
            NativeEnabled("show");
            var independent = new Window { Width = 240, Height = 160, ShowInTaskbar = false }; independent.Show();
            check(true, $"Independent standard window: native-enabled={IsWindowEnabled(new WindowInteropHelper(independent).Handle)}, thread-modal={ComponentDispatcher.IsThreadModal}");
            independent.Close();
            Describe(editor, "editor", check);
            check(editor.ResizeMode == ResizeMode.CanResize && editor.ActualWidth <= 960 && editor.ActualHeight <= 600, "The editor remains resizable at the 960-pixel split-window size");
            var editorChrome = WindowChrome.GetWindowChrome(editor); var editorHits = CornerHits(editor);
            check(true, $"Actual editor at 8px: {editorHits.Detail}");
            editorChrome.CornerRadius = new CornerRadius(0); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); var squareHits = CornerHits(editor);
            check(true, $"Actual editor baseline 0px: {squareHits.Detail}");
            editorChrome.CornerRadius = new CornerRadius(8); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editorHits = CornerHits(editor);
            check(editorChrome.CornerRadius == new CornerRadius(8) && editorChrome.ResizeBorderThickness == new Thickness(4) && editorHits.Hits == 4,
                "The actual editor has 8px rounded corners and all four resize corners remain reachable");
            var close = Descendants(editor).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "닫기" && WindowChrome.GetIsHitTestVisibleInChrome(b));
            var resizeEdge = Descendants(editor).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "EditorCaptionResizeEdge");
            var icon = close.Content as SvgViewbox;
            var iconPosition = icon?.TransformToAncestor(close).Transform(new Point()) ?? new Point(-1, -1);
            var iconBounds = icon?.Drawings?.Bounds ?? Rect.Empty;
            check(close.ActualWidth == 46 && close.ActualHeight == 32 && icon is { Width: 16, Height: 16 } && !iconBounds.IsEmpty
                && Math.Abs(iconPosition.X - (15 + iconBounds.X)) < 1 && Math.Abs(iconPosition.Y - (8 + iconBounds.Y)) < 1,
                "The close button retains its 46×32 area and original centred 16px Figma icon");
            var closeCenter = close.TranslatePoint(new Point(23, 16), editor);
            var closeHit = editor.InputHitTest(closeCenter) as DependencyObject;
            var screenCenter = close.PointToScreen(new Point(23, 16));
            var packedCenter = unchecked((int)((((int)screenCenter.Y & 0xffff) << 16) | ((int)screenCenter.X & 0xffff)));
            var nativeCenter = SendMessage(new WindowInteropHelper(editor).Handle, 0x0084, IntPtr.Zero, new IntPtr(packedCenter)).ToInt64();
            check(IsWithin(closeHit, close) && nativeCenter == 1 && resizeEdge.IsVisible && resizeEdge.ActualWidth == 4,
                $"The close centre is a native client hit and routes to the WPF button; outer four pixels resize (native={nativeCenter})");
            editor.WindowState = WindowState.Maximized; await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            NativeEnabled("maximize");
            check(editor.WindowState == WindowState.Maximized && !resizeEdge.IsVisible && IsWithin(editor.InputHitTest(close.TranslatePoint(new Point(44, 16), editor)) as DependencyObject, close), "The maximized editor restores the full close button hit area");
            editor.WindowState = WindowState.Normal; await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            NativeEnabled("restore");
            check(editor.WindowState == WindowState.Normal && resizeEdge.IsVisible && editorChrome.ResizeBorderThickness == new Thickness(4), "The actual rounded editor restores with its resize border intact");
            await CheckMonitorsAsync(editor, check);
            NativeEnabled("monitor moves");
            if (Environment.GetEnvironmentVariable("CHROME_EDITOR_CAPTURE") is { Length: > 0 } editorCapture) await WindowsSmoke.RenderElementAsync(editor, editorCapture);
            var standardModal = new Window { Owner = editor, Width = 240, Height = 160, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            standardModal.Loaded += (_, _) => standardModal.Dispatcher.BeginInvoke(() => { NativeEnabled("standard modal open"); standardModal.Close(); });
            NativeEnabled("before standard modal"); standardModal.ShowDialog(); NativeEnabled("standard modal closed");
            var range = new ExportRangeDialog([1, 2], null) { Owner = editor };
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) }; var expired = false;
            timeout.Tick += (_, _) => { expired = true; timeout.Stop(); if (range.IsVisible) range.Close(); };
            range.Loaded += (_, _) => range.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    NativeEnabled("range modal open");
                    range.UpdateLayout(); Describe(range, "modal", check);
                    check(range.Content is Border { CornerRadius.TopLeft: 8 } && !range.AllowsTransparency, "The modal uses an eight-pixel WPF content border without a per-pixel transparent window");
                }
                finally { range.Close(); }
            });
            NativeEnabled("before range modal"); timeout.Start(); range.ShowDialog(); timeout.Stop(); check(!expired, "Native window audit closes its modal normally");
            NativeEnabled("range modal closed");
            var baseline = new Window { Width = 240, Height = 160, ShowInTaskbar = false }; baseline.Show();
            NativeEnabled("baseline shown");
            await Task.Delay(150); SystemCommands.CloseWindow(baseline); await Task.Delay(400);
            NativeEnabled("baseline closed");
            check(true, $"Standard WPF window SystemCommands.CloseWindow baseline closed={!baseline.IsVisible}");
            if (baseline.IsVisible) baseline.Close();
            await editor.ReleaseResourcesAsync(); editor.PrepareSmokeShutdown();
            var closingCount = 0; var closedCount = 0;
            editor.Closing += (_, _) => closingCount++; editor.Closed += (_, _) => closedCount++;
            var handle = new WindowInteropHelper(editor).Handle;
            check(true, $"Before close: enabled={editor.IsEnabled}, native-enabled={IsWindowEnabled(handle)}, active={editor.IsActive}, close-enabled={close.IsEnabled}, owner={editor.Owner != null}, visible={editor.IsVisible}, style=0x{unchecked((uint)GetWindowLong(handle, -16)):X8}, close-menu=0x{GetMenuState(GetSystemMenu(handle, false), 0xF060, 0):X8}");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(400);
            check(true, $"After button: visible={editor.IsVisible}, closing={closingCount}, closed={closedCount}");
            var closedByButton = !editor.IsVisible;
            if (editor.IsVisible) { SystemCommands.CloseWindow(editor); await Task.Delay(400); check(true, $"After direct SystemCommands: visible={editor.IsVisible}, closing={closingCount}, closed={closedCount}"); }
            if (editor.IsVisible)
            {
                editorChrome.CornerRadius = new CornerRadius(0); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                SystemCommands.CloseWindow(editor); await Task.Delay(400);
                check(true, $"After 0px direct SystemCommands: visible={editor.IsVisible}, closing={closingCount}, closed={closedCount}");
            }
            check(closedByButton, "The editor close button still invokes the real close action");
            if (editor.IsVisible) { editor.Close(); check(true, $"After direct Close(): visible={editor.IsVisible}, closing={closingCount}, closed={closedCount}"); }
        }
        finally { if (editor.IsVisible) { await editor.ReleaseResourcesAsync(); editor.PrepareSmokeShutdown(); editor.Close(); } }
        await TestRoundedProbeAsync(check);
    }
    private static bool IsWithin(DependencyObject? element, DependencyObject ancestor)
    {
        while (element != null) { if (element == ancestor) return true; element = VisualTreeHelper.GetParent(element); }
        return false;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static async Task TestRoundedProbeAsync(Action<bool, string> check)
    {
        var probe = new Window { Title = "창 테두리 진단", Width = 960, Height = 600, MinWidth = 480, MinHeight = 360, ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = new Grid { Background = System.Windows.Media.Brushes.White } };
        WindowFrame.Apply(probe, editor: true); probe.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); probe.UpdateLayout();
            var chrome = WindowChrome.GetWindowChrome(probe); var initial = CornerHits(probe);
            chrome.CornerRadius = new CornerRadius(8); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); probe.UpdateLayout();
            var rounded = CornerHits(probe);
            check(true, $"Probe DPI={GetDpiForWindow(new WindowInteropHelper(probe).Handle)}; monitors={GetSystemMetrics(80)}; baseline region={initial.Region}, viable corners={initial.Hits}; 8px region={rounded.Region}, viable corners={rounded.Hits}");
            check(chrome.ResizeBorderThickness == new Thickness(4) && rounded.Region == 3 && rounded.Hits == 4,
                "The separate 8px trial retains the four-pixel resize border and a usable hit point in all four rounded corners");
            if (Environment.GetEnvironmentVariable("CHROME_PROBE_CAPTURE") is { Length: > 0 } probeCapture) await WindowsSmoke.RenderElementAsync(probe, probeCapture);
            probe.WindowState = WindowState.Maximized; await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(probe.WindowState == WindowState.Maximized, "The 8px trial can maximize");
            probe.WindowState = WindowState.Normal; await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(probe.WindowState == WindowState.Normal && chrome.ResizeBorderThickness == new Thickness(4), "The trial can restore while retaining the resize border");
            var work = SystemParameters.WorkArea; probe.Left = work.Left; probe.Top = work.Top; probe.Width = Math.Max(480, Math.Min(960, work.Width / 2)); probe.Height = Math.Max(360, work.Height);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); probe.UpdateLayout();
            check(probe.ActualWidth >= 480 && probe.ResizeMode == ResizeMode.CanResize, "The rounded trial remains operable when sized to half the work area (not a native snap test)");
        }
        finally { probe.Close(); }
    }
    private static async Task CheckMonitorsAsync(Window window, Action<bool, string> check)
    {
        var monitors = new List<(IntPtr Handle, NativeRect Bounds)>();
        MonitorEnum callback = (IntPtr monitor, IntPtr _, ref NativeRect bounds, IntPtr __) => { monitors.Add((monitor, bounds)); return true; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero)) { check(false, "Monitor enumeration succeeded"); return; }
        var handle = new WindowInteropHelper(window).Handle;
        foreach (var (monitor, bounds) in monitors)
        {
            var moved = SetWindowPos(handle, IntPtr.Zero, bounds.Left + 32, bounds.Top + 32, 0, 0, 0x0001 | 0x0004 | 0x0010);
            await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var actualMonitor = MonitorFromWindow(handle, 2); var dpi = GetDpiForWindow(handle); var hits = CornerHits(window);
            check(moved && actualMonitor == monitor && dpi > 0 && hits.Hits == 4,
                $"Actual editor reaches monitor {bounds} at DPI {dpi}, retaining four resize hit targets (region={hits.Region}; {hits.Detail})");
        }
        check(true, $"Monitor audit visited {monitors.Count} display(s); actual Windows snap and a DPI configuration not present on this machine remain untested");
    }
    private static (int Region, int Hits, string Detail) CornerHits(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle; GetWindowRect(handle, out var bounds);
        var region = CreateRectRgn(0, 0, 0, 0); var regionKind = GetWindowRgn(handle, region); var hits = 0; var details = new List<string>();
        try
        {
            var pixels = Math.Max(1, (int)Math.Ceiling(4 * GetDpiForWindow(handle) / 96d));
            foreach (var (right, bottom, hit) in new[] { (false, false, 13), (true, false, 14), (false, true, 16), (true, true, 17) })
            {
                var found = false; var inside = 0; var codes = new HashSet<long>();
                for (var x = 1; x <= pixels && !found; x++) for (var y = 1; y <= pixels && !found; y++)
                {
                    var localX = right ? bounds.Right - bounds.Left - x : x; var localY = bottom ? bounds.Bottom - bounds.Top - y : y;
                    if (regionKind != 0 && !PtInRegion(region, localX, localY)) continue;
                    inside++;
                    var screenX = bounds.Left + localX; var screenY = bounds.Top + localY;
                    var packed = unchecked((int)(((screenY & 0xffff) << 16) | (screenX & 0xffff)));
                    var observed = SendMessage(handle, 0x0084, IntPtr.Zero, new IntPtr(packed)).ToInt64(); codes.Add(observed); found = observed == hit;
                }
                if (found) hits++; details.Add($"expected={hit} inside={inside} codes=[{string.Join(',', codes)}]");
            }
        }
        finally { DeleteObject(region); }
        return (regionKind, hits, string.Join("; ", details));
    }
    private static void Describe(Window window, string name, Action<bool, string> check)
    {
        var chrome = WindowChrome.GetWindowChrome(window); var handle = new WindowInteropHelper(window).Handle;
        var style = unchecked((uint)GetWindowLong(handle, -16)); var extended = unchecked((uint)GetWindowLong(handle, -20));
        var region = CreateRectRgn(0, 0, 0, 0); int regionKind;
        try { regionKind = GetWindowRgn(handle, region); } finally { DeleteObject(region); }
        var compositionHr = DwmIsCompositionEnabled(out var composed);
        var cornerHr = DwmGetWindowAttribute(handle, 33, out int cornerPreference, sizeof(int));
        var frameHr = DwmGetWindowAttribute(handle, 9, out NativeRect frame, Marshal.SizeOf<NativeRect>());
        var hasWindowRect = GetWindowRect(handle, out var rect);
        check(true, $"Native {name}: OS={Environment.OSVersion.Version}; WPF-Corner={chrome?.CornerRadius}; ResizeBorder={chrome?.ResizeBorderThickness}; Glass={chrome?.GlassFrameThickness}; Style=0x{style:X8}; ExStyle=0x{extended:X8}; Layered={(extended & 0x00080000) != 0}; WindowRegionResult={regionKind}; DWMCompositionHR=0x{compositionHr:X8}; DWMComposition={composed}; CornerPreferenceHR=0x{cornerHr:X8}; CornerPreference={cornerPreference}; WindowRect={(hasWindowRect ? rect.ToString() : "unavailable")}; ExtendedFrameHR=0x{frameHr:X8}; ExtendedFrame={frame}");
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        internal int Left, Top, Right, Bottom;
        public override readonly string ToString() => $"{Left},{Top},{Right},{Bottom}";
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetSystemMenu(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool revert);
    [DllImport("user32.dll")] private static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr hwnd, IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PtInRegion(IntPtr region, int x, int y);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out NativeRect value, int size);
}
