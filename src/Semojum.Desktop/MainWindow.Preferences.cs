using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;
public partial class MainWindow
{
    private EditorAppearance appearance = new();
    private readonly DotSpeech dotSpeech = new();
    private readonly Dictionary<TextEditor, WideCaret> wideCarets = [];
    internal void ApplyAppearance(EditorAppearance value)
    {
        AccountPreferenceStore.Validate(new(Appearance: value)); appearance = value;
        Brush C(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        var foreground = value.HighContrast ? Brushes.White : C(value.TextColor);
        var background = value.HighContrast ? Brushes.Black : C(value.BackgroundColor);
        void Apply(TextEditor editor, bool braille)
        {
            var family = new FontFamily { LineSpacing = value.LineSpacing };
            family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Target = braille ? "Segoe UI Symbol" : value.FontFamily });
            editor.FontFamily = family; editor.FontSize = braille ? value.BrailleSize : value.TextSize;
            editor.FontWeight = value.Bold ? FontWeights.Bold : FontWeights.Normal;
            editor.Foreground = braille && !value.HighContrast ? C(value.BrailleColor) : foreground; editor.Background = background;
            editor.TextArea.SelectionBrush = value.HighContrast ? Brushes.Yellow : C(value.SelectionColor);
            editor.TextArea.SelectionForeground = value.HighContrast ? Brushes.Black : C(value.SelectionTextColor);
            editor.TextArea.Caret.CaretBrush = foreground;
            var view = editor.TextArea.TextView;
            view.CurrentLineBackground = value.HighContrast ? C("#202020") : C(value.CurrentLineColor);
            view.NonPrintableCharacterBrush = value.HighContrast ? Brushes.Cyan : C(value.MarkerColor);
            view.ColumnRulerPen = new Pen(value.HighContrast ? Brushes.White : C(value.OutlineColor), value.OutlineWidth);
            editor.Options.ShowSpaces = editor.Options.ShowTabs = editor.Options.ShowEndOfLine = value.ShowFormattingMarks;
            editor.ShowLineNumbers = value.ShowLineNumbers;
            foreach (var margin in editor.TextArea.LeftMargins.OfType<LineNumberMargin>().ToArray())
            {
                var index = editor.TextArea.LeftMargins.IndexOf(margin);
                editor.TextArea.LeftMargins[index] = new ColoredLineNumbers { Background = value.HighContrast ? Brushes.Black : C(value.LineNumberBackground), Margin = margin.Margin };
            }
            editor.LineNumbersForeground = value.HighContrast ? Brushes.White : C(value.LineNumberColor);
            foreach (var colorizer in view.LineTransformers.OfType<TagColorizer>()) colorizer.TagBrush = value.HighContrast ? Brushes.Cyan : C(value.TagColor);
            if (!wideCarets.TryGetValue(editor, out var caret)) { caret = new(editor); wideCarets[editor] = caret; view.BackgroundRenderers.Add(caret); }
            caret.Width = value.CursorWidth; view.Redraw();
        }
        Apply(SourceEditor, false); Apply(Braille.Editor, true); if (detachedPane is not null) Apply(detachedPane.Editor, true);
        Braille.NormalForeground = value.HighContrast ? Brushes.White : C(value.BrailleColor);
        if (detachedPane is not null) detachedPane.NormalForeground = Braille.NormalForeground;
        Braille.RefreshAppearance(); detachedPane?.RefreshAppearance();
        Braille.GridView.HighContrast = value.HighContrast; Braille.GridView.InvalidateFaces();
        if (detachedPane is not null) { detachedPane.GridView.HighContrast = value.HighContrast; detachedPane.GridView.InvalidateFaces(); }
        LineNumbersToggle.IsChecked = value.ShowLineNumbers;
        EditorToolbar.Visibility = value.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        if (!value.SpeakDots) dotSpeech.Stop();
    }
    private async Task EditAppearanceAsync(bool colors)
    {
        var dialog = new AppearanceDialog(appearance, ApplyAppearance, colors) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        accountPreferences = accountPreferences with { Appearance = dialog.Result }; await PersistAccountPreferencesAsync();
    }
    private void SpeakCaret(TextEditor editor)
    {
        if (smoke || !appearance.SpeakDots || !editor.IsKeyboardFocusWithin) return;
        try { if (editor.CaretOffset < editor.Document.TextLength) dotSpeech.Speak(editor.Document.GetCharAt(editor.CaretOffset), appearance.SpeechRate); else dotSpeech.Stop(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { appearance = appearance with { SpeakDots = false }; session.Status = "점형 읽기를 사용할 수 없습니다: " + ex.Message; }
    }
}
