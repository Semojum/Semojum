using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Threading;
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
    internal static DrawingGroup MemoDrawing => MemoIcon.Value;
    internal static DrawingGroup WarningDrawing => WarningIcon.Value;
    private static DrawingGroup LoadIcon(string name)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}"))
            ?? throw new InvalidOperationException($"Figma 아이콘을 불러올 수 없습니다: {name}");
        using var stream = resource.Stream;
        var drawing = new FileSvgReader(new WpfDrawingSettings(), false).Read(stream);
        if (drawing.CanFreeze) drawing.Freeze();
        return drawing;
    }
    public Func<int, (EditorBlock? Block, int Line)>? ResolveLine { get; init; }
    public double GutterWidth { get; set; } = 16;
    public Brush? Background { get; set; }
    public bool ShowMemos { get; set; } = true;
    public bool ShowReviews { get; set; } = true;
    public Action<int>? ReviewRequested { get; init; }
    internal string ReviewPreview(int line)
    {
        var resolved = ResolveLine?.Invoke(line) ?? (active(), line);
        if (!ShowReviews || resolved.Item2 != 0 || resolved.Item1?.Translation is not { NeedsReview: true } result) return "";
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
        var text = !ShowMemos || resolved.Item1 is not { } block ? "" : string.Join("\n\n", block.Memos
        .Where(m => m.Panel == panel && m.LineIndex == resolved.Item2 && !m.Resolved)
        .Select(m => $"{m.Author ?? "작성자 미상"}\n{m.Body}" + string.Concat((m.Replies ?? []).Select(r => $"\n\n{r.Author ?? "작성자 미상"}\n{r.Body}"))));
        return text.Length == 0 ? "" : "메모\n" + text;
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (TextView?.VisualLinesValid != true) return;
        var y = e.GetPosition(this).Y + TextView.VerticalOffset;
        var line = TextView.VisualLines.FirstOrDefault(l => y >= l.VisualTop && y < l.VisualTop + l.Height);
        var text = line is null ? "" : string.Join("\n\n", new[] { Preview(line.FirstDocumentLine.LineNumber - 1), ReviewPreview(line.FirstDocumentLine.LineNumber - 1) }.Where(t => t.Length > 0));
        if (ToolTip is ToolTip current && current.Tag as string == text) return;
        if (ToolTip is ToolTip old) old.IsOpen = false;
        ToolTip = text.Length == 0 ? null : new ToolTip { Tag = text, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 } };
        ToolTipService.SetInitialShowDelay(this, 150);
    }
    protected override void OnMouseLeave(MouseEventArgs e)
    { if (ToolTip is ToolTip tip) tip.IsOpen = false; base.OnMouseLeave(e); }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Max(GutterWidth, markerSlots * 16), 0);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        AutomationProperties.SetName(this, panel == "TEXT" ? "원문 메모와 점역 검토 표시" : "점자 메모와 점역 검토 표시");
        AutomationProperties.SetHelpText(this, "줄 옆의 메모와 점역 검토 표시입니다. 해당 줄에 마우스를 올리면 상세 내용을 볼 수 있습니다.");
        InvalidateVisual();
    }
    private void Changed(object? sender, EventArgs e) => InvalidateVisual();
    internal bool IsReviewHit(int line, double x) => ReviewPreview(line).Length > 0 &&
        (Preview(line).Length == 0 || (markerSlots == 2 && x >= 16));
    internal void ActivateLine(int line, double x)
    {
        if (IsReviewHit(line, x)) ReviewRequested?.Invoke(line);
        else if (ShowMemos) open(line);
    }
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Background is not null) drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        if (TextView?.VisualLinesValid != true) return;
        var slots = TextView.VisualLines.Select(line => (Preview(line.FirstDocumentLine.LineNumber - 1).Length > 0 ? 1 : 0) + (ReviewPreview(line.FirstDocumentLine.LineNumber - 1).Length > 0 ? 1 : 0)).DefaultIfEmpty(0).Max();
        if (markerSlots != slots && !layoutUpdateQueued)
        {
            layoutUpdateQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { layoutUpdateQueued = false; markerSlots = slots; InvalidateMeasure(); InvalidateVisual(); }));
        }
        foreach (var line in TextView.VisualLines)
        {
            var memo = Preview(line.FirstDocumentLine.LineNumber - 1).Length > 0;
            var review = ReviewPreview(line.FirstDocumentLine.LineNumber - 1).Length > 0;
            if (!memo && !review) continue;
            var y = line.VisualTop - TextView.VerticalOffset + (line.Height - 16) / 2;
            void Icon(Drawing drawing, double x) { drawingContext.PushTransform(new TranslateTransform(x, y)); drawingContext.DrawDrawing(drawing); drawingContext.Pop(); }
            if (memo) Icon(MemoDrawing, 0);
            if (review && (!memo || markerSlots == 2)) Icon(WarningDrawing, memo ? 16 : 0);
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
