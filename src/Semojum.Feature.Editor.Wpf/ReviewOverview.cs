using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

internal readonly record struct ReviewOverviewMarker(int Offset, int Length, ReviewKind? Kind);

/// <summary>
/// Figma 훑어보기 띠: an independent 12px column right of the text. The whole document maps
/// onto its height, so it shows review marks and the visible part even without a scrollbar.
/// It replaces the hidden native vertical scrollbar for mouse (click, drag, wheel) and UIA.
/// </summary>
internal sealed class ReviewOverview : FrameworkElement
{
    private readonly TextEditor editor;
    private readonly Func<IReadOnlyList<ReviewOverviewMarker>> markers;
    private DrawingGroup? cached;
    // What the cached drawing was projected from: strip size, document height, wrap width,
    // contrast, and each marker's line/column. Typing that moves none of them keeps the drawing.
    private (double Width, double Height, double Extent, double TextWidth, bool Contrast, bool System) lastLayout;
    private (int FirstLine, int FirstColumn, int LastLine, int LastColumn, ReviewKind? Kind)[] lastAnchors = [];
    private bool rebuildQueued;
    private bool highContrast;
    private double grab;
    internal int CacheBuildCount { get; private set; }
    internal int MarkerCount { get; private set; }
    internal int VisualLineLookupCount { get; private set; }
    internal long LastBuildElapsedMilliseconds { get; private set; }
    internal IReadOnlyList<(int Pixel, ReviewKind? Kind)> PaintedMarkers { get; private set; } = [];
    internal bool HighContrast { get => highContrast; set { if (highContrast == value) return; highContrast = value; InvalidateMarkers(); } }
    internal Func<bool> SystemHighContrast { get; set; } = () => SystemParameters.HighContrast;
    internal bool ListeningForSystemChanges { get; private set; }
    /// <summary>Document height the strip represents. A short document maps onto the viewport so marks sit beside their lines.</summary>
    internal double Extent => Math.Max(editor.ExtentHeight, editor.ViewportHeight);
    internal double ScrollableHeight => Math.Max(0, editor.ExtentHeight - editor.ViewportHeight);

