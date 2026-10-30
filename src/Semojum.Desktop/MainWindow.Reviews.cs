using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private IReadOnlyList<(int Start, ReviewMark Mark)> ActiveReviewRanges(ReviewPanel panel) => session?.ActiveBlock is { } block
        ? block.ReviewMarks.Where(m => m.Panel == panel && !m.IsStale).Select(m => (m.Start, m)).ToArray() : [];
    private BlockSpan[] CurrentReviewSelection()
    {
        if (lastGrid is not null) return lastGrid.Selection.Where(s => s.Length > 0).ToArray();
        if (lastEditor == PageEditor.Editor) return PageEditor.Selection.Where(s => s.Length > 0).ToArray();
        if (session.ActiveBlock is not { } block || lastEditor is null || lastEditor.SelectionLength == 0) return [];
        return [new BlockSpan(block, lastEditor.SelectionStart, lastEditor.SelectionLength)];
    }

    private ReviewPanel CurrentReviewPanel() => ActiveSearchPanel == SearchPanel.Text ? ReviewPanel.Text : ReviewPanel.Braille;

    private bool ValidateReviewSelection(BlockSpan[] spans, ReviewPanel panel)
    {
        if (session.IsReadOnly) { session.Status = "읽기 전용 문서에는 검토 표시를 변경할 수 없습니다."; return false; }
        if (spans.Length == 0) { session.Status = "먼저 검토할 텍스트 또는 점자를 선택해 주세요."; return false; }
        if (spans.Select(s => s.Block.Page).Distinct().Count() != 1 || spans.Any(s => !session.Blocks.Contains(s.Block)
            || s.Block.Metadata.IsBlocked || panel == ReviewPanel.Braille && s.Block.IsBrailleLocked))
        { session.Status = "선택한 범위에 편집할 수 없는 블록이 있습니다."; return false; }
        foreach (var span in spans)
        {
            var text = panel == ReviewPanel.Text ? span.Block.Source : span.Block.Braille;
            var end = span.Start + span.Length;
            if (span.Start < 0 || span.Length <= 0 || end > text.Length || SplitsCharacter(text, span.Start) || SplitsCharacter(text, end))
            { session.Status = "문자 중간이 포함되어 있습니다. 범위를 다시 선택해 주세요."; return false; }
        }
        return true;
    }

    private static bool SplitsCharacter(string text, int offset) => offset > 0 && offset < text.Length
        && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);

    private void ChangeReviews(EditorBlock focus, IEnumerable<EditorBlock> affected, Action change)
    {
        var blocks = affected.Distinct().ToArray();
        var before = blocks.ToDictionary(b => b, b => b.ReviewMarks.ToArray());
        change();
        var after = blocks.ToDictionary(b => b, b => b.ReviewMarks.ToArray());
        if (blocks.All(b => before[b].SequenceEqual(after[b]))) return;
        documents.RecordReviewEdit(focus, before, after);
        RefreshReviewDisplays();
    }

    private void SetReviewSelection(ReviewKind kind)
    {
        var panel = CurrentReviewPanel(); var spans = CurrentReviewSelection();
        if (!ValidateReviewSelection(spans, panel)) return;
        ChangeReviews(spans[0].Block, spans.Select(s => s.Block), () =>
        {
            foreach (var span in spans)
            {
                // A new choice replaces only the exact same range. Partly overlapping
                // reviews remain separately available in the list rather than being lost.
                foreach (var old in span.Block.ReviewMarks.Where(m => !m.IsStale && m.Panel == panel
                    && m.Start == span.Start && m.Length == span.Length).ToArray()) span.Block.ReviewMarks.Remove(old);
                span.Block.AddReviewMark(kind, panel, span.Start, span.Length);
            }
        });
        session.Status = $"{ReviewLabel(kind)} 표시를 선택 범위에 저장했습니다. 로컬 SMJ에 저장해 주세요.";
    }

    private void RemoveSelectedReviews()
    {
        var panel = CurrentReviewPanel(); var spans = CurrentReviewSelection();
        if (!ValidateReviewSelection(spans, panel)) return;
        ChangeReviews(spans[0].Block, spans.Select(s => s.Block), () =>
        {
            foreach (var span in spans)
                foreach (var mark in span.Block.ReviewMarks.Where(m => !m.IsStale && m.Panel == panel
                    && m.Start < span.Start + span.Length && span.Start < m.Start + m.Length).ToArray()) span.Block.ReviewMarks.Remove(mark);
        });
        session.Status = "선택 범위와 겹친 검토 표시를 지웠습니다. 실행 취소로 복원할 수 있습니다.";
    }

    private static string ReviewLabel(ReviewKind kind) => kind switch
    {
        ReviewKind.SuggestCorrection => "고침 제안",
        ReviewKind.SuggestDeletion => "지울 제안",
        ReviewKind.HumanConfirmed => "사람이 확인함",
        _ => "검토 표시"
    };

    private sealed record ReviewEntry(EditorBlock Block, ReviewMark Mark)
    {
        public override string ToString() => $"{Block.Page}쪽 · {(Mark.Panel == ReviewPanel.Text ? "텍스트" : "점자")} · {ReviewLabel(Mark.Kind)}"
            + (Mark.IsStale ? " · 다시 확인 필요" : "") + $"\n{Mark.OriginalText}";
    }

    private void ShowReviewList() => CreateReviewListWindow().ShowDialog();

    internal Window CreateReviewListWindow()
    {
        var originalSelection = CurrentReviewSelection(); var selectedPanel = CurrentReviewPanel();
        var capturedSession = session; var capturedVersion = session.ChangeVersion;
        var window = new Window { Owner = this, Title = "검토 표시", Width = 560, Height = 430, MinWidth = 360, MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var root = new DockPanel { Margin = new Thickness(16) }; window.Content = root;
        var info = new TextBlock { Text = "편집으로 위치가 달라진 표시는 다시 확인해야 합니다. 검토 표시는 로컬 SMJ에 저장됩니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(info, Dock.Top); root.Children.Add(info);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        var list = new ListBox(); System.Windows.Automation.AutomationProperties.SetName(list, "검토 표시 목록"); root.Children.Add(list);
        void Refresh() => list.ItemsSource = session.Blocks.SelectMany(b => b.ReviewMarks.Select(m => new ReviewEntry(b, m))).ToArray();
        Button Button(string title, Action action)
        {
            var button = new Button { Content = title, MinWidth = 86, Height = 32, Margin = new Thickness(6, 0, 0, 0) };
            button.Click += (_, _) => action(); buttons.Children.Add(button); return button;
        }
        Button("다시 지정", () =>
        {
            if (list.SelectedItem is not ReviewEntry { Mark.IsStale: true } entry) { info.Text = session.Status = "다시 확인이 필요한 표시를 선택해 주세요."; return; }
            if (!ReferenceEquals(capturedSession, session) || capturedSession.ChangeVersion != capturedVersion)
            { info.Text = "문서가 변경되어 기존 선택을 사용할 수 없습니다. 창을 닫고 범위를 다시 선택해 주세요."; return; }
            if (!ValidateReviewSelection(originalSelection, selectedPanel) || selectedPanel != entry.Mark.Panel
                || originalSelection.Length != 1 || originalSelection[0].Block != entry.Block)
            { info.Text = session.Status = "같은 블록과 패널에서 새 범위를 선택한 뒤 다시 지정해 주세요."; return; }
            var span = originalSelection[0];
            ChangeReviews(entry.Block, [entry.Block], () =>
            { entry.Block.ReviewMarks.Remove(entry.Mark); entry.Block.AddReviewMark(entry.Mark.Kind, selectedPanel, span.Start, span.Length); });
            info.Text = session.Status = "선택 범위에 검토 표시를 다시 지정했습니다."; Refresh();
        });
        Button("선택 삭제", () =>
        {
            if (session.IsReadOnly) { info.Text = "읽기 전용 문서에는 검토 표시를 변경할 수 없습니다."; return; }
            if (list.SelectedItem is not ReviewEntry entry) { info.Text = "삭제할 검토 표시를 선택해 주세요."; return; }
            ChangeReviews(entry.Block, [entry.Block], () => entry.Block.ReviewMarks.Remove(entry.Mark));
            info.Text = session.Status = "검토 표시를 지웠습니다. 실행 취소로 복원할 수 있습니다."; Refresh();
        });
        Button("닫기", window.Close);
        Refresh(); return window;
    }

    private void RefreshReviewDisplays()
    {
        PageEditor.Editor.TextArea.TextView.Redraw(); SourceEditor.TextArea.TextView.Redraw();
        Braille.GridView.InvalidateFaces(); detachedPane?.GridView.InvalidateFaces();
    }
}
