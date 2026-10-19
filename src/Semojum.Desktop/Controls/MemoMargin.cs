using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;

namespace Semojum.Desktop.Controls;
public sealed class MemoMargin(Func<EditorBlock?> active, string panel, Action<int> open) : AbstractMargin
{
    protected override Size MeasureOverride(Size availableSize) => new(16, 0);
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
        if (TextView?.VisualLinesValid != true || active() is not { } block) return;
        foreach (var line in TextView.VisualLines)
        {
            var memos = block.Memos.Where(m => m.Panel == panel && m.LineIndex == line.FirstDocumentLine.LineNumber - 1).ToArray();
            if (memos.Length == 0) continue;
            var y = line.VisualTop - TextView.VerticalOffset + Math.Min(10, line.Height / 2);
            drawingContext.DrawEllipse(memos.All(m => m.Resolved) ? Brushes.LightGray : Brushes.Gold, new Pen(Brushes.DimGray, 1), new Point(8, y), 5, 5);
        }
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (TextView?.VisualLinesValid != true) return;
        var y = e.GetPosition(this).Y + TextView.VerticalOffset;
        var line = TextView.VisualLines.FirstOrDefault(l => y >= l.VisualTop && y < l.VisualTop + l.Height);
        if (line is not null) { e.Handled = true; open(line.FirstDocumentLine.LineNumber - 1); }
    }
}
