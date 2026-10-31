using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

/// <summary>Display legacy paragraph indent tags without adding spaces to the saved source.</summary>
public sealed class IndentTagGenerator : VisualLineElementGenerator
{
    private int count;
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (startOffset != line.Offset) return -1;
        var source = CurrentContext.Document.GetText(line);
        count = source.StartsWith("<!2칸>", StringComparison.Ordinal) ? 2 : source.StartsWith("<!4칸>", StringComparison.Ordinal) ? 4 : 0;
        return count == 0 ? -1 : startOffset;
    }
    public override VisualLineElement ConstructElement(int offset) => new FormattedTextElement(new string(' ', count), 0);
}
