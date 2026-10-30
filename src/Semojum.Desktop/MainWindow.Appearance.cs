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
        var selected = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0, 95, 184), 0), new(Color.FromRgb(0, 95, 184), 2d / 35),
            new(Colors.White, 2d / 35), new(Colors.White, 1)
        }, new Point(0, 0), new Point(0, 1));
        var neutral = new SolidColorBrush(Color.FromRgb(249, 249, 249));
        SourceHeader.Background = (editor == SourceEditor || editor == PageEditor.Editor) ? selected : neutral;
        OriginalHeader.Background = editor is null ? selected : neutral;
        Braille.Header.Background = editor is not null && editor != SourceEditor && editor != PageEditor.Editor ? selected : neutral;
        var sourceActive = editor == SourceEditor || editor == PageEditor.Editor;
        var originalActive = editor is null;
        var brailleActive = !originalActive && !sourceActive;
        SourcePanelTitle.FontWeight = sourceActive ? FontWeights.Bold : FontWeights.Normal;
        OriginalPanelTitle.FontWeight = originalActive ? FontWeights.Bold : FontWeights.Normal;
        Braille.PanelTitle.FontWeight = brailleActive ? FontWeights.Bold : FontWeights.Normal;
        var ink = new SolidColorBrush(Color.FromRgb(26, 26, 26));
        var muted = new SolidColorBrush(Color.FromRgb(95, 95, 95));
        SourcePanelTitle.Foreground = sourceActive ? ink : muted;
        OriginalPanelTitle.Foreground = originalActive ? ink : muted;
        Braille.PanelTitle.Foreground = brailleActive ? ink : muted;
        if (detachedPane is not null) detachedPane.Header.Background = Braille.Header.Background;
        RefreshLinkedSource();
        RefreshEditorStatus();
    }

    private void RefreshEditorStatus()
    {
        if (session?.ActiveBlock is not { } block || CaretStatus is null) return;
        var editor = lastEditor ?? SourceEditor;
        var braille = editor != SourceEditor && editor != PageEditor.Editor;
        var input = braille ? (Braille.SixDotEnabled ? "점형" : "점자") : "텍스트";
        var pageNumbers = session.Blocks.Select(b => b.Page).Concat(session.PageStatuses.Keys).Concat(session.OriginalPages.Keys).Distinct().Order().ToArray();
        var pages = pageNumbers.Length;
        OptionsLabel.Text = $"옵션 2024 · {(session.Options.KoreanGrade1 ? "1급" : "2급")} · {session.Layout.Columns}×{session.Layout.Lines}";
        StatusOptions.Text = $"옵션 2024 · {(session.Options.KoreanGrade1 ? "1급" : "2급")} · {session.Layout.Columns}×{session.Layout.Lines}";
        StatusInput.Text = $"입력 {input}";
        StatusPage.Text = pageNumbers.SequenceEqual(Enumerable.Range(1, pages)) ? $"쪽 {block.Page} / {pages}" : $"쪽 {block.Page}";
        StatusPage.ToolTip = $"원본 {block.Page}쪽 (총 {pages}쪽)";
        CaretStatus.Text = $"줄 {editor.TextArea.Caret.Line}";
        StatusCell.Text = $"칸 {editor.TextArea.Caret.Column} / {session.Layout.Columns}";
        if (lastGrid is not null)
        {
            var position = lastGrid.CaretCoordinates;
            StatusPage.Text = position is { } p ? $"점자 {p.Face} / {lastGrid.Items.Count}면" : $"총 {lastGrid.Items.Count}면";
            StatusPage.ToolTip = lastGrid.DescribeCaret();
            CaretStatus.Text = $"줄 {position?.Line.ToString() ?? "—"}";
            StatusCell.Text = $"칸 {position?.Cell.ToString() ?? "—"} / {session.Layout.Columns}";
        }
        OriginalPosition.Text = session.OriginalPages.ContainsKey(block.Page) ? $"{block.Page}쪽 · 맞춤" : "";
        RefreshPanelPageBars();
    }

    private void ZoomPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceEditor is null || ZoomPicker.SelectedItem is not string text) return;
        if (double.TryParse(text.TrimEnd('%'), out var percent)) SetFontSize(14 * percent / 100);
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (settingFontSize || SourceEditor is null) return;
        var size = e.NewValue <= 50 ? 10 + e.NewValue * 4 / 50 : 14 + (e.NewValue - 50) * 18 / 50;
        SetFontSize(size);
    }

    private void FontSizePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceEditor is not null && FontSizeLabel.SelectedItem is ComboBoxItem item && double.TryParse(item.Content?.ToString(), out var size)) SetFontSize(size);
    }
    private void TextStylePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SourceEditor is null || TextStylePicker.SelectedIndex <= 0 || TextStylePicker.SelectedItem is not ComboBoxItem item) return;
        InsertTag(item.Content.ToString()!); TextStylePicker.SelectedIndex = 0;
    }
    private void IndentPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !IndentPicker.IsKeyboardFocusWithin || session.IsReadOnly) return;
        SelectionEdit("indent", IndentPicker.SelectedIndex + 1);
    }

    private void LineNumbersToggle_Click(object sender, RoutedEventArgs e) => ApplyLineNumbers();
    private void ApplyLineNumbers()
    {
        SourceEditor.ShowLineNumbers = PageEditor.Editor.ShowLineNumbers = Braille.Editor.ShowLineNumbers = LineNumbersToggle.IsChecked;
        if (detachedPane is not null) detachedPane.Editor.ShowLineNumbers = LineNumbersToggle.IsChecked;
        Braille.GridView.ShowLineNumbers = LineNumbersToggle.IsChecked; Braille.GridView.InvalidateFaces();
        if (detachedPane is not null) { detachedPane.GridView.ShowLineNumbers = LineNumbersToggle.IsChecked; detachedPane.GridView.InvalidateFaces(); }
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
