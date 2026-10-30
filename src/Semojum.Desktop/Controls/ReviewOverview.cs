using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

internal readonly record struct ReviewOverviewMarker(int Offset, int Length, ReviewKind? Kind);

/// <summary>
/// Read-only marks over the actual native scroll track. Hit testing stays with the
/// original scrollbar; the highlighted viewport follows its real draggable thumb.
/// </summary>
internal sealed class ReviewOverview : FrameworkElement
{
    private readonly TextEditor editor;
    private readonly Func<IReadOnlyList<ReviewOverviewMarker>> markers;
    private DrawingGroup? cached;
    private Size lastSize;
    private Rect lastTrack;
    private double lastExtent = -1;
    private bool dirty = true;
    private bool highContrast;
    internal int CacheBuildCount { get; private set; }
    internal int MarkerCount { get; private set; }
    internal int VisualLineLookupCount { get; private set; }
    internal long LastBuildElapsedMilliseconds { get; private set; }
    internal IReadOnlyList<(int Pixel, ReviewKind? Kind)> PaintedMarkers { get; private set; } = [];
    internal bool HighContrast { get => highContrast; set { if (highContrast == value) return; highContrast = value; InvalidateMarkers(); } }
    internal Func<bool> SystemHighContrast { get; set; } = () => SystemParameters.HighContrast;
    internal bool ListeningForSystemChanges { get; private set; }

    internal ReviewOverview(TextEditor editor, Func<IReadOnlyList<ReviewOverviewMarker>> markers)
    {
        this.editor = editor; this.markers = markers;
        Loaded += (_, _) => { if (!ListeningForSystemChanges) { SystemParameters.StaticPropertyChanged += SystemChanged; ListeningForSystemChanges = true; } };
        Unloaded += (_, _) => { if (ListeningForSystemChanges) { SystemParameters.StaticPropertyChanged -= SystemChanged; ListeningForSystemChanges = false; } };
        editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => InvalidateVisual();
        editor.TextArea.TextView.VisualLinesChanged += (_, _) => InvalidateVisual();
        editor.SizeChanged += (_, _) => InvalidateMarkers();
        // A wrapped document can acquire its scrollbar only after our first
        // render. Retry once layout has actually created the native track.
        editor.LayoutUpdated += (_, _) =>
        {
            if (cached is null && Descendants(editor).OfType<ScrollBar>().Any(b => b.Orientation == Orientation.Vertical && b.IsVisible && b.Track?.ActualHeight > 0))
                InvalidateVisual();
        };
    }

    internal void InvalidateMarkers() { dirty = true; InvalidateVisual(); }
    internal void SystemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SystemParameters.HighContrast)) InvalidateMarkers(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scroll = Descendants(editor).OfType<ScrollBar>().FirstOrDefault(b => b.Orientation == Orientation.Vertical && b.IsVisible);
        var track = scroll?.Track;
        if (track is null || track.ActualHeight <= 0 || editor.Document is null || editor.ExtentHeight <= 0) return;
        Rect bounds;
        try { bounds = track.TransformToVisual(this).TransformBounds(new Rect(0, 0, track.ActualWidth, track.ActualHeight)); }
        catch (InvalidOperationException) { return; }
        bounds.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
        if (bounds.IsEmpty || bounds.Height <= 0 || bounds.Width <= 0) return;
        if (dirty || cached is null || lastSize != RenderSize || lastTrack != bounds || Math.Abs(lastExtent - editor.ExtentHeight) > .01)
        {
            cached = BuildTrack(bounds); lastSize = RenderSize; lastTrack = bounds; lastExtent = editor.ExtentHeight; dirty = false; CacheBuildCount++;
        }
        dc.DrawDrawing(cached);
        if (track.Thumb is { IsVisible: true } thumb)
        {
            try
            {
                var rect = thumb.TransformToVisual(this).TransformBounds(new Rect(0, 0, thumb.ActualWidth, thumb.ActualHeight));
                rect.Intersect(bounds);
                if (!rect.IsEmpty && rect.Height > 0)
                    dc.DrawRectangle(null, new Pen(SystemHighContrast() ? SystemColors.WindowTextBrush : HighContrast ? Brushes.White : ColorBrush("#D1D1D1"), 1), rect);
            }
            catch (InvalidOperationException) { /* the scrollbar is being re-templated */ }
        }
    }

    private DrawingGroup BuildTrack(Rect bounds)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var group = new DrawingGroup();
        var systemHighContrast = SystemHighContrast();
        var contrast = HighContrast || systemHighContrast;
        var contrastInk = systemHighContrast ? SystemColors.WindowTextBrush : Brushes.White;
        var contrastBackground = systemHighContrast ? SystemColors.WindowBrush : Brushes.Black;
        using (var dc = group.Open())
        {
            dc.PushClip(new RectangleGeometry(bounds));
            dc.DrawRectangle(contrast ? contrastBackground : ColorBrush("#FAFAFA"), null, bounds);
            dc.DrawLine(new Pen(contrast ? contrastInk : ColorBrush("#EAEAEA"), 1), bounds.TopLeft, bounds.BottomLeft);
            var source = markers(); MarkerCount = source.Count;
            var drawn = new HashSet<(int, ReviewKind?)>();
            var pixels = new List<(int, ReviewKind?)>();
            var processedRanges = new HashSet<(int Start, int End, ReviewKind? Kind)>();
            // Many overlapping reviews sample the same visual lines. Project each
            // document line into the track at most once per cached drawing.
            var linePixels = new int[editor.Document.LineCount + 1];
            Array.Fill(linePixels, int.MinValue);
            var offsetPixels = new Dictionary<int, int>();
            VisualLineLookupCount = 0;
            int Pixel(double top) => (int)Math.Round(bounds.Top + Math.Clamp(top / editor.ExtentHeight, 0, 1) * Math.Max(0, bounds.Height - 3));
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
            // Paint unresolved suggestions last, including adjacent 3px marks,
            // so a later confirmation cannot visually erase a correction/deletion.
            var ordered = pixels.OrderBy(p => p.Item2 switch { ReviewKind.SuggestDeletion => 3, ReviewKind.SuggestCorrection => 2, ReviewKind.HumanConfirmed => 1, _ => 0 }).ToArray();
            foreach (var (pixel, kind) in ordered)
            {
                var brush = contrast ? contrastInk : kind is { } review ? ReviewVisuals.Ink(review) : ColorBrush("#005FB8");
                dc.DrawRoundedRectangle(brush, null, new Rect(bounds.Left + 2, pixel, Math.Max(0, Math.Min(8, bounds.Width - 3)), 3), 1, 1);
            }
            PaintedMarkers = ordered;
            dc.Pop();
        }
        if (group.CanFreeze) group.Freeze();
        LastBuildElapsedMilliseconds = timer.ElapsedMilliseconds;
        return group;
    }

    private static Brush ColorBrush(string value) => (Brush)new BrushConverter().ConvertFromInvariantString(value)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        { var child = VisualTreeHelper.GetChild(node, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
