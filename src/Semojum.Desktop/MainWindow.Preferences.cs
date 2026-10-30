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
    internal void ApplyAppearance(EditorAppearance value) => ApplyAppearance(value, explicitAppearance: false);
    internal void ApplyAppearance(EditorAppearance value, bool explicitAppearance)
    {
        AccountPreferenceStore.Validate(new(Appearance: value)); appearance = value;
        Brush C(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        var customLine = explicitAppearance || accountPreferences.Appearance is not null || !value.CurrentLineColor.Equals(new EditorAppearance().CurrentLineColor, StringComparison.OrdinalIgnoreCase);
        var gridLine = C(customLine ? value.CurrentLineColor : AppearanceDialog.DefaultCurrentLineColor);
        var foreground = value.HighContrast ? Brushes.White : C(value.TextColor);
        var background = value.HighContrast ? Brushes.Black : C(value.BackgroundColor);
        void Apply(TextEditor editor, bool braille)
        {
            // Figma uses a 28 DIP baseline for both 14 DIP source and 22 DIP braille.
            var natural = braille ? new FontFamily("Segoe UI Symbol") : value.FontFamily == "Noto Sans KR" ? (FontFamily)FindResource("InterfaceFont") : new FontFamily(value.FontFamily);
            var spacing = value.LineSpacing / 1.3 * (braille ? 28d / 22 : 2);
            var family = new FontFamily { LineSpacing = spacing, Baseline = natural.Baseline + (spacing - natural.LineSpacing) / 2 };
            family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Target = natural.Source });
            editor.FontFamily = family; editor.FontSize = braille ? value.BrailleSize : value.TextSize;
            editor.FontWeight = value.Bold ? FontWeights.Bold : FontWeights.Normal;
            editor.Foreground = braille && !value.HighContrast ? C(value.BrailleColor) : foreground; editor.Background = background;
            editor.TextArea.SelectionBrush = value.HighContrast ? Brushes.Yellow : C(value.SelectionColor);
            editor.TextArea.SelectionForeground = value.HighContrast ? Brushes.Black : C(value.SelectionTextColor);
            editor.TextArea.Caret.CaretBrush = foreground;
            var view = editor.TextArea.TextView;
            view.CurrentLineBackground = value.HighContrast ? C("#202020") : gridLine;
            view.CurrentLineBorder = null;
            view.NonPrintableCharacterBrush = value.HighContrast ? Brushes.Cyan : C(value.MarkerColor);
            view.ColumnRulerPen = braille ? new Pen(value.HighContrast ? Brushes.White : C(value.OutlineColor), value.OutlineWidth) : null;
            editor.Options.ShowSpaces = editor.Options.ShowTabs = editor.Options.ShowEndOfLine = value.ShowFormattingMarks;
            editor.ShowLineNumbers = value.ShowLineNumbers;
            ApplyLineNumberAppearance(editor, value);
            if (editor == PageEditor.Editor)
            {
                PageEditor.OverviewHighContrast = value.HighContrast;
                foreach (var memo in editor.TextArea.LeftMargins.OfType<MemoMargin>()) { memo.GutterWidth = 12; memo.Background = value.HighContrast ? Brushes.Black : C(value.LineNumberBackground); memo.InvalidateMeasure(); }
                foreach (var separator in editor.TextArea.LeftMargins.OfType<System.Windows.Shapes.Line>().ToArray())
                {
                    editor.TextArea.LeftMargins.Remove(separator);
                }
                if (!editor.TextArea.LeftMargins.OfType<SourceTextInset>().Any()) editor.TextArea.LeftMargins.Add(new SourceTextInset(editor));
                foreach (var inset in editor.TextArea.LeftMargins.OfType<SourceTextInset>()) { inset.LineBrush = value.HighContrast ? Brushes.White : C("#D6D6D6"); inset.CurrentLineMarkerBrush = value.HighContrast ? Brushes.White : C("#005FB8"); inset.InvalidateVisual(); }
            }
            editor.LineNumbersForeground = value.HighContrast ? Brushes.White : C(value.LineNumberColor);
            foreach (var colorizer in view.LineTransformers.OfType<TagColorizer>()) colorizer.TagBrush = value.HighContrast ? Brushes.Cyan : C(value.TagColor);
            if (!wideCarets.TryGetValue(editor, out var caret)) { caret = new(editor); wideCarets[editor] = caret; view.BackgroundRenderers.Add(caret); }
            caret.Width = value.CursorWidth; view.Redraw();
        }
        Apply(SourceEditor, false); Apply(PageEditor.Editor, false); Apply(Braille.Editor, true); if (detachedPane is not null) Apply(detachedPane.Editor, true);
        Braille.NormalForeground = value.HighContrast ? Brushes.White : C(value.BrailleColor);
        if (detachedPane is not null) detachedPane.NormalForeground = Braille.NormalForeground;
        Braille.RefreshAppearance(); detachedPane?.RefreshAppearance();
        Braille.GridView.FontSize = value.BrailleSize; Braille.GridView.HighContrast = value.HighContrast; Braille.GridView.CurrentLineBrush = gridLine; Braille.GridView.SelectionHighlightBrush = C(value.SelectionColor); Braille.GridView.SelectionTextBrush = C(value.SelectionTextColor); Braille.GridView.ShowLineNumbers = value.ShowLineNumbers; Braille.GridView.InvalidateFaces();
        if (detachedPane is not null) { detachedPane.GridView.FontSize = value.BrailleSize; detachedPane.GridView.HighContrast = value.HighContrast; detachedPane.GridView.CurrentLineBrush = gridLine; detachedPane.GridView.SelectionHighlightBrush = C(value.SelectionColor); detachedPane.GridView.SelectionTextBrush = C(value.SelectionTextColor); detachedPane.GridView.ShowLineNumbers = value.ShowLineNumbers; detachedPane.GridView.InvalidateFaces(); }
        LineNumbersToggle.IsChecked = value.ShowLineNumbers;
        FormattingToggle.IsChecked = value.ShowFormattingMarks;
        ToolbarToggle.IsChecked = value.ShowToolbar;
        EditorToolbar.Visibility = value.ShowToolbar ? Visibility.Visible : Visibility.Collapsed;
        if (!value.SpeakDots) dotSpeech.Stop();
    }
    private void ApplyLineNumberAppearance(TextEditor editor, EditorAppearance value)
    {
        Brush C(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        foreach (var margin in editor.TextArea.LeftMargins.OfType<LineNumberMargin>().ToArray())
        {
            var index = editor.TextArea.LeftMargins.IndexOf(margin);
            editor.TextArea.LeftMargins[index] = new ColoredLineNumbers { Background = value.HighContrast ? Brushes.Black : C(value.LineNumberBackground), Margin = margin.Margin,
                NumberBrush = value.HighContrast ? Brushes.White : C(value.LineNumberColor), Compact = editor == PageEditor.Editor, HighContrast = value.HighContrast,
                CustomNumberColor = !value.LineNumberColor.Equals(new EditorAppearance().LineNumberColor, StringComparison.OrdinalIgnoreCase),
                CustomBackground = !value.LineNumberBackground.Equals(new EditorAppearance().LineNumberBackground, StringComparison.OrdinalIgnoreCase),
                MinWidth = editor == PageEditor.Editor ? 32 : 0 };
        }
    }
    private async Task EditAppearanceAsync(bool colors)
    {
        var initial = accountPreferences.Appearance is null && appearance.CurrentLineColor.Equals(new EditorAppearance().CurrentLineColor, StringComparison.OrdinalIgnoreCase)
            ? appearance with { CurrentLineColor = AppearanceDialog.DefaultCurrentLineColor } : appearance;
        var dialog = new AppearanceDialog(initial, value => ApplyAppearance(value, !ReferenceEquals(value, initial)), colors) { Owner = this };
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
