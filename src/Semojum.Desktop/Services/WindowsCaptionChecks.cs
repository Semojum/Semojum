using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsCaptionChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 900 };
        window.Show();
        try
        {
            await Task.Delay(200); window.UpdateLayout();
            var buttons = Descendants(window).OfType<Button>().Where(WindowChrome.GetIsHitTestVisibleInChrome).ToArray();
            foreach (var (name, asset) in new[] { ("최소화", "min"), ("최대화 / 복원", "max"), ("닫기", "close") })
            {
                var button = buttons.Single(b => AutomationProperties.GetName(b) == name);
                var icon = button.Content as SvgViewbox;
                var position = icon is null ? new Point(-1, -1) : icon.TransformToAncestor(button).Transform(new Point());
                var bounds = icon?.Drawings?.Bounds ?? Rect.Empty;
                check(button.Width == 46 && button.Height == 32 && icon is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false }
                    && icon.Source is { } source && source.ToString().EndsWith("/editor-caption-" + asset + ".svg", StringComparison.Ordinal)
                    // Stretch=None measures the painted bounds, not the 16px SVG viewport. Its
                    // effective position must be the centred 16px slot plus the original bounds.
                    && Math.Abs(icon.ActualWidth - bounds.Width) < 1 && Math.Abs(icon.ActualHeight - bounds.Height) < 1
                    && Math.Abs(position.X - (15 + bounds.X)) < 1 && Math.Abs(position.Y - (8 + bounds.Y)) < 1,
                    $"Editor {name} uses the exact Figma icon in its 46×32 caption button (actual {position.X:F1},{position.Y:F1}; icon {icon?.ActualWidth:F1}×{icon?.ActualHeight:F1}; bounds {icon?.Drawings?.Bounds})");
            }
            var logo = Descendants(window).OfType<Border>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "EditorCaptionLogo");
            check(logo is { Width: 16, Height: 16, CornerRadius.TopLeft: 4, Child: TextBlock { Text: "S" } }
                && logo.Background is SolidColorBrush { Color: var color } && color == Color.FromRgb(0, 95, 184),
                "Editor caption shows the 16px blue Figma S logo");
            window.Title = "문서 - 세모점";
            check(Descendants(window).OfType<TextBlock>().Any(t => t.Text == "문서 - 세모점"),
                "Editor caption retains the real document title and app name");
            if (Environment.GetEnvironmentVariable("CAPTION_CAPTURE") is { Length: > 0 } capture)
                await WindowsSmoke.RenderElementAsync(window, capture);
            if (Environment.GetEnvironmentVariable("CAPTION_INTERACTIVE_CHECKS") == "1")
            {
                // A direct command on a standard window distinguishes a host restriction from
                // a caption button regression. Never count a restricted host as a passing test.
                var baseline = new Window { Width = 240, Height = 160, ShowInTaskbar = false };
                try
                {
                    baseline.Show(); baseline.Activate(); await Task.Delay(200);
                    var baselineHandle = new WindowInteropHelper(baseline).Handle;
                    SetForegroundWindow(baselineHandle); await Task.Delay(100);
                    await WindowsInteractiveFocusWait.WaitAsync(baseline, "기본 창 명령");
                    if (GetForegroundWindow() != baselineHandle) { Console.WriteLine("SKIP: caption commands require the standard test window to be the native foreground window."); return; }
                    SystemCommands.MinimizeWindow(baseline); await Task.Delay(300);
                    if (baseline.WindowState != WindowState.Minimized)
                    { Console.WriteLine("SKIP: caption commands unavailable; minimizing a standard test window failed."); return; }
                }
                finally { baseline.Close(); }
                var framed = new Window { Width = 240, Height = 160, ShowInTaskbar = false, Content = new TextBlock { Text = "기존 편집기 프레임" } };
                try
                {
                    WindowFrame.Apply(framed, editor: true);
                    framed.Show(); framed.Activate(); await Task.Delay(200);
                    var frameHandle = new WindowInteropHelper(framed).Handle;
                    SetForegroundWindow(frameHandle); await Task.Delay(100);
                    await WindowsInteractiveFocusWait.WaitAsync(framed, "편집기 프레임 명령");
                    if (!framed.IsActive || GetForegroundWindow() != frameHandle) { Console.WriteLine("SKIP: caption commands require the test frame to be the native foreground window."); return; }
                    SystemCommands.MinimizeWindow(framed); await Task.Delay(300);
                    if (framed.WindowState != WindowState.Minimized)
                    { check(false, "Direct minimize fails on the editor frame although the standard test window minimized"); return; }
                }
                finally { framed.Close(); }
                window.Activate(); await Task.Delay(200);
                var handle = new WindowInteropHelper(window).Handle;
                SetForegroundWindow(handle); await Task.Delay(100);
                await WindowsInteractiveFocusWait.WaitAsync(window, "편집기 창 명령");
                if (!window.IsActive || GetForegroundWindow() != handle) { Console.WriteLine("SKIP: caption commands require the test editor to be the native foreground window."); return; }
                var events = new List<string>();
                var source = HwndSource.FromHwnd(handle);
                HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                {
                    if (message is 0x0005 or 0x0024 or 0x0112 or 0x0006) events.Add($"0x{message:X}:{wParam.ToInt64():X}");
                    return IntPtr.Zero;
                };
                source?.AddHook(hook);
                SystemCommands.MinimizeWindow(window);
                for (var i = 0; i < 15 && window.WindowState != WindowState.Minimized; i++) await Task.Delay(200);
                if (window.WindowState != WindowState.Minimized)
                {
                    Console.WriteLine($"CAPTION DIAGNOSTIC: direct minimize state={window.WindowState}, active={window.IsActive}, owner={window.Owner?.Title ?? "none"}, owned={window.OwnedWindows.Count}, visible={window.IsVisible}, hwnd={handle}, foreground={GetForegroundWindow()}, iconic={IsIconic(handle)}, style=0x{GetWindowLong(handle, -16):X}, messages={string.Join(',', events)}");
                    buttons.Single(b => AutomationProperties.GetName(b) == "최소화").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(500);
                    Console.WriteLine($"CAPTION DIAGNOSTIC: minimize button result={window.WindowState}, iconic={IsIconic(handle)}, messages={string.Join(',', events)}");
                    window.WindowState = WindowState.Minimized; await Task.Delay(300);
                    Console.WriteLine($"CAPTION DIAGNOSTIC: direct WindowState property result={window.WindowState}, active={window.IsActive}");
                    if (source is not null) source.RemoveHook(hook);
                    check(false, "Direct minimize fails on the active test editor although the standard window and editor frame minimized"); return;
                }
                if (source is not null) source.RemoveHook(hook);
                window.WindowState = WindowState.Normal; window.Activate(); await Task.Delay(200);
                var minimize = buttons.Single(b => AutomationProperties.GetName(b) == "최소화");
                var maximize = buttons.Single(b => AutomationProperties.GetName(b) == "최대화 / 복원");
                minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(window.WindowState == WindowState.Minimized, "The Figma minimize button minimizes the editor window");
                window.WindowState = WindowState.Normal; window.Activate(); await Task.Delay(100);
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(window.WindowState == WindowState.Maximized, "The Figma maximize button maximizes the editor window");
                maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(window.WindowState == WindowState.Normal, "The Figma maximize button restores the editor window");
                await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown();
                buttons.Single(b => AutomationProperties.GetName(b) == "닫기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                check(!window.IsVisible, "The Figma close button closes the editor window");
            }
            else Console.WriteLine("SKIP: caption minimize, maximize, restore and close require an interactive desktop (set CAPTION_INTERACTIVE_CHECKS=1).");
        }
        finally { if (window.IsVisible) { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); } }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
}
