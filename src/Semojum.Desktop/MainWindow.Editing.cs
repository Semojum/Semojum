using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private void InstallEditorMenu(TextEditor editor)
    {
        var menu = new ContextMenu(); editor.ContextMenu = menu;
        menu.Opened += (_, _) => SetActiveEditor(editor);
        foreach (var (label, action) in new[] { ("메모 추가·답글", "memo"), ("대체 초안 보기", "drafts"), ("적용 규정 보기", "rules"), ("강조 넣기", "tag:강조"), ("쪽바꿈 넣기", "tag:쪽바꿈"), ("복사", "copy"), ("잘라내기", "cut"), ("붙여넣기", "paste") })
        {
            var item = new MenuItem { Header = label }; item.Click += async (_, _) => await ExecuteAsync(action); menu.Items.Add(item);
        }
        var insert = new MenuItem { Header = "삽입" };
        foreach (var definition in BrailleTags.All)
        {
            var item = new MenuItem { Header = definition.Name }; item.Click += (_, _) => InsertTag(definition.Name); insert.Items.Add(item);
        }
        menu.Items.Add(insert);
    }
    private SearchPanel selectionPanel;
    private bool selectedWholeBlock;
    private bool draggingBlocks;
    private void InstallEditingKeys(TextEditor editor) => editor.PreviewKeyDown += async (_, e) =>
    {
        if (session.ActiveBlock is not { } block || editor.IsReadOnly) return;
        var panel = editor == SourceEditor ? SearchPanel.Text : SearchPanel.Braille;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
        {
            e.Handled = true;
            if (selectedWholeBlock) SelectPageBlocks(panel); else { editor.SelectAll(); selectedWholeBlock = true; }
            return;
        }
        selectedWholeBlock = false;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        { e.Handled = true; InsertTag("쪽바꿈"); return; }
        if (Keyboard.Modifiers != ModifierKeys.None || editor.SelectionLength != 0) return;
        var index = session.Blocks.IndexOf(block);
        var atStart = editor.CaretOffset == 0; var atEnd = editor.CaretOffset == editor.Document.TextLength;
        if ((e.Key is Key.Left or Key.Up) && atStart && index > 0)
        { e.Handled = true; await NavigateToAsync(new(session.Blocks[index - 1], panel, panel == SearchPanel.Text ? session.Blocks[index - 1].Source.Length : session.Blocks[index - 1].Braille.Length)); }
        else if ((e.Key is Key.Right or Key.Down) && atEnd && index + 1 < session.Blocks.Count)
        { e.Handled = true; await NavigateToAsync(new(session.Blocks[index + 1], panel, 0)); }
        else if (e.Key == Key.Back && atStart && index > 0 && session.Blocks[index - 1].Page == block.Page)
        { e.Handled = true; await MergeBlocksAsync(session.Blocks[index - 1], block, panel); }
        else if ((e.Key is Key.Delete or Key.Insert) && atEnd && index + 1 < session.Blocks.Count && session.Blocks[index + 1].Page == block.Page)
        { e.Handled = true; await MergeBlocksAsync(block, session.Blocks[index + 1], panel); }
    };
    private void EditStructure(Action action)
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } block) return;
        var before = StructureUndo.Capture(session, block.Page);
        foreach (var b in session.Blocks.Where(b => b.Page == block.Page)) translator.Invalidate(b);
        previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
        action();
        documents.History(block.Page).Push(new StructureUndo(session, before, StructureUndo.Capture(session, block.Page)));
        ShowActiveBlock();
        if (SelectionPane.Visibility == Visibility.Visible) PopulateBlockSelection();
    }
    internal async Task AddBlockAsync()
    {
        if (session.ActiveBlock is not { } active) return;
        EditStructure(() => BlockEditing.AddAfter(session, active)); SourceEditor.Focus();
        await Task.CompletedTask;
    }
    private async Task MergeBlocksAsync(EditorBlock first, EditorBlock second, SearchPanel panel)
    {
        if (first.IsBrailleLocked || second.IsBrailleLocked) { session.Status = "점역 중이거나 잠긴 블록은 합칠 수 없습니다."; return; }
        var decision = first.HasManualBraille || second.HasManualBraille ? ChooseTranslation(first.HasManualBraille ? first : second) : ManualEditDecision.Overwrite;
        if (decision == ManualEditDecision.Cancel) return;
        var caret = panel == SearchPanel.Text ? first.Source.Length : first.Braille.Length;
        EditStructure(() => BlockEditing.Merge(session, first, second, decision == ManualEditDecision.Keep));
        if (decision == ManualEditDecision.Overwrite) await TranslateAsync(first, decision);
        SearchEditor(panel).CaretOffset = Math.Min(caret, SearchEditor(panel).Document.TextLength);
        SearchEditor(panel).Focus();
    }
    private void PopulateBlockSelection()
    {
        BlockSelectionList.ItemsSource = session.Blocks.Where(b => b.Page == session.ActiveBlock?.Page).ToArray();
        SelectionCaption.Text = $"{(selectionPanel == SearchPanel.Text ? "텍스트" : "점자")} · {session.ActiveBlock?.Page}쪽 · Shift+클릭 / 드래그로 블록 선택";
    }
    private void SelectPageBlocks(SearchPanel panel)
    {
        selectionPanel = panel; SelectionPane.Visibility = Visibility.Visible; PopulateBlockSelection(); BlockSelectionList.SelectAll();
    }
    private EditorBlock[] SelectedBlocks() => SelectionPane.Visibility == Visibility.Visible ?
        BlockSelectionList.SelectedItems.Cast<EditorBlock>().OrderBy(b => session.Blocks.IndexOf(b)).ToArray() : [];
    private void SelectionHide_Click(object sender, RoutedEventArgs e) { SelectionPane.Visibility = Visibility.Collapsed; BlockSelectionList.UnselectAll(); }
    private void BlockSelection_MouseDown(object sender, MouseButtonEventArgs e) => draggingBlocks = true;
    private void BlockSelection_MouseMove(object sender, MouseEventArgs e)
    {
        if (!draggingBlocks || e.LeftButton != MouseButtonState.Pressed) { draggingBlocks = false; return; }
        if (ItemsControl.ContainerFromElement(BlockSelectionList, e.OriginalSource as DependencyObject) is ListBoxItem item) item.IsSelected = true;
    }
    private bool CopySelectedBlocks(bool cut)
    {
        var blocks = SelectedBlocks(); if (blocks.Length == 0) return false;
        if (selectionPanel == SearchPanel.Braille && cut && blocks.Any(b => b.IsBrailleLocked)) return true;
        Clipboard.SetText(string.Join("\n", blocks.Select(b => selectionPanel == SearchPanel.Text ? b.Source : b.Braille)));
        if (cut) EditStructure(() => { if (selectionPanel == SearchPanel.Braille) foreach (var block in blocks) block.EditBraille(""); else BlockEditing.Delete(session, blocks); }); return true;
    }
    private bool PasteSelectedBlocks()
    {
        var blocks = SelectedBlocks(); if (blocks.Length == 0 || !Clipboard.ContainsText()) return false;
        var text = Clipboard.GetText();
        if (selectionPanel == SearchPanel.Braille && (!DocumentSearch.IsBrailleText(text) || blocks.Any(b => b.IsBrailleLocked)))
        { session.Status = "잠긴 점자 블록이 있거나 점자가 아닌 내용입니다."; return true; }
        EditStructure(() =>
        {
            var first = blocks[0];
            foreach (var b in blocks.Skip(1)) { if (selectionPanel == SearchPanel.Text) session.Blocks.Remove(b); else b.EditBraille(""); }
            if (selectionPanel == SearchPanel.Text) { first.Source = text; first.Translation = null; }
            else first.EditBraille(text);
            session.ActiveBlock = first;
        }); return true;
    }
    private void SelectionEdit(string action)
    {
        var blocks = SelectedBlocks(); if (blocks.Length == 0 && session.ActiveBlock is { } active) blocks = [active];
        if (blocks.Length == 0 || blocks.Any(b => b.IsBrailleLocked)) return;
        EditStructure(() =>
        {
            if (action == "deleteBlock") { BlockEditing.Delete(session, blocks); return; }
            if (action is "moveBlockUp" or "moveBlockDown")
            {
                var step = action == "moveBlockUp" ? -1 : 1;
                var edge = step < 0 ? blocks[0] : blocks[^1]; var target = session.Blocks.IndexOf(edge) + step;
                if (target < 0 || target >= session.Blocks.Count || session.Blocks[target].Page != edge.Page || blocks.Contains(session.Blocks[target])) return;
                foreach (var b in step < 0 ? blocks : blocks.Reverse()) session.Blocks.Move(session.Blocks.IndexOf(b), session.Blocks.IndexOf(b) + step);
                return;
            }
            foreach (var b in blocks)
            {
                var text = selectionPanel == SearchPanel.Text ? b.Source : b.Braille;
                var blank = selectionPanel == SearchPanel.Text ? ' ' : '⠀';
                text = string.Join("\n", text.Split('\n').Select(line => action == "indent" ? blank + line : line.Length > 0 && line[0] == blank ? line[1..] : line));
                if (selectionPanel == SearchPanel.Text) b.Source = text; else b.EditBraille(text);
            }
        });
    }
    private void ClearCurrentLine()
    {
        var editor = lastEditor ?? SourceEditor; if (editor.IsReadOnly) return;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset); editor.Document.Remove(line.Offset, line.Length);
    }
}
