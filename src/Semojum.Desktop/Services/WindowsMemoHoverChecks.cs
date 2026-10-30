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
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

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
            var heading = (StackPanel)rows[0].Children.OfType<StackPanel>().Single().Children[0];
            var author = (TextBlock)heading.Children[0];
            check(rows.All(row => row.ActualHeight >= 66) && author.FontSize == 14 && author.FontWeight == FontWeights.Bold,
                "Memo rows keep the Figma minimum height and author typography");

            if (Environment.GetEnvironmentVariable("MEMO_INTERACTIVE_CHECKS") == "1" && GetCursorPos(out var originalPointer))
            {
                try
                {
                    window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle);
                    await Task.Delay(200);
                    var p = rows[0].PointToScreen(new Point(12, 12)); var moved = SetCursorPos((int)p.X, (int)p.Y);
                    await Task.Delay(200); Mouse.Synchronize(); GetCursorPos(out var actual);
                    var hitWindow = GetAncestor(WindowFromPoint(actual), 2);
                    if (!moved || !window.IsActive || Math.Abs(actual.X - p.X) > 2 || Math.Abs(actual.Y - p.Y) > 2
                        || hitWindow != new WindowInteropHelper(window).Handle)
                    {
                        Console.WriteLine($"MEMO POINTER DIAGNOSTIC: requested={p}, actual={actual.X},{actual.Y}, WPF={Mouse.GetPosition(rows[0])}, over={Mouse.DirectlyOver?.GetType().Name}, target={new WindowInteropHelper(window).Handle}, hit={hitWindow}, active={window.IsActive}");
                        Console.WriteLine("SKIP: memo hover requires an active test window and a usable pointer.");
                    }
                    else
                    {
                        check(rows[0].IsMouseOver, "WPF recognizes pointer hover on the actual memo row when the test window is under the cursor");
                        var local = Mouse.GetPosition(rows[0]);
                        check(root.All(b => b.Opacity == 1) && reply.All(b => b.Opacity == 0), $"Hover reveals only the hovered row actions (root hover={rows[0].IsMouseOver}, reply hover={rows[1].IsMouseOver}, local={local}, over={Mouse.DirectlyOver?.GetType().Name}, root opacity={string.Join(',', root.Select(b => b.Opacity))}, reply opacity={string.Join(',', reply.Select(b => b.Opacity))})");
                        if (Environment.GetEnvironmentVariable("MEMO_HOVER_CAPTURE") is { Length: > 0 } capture)
                            await WindowsSmoke.RenderElementAsync(window, capture);
                        var away = window.PointToScreen(new Point(window.ActualWidth - 8, window.ActualHeight - 8));
                        SetCursorPos((int)away.X, (int)away.Y); await Task.Delay(200);
                        check(root.All(b => b.Opacity == 0), "Actions hide again after pointer leaves");
                    }
                    if (window.IsActive)
                    { Keyboard.Focus(reply[0]); await Task.Delay(100); check(reply[0].IsKeyboardFocused && reply[0].Opacity == 1, "Keyboard focus reveals reply action"); }
                    else Console.WriteLine("SKIP: memo keyboard focus requires an active test window.");
                }
                finally { SetCursorPos(originalPointer.X, originalPointer.Y); }
            }
            else Console.WriteLine("SKIP: memo hover and keyboard focus require an interactive desktop (set MEMO_INTERACTIVE_CHECKS=1).");
            var deleted = false; view.DeleteAction = (_, index) => deleted = index == 0;
            reply[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(deleted, "Reply action retains its existing behavior");
            view.CanEdit = false; window.UpdateLayout();
            rows = ((StackPanel)view.Content).Children.OfType<DockPanel>().ToArray();
            check(rows.SelectMany(r => r.Children.OfType<Button>()).All(b => !b.IsEnabled), "Read-only mode keeps row actions disabled");
            var longText = string.Join(" ", Enumerable.Repeat("긴 메모 내용", 80));
            view.DataContext = memo with { Body = longText };
            window.UpdateLayout();
            var longRow = ((StackPanel)view.Content).Children.OfType<DockPanel>().First();
            var longLines = longRow.Children.OfType<StackPanel>().Single();
            var longMessage = (TextBlock)longLines.Children[1];
            check(longRow.DesiredSize.Height > 66 && longMessage.Text == longText,
                "A long memo expands its row instead of truncating its text");

            var model = new DocumentSession();
            var block = new EditorBlock { Id = 1, Page = 1, Source = "본문" };
            model.Add(block); block.Memos.Add(memo);
            var dialog = new MemoThreadDialog(block, "TEXT", 0, "본문", action => action(), model);
            try
            {
                dialog.Show(); await Task.Delay(100); dialog.UpdateLayout();
                var y = dialog.MemoItems.TransformToAncestor(dialog).Transform(new Point()).Y;
                check(y < 15, "The memo content starts at the top of the Figma popover without a separate toolbar");
                var menu = ((FrameworkElement)dialog.MemoItems.Parent).ContextMenu!;
                var labels = menu.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
                check(labels.Contains("이전 메모") && labels.Contains("다음 메모"),
                    "Memo navigation remains available from the context menu");
            }
            finally { dialog.Close(); }
        }
        finally { window.Close(); }
    }
}
