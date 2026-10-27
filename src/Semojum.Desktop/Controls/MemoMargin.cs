using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;

namespace Semojum.Desktop.Controls;
public sealed class MemoMargin(Func<EditorBlock?> active, string panel, Action<int> open) : AbstractMargin
{
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
        return "점역 검토 필요" + (result.Blocked ? " · 점역 차단" : "")
            + string.Concat(result.Dropped.Select(d => $"\n인식하지 못한 문자: {d.Text} ({d.Count}개)"))
            + (result.UnknownTags?.Length > 0 ? "\n알 수 없는 태그: " + string.Join(", ", result.UnknownTags) : "");
    }
    internal string Preview(int line)
    {
        var resolved = ResolveLine?.Invoke(line) ?? (active(), line);
        return !ShowMemos || resolved.Item1 is not { } block ? "" : string.Join("\n\n", block.Memos
        .Where(m => m.Panel == panel && m.LineIndex == resolved.Item2 && !m.Resolved)
        .Select(m => $"{m.Author ?? "작성자 미상"}\n{m.Body}" + string.Concat((m.Replies ?? []).Select(r => $"\n\n{r.Author ?? "작성자 미상"}\n{r.Body}"))));
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
    protected override Size MeasureOverride(Size availableSize) => new(GutterWidth, 0);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        InvalidateVisual();
    }
    private void Changed(object? sender, EventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Background is not null) drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
        if (TextView?.VisualLinesValid != true) return;
        foreach (var line in TextView.VisualLines)
        {
            var memo = Preview(line.FirstDocumentLine.LineNumber - 1).Length > 0;
            var review = ReviewPreview(line.FirstDocumentLine.LineNumber - 1).Length > 0;
            if (!memo && !review) continue;
            var y = line.VisualTop - TextView.VerticalOffset + Math.Min(10, line.Height / 2);
            if (memo) drawingContext.DrawEllipse(Brushes.Gold, new Pen(Brushes.DimGray, 1), new Point(GutterWidth / 2, y), 5, 5);
            if (review) drawingContext.DrawText(new FormattedText("!", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface((FontFamily)Application.Current.FindResource("InterfaceFont"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 12,
                Brushes.Firebrick, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(GutterWidth / 2 - 2, y - 8));
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
            if (ReviewPreview(index).Length > 0 && Preview(index).Length == 0) ReviewRequested?.Invoke(index);
            else if (ShowMemos) open(index);
        }
    }
}
