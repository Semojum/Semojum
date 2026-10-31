using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Threading;
using System.Runtime.CompilerServices;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Semojum.Desktop.Controls;
public sealed class MemoMargin(Func<EditorBlock?> active, string panel, Action<int> open) : AbstractMargin
{
    private static readonly Lazy<DrawingGroup> MemoIcon = new(() => LoadIcon("review-status-memo.svg"));
    private static readonly Lazy<DrawingGroup> WarningIcon = new(() => LoadIcon("review-status-warning.svg"));
    private int markerSlots;
    private bool layoutUpdateQueued;
    private sealed class LineOffsetsCache
    {
        internal long Revision = -1;
        internal int[] Offsets = [];
    }
    private readonly ConditionalWeakTable<EditorBlock, LineOffsetsCache> lineOffsets = new();
    internal int LineOffsetsBuildCount { get; private set; }
    internal static DrawingGroup MemoDrawing => MemoIcon.Value;
    internal static DrawingGroup WarningDrawing => WarningIcon.Value;
    private static DrawingGroup LoadIcon(string name)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Semojum.Feature.Editor.Wpf;component/Assets/Figma/{name}"))
            ?? throw new InvalidOperationException($"Figma 아이콘을 불러올 수 없습니다: {name}");
        using var stream = resource.Stream;
        var drawing = new FileSvgReader(new WpfDrawingSettings(), false).Read(stream);
        if (drawing.CanFreeze) drawing.Freeze();
        return drawing;
    }
    public Func<int, (EditorBlock? Block, int Line)>? ResolveLine { get; init; }
    public double GutterWidth { get; set; } = 16;
    public double SlotPadding { get; set; }
    public Brush? Background { get; set; }
    public bool ShowMemos { get; set; } = true;
    public bool ShowReviews { get; set; } = true;
    public Action<int>? ReviewRequested { get; init; }
    public Action? ManualReviewRequested { get; init; }
    internal ReviewKind[] ManualKinds(int line)
    {
        if (!ShowReviews) return [];
        var resolved = ResolveLine?.Invoke(line) ?? (active(), line);
        return ManualKinds(resolved);
    }
    private ReviewKind[] ManualKinds((EditorBlock? Block, int Line) resolved)
    {
        if (!ShowReviews || resolved.Block is not { } block) return [];
        var target = panel == "TEXT" ? ReviewPanel.Text : ReviewPanel.Braille;
        if (!block.ReviewMarks.Any(m => !m.IsStale && m.Panel == target)) return [];
        var text = panel == "TEXT" ? block.Source : block.Braille;
        var revision = panel == "TEXT" ? block.SourceRevision : block.BrailleRevision;
        var cached = lineOffsets.GetValue(block, _ => new LineOffsetsCache());
        if (cached.Revision != revision)
        {
            cached.Offsets = DocumentNavigation.LineOffsets(text);
            cached.Revision = revision;
            LineOffsetsBuildCount++;
        }
        var offsets = cached.Offsets;
        if (resolved.Line < 0 || resolved.Line >= offsets.Length) return [];
        var start = offsets[resolved.Line]; var end = resolved.Line + 1 < offsets.Length ? offsets[resolved.Line + 1] : text.Length;
        return block.ReviewMarks.Where(m => !m.IsStale && m.Panel == target && m.Start < end && start < m.Start + m.Length)
            .Select(m => m.Kind).Distinct().ToArray();
    }
    internal string ReviewPreview(int line)
    {
        var resolved = ResolveLine?.Invoke(line) ?? (active(), line);
        return ReviewPreview(resolved);
    }
    private string ReviewPreview((EditorBlock? Block, int Line) resolved)
    {
        if (!ShowReviews || resolved.Line != 0 || resolved.Block?.Translation is not { NeedsReview: true } result) return "";
        return DescribeReview(result);
    }
    internal static string DescribeReview(BrailleTranslation result)
    {
        return "점역 검토 필요" + (result.Blocked ? " · 점역 차단" : "")
            + string.Concat(result.Dropped.Select(d => $"\n인식하지 못한 문자: {d.Text} ({d.Count}개)"))
            + (result.UnknownTags?.Length > 0 ? "\n알 수 없는 태그: " + string.Join(", ", result.UnknownTags) : "");
    }
    internal string Preview(int line)
    {
        var resolved = ResolveLine?.Invoke(line) ?? (active(), line);
        return Preview(resolved);
    }
    private string Preview((EditorBlock? Block, int Line) resolved)
    {
        var text = !ShowMemos || resolved.Block is not { } block ? "" : string.Join("\n\n", block.Memos
        .Where(m => m.Panel == panel && m.LineIndex == resolved.Line && !m.Resolved)
        .Select(m => $"{m.Author ?? "작성자 미상"}\n{m.Body}" + string.Concat((m.Replies ?? []).Select(r => $"\n\n{r.Author ?? "작성자 미상"}\n{r.Body}"))));
        return text.Length == 0 ? "" : "메모\n" + text;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TextView?.VisualLinesValid != true) return;
        var y = e.GetPosition(this).Y + TextView.VerticalOffset;
        var line = TextView.VisualLines.FirstOrDefault(l => y >= l.VisualTop && y < l.VisualTop + l.Height);
        var text = line is null ? "" : string.Join("\n\n", new[] { Preview(line.FirstDocumentLine.LineNumber - 1), ReviewPreview(line.FirstDocumentLine.LineNumber - 1),
            string.Join("\n", ManualKinds(line.FirstDocumentLine.LineNumber - 1).Select(ReviewVisuals.Label)) }.Where(t => t.Length > 0));
        if (ToolTip is ToolTip current && current.Tag as string == text) return;
        if (ToolTip is ToolTip old) old.IsOpen = false;
        ToolTip = text.Length == 0 ? null : new ToolTip { Tag = text, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 } };
        ToolTipService.SetInitialShowDelay(this, 150);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    { if (ToolTip is ToolTip tip) tip.IsOpen = false; base.OnMouseLeave(e); }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(GutterWidth, markerSlots * 16 + SlotPadding), 0);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        AutomationProperties.SetName(this, panel == "TEXT" ? "원문 메모와 점역 검토 표시" : "점자 메모와 점역 검토 표시");
        AutomationProperties.SetHelpText(this, "줄 옆의 메모와 검토 표시입니다. 세 검토 상태는 편집 메뉴의 검토 표시 목록에서도 확인할 수 있습니다.");
        InvalidateVisual();
    }
    private void Changed(object? sender, EventArgs e) => InvalidateVisual();
    internal bool IsReviewHit(int line, double x) => ReviewPreview(line).Length > 0 &&
        x >= SlotPadding && (int)((x - SlotPadding) / 16) == (Preview(line).Length > 0 ? 1 : 0);
    internal void ActivateLine(int line, double x)
    {
        if (x < SlotPadding) return;
        var memo = Preview(line).Length > 0; var warning = ReviewPreview(line).Length > 0; var slot = (int)((x - SlotPadding) / 16);
        if (memo && slot == 0) open(line);
        else if (IsReviewHit(line, x)) ReviewRequested?.Invoke(line);
        else if (slot >= (memo ? 1 : 0) + (warning ? 1 : 0) && slot < (memo ? 1 : 0) + (warning ? 1 : 0) + ManualKinds(line).Length) ManualReviewRequested?.Invoke();
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Background is not null) drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        if (TextView?.VisualLinesValid != true) return;
        var rows = TextView.VisualLines.Select(line =>
        {
            var index = line.FirstDocumentLine.LineNumber - 1;
            var resolved = ResolveLine?.Invoke(index) ?? (active(), index);
            return (Line: line, Memo: Preview(resolved).Length > 0, Review: ReviewPreview(resolved).Length > 0, Kinds: ManualKinds(resolved));
        }).ToArray();
        var slots = rows.Select(row => (row.Memo ? 1 : 0) + (row.Review ? 1 : 0) + row.Kinds.Length).DefaultIfEmpty(0).Max();
        if (markerSlots != slots && !layoutUpdateQueued)
        {
            layoutUpdateQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { layoutUpdateQueued = false; markerSlots = slots; InvalidateMeasure(); InvalidateVisual(); }));
        }
        foreach (var row in rows)
        {
            var line = row.Line; var memo = row.Memo; var review = row.Review; var kinds = row.Kinds;
            if (!memo && !review && kinds.Length == 0) continue;
            var y = line.VisualTop - TextView.VerticalOffset + (line.Height - 16) / 2;
            void Icon(Drawing drawing, double x) { drawingContext.PushTransform(new TranslateTransform(x, y)); drawingContext.DrawDrawing(drawing); drawingContext.Pop(); }
            if (memo) Icon(MemoDrawing, SlotPadding);
            if (review) Icon(WarningDrawing, SlotPadding + (memo ? 16 : 0));
            var x = SlotPadding + ((memo ? 1 : 0) + (review ? 1 : 0)) * 16;
            foreach (var kind in kinds) { Icon(ReviewVisuals.Icon(kind), x); x += 16; }
            if (kinds.Length > 0) drawingContext.DrawRectangle(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : ReviewVisuals.Ink(kinds[^1]), null,
                new Rect(0, line.VisualTop - TextView.VerticalOffset, 2, line.Height));
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (TextView?.VisualLinesValid != true) return;
        var y = e.GetPosition(this).Y + TextView.VerticalOffset;
        var line = TextView.VisualLines.FirstOrDefault(l => y >= l.VisualTop && y < l.VisualTop + l.Height);
        if (line is not null)
        {
            var index = line.FirstDocumentLine.LineNumber - 1; e.Handled = true;
            ActivateLine(index, e.GetPosition(this).X);
        }
    }
}
