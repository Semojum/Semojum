using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private void SetActiveEditor(TextEditor? editor)
    {
        lastGrid = null;
        if (editor is not null) lastEditor = editor;
        var selected = new SolidColorBrush(Color.FromRgb(204, 228, 247));
        var neutral = (Brush)FindResource("PanelBlue");
        SourceHeader.Background = (editor == SourceEditor || editor == PageEditor.Editor) ? selected : neutral;
        OriginalHeader.Background = editor is null ? selected : neutral;
        Braille.Header.Background = editor is not null && editor != SourceEditor && editor != PageEditor.Editor ? selected : neutral;
        if (detachedPane is not null) detachedPane.Header.Background = Braille.Header.Background;
        RefreshEditorStatus();
    }

    private void RefreshEditorStatus()
    {
        if (session?.ActiveBlock is not { } block || CaretStatus is null) return;
        var editor = lastEditor ?? SourceEditor;
        var braille = editor != SourceEditor && editor != PageEditor.Editor;
        var input = braille ? (Braille.SixDotEnabled ? "점형" : "점자") : "텍스트";
        var pages = session.Blocks.Select(b => b.Page).Concat(session.PageStatuses.Keys).Distinct().Count();
        OptionsLabel.Text = $"한글 {(session.Options.KoreanGrade1 ? "정자" : "약자")} · 영문 {(session.Options.EnglishGrade1 ? "정자" : "약자")} · {session.Layout.Columns}×{session.Layout.Lines}";
        CaretStatus.Text = $"입력 {input}   |   원본 {block.Page}쪽 ({pages}쪽)   |   줄 {editor.TextArea.Caret.Line}   |   칸 {editor.TextArea.Caret.Column}   |   {(session.IsDirty ? "저장할 변경 있음" : "저장됨")}";
        if (lastGrid is not null) CaretStatus.Text = $"입력 {input}   |   {lastGrid.DescribeCaret()}   |   {(session.IsDirty ? "저장할 변경 있음" : "저장됨")}";
        OriginalPosition.Text = $"{block.Page}쪽 · 맞춤";
    }

    private void ZoomPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceEditor is null || ZoomPicker.SelectedItem is not string text) return;
        if (double.TryParse(text.TrimEnd('%'), out var percent)) SetFontSize(14 * percent / 100);
    }

    private void LineNumbersToggle_Click(object sender, RoutedEventArgs e) => ApplyLineNumbers();
    private void ApplyLineNumbers()
    {
        SourceEditor.ShowLineNumbers = Braille.Editor.ShowLineNumbers = LineNumbersToggle.IsChecked;
        if (detachedPane is not null) detachedPane.Editor.ShowLineNumbers = LineNumbersToggle.IsChecked;
    }

    internal void HidePanel(string action)
    {
        switch (action)
        {
            case "hideOriginal":
                if (detachedPanels.TryGetValue(OriginalPanel, out var original)) original.Close();
                OriginalToggle.IsChecked = false; break;
            case "hideSource":
                if (detachedPanels.TryGetValue(SourcePanel, out var source)) source.Close();
                SourceToggle.IsChecked = false; break;
            case "hideBraille":
                detachedWindow?.Close(); BrailleToggle.IsChecked = false; break;
        }
        // Keep one editor reachable even when the last visible panel is closed.
        if (!SourceToggle.IsChecked && !BrailleToggle.IsChecked) SourceToggle.IsChecked = true;
        UpdatePanelLayout();
    }

    private void PanelToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender == SourceToggle && detachedPanels.TryGetValue(SourcePanel, out var source)) source.Close();
        if (sender == BrailleToggle) detachedWindow?.Close();
        if (!SourceToggle.IsChecked && !BrailleToggle.IsChecked) SourceToggle.IsChecked = true;
        UpdatePanelLayout();
    }
}
