using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Controls;

/// <summary>Readable space markers; each displayed bullet still represents one original space.</summary>
public sealed class SpaceMarkGenerator(TextEditor editor) : VisualLineElementGenerator
{
    public bool Enabled { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (!Enabled) return -1;
        var end = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        for (var offset = startOffset; offset < end; offset++)
            if (CurrentContext.Document.GetCharAt(offset) == ' ') return offset;
        return -1;
    }

    public override VisualLineElement ConstructElement(int offset) => new SpaceElement(editor);

    private sealed class SpaceElement(TextEditor editor) : FormattedTextElement("•", 1)
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
        var generator = view.ElementGenerators.OfType<SpaceMarkGenerator>().FirstOrDefault();
        if (generator is null) { generator = new(editor); view.ElementGenerators.Add(generator); }
        generator.Enabled = enabled;
        editor.Options.ShowSpaces = false;
        view.Redraw();
    }
}
