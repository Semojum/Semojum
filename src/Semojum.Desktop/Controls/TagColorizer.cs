using System.Text.RegularExpressions;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

public sealed partial class TagColorizer : DocumentColorizingTransformer
{
    private static readonly Brush TagBrush = new SolidColorBrush(Color.FromRgb(45, 96, 158));
    [GeneratedRegex(@"<![^>]*>")] private static partial Regex Tags();
    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        foreach (Match match in Tags().Matches(text))
            ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length,
                element => element.TextRunProperties.SetForegroundBrush(TagBrush));
    }
}
