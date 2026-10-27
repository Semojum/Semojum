using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private void ShowGoTo()
    {
        var panel = ActiveSearchPanel;
        new GoToDialog(panel, (target, value) => NavigateAsync(target, value, panel)) { Owner = this }.ShowDialog();
    }

    internal async Task<string> NavigateAsync(NavigationTarget target, string input, SearchPanel panel)
    {
        if (target == NavigationTarget.BraillePage)
        {
            var face = DocumentNavigation.Number(input, lastPreviewPage);
            var pane = detachedPane ?? Braille;
            if (!pane.GridView.GoToFace(face)) throw new ArgumentException($"점자 면은 1–{pane.GridView.Items.Count} 범위로 입력해 주세요.");
            lastPreviewPage = face; return $"점자 {face}면으로 이동했습니다.";
        }
        if (session.ActiveBlock is not { } active) return "열린 작업 블록이 없습니다.";
        var editor = SearchEditor(panel);
        var destination = DocumentNavigation.Resolve(session.Blocks, active, panel, editor.CaretOffset, target, input);
        return await NavigateToAsync(destination);
    }

    private async Task<string> NavigateToAsync(NavigationLocation destination)
    {
        var owner = session;
        var previous = owner.ActiveBlock;
        if (previous is not null && previous != destination.Block && autoTranslate
            && translatedRevisions.GetValueOrDefault(previous, -1) != previous.SourceRevision)
        {
            var decision = ChooseTranslation(previous);
            if (decision == ManualEditDecision.Cancel) return "이동을 취소했습니다.";
            previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
            await TranslateAsync(previous, decision, interactive: false);
            if (owner != session) return "문서가 바뀌어 이동을 취소했습니다.";
        }
        session.ActiveBlock = destination.Block;
        var editor = SearchEditor(destination.Panel);
        if (destination.Panel == SearchPanel.Text) SourceToggle.IsChecked = true;
        else BrailleToggle.IsChecked = true;
        UpdatePanelLayout();
        editor.Select(Math.Clamp(destination.Offset, 0, editor.Document.TextLength), 0);
        editor.ScrollToLine(editor.TextArea.Caret.Line);
        if (destination.Panel == SearchPanel.Text && PageEditor.Visibility == System.Windows.Visibility.Visible)
        { PageEditor.FollowActive(destination.Offset); SetActiveEditor(PageEditor.Editor); }
        else SetActiveEditor(editor);
        return session.Status = $"원본 {destination.Block.Page}쪽 · 블록 {destination.Block.Id} · {editor.TextArea.Caret.Line}줄로 이동했습니다.";
    }
}
