using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private string searchQuery = "";
    private SearchPanel ActiveSearchPanel => lastEditor is not null && lastEditor != SourceEditor && lastEditor != PageEditor.Editor ? SearchPanel.Braille : SearchPanel.Text;
    private TextEditor SearchEditor(SearchPanel panel) => panel == SearchPanel.Text ? SourceEditor : detachedPane?.Editor ?? Braille.Editor;

    private async Task ShowSearchAsync(bool replace)
    {
        var panel = ActiveSearchPanel;
        var owner = session;
        if (autoTranslate && owner.ActiveBlock is { } active
            && translatedRevisions.GetValueOrDefault(active, -1) != active.SourceRevision)
        {
            var decision = ChooseTranslation(active);
            if (decision == ManualEditDecision.Cancel) return;
            previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
            await TranslateAsync(active, decision, interactive: false);
            if (session != owner) return;
        }
        var selected = SearchEditor(panel).SelectedText;
        if (selected.Length is > 0 and < 1000) searchQuery = selected;
        new FindDialog(panel, searchQuery, replace, (query, target) => FindNextAsync(query, target),
            (query, replacement, target, all) => ReplaceMatchesAsync(query, replacement, target, all), sixDotKeys) { Owner = this }.ShowDialog();
    }

    internal async Task<string> FindNextAsync(string query, SearchPanel panel)
    {
        searchQuery = query;
        if (query.Length == 0) return "찾을 내용을 입력해 주세요.";
        if (panel == SearchPanel.Braille && !DocumentSearch.IsBrailleText(query)) return "점자 찾기에는 점자와 공백·줄바꿈만 입력할 수 있습니다.";
        var editor = SearchEditor(panel);
        var match = DocumentSearch.Next(session.Blocks, panel, query, session.ActiveBlock, editor.SelectionStart + editor.SelectionLength);
        if (match is null) return "일치하는 내용이 없습니다.";
        if (session.ActiveBlock is { } previous && previous != match.Block && autoTranslate
            && translatedRevisions.GetValueOrDefault(previous, -1) != previous.SourceRevision)
        {
            var owner = session;
            var decision = ChooseTranslation(previous);
            if (decision == ManualEditDecision.Cancel) return "다음 결과로 이동하지 않았습니다.";
            await TranslateAsync(previous, decision, interactive: false);
            if (session != owner) return "문서가 바뀌어 검색을 종료했습니다.";
        }
        session.ActiveBlock = match.Block;
        editor = SearchEditor(panel);
        editor.Select(match.Offset, match.Length);
        editor.ScrollToLine(editor.Document.GetLineByOffset(match.Offset).LineNumber);
        if (panel == SearchPanel.Text && PageEditor.Visibility == System.Windows.Visibility.Visible)
        { PageEditor.FollowActive(match.Offset, match.Length); lastEditor = PageEditor.Editor; }
        else
        {
            lastEditor = editor;
            if (panel == SearchPanel.Braille)
            {
                lastGrid = (detachedPane ?? Braille).GridView;
                lastGrid.SelectSearchResult(match.Block, match.Offset, match.Length);
            }
        }
        var all = DocumentSearch.FindAll(session.Blocks, panel, query).ToList();
        return session.Status = $"{all.FindIndex(m => m.Block == match.Block && m.Offset == match.Offset) + 1} / {all.Count} · {match.Block.Page}쪽";
    }

    internal async Task<string> ReplaceMatchesAsync(string query, string replacement, SearchPanel panel, bool all)
    {
        if (session.IsReadOnly) return "읽기 전용 문서는 내용을 바꿀 수 없습니다. 다른 이름으로 저장한 뒤 수정해 주세요.";
        searchQuery = query;
        if (query.Length == 0) return "찾을 내용을 입력해 주세요.";
        if (panel == SearchPanel.Braille && !DocumentSearch.IsBrailleText(query)) return "점자 찾기에는 점자와 공백·줄바꿈만 입력할 수 있습니다.";
        if (panel == SearchPanel.Braille && !DocumentSearch.IsBrailleText(replacement)) return "점자 바꾸기에는 점자와 공백·줄바꿈만 입력할 수 있습니다.";
        var owner = session;
        var editor = SearchEditor(panel);
        IReadOnlyList<SearchMatch> matches;
        if (all) matches = DocumentSearch.FindAll(owner.Blocks, panel, query);
        else
        {
            if (owner.ActiveBlock is null || !editor.SelectedText.Equals(query, StringComparison.OrdinalIgnoreCase))
                return await FindNextAsync(query, panel);
            matches = [new(owner.ActiveBlock, editor.SelectionStart, editor.SelectionLength)];
        }
        bool Protected(SearchMatch match) => match.Block.Metadata.IsBlocked || panel == SearchPanel.Braille && match.Block.IsBrailleLocked;
        var groups = matches.Where(m => !Protected(m) && DocumentSearch.Content(m.Block, panel).Substring(m.Offset, m.Length) != replacement)
            .GroupBy(m => m.Block).ToArray();
        var locked = matches.Count(Protected);
        if (groups.Length == 0) return locked > 0 ? $"잠긴 내용 {locked}곳은 유지했습니다. 변경한 내용은 없습니다." : "변경할 내용이 없습니다.";
        var decisions = new Dictionary<EditorBlock, ManualEditDecision>();
        if (panel == SearchPanel.Text)
            foreach (var group in groups)
            {
                var choice = ChooseTranslation(group.Key);
                if (choice == ManualEditDecision.Cancel) return "바꾸기를 취소했습니다. 내용은 변경하지 않았습니다.";
                decisions.Add(group.Key, choice);
            }
        var changed = new List<EditorBlock>(); var count = 0;
        // Suppress the active editor's debounce; every affected block is translated explicitly below.
        previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
        var wasSwitching = switching; switching = true;
        try
        {
            foreach (var group in groups)
            {
                var document = panel == SearchPanel.Text ? documents.Source(group.Key) : documents.Braille(group.Key);
                var edits = group.Where(m => document.GetText(m.Offset, m.Length) != replacement).OrderByDescending(m => m.Offset).ToArray();
                if (edits.Length == 0) continue;
                using (document.RunUpdate())
                    foreach (var match in edits) document.Replace(match.Offset, match.Length, replacement);
                count += edits.Length; changed.Add(group.Key);
            }
        }
        finally { switching = wasSwitching; }
        if (owner.ActiveBlock is { } activeBlock) activeBlock.SourceCaret = SourceEditor.CaretOffset;
        var translationFailures = 0;
        foreach (var block in changed)
        {
            if (panel != SearchPanel.Text || session != owner) break;
            await TranslateAsync(block, decisions[block], interactive: false);
            if (block.IsBrailleLocked) translationFailures++;
        }
        if (session != owner) return "문서가 바뀌어 바꾸기 작업을 종료했습니다.";
        if (!all && count > 0) await FindNextAsync(query, panel);
        var result = $"{changed.Count}개 블록의 {count}곳을 바꿨습니다.";
        if (locked > 0) result += $" 잠긴 내용 {locked}곳은 유지했습니다.";
        if (translationFailures > 0) result += $" {translationFailures}개 블록의 재점역에 실패했습니다. 원문 수정은 유지되며 다시 점역할 수 있습니다.";
        return session.Status = result;
    }
}
