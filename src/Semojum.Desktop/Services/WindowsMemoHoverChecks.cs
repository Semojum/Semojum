using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;
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
                var pointerMoved = false;
                try
                {
                    window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle);
                    await Task.Delay(200);
                    var focusReady = await WindowsInteractiveFocusWait.WaitAsync(window, "메모 표시");
                    if (focusReady) focusReady = GetCursorPos(out originalPointer);
                    var p = rows[0].PointToScreen(new Point(12, 12)); var moved = focusReady && SetCursorPos((int)p.X, (int)p.Y);
                    pointerMoved = moved;
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
                finally { if (pointerMoved) SetCursorPos(originalPointer.X, originalPointer.Y); }
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
        await VerifyBubbleAsync(check);
    }

    // Final Figma 「메모 표시에 마우스 올림」: every memo marker opens one shared non-modal bubble without taking focus,
    // and it closes 0.3s after the pointer has left both the marker and the bubble.
    private static async Task VerifyBubbleAsync(Action<bool, string> check)
    {
        var main = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 600 };
        main.Show(); Window? gridWindow = null;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var block = main.Session.ActiveBlock!; block.Source = "첫 줄"; block.Braille = "⠁";
            block.Memos.Add(new("text-hover", null, "TEXT", 0, "첫 줄", "원문 메모", DateTimeOffset.Now, Author: "검수자"));
            block.Memos.Add(new("braille-hover", null, "BRAILLE", 0, "⠁", "점자 메모", DateTimeOffset.Now, Author: "검수자"));
            main.PageEditor.Refresh(); await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var hover = main.MemoHover;
            var page = main.PageEditor.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            var source = main.SourceEditor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            var braille = main.Braille.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            main.ToggleDetached();
            var detached = main.DetachedPane!;
            check(new object?[] { page.HoverBubble, source.HoverBubble, braille.HoverBubble, main.Braille.GridView.HoverBubble,
                detached.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single().HoverBubble, detached.GridView.HoverBubble }.All(b => ReferenceEquals(b, hover)),
                "The text editor, the auxiliary source and braille editors and both braille grids share one memo hover bubble");
            main.ToggleDetached();
            static void Raise(UIElement element, RoutedEvent routed) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = routed });
            static async Task<long> ClosesAfterAsync(Window bubble)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                while (bubble.IsVisible && watch.ElapsedMilliseconds < 3000) await Task.Delay(10);
                return bubble.IsVisible ? -1 : watch.ElapsedMilliseconds;
            }
            async Task VerifyMarker(string name, Func<bool> enter, UIElement marker, string body)
            {
                var focused = Keyboard.FocusedElement;
                var opened = enter(); var bubble = hover.Current;
                check(opened && bubble is { IsVisible: true } && bubble.Owner == main && !bubble.ShowActivated && !bubble.IsActive && !bubble.IsKeyboardFocusWithin
                    && !ComponentDispatcher.IsThreadModal && ReferenceEquals(Keyboard.FocusedElement, focused)
                    && bubble.MemoItems.Items.Cast<LineMemo>().Select(m => m.Body).SequenceEqual([body]),
                    $"Hovering the {name} memo marker opens its thread as a non-modal bubble without taking focus");
                if (bubble is null) return;
                Raise(marker, Mouse.MouseLeaveEvent);
                var closed = await ClosesAfterAsync(bubble);
                check(closed >= 250 && hover.Current is null, $"The {name} memo bubble closes 0.3s after the pointer leaves the marker (closed after {closed}ms)");
            }
            await VerifyMarker("text editor", () => page.HoverLine(0, page.SlotPadding + 8), page, "원문 메모");
            await VerifyMarker("auxiliary source editor", () => source.HoverLine(0, source.SlotPadding + 8), source, "원문 메모");
            await VerifyMarker("auxiliary braille editor", () => braille.HoverLine(0, braille.SlotPadding + 8), braille, "점자 메모");
            var face = new BrailleGridFace(1, [[new GridCell(block, 0, '⠁')]]);
            var grid = new BrailleGridView { Blocks = [block], ItemsSource = new[] { face }, HoverBubble = hover };
            gridWindow = new Window { Width = 320, Height = 160, Content = grid, ShowInTaskbar = false }; gridWindow.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); gridWindow.UpdateLayout();
            var faceVisual = Descendants(grid).OfType<FrameworkElement>().First(e => e.GetType().Name == "Face");
            await VerifyMarker("braille grid", () => grid.HoverMarker(face, 0, BrailleGridView.MarkerStart + 8), faceVisual, "점자 메모");
            check(!grid.HoverMarker(face, 0, BrailleGridView.MarkerStart + BrailleGridView.MarkerSize + 4) && !page.HoverLine(0, page.SlotPadding + 24) && hover.Current is null,
                "Only the memo slot opens the bubble; other marker slots keep their tooltips");

            check(page.HoverLine(0, page.SlotPadding + 8) && hover.Current is not null, "The memo bubble reopens on the next hover");
            var reentered = hover.Current!;
            Raise(page, Mouse.MouseLeaveEvent); Raise(reentered, Mouse.MouseEnterEvent); await Task.Delay(600);
            check(reentered.IsVisible && hover.Current == reentered, "Moving the pointer from the marker into the bubble keeps it open");
            Raise(reentered, Mouse.MouseLeaveEvent);
            var bubbleLeft = await ClosesAfterAsync(reentered);
            check(bubbleLeft >= 250 && hover.Current is null, $"Leaving the bubble closes it 0.3s later (closed after {bubbleLeft}ms)");

            check(page.HoverLine(0, page.SlotPadding + 8) && hover.Current is not null, "The memo bubble opens before the marker is clicked");
            var hovered = hover.Current!; MemoThreadDialog? modal = null; var modalShown = false; var bubbleClosed = false;
            var closer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            closer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<MemoThreadDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == main && d != hovered) is not { } shown) return;
                modal = shown; modalShown = ComponentDispatcher.IsThreadModal; bubbleClosed = !hovered.IsVisible; closer.Stop(); shown.Close();
            };
            closer.Start();
            try { page.ActivateLine(0, page.SlotPadding + 8); var deadline = DateTime.UtcNow.AddSeconds(4); while (modal is null && DateTime.UtcNow < deadline) await Task.Delay(10); }
            finally { closer.Stop(); }
            check(modal is not null && modalShown && bubbleClosed && hover.Current is null, "Clicking the memo marker closes the bubble and opens the modal memo thread");

            // Switching to read-only queues a page refresh; until it runs no line resolves, so wait or the bubble check below proves nothing.
            main.Session.IsReadOnly = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(main.PageEditor.ResolveLine(0).Block == block && page.Preview(0).Contains("원문 메모"),
                $"A read-only memo marker keeps its tooltip text (preview length={page.Preview(0).Length}, resolved={main.PageEditor.ResolveLine(0).Block is not null})");
            check(!page.HoverLine(0, page.SlotPadding + 8), "A read-only memo marker does not open the bubble");
            check(hover.Current is null, "A read-only memo marker leaves no bubble open");
            main.Session.IsReadOnly = false;
        }
        finally { main.MemoHover.Close(); gridWindow?.Close(); await main.ReleaseResourcesAsync(); main.PrepareSmokeShutdown(); main.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root, i))) yield return child;
    }
}