    internal ReviewOverview(TextEditor editor, Func<IReadOnlyList<ReviewOverviewMarker>> markers)
    {
        this.editor = editor; this.markers = markers;
        Width = 12; ClipToBounds = true; Focusable = false;
        AutomationProperties.SetName(this, "훑어보기 띠");
        Loaded += (_, _) => { if (!ListeningForSystemChanges) { SystemParameters.StaticPropertyChanged += SystemChanged; ListeningForSystemChanges = true; } InvalidateMarkers(); };
        Unloaded += (_, _) => { if (ListeningForSystemChanges) { SystemParameters.StaticPropertyChanged -= SystemChanged; ListeningForSystemChanges = false; } };
        // Scrolling only moves the viewport frame; a new document height or viewport moves every mark.
        editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        { if (e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0) InvalidateMarkers(); else InvalidateVisual(); }), true);
        editor.SizeChanged += (_, _) => InvalidateMarkers();
    }

    /// <summary>Redraws the frame now and re-projects the marks after the pending edit and layout have painted.</summary>
    internal void InvalidateMarkers()
    {
        InvalidateVisual();
        if (rebuildQueued) return;
        rebuildQueued = true;
        Dispatcher.BeginInvoke(Rebuild, DispatcherPriority.Background);
    }
    internal void SystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.HighContrast)) InvalidateMarkers(); }
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); InvalidateMarkers(); }

    private void Rebuild()
    {
        rebuildQueued = false;
        if (editor.Document is null || ActualWidth <= 0 || ActualHeight <= 0 || Extent <= 0) return;
        var source = markers(); MarkerCount = source.Count;
        var system = SystemHighContrast(); var contrast = HighContrast || system;
        var layout = (ActualWidth, ActualHeight, Extent, editor.TextArea.TextView.ActualWidth, contrast, system);
        var anchors = Anchors(source);
        if (cached is not null && layout == lastLayout && anchors.AsSpan().SequenceEqual(lastAnchors)) return;
        cached = BuildTrack(source, contrast, system ? SystemColors.WindowTextBrush : Brushes.White);
        lastLayout = layout; lastAnchors = anchors; CacheBuildCount++;
        InvalidateVisual();
    }

    private (int, int, int, int, ReviewKind?)[] Anchors(IReadOnlyList<ReviewOverviewMarker> source)
    {
        var document = editor.Document; var result = new (int, int, int, int, ReviewKind?)[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            var marker = source[i];
            if (marker.Offset < 0 || marker.Offset > document.TextLength) { result[i] = (-1, marker.Offset, -1, marker.Length, marker.Kind); continue; }
            var first = document.GetLineByOffset(marker.Offset);
            if (marker.Kind is null) { result[i] = (first.LineNumber, 0, first.LineNumber, 0, null); continue; }
            var end = (int)Math.Min(document.TextLength, (long)marker.Offset + Math.Max(1, marker.Length) - 1);
            var last = document.GetLineByOffset(end);
            result[i] = (first.LineNumber, marker.Offset - first.Offset, last.LineNumber, end - last.Offset, marker.Kind);
        }
        return result;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var system = SystemHighContrast(); var contrast = HighContrast || system;
        var contrastInk = system ? SystemColors.WindowTextBrush : Brushes.White;
        dc.DrawRectangle(contrast ? system ? SystemColors.WindowBrush : Brushes.Black : Fill, null, new Rect(RenderSize));
        dc.DrawRectangle(contrast ? contrastInk : Line, null, new Rect(0, 0, 1, ActualHeight));
        if (cached is not null) dc.DrawDrawing(cached);
        // Figma '보는 구간': the visible part of the document, drawn only when there is more to scroll to.
        if (ViewportRect() is { } view)
            dc.DrawRectangle(null, new Pen(contrast ? contrastInk : Frame, 1), new Rect(view.X + .5, view.Y + .5, Math.Max(0, view.Width - 1), Math.Max(0, view.Height - 1)));
    }

    internal Rect? ViewportRect()
    {
        var extent = Extent;
        if (ActualHeight <= 0 || extent <= 0 || ScrollableHeight < 1) return null;
        var height = Math.Max(4, editor.ViewportHeight / extent * ActualHeight);
        return new Rect(0, Math.Clamp(editor.VerticalOffset / extent * ActualHeight, 0, Math.Max(0, ActualHeight - height)), ActualWidth, height);
    }

    /// <summary>A press on the frame drags it like a scrollbar thumb; elsewhere it centres the view on that part of the document.</summary>
    internal void JumpTo(double y)
    {
        var point = y / Math.Max(1, ActualHeight) * Extent;
        grab = ViewportRect() is { } view && y >= view.Top && y <= view.Bottom ? point - editor.VerticalOffset : editor.ViewportHeight / 2;
        DragTo(y);
    }
    internal void DragTo(double y) => editor.ScrollToVerticalOffset(Math.Clamp(y / Math.Max(1, ActualHeight) * Extent - grab, 0, ScrollableHeight));

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse(); JumpTo(e.GetPosition(this).Y); e.Handled = true;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) DragTo(e.GetPosition(this).Y);
    }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var step = SystemParameters.WheelScrollLines is > 0 and var lines ? lines * editor.TextArea.TextView.DefaultLineHeight : editor.ViewportHeight;
        editor.ScrollToVerticalOffset(Math.Clamp(editor.VerticalOffset - e.Delta / 120d * step, 0, ScrollableHeight)); e.Handled = true;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new OverviewAutomationPeer(this);

    private DrawingGroup BuildTrack(IReadOnlyList<ReviewOverviewMarker> source, bool contrast, Brush contrastInk)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var group = new DrawingGroup();
        var bounds = new Rect(RenderSize); var extent = Extent;
        using (var dc = group.Open())
        {
            var drawn = new HashSet<(int, ReviewKind?)>();
            var pixels = new List<(int, ReviewKind?)>();
            var processedRanges = new HashSet<(int Start, int End, ReviewKind? Kind)>();
            // Many overlapping reviews sample the same visual lines. Project each
            // document line into the strip at most once per cached drawing.
            var linePixels = new int[editor.Document.LineCount + 1];
            Array.Fill(linePixels, int.MinValue);
            var offsetPixels = new Dictionary<int, int>();
            VisualLineLookupCount = 0;
            int Pixel(double top) => (int)Math.Round(Math.Clamp(top / extent, 0, 1) * Math.Max(0, bounds.Height - 3));
            int LinePixel(int line)
            {
                if (linePixels[line] != int.MinValue) return linePixels[line];
                VisualLineLookupCount++;
                return linePixels[line] = Pixel(editor.TextArea.TextView.GetVisualTopByDocumentLine(line));
            }
            int OffsetPixel(int offset)
            {
                if (offsetPixels.TryGetValue(offset, out var pixel)) return pixel;
                VisualLineLookupCount++;
                var position = new TextViewPosition(editor.Document.GetLocation(offset));
                return offsetPixels[offset] = Pixel(editor.TextArea.TextView.GetVisualPosition(position, VisualYPosition.LineTop).Y);
            }
            void Add(int pixel, ReviewKind? kind)
            { if (drawn.Add((pixel, kind))) pixels.Add((pixel, kind)); }
            foreach (var marker in source)
            {
                if (marker.Offset < 0 || marker.Offset > editor.Document.TextLength) continue;
                var first = editor.Document.GetLineByOffset(marker.Offset).LineNumber;
                var end = Math.Min(editor.Document.TextLength, (long)marker.Offset + Math.Max(1, marker.Length) - 1);
                var last = editor.Document.GetLineByOffset((int)end).LineNumber;
                if (!processedRanges.Add((marker.Offset, (int)end, marker.Kind))) continue;
                // Memos specify logical lines. Manual annotations specify exact
                // characters, which may lie well below the first wrapped row.
                if (marker.Kind is null) { Add(LinePixel(first), null); continue; }
                Add(OffsetPixel(marker.Offset), marker.Kind);
                var count = Math.Min(last - first + 1, Math.Max(1, (int)Math.Ceiling(bounds.Height / 3)));
                for (var i = 1; i + 1 < count; i++)
                {
                    var line = first + (int)((long)(last - first) * i / (count - 1));
                    Add(LinePixel(line), marker.Kind);
                }
                Add(OffsetPixel((int)end), marker.Kind);
            }
            // Paint server warnings and unresolved suggestions last, including adjacent 3px marks,
            // so a later confirmation cannot visually erase a warning, correction or deletion.
            var ordered = pixels.OrderBy(p => p.Item2 switch { ReviewKind.OpenWarning => 4, ReviewKind.SuggestDeletion => 3, ReviewKind.SuggestCorrection => 2, ReviewKind.HumanConfirmed => 1, _ => 0 }).ToArray();
            foreach (var (pixel, kind) in ordered)
            {
                var brush = contrast ? contrastInk : kind is { } review ? ReviewVisuals.Ink(review) : Memo;
                dc.DrawRoundedRectangle(brush, null, new Rect(2, pixel, Math.Max(0, Math.Min(8, bounds.Width - 3)), 3), 1, 1);
            }
            PaintedMarkers = ordered;
        }
        if (group.CanFreeze) group.Freeze();
        LastBuildElapsedMilliseconds = timer.ElapsedMilliseconds;
        return group;
    }

    // Figma 263:6934 fill, 세로선, 보는 구간 and 자리 note colours.
    private static readonly Brush Fill = Frozen("#FAFAFA"), Line = Frozen("#EAEAEA"), Frame = Frozen("#D1D1D1"), Memo = Frozen("#005FB8");
    private static Brush Frozen(string value) { var brush = (Brush)new BrushConverter().ConvertFromInvariantString(value)!; brush.Freeze(); return brush; }

    /// <summary>The hidden native scrollbar's UIA role: a vertical scrollbar whose value is the editor's scroll offset.</summary>
    private sealed class OverviewAutomationPeer(ReviewOverview owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        private ReviewOverview Strip => (ReviewOverview)Owner;
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ScrollBar;
        protected override string GetClassNameCore() => nameof(ReviewOverview);
        protected override AutomationOrientation GetOrientationCore() => AutomationOrientation.Vertical;
        protected override bool IsContentElementCore() => false;
        public override object GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);
        public double Value => Strip.editor.VerticalOffset;
        public double Minimum => 0;
        public double Maximum => Strip.ScrollableHeight;
        public double SmallChange => Strip.editor.TextArea.TextView.DefaultLineHeight;
        public double LargeChange => Strip.editor.ViewportHeight;
        public bool IsReadOnly => !IsEnabled();
        public void SetValue(double value)
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            if (double.IsNaN(value) || value < Minimum || value > Maximum) throw new ArgumentOutOfRangeException(nameof(value));
            Strip.editor.ScrollToVerticalOffset(value);
        }
    }
}
