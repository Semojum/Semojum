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
    public Brush NumberBrush { get; set; } = Brushes.Gray;
    public bool Compact { get; set; }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (!Compact) { base.OnRender(dc); return; }
        if (TextView?.VisualLinesValid != true) return;
        foreach (var line in TextView.VisualLines)
        {
            var text = new FormattedText(line.FirstDocumentLine.LineNumber.ToString(), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface((FontFamily)Application.Current.FindResource("InterfaceFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 12, NumberBrush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(RenderSize.Width / 2 + 2 - text.Width / 2, line.VisualTop - TextView.VerticalOffset + 14));
        }
    }
}

/// <summary>The 12 DIP space before source text belongs to the current-line highlight.</summary>
public sealed class SourceTextInset : AbstractMargin
{
    private readonly TextEditor editor;
    public Brush LineBrush { get; set; } = Brushes.LightGray;
    public Brush CurrentLineMarkerBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0, 95, 184));
    public SourceTextInset(TextEditor editor)
    {
        this.editor = editor;
        editor.TextArea.Caret.PositionChanged += (_, _) => InvalidateVisual();
    }
    protected override Size MeasureOverride(Size availableSize) => new(12, 0);
    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
    }
    private void Changed(object? sender, EventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        Rect? marker = null;
        if (TextView?.VisualLinesValid == true && editor.Options.HighlightCurrentLine)
        {
            var line = TextView.VisualLines.FirstOrDefault(l => editor.TextArea.Caret.Line >= l.FirstDocumentLine.LineNumber && editor.TextArea.Caret.Line <= l.LastDocumentLine.LineNumber);
            if (line is not null)
            {
                dc.DrawRectangle(TextView.CurrentLineBackground, null, new Rect(0, line.VisualTop - TextView.VerticalOffset, 12, line.Height));
                var top = TextView.GetVisualPosition(editor.TextArea.Caret.Position, VisualYPosition.LineTop).Y - TextView.VerticalOffset;
                var bottom = TextView.GetVisualPosition(editor.TextArea.Caret.Position, VisualYPosition.LineBottom).Y - TextView.VerticalOffset;
                marker = new Rect(0, top, 2, Math.Max(0, bottom - top));
            }
        }
        dc.DrawLine(new Pen(LineBrush, 1), new Point(.5, 0), new Point(.5, ActualHeight));
        if (marker is { } rect) dc.DrawRectangle(CurrentLineMarkerBrush, null, rect);
    }
}
