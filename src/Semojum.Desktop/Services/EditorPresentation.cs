using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;

namespace Semojum.Desktop.Services;

// Supplement the native caret without replacing its IME/accessibility position.
public sealed class WideCaret : IBackgroundRenderer
{
    private readonly TextEditor editor;
    public double Width { get; set; } = 2;
    public KnownLayer Layer => KnownLayer.Caret;
    public WideCaret(TextEditor editor)
    {
        this.editor = editor;
        editor.TextArea.Caret.PositionChanged += (_, _) => editor.TextArea.TextView.InvalidateLayer(Layer);
        editor.IsKeyboardFocusWithinChanged += (_, _) => editor.TextArea.TextView.InvalidateLayer(Layer);
    }
    public void Draw(TextView view, DrawingContext context)
    {
        if (!editor.IsKeyboardFocusWithin || !view.VisualLinesValid || editor.Document is null) return;
        var rect = editor.TextArea.Caret.CalculateCaretRectangle();
        if (rect.IsEmpty) return;
        rect.Offset(-view.HorizontalOffset, -view.VerticalOffset); rect.Width = Width;
        context.DrawRectangle(editor.Foreground, null, rect);
    }
}
public sealed class DotSpeech : IDisposable
{
    private object? voice;
    public void Speak(char cell, int rate)
    {
        var bits = cell - 0x2800;
        if (bits is < 0 or > 255) { Stop(); return; }
        var text = bits == 0 ? "빈칸" : string.Join(" ", Enumerable.Range(1, 8).Where(i => (bits & (1 << (i - 1))) != 0)) + " 점";
        voice ??= Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice") ?? throw new InvalidOperationException("Windows 음성을 사용할 수 없습니다."));
        dynamic speaker = voice!; speaker.Rate = rate; speaker.Speak(text, 3); // async + purge queued speech
    }
    public void Stop() { if (voice is not null) { dynamic speaker = voice; speaker.Speak("", 3); } }
    public void Dispose() { if (voice is null) return; Stop(); Marshal.FinalReleaseComObject(voice); voice = null; }
}
public sealed class ColoredLineNumbers : LineNumberMargin
{
    public Brush Background { get; set; } = Brushes.WhiteSmoke;
    protected override void OnRender(DrawingContext dc) { dc.DrawRectangle(Background, null, new Rect(RenderSize)); base.OnRender(dc); }
}
