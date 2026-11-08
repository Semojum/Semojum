using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Semojum.Desktop.Controls;

internal static class ReviewVisuals
{
    internal static string Label(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => "고침 제안",
        ReviewKind.SuggestDeletion => "지울 제안",
        ReviewKind.HumanConfirmed => "사람이 확인함",
        ReviewKind.OpenWarning => "안 풀린 경고",
        _ => "검토 표시"
    };
    internal static Brush Background(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => new SolidColorBrush(Color.FromRgb(255, 244, 206)),
        ReviewKind.SuggestDeletion or ReviewKind.OpenWarning => new SolidColorBrush(Color.FromRgb(253, 231, 233)),
        _ => new SolidColorBrush(Color.FromRgb(223, 246, 221))
    };
    internal static Brush Ink(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => new SolidColorBrush(Color.FromRgb(157, 93, 0)),
        ReviewKind.SuggestDeletion or ReviewKind.OpenWarning => new SolidColorBrush(Color.FromRgb(196, 43, 28)),
        _ => new SolidColorBrush(Color.FromRgb(15, 123, 15))
    };
    /// <summary>Figma 「안 풀린 경고」 물결밑줄: a 1px zigzag, 2px tall with a 4px period, just above <paramref name="bottom"/>.</summary>
    internal static void Wave(DrawingContext dc, Brush brush, double left, double right, double bottom)
    {
        if (right <= left) return;
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(new Point(left, bottom - .5), false, false);
            var up = true;
            for (var x = left + 2; ; x += 2, up = !up)
            {
                figure.LineTo(new Point(Math.Min(x, right), up ? bottom - 2.5 : bottom - .5), true, true);
                if (x >= right) break;
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(brush, 1), geometry);
    }
    private static readonly Dictionary<ReviewKind, DrawingGroup> icons = [];
    internal static DrawingGroup Icon(ReviewKind kind)
    {
        if (icons.TryGetValue(kind, out var icon)) return icon;
        var name = kind switch { ReviewKind.SuggestCorrection => "draft", ReviewKind.SuggestDeletion => "trash", ReviewKind.OpenWarning => "warning", _ => "resolve" };
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Semojum.Feature.Editor.Wpf;component/Assets/Figma/review-status-{name}.svg"))
            ?? throw new InvalidOperationException($"검토 아이콘을 찾을 수 없습니다: {name}");
        using var stream = resource.Stream;
        icon = new FileSvgReader(new WpfDrawingSettings(), false).Read(stream);
        if (icon.CanFreeze) icon.Freeze();
        icons.Add(kind, icon); return icon;
    }
}

internal sealed class ReviewRangeRenderer(Func<IReadOnlyList<(int Start, ReviewMark Mark)>> ranges, Func<bool> enabled,
    Func<(int Start, int Length)> selection) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Selection;
    public void Draw(TextView view, DrawingContext dc)
    {
        if (!enabled() || !view.VisualLinesValid) return;
        var all = ranges();
        var seen = new HashSet<(int Start, int Length, ReviewKind Kind)>();
        var unique = new List<(int Start, ReviewMark Mark)>();
        // An identical later opaque range completely covers an earlier one.
        // Retaining the last occurrence preserves the order relative to every
        // differently coloured overlapping range and to the selection gap.
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var item = all[i];
            if (!item.Mark.IsStale && item.Mark.Length > 0 && seen.Add((item.Start, item.Mark.Length, item.Mark.Kind))) unique.Add(item);
        }
        unique.Reverse();
        DrawRanges(view, dc, unique, selection());
    }
    internal void DrawReferenceForChecks(TextView view, DrawingContext dc)
    {
        if (!enabled() || !view.VisualLinesValid) return;
        DrawRanges(view, dc, ranges().Where(r => !r.Mark.IsStale && r.Mark.Length > 0), selection());
    }
    private static void DrawRanges(TextView view, DrawingContext dc, IEnumerable<(int Start, ReviewMark Mark)> marks, (int Start, int Length) selected)
    {
        foreach (var (start, mark) in marks)
        {
            var end = start + mark.Length;
            var parts = selected.Length <= 0 || selected.Start >= end || selected.Start + selected.Length <= start
                ? new[] { (Start: start, End: end) }
                : new[] { (Start: start, End: Math.Min(end, selected.Start)), (Start: Math.Max(start, selected.Start + selected.Length), End: end) };
            foreach (var part in parts.Where(p => p.End > p.Start))
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(view, new TextSegment { StartOffset = part.Start, Length = part.End - part.Start }))
            {
                // Snap every edge to whole device pixels. A soft anti-aliased edge grows darker with
                // each identical range drawn over it, so dropping duplicates would not be exact.
                if (!SystemParameters.HighContrast)
                {
                    dc.PushGuidelineSet(new GuidelineSet([rect.Left, rect.Right], [rect.Top, rect.Bottom]));
                    dc.DrawRectangle(ReviewVisuals.Background(mark.Kind), null, rect); dc.Pop();
                }
                if (mark.Kind == ReviewKind.HumanConfirmed) continue;
                var color = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : ReviewVisuals.Ink(mark.Kind);
                if (mark.Kind == ReviewKind.OpenWarning) { ReviewVisuals.Wave(dc, color, rect.Left, rect.Right, rect.Bottom); continue; }
                // A 1px line whose top edge is snapped covers exactly one pixel row: the rect's last row, or its middle.
                var y = mark.Kind == ReviewKind.SuggestCorrection ? rect.Bottom - .5 : rect.Top + rect.Height / 2;
                dc.PushGuidelineSet(new GuidelineSet([rect.Left, rect.Right], [y - .5]));
                dc.DrawLine(new Pen(color, 1), new Point(rect.Left, y), new Point(rect.Right, y)); dc.Pop();
            }
        }
    }
}
