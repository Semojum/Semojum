using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

/// <summary>Display legacy paragraph indent tags as real spaces without changing the saved source.</summary>
public sealed partial class IndentTagGenerator : VisualLineElementGenerator
{
    private int count, length;
    [GeneratedRegex(@"^<!(\d{1,2})칸>")] private static partial Regex IndentTag();
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.FirstDocumentLine;
        if (startOffset != line.Offset) return -1;
        var match = IndentTag().Match(CurrentContext.Document.GetText(line));
        count = match.Success ? int.Parse(match.Groups[1].Value) : 0; length = match.Length;
        return count == 0 ? -1 : startOffset;
    }
    // Cover the whole <!N칸> tag (documentLength = tag length) with N real spaces.
    public override VisualLineElement ConstructElement(int offset) => new FormattedTextElement(new string(' ', count), length);
}
