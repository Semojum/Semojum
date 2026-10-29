using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsMemoHoverChecks
{
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var memo = new LineMemo("hover", null, "TEXT", 0, "본문", "메모", DateTimeOffset.Now,
            Author: "작성자", Replies: [new("답글 작성자", "답글", DateTimeOffset.Now)]);
        var view = new MemoConversationView { CanEdit = true, DataContext = memo, Width = 338 };
        var window = new Window { Width = 360, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = view };
        try
        {
            window.Show(); await Task.Delay(200); window.UpdateLayout();
            var rows = ((StackPanel)view.Content).Children.OfType<DockPanel>().ToArray();
            var root = rows[0].Children.OfType<Button>().ToArray();
            var reply = rows[1].Children.OfType<Button>().ToArray();
            check(root.Length == 2 && reply.Length == 1, "Root has resolve and delete, reply has delete only");
            if (rows.All(r => !r.IsMouseOver && !r.IsKeyboardFocusWithin))
                check(root.All(b => b.Opacity == 0) && reply.All(b => b.Opacity == 0), "Actions are hidden until hover or keyboard focus");
            check(rows.All(r => r.Children.OfType<StackPanel>().Count() == 1 && r.Children.Count == r.Children.OfType<Button>().Count() + 1), "Author and message are shown without an avatar");
            check(root.Concat(reply).All(b => UIElementAutomationPeer.CreatePeerForElement(b)?.GetName() == AutomationProperties.GetName(b) && !string.IsNullOrEmpty(AutomationProperties.GetName(b))), "Every action has a screen-reader name");

            if (Environment.GetEnvironmentVariable("MEMO_INTERACTIVE_CHECKS") == "1")
            {
                window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle);
                await Task.Delay(200);
                var p = rows[0].PointToScreen(new Point(12, 12)); var moved = SetCursorPos((int)p.X, (int)p.Y);
                await Task.Delay(200);
                if (!moved || !window.IsActive)
                    throw new InvalidOperationException("Interactive memo checks require an active desktop and access to its pointer.");
                check(root.All(b => b.Opacity == 1) && reply.All(b => b.Opacity == 0), "Hover reveals only the hovered row actions");
                if (Environment.GetEnvironmentVariable("MEMO_HOVER_CAPTURE") is { Length: > 0 } capture)
                    await WindowsSmoke.RenderElementAsync(window, capture);
                SetCursorPos(0, 0); await Task.Delay(200);
                check(root.All(b => b.Opacity == 0), "Actions hide again after pointer leaves");
                Keyboard.Focus(reply[0]); await Task.Delay(100);
                check(reply[0].IsKeyboardFocused && reply[0].Opacity == 1, "Keyboard focus reveals reply action");
            }
            else Console.WriteLine("SKIP: memo hover and keyboard focus require an interactive desktop (set MEMO_INTERACTIVE_CHECKS=1).");
            var deleted = false; view.DeleteAction = (_, index) => deleted = index == 0;
            reply[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(deleted, "Reply action retains its existing behavior");
            view.CanEdit = false; window.UpdateLayout();
            rows = ((StackPanel)view.Content).Children.OfType<DockPanel>().ToArray();
            check(rows.SelectMany(r => r.Children.OfType<Button>()).All(b => !b.IsEnabled), "Read-only mode keeps row actions disabled");
        }
        finally { window.Close(); }
    }
}
