using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Services;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    internal void SelectAllContext()
    {
        if (lastGrid is not null) { lastGrid.SelectContext(); return; }
        if (lastEditor == PageEditor.Editor) { PageEditor.SelectContext(); return; }
        var editor = lastEditor ?? SourceEditor;
        if (selectedWholeBlock) SelectPageBlocks(ActiveSearchPanel);
        else { editor.SelectAll(); selectedWholeBlock = true; }
    }
    internal void SelectTextRange()
    {
        if (session.ActiveBlock is null) return;
        if (ActiveSearchPanel == SearchPanel.Braille)
        { (detachedPane ?? Braille).GridView.Focus(); session.Status = "모눈에서 드래그 또는 Shift+클릭으로 선택하세요."; return; }
        PageEditor.Visibility = Visibility.Visible; SourceEditor.Visibility = Visibility.Collapsed;
        PageEditor.Refresh(); PageEditor.Editor.Focus();
    }
    private void InitializeContinuousEditor()
    {
        PageEditor.Editor.TextArea.LeftMargins.Add(new Controls.MemoMargin(() => session?.ActiveBlock, "TEXT", line =>
        {
            var resolved = PageEditor.ResolveLine(line);
            if (resolved.Block is null) return;
            session.ActiveBlock = resolved.Block;
            ShowLineMemos(SearchPanel.Text, resolved.Line);
        }) { ResolveLine = PageEditor.ResolveLine, ReviewRequested = async line =>
        {
            if (PageEditor.ResolveLine(line).Block is { } block) session.ActiveBlock = block;
            await ExecuteAsync("pageStatus");
        } });
        PageEditor.Replay = redo => ReplayHistory(redo);
        PageEditor.InsertPageBreak = () => InsertTag("쪽바꿈");
        InstallEditorMenu(PageEditor.Editor);
        PageEditor.Replace = (spans, value) =>
        {
            if (spans.Length == 0 || session.IsReadOnly) return;
            var decision = ManualEditDecision.Overwrite;
            if (spans.Length > 1 && spans.FirstOrDefault(s => s.Block.HasManualBraille) is { } manual)
            { decision = ChooseTranslation(manual.Block); if (decision == ManualEditDecision.Cancel) return; }
            session.ActiveBlock = spans[0].Block;
            EditStructure(() =>
            {
                var edited = BlockRange.Replace(session, spans, SearchPanel.Text, value);
                if (spans.Length > 1 && decision == ManualEditDecision.Overwrite) edited.HasManualBraille = false;
            });
            if (decision == ManualEditDecision.Keep) translatedRevisions[spans[0].Block] = spans[0].Block.SourceRevision;
            QueuePreview(decision != ManualEditDecision.Keep);
        };
        PageEditor.Editor.GotKeyboardFocus += (_, _) => SetActiveEditor(PageEditor.Editor);
        PageEditor.Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (session?.ActiveBlock is { } block)
                SourceEditor.CaretOffset = Math.Clamp(block.SourceCaret, 0, SourceEditor.Document.TextLength);
            RefreshLinkedSource();
            RefreshSourcePosition();
        };
        PageEditor.Editor.TextArea.SelectionChanged += (_, _) => RefreshLinkedSource();
    }
    private void InstallEditorMenu(TextEditor editor)
    {
        var menu = CreateEditorContextMenu(); editor.ContextMenu = menu;
        menu.Opened += (_, _) => SetActiveEditor(editor);
    }
    private ContextMenu CreateEditorContextMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("EditorContextMenu") };
        foreach (var (label, action) in new[] { ("메모 추가", "memo"), ("", ""), ("대체 초안 보기", "drafts"), ("적용 규정 보기", "rules") })
        {
            if (label.Length == 0) { menu.Items.Add(new Separator { Style = (Style)menu.Style.Resources[typeof(Separator)], Height = 9, Margin = new(0) }); continue; }
            var item = new MenuItem { Header = label }; item.Click += async (_, _) => await ExecuteAsync(action); menu.Items.Add(item);
        }
        return menu;
    }
    private SearchPanel selectionPanel;
    private bool selectedWholeBlock;
    private bool draggingBlocks;
    private bool CanReplayHistory(bool redo) => session?.IsReadOnly == false && session.ActiveBlock is { } block
        && (redo ? documents.History(block.Page).CanRedo : documents.History(block.Page).CanUndo);
    internal void ReplayHistory(bool redo = false)
    {
        if (!CanReplayHistory(redo) || session.ActiveBlock is not { } block) return;
        foreach (var item in session.Blocks.Where(b => b.Page == block.Page)) translator.Invalidate(item);
        previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
        var target = documents.Replay(block.Page, redo);
        if (target is not null && session.Blocks.Contains(target)) session.ActiveBlock = target;
        ShowActiveBlock(); QueuePreview(true);
    }
    private void InstallEditingKeys(TextEditor editor)
    {
        // Intercept the routed commands before AvalonEdit consults its intentionally empty
        // local stack. Main and detached editors use the same per-page model history.
        editor.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, e) =>
        {
            if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
            e.CanExecute = CanReplayHistory(e.Command == ApplicationCommands.Redo); e.Handled = true;
        }));
        editor.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
        {
            if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
            ReplayHistory(e.Command == ApplicationCommands.Redo); e.Handled = true;
        }));
        editor.PreviewKeyDown += async (_, e) =>
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
    }
    internal void EditStructure(Action action)
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
        var decision = ManualEditDecision.Overwrite;
        if (first.HasManualBraille || second.HasManualBraille)
        {
            var merge = new Dialogs.MergeBlocksDialog { Owner = this }; merge.ShowDialog(); decision = merge.Decision;
        }
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
    private async Task<bool> PasteSelectedBlocksAsync()
    {
        var blocks = SelectedBlocks(); if (blocks.Length == 0 || !Clipboard.ContainsText()) return false;
        var text = Clipboard.GetText();
        if (selectionPanel == SearchPanel.Braille && (!DocumentSearch.IsBrailleText(text) || blocks.Any(b => b.IsBrailleLocked)))
        { session.Status = "잠긴 점자 블록이 있거나 점자가 아닌 내용입니다."; return true; }
        var decision = ManualEditDecision.Overwrite;
        if (selectionPanel == SearchPanel.Text && blocks.FirstOrDefault(b => b.HasManualBraille) is { } manual)
        { decision = ChooseTranslation(manual); if (decision == ManualEditDecision.Cancel) return true; }
        var spans = blocks.Select(b => new BlockSpan(b, 0, BlockRange.Value(b, selectionPanel).Length)).ToArray();
        EditorBlock? edited = null;
        EditStructure(() => edited = BlockRange.Replace(session, spans, selectionPanel, text));
        if (selectionPanel == SearchPanel.Text && edited is not null && autoTranslate) await TranslateAsync(edited, decision, false);
        return true;
    }
    private void SelectionEdit(string action, int indent = 1)
    {
        if (action is "indent" or "unindent" && SelectedBlocks().Length == 0)
        {
            var panel = ActiveSearchPanel;
            var editor = lastEditor ?? SourceEditor;
            var spans = lastGrid is not null ? lastGrid.Selection : editor == PageEditor.Editor ? PageEditor.Selection :
                session.ActiveBlock is { } b ? [new BlockSpan(b, editor.SelectionStart, editor.SelectionLength)] : Array.Empty<BlockSpan>();
            if (spans.Length == 0 || spans.Any(s => s.Block.IsBrailleLocked || s.Block.Metadata.IsBlocked)) return;
            session.ActiveBlock = spans[0].Block;
            EditStructure(() =>
            {
                foreach (var span in spans)
                {
                    var text = BlockRange.Value(span.Block, panel);
                    var start = span.Start == 0 ? 0 : text.LastIndexOf('\n', span.Start - 1) + 1;
                    var end = span.Start + span.Length;
                    if (span.Length > 0 && end > start && text[end - 1] == '\n') end--;
                    var stop = text.IndexOf('\n', end); if (stop < 0) stop = text.Length;
                    var blank = panel == SearchPanel.Text ? ' ' : '⠀';
                    var value = string.Join("\n", text[start..stop].Split('\n').Select(line =>
                        BrailleMarkup.Markers(line).Length > 0 ? line : action == "indent" ? new string(blank, indent) + line : line.StartsWith(blank) ? line[1..] : line));
                    value = text[..start] + value + text[stop..];
                    if (panel == SearchPanel.Text) span.Block.Source = value; else span.Block.EditBraille(value);
                }
            });
            if (panel == SearchPanel.Text) QueuePreview(true);
            return;
        }
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
                text = string.Join("\n", text.Split('\n').Select(line => action == "indent" ? new string(blank, indent) + line : line.Length > 0 && line[0] == blank ? line[1..] : line));
                if (selectionPanel == SearchPanel.Text) b.Source = text; else b.EditBraille(text);
            }
        });
    }
    private void ClearCurrentLine()
    {
        if (lastGrid is not null) { _ = lastGrid.ClearLineAsync(); return; }
        var editor = lastEditor ?? SourceEditor; if (editor.IsReadOnly) return;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset); editor.Document.Remove(line.Offset, line.Length);
    }
}
