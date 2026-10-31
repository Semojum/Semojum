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
        _ => "검토 표시"
    };
    internal static Brush Background(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => new SolidColorBrush(Color.FromRgb(255, 244, 206)),
        ReviewKind.SuggestDeletion => new SolidColorBrush(Color.FromRgb(253, 231, 233)),
        _ => new SolidColorBrush(Color.FromRgb(223, 246, 221))
    };
    internal static Brush Ink(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => new SolidColorBrush(Color.FromRgb(157, 93, 0)),
        ReviewKind.SuggestDeletion => new SolidColorBrush(Color.FromRgb(196, 43, 28)),
        _ => new SolidColorBrush(Color.FromRgb(15, 123, 15))
    };
    private static readonly Dictionary<ReviewKind, DrawingGroup> icons = [];
    internal static DrawingGroup Icon(ReviewKind kind)
    {
        if (icons.TryGetValue(kind, out var icon)) return icon;
        var name = kind switch { ReviewKind.SuggestCorrection => "draft", ReviewKind.SuggestDeletion => "trash", _ => "resolve" };
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
                if (!SystemParameters.HighContrast) dc.DrawRectangle(ReviewVisuals.Background(mark.Kind), null, rect);
                if (mark.Kind == ReviewKind.HumanConfirmed) continue;
                var color = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : ReviewVisuals.Ink(mark.Kind);
                var y = mark.Kind == ReviewKind.SuggestCorrection ? rect.Bottom - 1 : rect.Top + rect.Height / 2;
                dc.DrawLine(new Pen(color, 1), new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
    }
}
