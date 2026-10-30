using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Editing;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsReviewOverviewChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var source = new EditorBlock { Id = 1, Page = 1, Source = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"원문 줄 {i:D4}")) };
        var session = new DocumentSession(); session.Add(source); session.ActiveBlock = source;
        var editor = new ContinuousSourceEditor();
        var memo = new MemoMargin(() => session.ActiveBlock, "TEXT", _ => { }) { ResolveLine = editor.ResolveLine };
        editor.Editor.TextArea.LeftMargins.Add(memo);
        var original = editor.Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().Single();
        var index = editor.Editor.TextArea.LeftMargins.IndexOf(original);
        var numbers = new ColoredLineNumbers { Compact = true, MinWidth = 32, Background = Brushes.WhiteSmoke, NumberBrush = Brushes.Gray };
        editor.Editor.TextArea.LeftMargins[index] = numbers;
        editor.Attach(session);
        var window = new Window { Width = 480, Height = 300, Content = editor, ShowInTaskbar = false };
        window.Show();
        try
        {
            async Task Paint() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            source.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 2);
            source.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Text, source.Source.IndexOf("원문 줄 1000", StringComparison.Ordinal), 2);
            source.Memos.Add(new("overview", null, "TEXT", 2000, "", "메모", DateTimeOffset.Now));
            source.Memos.Add(new("resolved", null, "TEXT", 10, "", "완료", DateTimeOffset.Now, Resolved: true));
            source.ReviewMarks.Add(new("invalid", ReviewKind.SuggestDeletion, ReviewPanel.Text, source.Source.Length + 1, 5, ""));
            source.ReviewMarks.Add(new("stale", ReviewKind.SuggestDeletion, ReviewPanel.Text, 20, 1, "", true));
            await Paint();
            check(editor.Overview.MarkerCount == 3 && memo.ManualKinds(0).Contains(ReviewKind.HumanConfirmed), "Overview uses only valid manual reviews and unresolved source memos at known locations");
            check(!editor.Overview.IsHitTestVisible && numbers.ActualWidth >= 32, "The review number gutter and overview preserve native hit testing");
            var numberColors = Colors(VisualTreeHelper.GetDrawing(numbers)).ToArray();
            check(numberColors.Contains(Color.FromRgb(223, 246, 221)) && numberColors.Contains(Color.FromRgb(15, 123, 15)), "A confirmed source line renders its green Figma background and stripe");
            var overviewColors = Colors(VisualTreeHelper.GetDrawing(editor.Overview)).ToArray();
            check(overviewColors.Contains(Color.FromRgb(0, 95, 184)) && overviewColors.Contains(Color.FromRgb(15, 123, 15)) && overviewColors.Contains(Color.FromRgb(157, 93, 0)), "Actual memo and review locations render blue, green and amber marks on the overview");
            numbers.CustomBackground = numbers.CustomNumberColor = true; numbers.Background = Brushes.Navy; numbers.NumberBrush = Brushes.White;
            numbers.InvalidateVisual(); await Paint();
            numberColors = Colors(VisualTreeHelper.GetDrawing(numbers)).ToArray();
            check(numberColors.Contains(ColorsValue(Brushes.Navy)) && numberColors.Contains(ColorsValue(Brushes.White)) && !numberColors.Contains(Color.FromRgb(223, 246, 221)), "Custom line-number foreground and background remain visible on reviewed lines");
            numbers.HighContrast = true; numbers.CustomBackground = numbers.CustomNumberColor = false; numbers.InvalidateVisual(); await Paint();
            numberColors = Colors(VisualTreeHelper.GetDrawing(numbers)).ToArray();
            check(!numberColors.Contains(Color.FromRgb(223, 246, 221)) && numberColors.Contains(ColorsValue(Brushes.White)), "High-contrast reviewed lines retain the configured high-contrast ink and background");
            numbers.HighContrast = false;
            numbers.SystemHighContrast = () => true; editor.Overview.SystemHighContrast = () => true;
            numbers.SystemChanged(null, new(nameof(SystemParameters.HighContrast))); editor.Overview.SystemChanged(null, new(nameof(SystemParameters.HighContrast))); await Paint();
            numberColors = Colors(VisualTreeHelper.GetDrawing(numbers)).ToArray();
            overviewColors = Colors(VisualTreeHelper.GetDrawing(editor.Overview)).ToArray();
            check(numbers.ListeningForSystemChanges && editor.Overview.ListeningForSystemChanges && numberColors.Contains(SystemColors.WindowTextColor)
                && overviewColors.Contains(SystemColors.WindowColor) && !overviewColors.Contains(Color.FromRgb(0, 95, 184)), "Mocked Windows high contrast refreshes both subscribed review visuals with system colors");
            numbers.SystemHighContrast = () => false; editor.Overview.SystemHighContrast = () => false;
            numbers.SystemChanged(null, new(nameof(SystemParameters.HighContrast))); editor.Overview.SystemChanged(null, new(nameof(SystemParameters.HighContrast))); await Paint();
            check(Colors(VisualTreeHelper.GetDrawing(editor.Overview)).Contains(Color.FromRgb(0, 95, 184)), "Turning off mocked Windows high contrast restores normal review colors");
            var scrollbar = Descendants(editor.Editor).OfType<ScrollBar>().FirstOrDefault(b => b.Orientation == Orientation.Vertical && b.IsVisible);
            check(scrollbar?.Track?.Thumb is { IsVisible: true }, "Native vertical track and draggable thumb remain present beneath the review overview");
            var builds = editor.Overview.CacheBuildCount;
            editor.Editor.ScrollToLine(1500); await Paint();
            check(editor.Editor.VerticalOffset > 0 && editor.Overview.CacheBuildCount <= builds + 1, "Scrolling a long document keeps the marker cache and moves the native viewport");
            if (scrollbar?.Track?.Thumb is { } thumb)
            {
                var previous = scrollbar.Value;
                thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                thumb.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent });
                thumb.RaiseEvent(new DragCompletedEventArgs(0, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
                await Paint();
                check(scrollbar.Value > previous, "The original scrollbar thumb responds to its drag events through the overview");
            }
            editor.Editor.Select(5, 3);
            check(editor.Editor.SelectionLength == 3 && editor.Selection.Sum(s => s.Length) == 3, "Source selection remains usable with the overview overlay");
            editor.ShowReviews = false; await Paint();
            check(editor.Overview.MarkerCount == 1, "Hiding reviews leaves only real source memos in the overview");
            editor.ShowMemos = false; await Paint();
            check(editor.Overview.MarkerCount == 0, "Hiding memos and reviews clears overview markers");
            editor.ShowReviews = true; editor.ShowMemos = true; source.Memos.Clear(); source.ReviewMarks.Clear(); await Paint();
            check(editor.Overview.MarkerCount == 0, "Removing actual annotations refreshes the overview");
            window.Height = 640; await Paint();
            var doc = editor.Editor.Document;
            var fixtures = new List<ReviewMark>();
            var originals = new Dictionary<int, string>();
            for (var i = 0; i < 3000; i++)
            {
                var first = i % 100 + 1;
                var start = doc.GetLineByNumber(first).Offset;
                var end = doc.GetLineByNumber(2900 - i % 100).EndOffset;
                if (!originals.TryGetValue(first, out var originalText)) originals[first] = originalText = source.Source[start..end];
                var mark = new ReviewMark("long-" + i, (ReviewKind)(i % 3), ReviewPanel.Text, start, end - start, originalText);
                fixtures.Add(mark); source.ReviewMarks.Add(mark);
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Paint(); watch.Stop();
            check(editor.Overview.MarkerCount == 3000 && editor.Overview.VisualLineLookupCount <= doc.LineCount + 200,
                $"3,000 overlapping long reviews reuse line and endpoint projections ({editor.Overview.VisualLineLookupCount} lookups, {editor.Overview.LastBuildElapsedMilliseconds} ms overview, {watch.ElapsedMilliseconds} ms full frame)");
            var track = scrollbar!.Track!;
            var bounds = track.TransformToVisual(editor.Overview).TransformBounds(new Rect(0, 0, track.ActualWidth, track.ActualHeight));
            bounds.Intersect(new Rect(0, 0, editor.Overview.ActualWidth, editor.Overview.ActualHeight));
            var tops = Enumerable.Range(1, doc.LineCount).Select(line => editor.Editor.TextArea.TextView.GetVisualTopByDocumentLine(line)).ToArray();
            var expected = new List<(int Pixel, ReviewKind? Kind)>(); var seen = new HashSet<(int, ReviewKind?)>();
            var offsets = new Dictionary<int, int>();
            int ExactPixel(int offset)
            {
                if (offsets.TryGetValue(offset, out var value)) return value;
                var top = editor.Editor.TextArea.TextView.GetVisualPosition(new ICSharpCode.AvalonEdit.TextViewPosition(doc.GetLocation(offset)), ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop).Y;
                return offsets[offset] = (int)Math.Round(bounds.Top + Math.Clamp(top / editor.Editor.ExtentHeight, 0, 1) * Math.Max(0, bounds.Height - 3));
            }
            foreach (var mark in fixtures)
            {
                var first = doc.GetLineByOffset(mark.Start).LineNumber;
                var last = doc.GetLineByOffset(mark.Start + mark.Length - 1).LineNumber;
                var count = Math.Min(last - first + 1, Math.Max(1, (int)Math.Ceiling(bounds.Height / 3)));
                var firstItem = (ExactPixel(mark.Start), (ReviewKind?)mark.Kind);
                if (seen.Add(firstItem)) expected.Add(firstItem);
                for (var i = 1; i + 1 < count; i++)
                {
                    var line = first + (int)((long)(last - first) * i / (count - 1));
                    var y = bounds.Top + Math.Clamp(tops[line - 1] / editor.Editor.ExtentHeight, 0, 1) * Math.Max(0, bounds.Height - 3);
                    var item = ((int)Math.Round(y), (ReviewKind?)mark.Kind);
                    if (seen.Add(item)) expected.Add(item);
                }
                var lastItem = (ExactPixel(mark.Start + mark.Length - 1), (ReviewKind?)mark.Kind);
                if (seen.Add(lastItem)) expected.Add(lastItem);
            }
            var expectedOrdered = expected.OrderBy(p => p.Kind switch { ReviewKind.SuggestDeletion => 3, ReviewKind.SuggestCorrection => 2, ReviewKind.HumanConfirmed => 1, _ => 0 });
            check(editor.Overview.PaintedMarkers.SequenceEqual(expectedOrdered), "Optimized projections match every reference pixel and paint unresolved states above confirmations");
            check(editor.Overview.PaintedMarkers.TakeWhile(p => p.Kind != ReviewKind.SuggestDeletion).All(p => p.Kind != ReviewKind.SuggestDeletion)
                && editor.Overview.PaintedMarkers.SkipWhile(p => p.Kind != ReviewKind.SuggestDeletion).All(p => p.Kind == ReviewKind.SuggestDeletion),
                "Deletion marks paint last when the same or adjacent compressed rows overlap");
            var view = editor.Editor.TextArea.TextView;
            var rangeRenderer = view.BackgroundRenderers.OfType<ReviewRangeRenderer>().Single();
            editor.Editor.Select(view.VisualLines.First().FirstDocumentLine.Offset + 1, 5); await Paint();
            var referencePixels = RangePixels(view, rangeRenderer, true);
            var optimizedPixels = RangePixels(view, rangeRenderer, false);
            check(referencePixels.SequenceEqual(optimizedPixels), "Duplicate overlapping review ranges preserve every rendered pixel and the selected-text gap");
            var longBuilds = editor.Overview.CacheBuildCount;
            watch.Restart();
            editor.Editor.ScrollToLine(2300); await Paint();
            watch.Stop();
            check(editor.Overview.CacheBuildCount == longBuilds && editor.Editor.VerticalOffset > 0, $"Scrolling many long reviews reuses the cached overview ({watch.ElapsedMilliseconds} ms full frame)");
            editor.ShowReviews = false; await Paint();
            watch.Restart(); editor.Editor.ScrollToLine(1000); await Paint(); watch.Stop();
            check(true, $"Comparison: scrolling with the same reviews hidden takes {watch.ElapsedMilliseconds} ms full frame");
            editor.ShowReviews = true; await Paint();
            source.ReviewMarks.Clear(); await Paint();
            source.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 0, 2); await Paint();
            check(editor.Overview.MarkerCount == 1, "Adding a new review refreshes the overview");
            source.Source = "교체된 원문"; await Paint();
            check(editor.Editor.Text == "교체된 원문" && source.ReviewMarks.All(m => m.IsStale), "Editing text invalidates the old review projection");
            editor.OverviewHighContrast = true; numbers.HighContrast = true;
            check(editor.Overview.HighContrast && numbers.HighContrast, "High contrast reaches both review gutter and overview");
            editor.Editor.ShowLineNumbers = false;
            check(!editor.Editor.ShowLineNumbers, "The line-number visibility setting still works");
        }
        finally { editor.Dispose(); window.Close(); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        check(!numbers.ListeningForSystemChanges && !editor.Overview.ListeningForSystemChanges, "Closing the editor releases Windows high-contrast subscriptions");
        await WrappedLineChecks(check);
        await LineNumberToggleChecks(check);
    }

    private static async Task LineNumberToggleChecks(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 600 }; window.Show();
        try
        {
            var block = window.Session.ActiveBlock!; block.Source = "검토할 첫 줄\n둘째 줄";
            block.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 0, 1);
            window.PageEditor.Refresh();
            window.ApplyAppearance(new EditorAppearance(LineNumberColor: "#123456", LineNumberBackground: "#ABCDEF"), true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var toggle = typeof(MainWindow).GetMethod("ApplyLineNumbers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            window.LineNumbersToggle.IsChecked = false; toggle.Invoke(window, null);
            check(!window.PageEditor.Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().Any(), "The real window hides source line numbers");
            window.LineNumbersToggle.IsChecked = true; toggle.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var restored = window.PageEditor.Editor.TextArea.LeftMargins.OfType<ColoredLineNumbers>().SingleOrDefault();
            check(restored is { Compact: true, CustomBackground: true, CustomNumberColor: true }
                && restored.Background is SolidColorBrush bg && bg.Color == Color.FromRgb(171, 205, 239)
                && restored.NumberBrush is SolidColorBrush ink && ink.Color == Color.FromRgb(18, 52, 86)
                && window.PageEditor.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single().ManualKinds(0).Contains(ReviewKind.SuggestDeletion),
                "Re-enabling line numbers restores the real review gutter and both custom colors");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static async Task WrappedLineChecks(Action<bool, string> check)
    {
        var block = new EditorBlock { Page = 1, Source = new string('가', 4000) };
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
        block.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 3500, 1);
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 3500, 1);
        block.Memos.Add(new("wrapped", null, "TEXT", 0, "", "첫 논리 줄", DateTimeOffset.Now));
        var session = new DocumentSession(); session.Add(block); session.ActiveBlock = block;
        var editor = new ContinuousSourceEditor(); editor.Attach(session);
        var window = new Window { Width = 400, Height = 300, Content = editor, ShowInTaskbar = false }; window.Show();
        try
        {
            async Task Paint() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            await Paint();
            var items = editor.Overview.PaintedMarkers;
            if (!items.Any(p => p.Kind == ReviewKind.HumanConfirmed) || !items.Any(p => p.Kind == ReviewKind.SuggestDeletion) || !items.Any(p => p.Kind is null))
            {
                check(false, $"Wrapped fixture renders all markers (count={editor.Overview.MarkerCount}, painted={items.Count}, extent={editor.Editor.ExtentHeight}, viewport={editor.Editor.ViewportHeight}, track={Descendants(editor.Editor).OfType<ScrollBar>().Count(b => b.IsVisible)}, text={editor.Editor.Text.Length})");
                return;
            }
            var first = items.First(p => p.Kind == ReviewKind.HumanConfirmed).Pixel;
            var last = items.Single(p => p.Kind == ReviewKind.SuggestDeletion).Pixel;
            check(last > first && items.Single(p => p.Kind is null).Pixel == first, "Wrapped single-line reviews follow their actual character rows while the memo uses the logical line start");
            check(items.Last().Kind == ReviewKind.SuggestDeletion, "Overlapping confirmation and deletion leave the deletion colour on top");
            editor.Editor.ScrollToVerticalOffset(editor.Editor.ExtentHeight / 2); await Paint();
            check(editor.Overview.PaintedMarkers.Any(p => p.Kind == ReviewKind.SuggestDeletion && p.Pixel > first), "Scrolling preserves wrapped character review positions");
            window.Width = 600; await Paint();
            items = editor.Overview.PaintedMarkers;
            check(items.Single(p => p.Kind == ReviewKind.SuggestDeletion).Pixel > items.First(p => p.Kind == ReviewKind.HumanConfirmed).Pixel,
                "Resizing recomputes distinct wrapped character positions");
        }
        finally { editor.Dispose(); window.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
    private static Color ColorsValue(SolidColorBrush brush) => brush.Color;
    private static byte[] RangePixels(ICSharpCode.AvalonEdit.Rendering.TextView view, ReviewRangeRenderer renderer, bool reference)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            if (reference) renderer.DrawReferenceForChecks(view, context); else renderer.Draw(view, context);
        }
        var width = Math.Max(1, (int)Math.Ceiling(view.ActualWidth)); var height = Math.Max(1, (int)Math.Ceiling(view.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var data = new byte[width * height * 4]; bitmap.CopyPixels(data, width * 4, 0); return data;
    }
    private static IEnumerable<Color> Colors(Drawing? drawing)
    {
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var color in Colors(child)) yield return color;
        if (drawing is GeometryDrawing geometry)
        {
            if (geometry.Brush is SolidColorBrush fill) yield return fill.Color;
            if (geometry.Pen?.Brush is SolidColorBrush stroke) yield return stroke.Color;
        }
        if (drawing is GlyphRunDrawing glyph && glyph.ForegroundBrush is SolidColorBrush text) yield return text.Color;
    }
}
