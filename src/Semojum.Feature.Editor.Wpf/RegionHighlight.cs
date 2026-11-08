using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

/// <summary>Presentation only: linked regions never become editable selections.</summary>
internal sealed class RegionHighlight(TextEditor editor, Func<IReadOnlyList<(int Start, int Length)>> ranges) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var (start, length) in ranges())
        {
            var from = Math.Max(start, line.Offset);
            var to = Math.Min(start + length, line.EndOffset);
            if (to <= from) continue;
            ChangeLinePart(from, to, element =>
            {
                element.TextRunProperties.SetBackgroundBrush(editor.TextArea.SelectionBrush);
                element.TextRunProperties.SetForegroundBrush(editor.TextArea.SelectionForeground);
            });
        }
    }
}
