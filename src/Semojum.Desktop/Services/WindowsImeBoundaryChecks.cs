using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;

namespace Semojum.Desktop.Services;

internal static class WindowsImeBoundaryChecks
{
    // The native IME itself still needs physical keyboard testing. These checks
    // exercise the event ordering with a deterministic composition callback.
    internal static async Task RunAsync(Window owner, Action<bool, string> check, Action<string> stage)
    {
        var editor = new TextEditor { Text = "기존", Height = 80 };
        var target = new TextBox(); var panel = new StackPanel();
        panel.Children.Add(editor); panel.Children.Add(target);
        var pending = true;
        NativeIme.Attach(editor, () =>
        {
            if (!pending) return;
            pending = false;
            editor.TextArea.PerformTextInput("가");
        });
        var window = new Window { Owner = owner, Content = panel, Width = 360, Height = 180 };
        window.Show();
        var sourceHandle = new WindowInteropHelper(window).Handle;
        var ownerHandle = new WindowInteropHelper(owner).Handle;
        var source = HwndSource.FromHwnd(sourceHandle);
        var ownerSource = HwndSource.FromHwnd(ownerHandle);
        var messages = new List<string>();
        HwndSourceHook hook = (nint hwnd, int message, nint wParam, nint lParam, ref bool handled) =>
        {
            if (message is 0x0006 or 0x001C or 0x0086)
                messages.Add($"{(hwnd == sourceHandle ? "ime" : "owner")}:0x{message:X}/0x{wParam.ToInt64():X}");
            return 0;
        };
        source?.AddHook(hook);
        ownerSource?.AddHook(hook);
        string State(string label) => $"IME {label}: foreground=0x{GetForegroundWindow().ToInt64():X}, " +
            $"ime=0x{sourceHandle.ToInt64():X}, owner=0x{ownerHandle.ToInt64():X}, " +
            $"imeActive={window.IsActive}, ownerActive={owner.IsActive}, editorFocused={editor.TextArea.IsKeyboardFocused}, " +
            $"pending={pending}, text={editor.Text}, messages={string.Join(',', messages)}";
        try
        {
            var firstFocus = editor.Focus(); editor.CaretOffset = editor.Document.TextLength;
            stage(State($"click setup focusReturned={firstFocus}"));
            target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = Mouse.PreviewMouseDownEvent });
            check(editor.Text == "기존가" && editor.TextArea.IsKeyboardFocused,
                "IME boundary completes pending text on another control's click before focus changes");
            target.Focus();
            check(editor.Text == "기존가" && target.Text == "" && target.IsKeyboardFocused,
                "IME boundary keeps completed text in its original editor when focus moves");

            messages.Clear();
            var sourceActivated = window.Activate();
            await WindowsInteractiveFocusWait.WaitAsync(window, "IME 창 전환");
            var editorFocused = editor.Focus();
            editor.CaretOffset = editor.Document.TextLength;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(50);
            var ready = sourceHandle != 0 && GetForegroundWindow() == sourceHandle && window.IsActive && editor.TextArea.IsKeyboardFocused;
            stage(State($"native setup activateReturned={sourceActivated} focusReturned={editorFocused} ready={ready}"));
            if (!ready)
            {
                const string reason = "SKIP: IME native deactivation requires the owned test window and editor to obtain foreground and keyboard focus.";
                Console.WriteLine(reason); stage(reason); return;
            }

            messages.Clear(); pending = true;
            var ownerActivated = owner.Activate();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(50);
            var foreground = GetForegroundWindow();
            var deactivated = foreground == ownerHandle ||
                (foreground != sourceHandle && messages.Any(m => m is "ime:0x6/0x0" or "ime:0x1C/0x0" or "ime:0x86/0x0"));
            stage(State($"native result ownerActivateReturned={ownerActivated} deactivated={deactivated}"));
            if (!deactivated)
            {
                const string reason = "SKIP: IME native deactivation could not be observed after requesting the test owner window.";
                Console.WriteLine(reason); stage(reason); return;
            }
            check(!pending && editor.Text == "기존가가",
                "IME boundary completes pending text when native window deactivation actually occurs");
            check(!window.IsActive,
                "IME test window reports inactive after its native foreground transition");
        }
        finally
        {
            source?.RemoveHook(hook);
            ownerSource?.RemoveHook(hook);
            window.Close();
        }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
