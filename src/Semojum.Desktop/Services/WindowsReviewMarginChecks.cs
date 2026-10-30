using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsReviewMarginChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var block = new EditorBlock { Source = "첫 줄\n둘째 줄", Braille = "⠁\n⠃", Translation = BrailleTranslation.Plain("⠁") };
        var source = new TextEditor { Text = block.Source, FontSize = 16, ShowLineNumbers = true };
        var memoClicks = 0; var reviewClicks = 0;
        var margin = new MemoMargin(() => block, "TEXT", line => { if (line == 0) memoClicks++; }) { GutterWidth = 12, ReviewRequested = line => { if (line == 0) reviewClicks++; } };
        source.TextArea.LeftMargins.Add(margin);
        var panel = new StackPanel(); panel.Children.Add(source);
        var window = new Window { Width = 500, Height = 220, Content = panel, ShowInTaskbar = false };
        window.Show();
        try
        {
            async Task Paint() { margin.InvalidateVisual(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(80); window.UpdateLayout(); }
            await Paint();
            check(Math.Abs(margin.ActualWidth - 12) < .5 && margin.Preview(0) == "" && margin.ReviewPreview(0) == "", "No real memo or review leaves the source editor's original 12px gutter empty");
            block.Memos.Add(new("memo", null, "TEXT", 0, "첫 줄", "검토할 부분", DateTimeOffset.Now, Author: "작성자"));
            block.Memos.Add(new("braille", null, "BRAILLE", 0, "⠁", "점자 메모", DateTimeOffset.Now));
            block.Memos.Add(new("resolved", null, "TEXT", 0, "첫 줄", "해결된 메모", DateTimeOffset.Now, Resolved: true));
            await Paint();
            check(margin.Preview(0).Contains("메모\n작성자\n검토할 부분") && !margin.Preview(0).Contains("점자 메모") && !margin.Preview(0).Contains("해결된 메모") && Math.Abs(margin.ActualWidth - 16) < .5,
                "Only unresolved source memos produce a labelled, unclipped 16px marker");
            block.Translation = new("⠁", [], [new("기호", 1, "unknown")], false, []);
            await Paint();
            check(margin.ReviewPreview(0).Contains("인식하지 못한 문자: 기호") && margin.ReviewPreview(1) == "", "Only the real review block's first line describes its dropped character");
            check(Math.Abs(margin.ActualWidth - 32) < .5 && !margin.IsReviewHit(0, 8) && margin.IsReviewHit(0, 24), "Coexisting memo and review use separate 16px slots and click targets");
            margin.ActivateLine(0, 8); margin.ActivateLine(0, 24);
            check(memoClicks == 1 && reviewClicks == 1, "Source mouse-handler coordinate dispatch invokes each actual callback separately");
            memoClicks = reviewClicks = 0;
            if (Environment.GetEnvironmentVariable("REVIEW_MOUSE_CHECKS") == "1" && GetCursorPos(out var originalPointer))
            {
                try
                {
                    var validCoordinates = true; var inputFailure = false;
                    window.Activate(); await Task.Delay(100);
                    var line = source.TextArea.TextView.VisualLines.First();
                    var y = line.VisualTop - source.TextArea.TextView.VerticalOffset + line.Height / 2;
                    async Task ClickSlot(double x)
                    {
                        var position = margin.PointToScreen(new Point(x, y));
                        if (!SetCursorPos((int)Math.Round(position.X), (int)Math.Round(position.Y))) { validCoordinates = false; return; }
                        await Task.Delay(100); Mouse.Synchronize();
                        var positionInMargin = Mouse.GetPosition(margin); GetCursorPos(out var actual);
                        var nativeReady = window.IsActive && Math.Abs(actual.X - position.X) < 2 && Math.Abs(actual.Y - position.Y) < 2
                            && GetAncestor(WindowFromPoint(actual), 2) == new WindowInteropHelper(window).Handle;
                        if (!nativeReady) { validCoordinates = false; Console.WriteLine($"SOURCE POINTER DIAGNOSTIC: requested={position}, cursor={actual.X},{actual.Y}, target={new WindowInteropHelper(window).Handle}, hit={GetAncestor(WindowFromPoint(actual), 2)}, active={window.IsActive}"); return; }
                        var wpfReady = margin.IsMouseOver && Math.Abs(positionInMargin.X - x) < 2 && Math.Abs(positionInMargin.Y - y) < 2;
                        if (!wpfReady) { inputFailure = true; validCoordinates = false; check(false, $"Source margin misses pointer input even though the active test window is under the cursor (WPF={positionInMargin}, hover={margin.IsMouseOver})"); return; }
                        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                    }
                    await ClickSlot(8); await ClickSlot(24);
                    if (validCoordinates) check(memoClicks == 1 && reviewClicks == 1, $"Actual pointer clicks invoke source memo and review callbacks separately (memo={memoClicks}, review={reviewClicks})");
                    else if (!inputFailure) Console.WriteLine("SKIP: source WPF mouse routing requires a desktop that reports the moved pointer coordinates.");
                }
                finally { SetCursorPos(originalPointer.X, originalPointer.Y); }
            }
            else Console.WriteLine("SKIP: source WPF mouse routing requires REVIEW_MOUSE_CHECKS=1 and a usable pointer.");
            check(!MemoMargin.MemoDrawing.Bounds.IsEmpty && !MemoMargin.WarningDrawing.Bounds.IsEmpty && MemoMargin.MemoDrawing.Bounds.Width <= 16 && MemoMargin.WarningDrawing.Bounds.Width <= 16,
                "Both original Figma 16px SVG assets have non-empty rendered geometry");
            check(AutomationProperties.GetName(margin).Contains("원문") && AutomationProperties.GetHelpText(margin).Contains("메모"), "Source margin exposes its purpose through UI Automation properties");
            if (Environment.GetEnvironmentVariable("REVIEW_MARGIN_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
            margin.ShowMemos = false; await Paint();
            check(Math.Abs(margin.ActualWidth - 16) < .5 && margin.Preview(0) == "" && margin.IsReviewHit(0, 8), "Hiding memos retains the unclipped review marker");
            margin.ShowReviews = false; margin.ShowMemos = true; await Paint();
            check(Math.Abs(margin.ActualWidth - 16) < .5 && margin.ReviewPreview(0) == "" && !margin.IsReviewHit(0, 8), "Hiding review retains the unclipped memo marker");
            var braille = new MemoMargin(() => block, "BRAILLE", _ => { });
            check(braille.Preview(0).Contains("점자 메모") && !braille.Preview(0).Contains("검토할 부분"), "Braille and source memo markers retain their separate panel data");
        }
        finally { window.Close(); }
        var cell0 = new GridCell(block, 0, '⠁'); var cell2 = new GridCell(block, 2, '⠃');
        block.Memos.Add(new("braille-second", null, "BRAILLE", 1, "⠃", "둘째 줄 메모", DateTimeOffset.Now));
        var firstFace = new BrailleGridFace(1, [[cell0], [new GridCell(block, 1, '⠀', true)]]);
        var secondFace = new BrailleGridFace(2, [[cell2], [cell2]]);
        var gridMemoClicks = 0; var gridReviewClicks = 0;
        var grid = new BrailleGridView { ItemsSource = new[] { firstFace, secondFace }, Blocks = [block],
            MemoRequested = (b, line) => { if (b == block && line == 0) gridMemoClicks++; }, ReviewRequested = b => { if (b == block) gridReviewClicks++; } };
        var gridWindow = new Window { Width = 480, Height = 240, Content = grid, ShowInTaskbar = false }; gridWindow.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); gridWindow.UpdateLayout();
            var first = grid.MarkersFor(firstFace); var second = grid.MarkersFor(secondFace);
            check(first.TryGetValue(0, out var both) && both.Memo && both.Review && !first.ContainsKey(1), "Grid markers require actual logical-line start cells and exclude insertion padding");
            check(second.TryGetValue(0, out var secondLine) && secondLine.Line == 1 && secondLine.Memo && !second.ContainsKey(1), "Grid markers span faces and do not duplicate the same logical line");
            var cachedBuilds = grid.MarkerCacheBuildCount;
            grid.Point(cell0, false); grid.Point(cell2, true); _ = grid.MarkersFor(firstFace);
            check(grid.MarkerCacheBuildCount == cachedBuilds, "Caret movement and selection reuse the marker index rather than scanning every face again");
            var memoIndex = block.Memos.ToList().FindIndex(m => m.LocalId == "braille"); var originalMemo = block.Memos[memoIndex];
            block.Memos[memoIndex] = originalMemo with { Resolved = true };
            check(grid.MarkersFor(firstFace).TryGetValue(0, out var resolvedMarker) && !resolvedMarker.Memo && resolvedMarker.Review, "Resolving a braille memo immediately invalidates the marker index");
            block.Memos[memoIndex] = originalMemo;
            var reviewResult = block.Translation; block.Translation = BrailleTranslation.Plain("⠁");
            check(grid.MarkersFor(firstFace).TryGetValue(0, out var updatedMarker) && updatedMarker.Memo && !updatedMarker.Review, "A changed translation immediately invalidates the marker index");
            block.Translation = reviewResult;
            grid.CoordinatesCurrent = false; check(grid.MarkersFor(firstFace).Count == 0, "Stale grid coordinates hide all markers"); grid.CoordinatesCurrent = true;
            grid.IsReadOnly = true; check(grid.MarkersFor(firstFace).Count == 1, "Read-only mode retains accurate status markers without changing the data"); grid.IsReadOnly = false;
            grid.ShowMemos = false; grid.InvalidateFaces(); check(grid.MarkersFor(firstFace).TryGetValue(0, out var warningOnly) && !warningOnly.Memo && warningOnly.Review, "Grid memo toggle keeps the actual review warning");
            grid.ShowMemos = true; grid.ShowReviews = false; grid.InvalidateFaces(); check(grid.MarkersFor(firstFace).TryGetValue(0, out var memoOnly) && memoOnly.Memo && !memoOnly.Review, "Grid review toggle keeps the actual braille memo");
            grid.ShowReviews = true; grid.InvalidateFaces(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); gridWindow.UpdateLayout();
            grid.Point(cell0, false);
            check(!grid.ActivateMarker(firstFace, 0, 39) && !grid.ActivateMarker(firstFace, 0, 72), "Grid marker hit targets do not consume the drag gutter or first editable cell");
            check(grid.ActivateMarker(firstFace, 0, 48) && grid.ActivateMarker(firstFace, 0, 64) && gridMemoClicks == 1 && gridReviewClicks == 1,
                "Grid mouse-handler coordinate dispatch invokes the two actual callbacks separately");
            check(grid.ActiveBlock == block && grid.CaretOffset == 0, "Opening marker callbacks does not alter the selected braille cell");
            gridMemoClicks = gridReviewClicks = 0;
            var faceVisual = Descendants(grid).OfType<FrameworkElement>().First(e => e.GetType().Name == "Face");
            var hit = faceVisual.GetType().GetMethod("Hit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            check(ReferenceEquals(hit.Invoke(faceVisual, [new Point(80, 14)]), cell0), "The first editable grid cell remains independently hittable after marker expansion");
            var hover = faceVisual.GetType().GetMethod("UpdateMarkerToolTip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            hover.Invoke(faceVisual, [new Point(48, 14)]);
            check(faceVisual.ToolTip?.ToString()?.Contains("점자 메모") == true, "Grid marker hover exposes its current memo detail");
            hover.Invoke(faceVisual, [new Point(100, 14)]);
            check(faceVisual.ToolTip == null, "Leaving a marker clears the previous tooltip");
            hover.Invoke(faceVisual, [new Point(48, 14)]); grid.CoordinatesCurrent = false;
            check(faceVisual.ToolTip == null, "Expiring grid coordinates clears a visible marker tooltip");
            grid.CoordinatesCurrent = true; hover.Invoke(faceVisual, [new Point(48, 14)]); grid.ShowMemos = false;
            check(faceVisual.ToolTip == null, "Changing the marker visibility clears a visible tooltip");
            grid.ShowMemos = true;
            grid.Point(cell2, true);
            check(grid.ActiveBlock == block && grid.CaretOffset == 2 && grid.Selection.Any(), "Selection across faces remains available with marker gutters");
            if (Environment.GetEnvironmentVariable("REVIEW_MOUSE_CHECKS") == "1" && GetCursorPos(out var savedPointer))
            {
                try
                {
                    var validCoordinates = true; var inputFailure = false;
                    gridWindow.Activate(); await Task.Delay(100);
                    foreach (var x in new[] { 48d, 64d })
                    {
                        var pos = faceVisual.PointToScreen(new Point(x, 14));
                        if (!SetCursorPos((int)Math.Round(pos.X), (int)Math.Round(pos.Y))) { validCoordinates = false; continue; }
                        await Task.Delay(100); Mouse.Synchronize();
                        var positionInFace = Mouse.GetPosition(faceVisual); GetCursorPos(out var actual);
                        var nativeReady = gridWindow.IsActive && Math.Abs(actual.X - pos.X) < 2 && Math.Abs(actual.Y - pos.Y) < 2
                            && GetAncestor(WindowFromPoint(actual), 2) == new WindowInteropHelper(gridWindow).Handle;
                        if (!nativeReady) { validCoordinates = false; Console.WriteLine($"GRID POINTER DIAGNOSTIC: requested={pos}, cursor={actual.X},{actual.Y}, target={new WindowInteropHelper(gridWindow).Handle}, hit={GetAncestor(WindowFromPoint(actual), 2)}, active={gridWindow.IsActive}"); continue; }
                        var wpfReady = faceVisual.IsMouseOver && Math.Abs(positionInFace.X - x) < 2 && Math.Abs(positionInFace.Y - 14) < 2;
                        if (!wpfReady) { inputFailure = true; validCoordinates = false; check(false, $"Braille grid misses pointer input even though the active test window is under the cursor (WPF={positionInFace}, hover={faceVisual.IsMouseOver})"); continue; }
                        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
                    }
                    if (validCoordinates) check(gridMemoClicks == 1 && gridReviewClicks == 1, $"Actual pointer clicks invoke grid memo and review callbacks separately (memo={gridMemoClicks}, review={gridReviewClicks})");
                    else if (!inputFailure) Console.WriteLine("SKIP: grid WPF mouse routing requires a desktop that reports the moved pointer coordinates.");
                }
                finally { SetCursorPos(savedPointer.X, savedPointer.Y); }
            }
            else Console.WriteLine("SKIP: grid WPF mouse routing requires REVIEW_MOUSE_CHECKS=1 and a usable pointer.");
            if (Environment.GetEnvironmentVariable("REVIEW_GRID_CAPTURE") is { Length: > 0 } gridCapture) await WindowsSmoke.RenderElementAsync(gridWindow, gridCapture);
            var adjacent = new BrailleGridFace(3, [[cell0], [cell2]]); grid.ItemsSource = new[] { adjacent }; grid.InvalidateFaces();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); gridWindow.UpdateLayout();
            var adjacentVisual = Descendants(grid).OfType<FrameworkElement>().First(e => e.GetType().Name == "Face");
            var adjacentHover = adjacentVisual.GetType().GetMethod("UpdateMarkerToolTip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            adjacentHover.Invoke(adjacentVisual, [new Point(48, 14)]); var firstDescription = adjacentVisual.ToolTip?.ToString();
            adjacentHover.Invoke(adjacentVisual, [new Point(48, 42)]); var secondDescription = adjacentVisual.ToolTip?.ToString();
            check(firstDescription?.Contains("점자 메모") == true && secondDescription?.Contains("둘째 줄 메모") == true && firstDescription != secondDescription,
                "Moving to an adjacent marked row replaces the tooltip with that row's current description");
        }
        finally { gridWindow.Close(); }
        var blankBlock = new EditorBlock { Id = 17, Braille = "⠁\n\n⠃" };
        blankBlock.Memos.Add(new("blank", null, "BRAILLE", 1, "", "빈 줄 메모", DateTimeOffset.Now));
        var layout = new PageLayout(Columns: 8, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함");
        var composed = new ComposedBraille("", [], [new ComposedPage(1, ["⠁", "", "⠃", ""],
            [[new CellAddress(17, 0)], [], [new CellAddress(17, 3)], []],
            [new CellAddress(17, 1), new CellAddress(17, 2), new CellAddress(17, 4), new CellAddress(17, 4)], ["body", "body", "body", "padding"])]);
        var blankFace = BrailleGrid.FromComposition(composed, [blankBlock], layout).Single();
        var blankGrid = new BrailleGridView { Blocks = [blankBlock], ItemsSource = new[] { blankFace } };
        check(blankGrid.MarkersFor(blankFace).TryGetValue(1, out var blankMarker) && blankMarker.Line == 1 && blankMarker.Memo && !blankGrid.MarkersFor(blankFace).ContainsKey(3),
            "A composed blank logical line uses its exact empty-prefix insertion anchor, while generated padding stays unmarked");
        var emptyBlock = new EditorBlock { Id = 18, Braille = "" }; emptyBlock.Memos.Add(new("empty", null, "BRAILLE", 0, "", "빈 문서", DateTimeOffset.Now));
        var emptyFace = BrailleGrid.Project([emptyBlock], layout).Single();
        var emptyGrid = new BrailleGridView { Blocks = [emptyBlock], ItemsSource = new[] { emptyFace } };
        check(emptyGrid.MarkersFor(emptyFace).Count == 0, "A placeholder cell for an entirely empty document is not treated as a verified line location");
        var main = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 600 };
        main.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var actual = main.PageEditor.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            check(actual.GutterWidth == 12 && Math.Abs(actual.ActualWidth - 12) < .5, "The real source editor preserves its 12px gutter without markers");
            var current = main.Session.ActiveBlock!;
            current.Source = "첫 줄"; current.Memos.Add(new("actual", null, "TEXT", 0, "첫 줄", "원문 메모", DateTimeOffset.Now));
            current.Translation = new("⠁", [], [new("기호", 1, "unknown")], false, []);
            main.PageEditor.Refresh(); actual.InvalidateVisual(); await Task.Delay(150); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            check(actual.Preview(0).Contains("원문 메모") && actual.ReviewPreview(0).Contains("기호") && Math.Abs(actual.ActualWidth - 32) < .5,
                "The actual 960px source editor shows both real 16px markers without clipping");
            var brailleMargin = main.Braille.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            check(!main.Braille.Editor.IsVisible && main.Braille.GridView.IsVisible && brailleMargin.Preview(0) == "", "The visible braille grid and hidden text editor are separate rendering paths");
            if (Environment.GetEnvironmentVariable("REVIEW_MAIN_CAPTURE") is { Length: > 0 } mainCapture) await WindowsSmoke.RenderElementAsync(main, mainCapture);
            current.Braille = "⠁"; current.Translation = new("⠁", [], [new("기호", 1, "unknown")], false, [], UnknownTags: ["미확인"]);
            main.Session.IsReadOnly = true;
            var localFace = new BrailleGridFace(1, [[new GridCell(current, 0, '⠁')]]);
            main.Braille.GridView.Blocks = [current]; main.Braille.GridView.ItemsSource = new[] { localFace }; main.Braille.GridView.CoordinatesCurrent = true;
            var localDetailSeen = false; var timeout = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) }; var ticks = 0;
            timeout.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<Semojum.Desktop.Dialogs.NoticeDialog>().FirstOrDefault(d => d.IsVisible) is { } notice)
                {
                    localDetailSeen = notice.MessageText == "점역 검토 필요" && notice.DetailText.Contains("기호") && notice.DetailText.Contains("미확인");
                    timeout.Stop(); notice.Close();
                }
                else if (++ticks >= 30) timeout.Stop();
            };
            timeout.Start(); var opened = main.Braille.GridView.ActivateMarker(localFace, 0, 48); timeout.Stop();
            check(opened && localDetailSeen, "The real read-only local document's grid warning opens its dropped-character and unknown-tag details");
        }
        finally { await main.ReleaseResourcesAsync(); main.PrepareSmokeShutdown(); main.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
