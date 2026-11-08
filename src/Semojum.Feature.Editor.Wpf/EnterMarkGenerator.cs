using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

/// <summary>A display-only Enter mark for LF, CRLF and CR; soft wraps are not line breaks.</summary>
public sealed class EnterMarkGenerator(TextEditor editor) : VisualLineElementGenerator
{
    public bool Enabled { get; set; }
    public override int GetFirstInterestedOffset(int startOffset)
    {
        var line = CurrentContext.VisualLine.LastDocumentLine;
        return Enabled && line.DelimiterLength > 0 && startOffset <= line.EndOffset ? line.EndOffset : -1;
    }
    public override VisualLineElement ConstructElement(int offset) => new EnterElement(editor);
    private sealed class EnterElement(TextEditor editor) : FormattedTextElement("↵", 0)
    {
        public override System.Windows.Media.TextFormatting.TextRun CreateTextRun(int startVisualColumn, ITextRunConstructionContext context)
        {
            TextRunProperties.SetForegroundBrush(editor.TextArea.TextView.NonPrintableCharacterBrush);
            TextRunProperties.SetTypeface(new Typeface("Segoe UI Symbol"));
            return base.CreateTextRun(startVisualColumn, context);
        }
    }
    public static void Configure(TextEditor editor, bool enabled)
    {
        var view = editor.TextArea.TextView;
        var generator = view.ElementGenerators.OfType<EnterMarkGenerator>().FirstOrDefault();
        if (generator is null) { generator = new(editor); view.ElementGenerators.Add(generator); }
        generator.Enabled = enabled;
        editor.Options.ShowEndOfLine = false;
        view.Redraw();
    }
}
