using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private async Task EditMathAsync()
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } active || engine is not LocalBrailleEngine local) return;
        var existing = active.Metadata.Type == "formula";
        var latex = existing ? active.Source.Replace("<!수식>", "").Replace("<!/수식>", "") : null;
        var dialog = new MathDialog((value, ct) => local.TranslateElementAsync(value, new(Type: "formula"), session.Options, ct), latex) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Translation is null) return;
        if (existing && ChooseTranslation(active) != ManualEditDecision.Overwrite) return;
        EditorBlock block = active;
        EditStructure(() =>
        {
            if (!existing)
            {
                block = new EditorBlock { Id = session.Blocks.Max(b => b.Id) + 1, Page = active.Page, Heading = "수식", Metadata = new(Type: "formula") };
                session.Blocks.Insert(session.Blocks.IndexOf(active) + 1, block);
            }
            block.Source = $"<!수식>{dialog.Latex}<!/수식>";
            block.Braille = dialog.BrailleInput.Text;
            block.Translation = dialog.Translation with { Cells = block.Braille, SourceText = block.Source,
                RuleTrail = dialog.Edited ? null : dialog.Translation.RuleTrail, Breaks = dialog.Edited ? [] : dialog.Translation.Breaks };
            block.HasManualBraille = dialog.Edited;
        });
        session.ActiveBlock = block; translatedRevisions[block] = block.SourceRevision;
        await Task.CompletedTask;
    }
    private async void ShowLineMemos(SearchPanel panel, int? requestedLine = null, bool showResolved = false)
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } block) return;
        var editor = SearchEditor(panel); var line = requestedLine ?? editor.TextArea.Caret.Line - 1;
        if (line < 0 || line >= editor.Document.LineCount) return;
        var text = editor.Document.GetText(editor.Document.GetLineByNumber(line + 1));
        var code = panel == SearchPanel.Text ? "TEXT" : "BRAILLE";
        if (!block.Memos.Any(m => m.Panel == code && m.LineIndex == line && (showResolved || !m.Resolved)))
        {
            var input = new MemoInputDialog(api.Account?.DisplayName) { Owner = this };
            if (input.ShowDialog() == true)
                EditStructure(() => block.Memos.Add(new(Guid.NewGuid().ToString("N"), null, code, line, text[..Math.Min(80, text.Length)], input.Value, DateTimeOffset.Now, Author: api.Account?.DisplayName)));
            foreach (var margin in editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>()) margin.InvalidateVisual();
            foreach (var margin in PageEditor.Editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>()) margin.InvalidateVisual();
            return;
        }
        var dialog = new MemoThreadDialog(block, panel == SearchPanel.Text ? "TEXT" : "BRAILLE", line, text, EditStructure, session, showResolved, api.Account?.DisplayName) { Owner = this };
        dialog.ShowDialog();
        foreach (var margin in editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>()) margin.InvalidateVisual();
        foreach (var margin in PageEditor.Editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>()) margin.InvalidateVisual();
        if (dialog.Destination is { } destination)
        {
            try { await NavigateToAsync(destination); ShowLineMemos(destination.Panel, showResolved: dialog.ShowResolved.IsChecked == true); }
            catch (Exception ex) { ShowError(ex); }
        }
    }
    private async Task ShowMemoListAsync()
    {
        var dialog = new MemoListDialog(session) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Destination is { } d) { await NavigateToAsync(d); ShowLineMemos(d.Panel, showResolved: dialog.IncludeResolved); }
    }
    private void ShowFooterInsert()
    {
        var dialog = new FooterInputDialog(lastPreviewPage) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Result is { } range)
            session.FooterRanges = FooterRange.Insert(session.FooterRanges, range);
    }
    private async Task EditTableAsync()
    {
        if (session.ActiveBlock is not { } block || block.Metadata.IsBlocked || engine is not LocalBrailleEngine local) return;
        var source = block.Source.Contains("<!표>") ? block.Source : block.Metadata.TableSource;
        if (source is null) throw new ArgumentException("현재 블록에는 편집할 표 구조가 없습니다.");
        var dialog = new TableDialog(source, block.Metadata, session.Options, local) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Translation is null) return;
        var choice = ChooseTranslation(block); if (choice == ManualEditDecision.Cancel) return;
        EditStructure(() =>
        {
            block.Metadata = block.Metadata with { Type = "table", TableSource = dialog.SourceText, TableDisplayText = dialog.SourceText, OriginalSource = block.Metadata.OriginalSource ?? block.Source };
            block.Source = dialog.SourceText;
            if (choice == ManualEditDecision.Overwrite) { block.Braille = dialog.Translation.Cells; block.Translation = dialog.Translation; block.HasManualBraille = false; }
        });
        translatedRevisions[block] = block.SourceRevision;
        session.Status = "표를 반영했습니다. Ctrl+S로 저장해 주세요.";
        await Task.CompletedTask;
    }
    private void PopulateTagMenu()
    {
        var groups = new (string Name, string[] Tags)[]
        {
            ("정렬", ["왼쪽정렬", "가운데정렬", "오른쪽정렬"]),
            ("점자 줄", ["상단줄", "중단줄", "하단줄", "상단굵은줄", "하단굵은줄", "표지시작", "표지세로", "표지끝"]),
            ("표 테두리", ["표시작", "표중앙", "표끝", "표구분선"]),
            ("한글 강조", ["강조", "굵은", "색강조", "취소선"]),
            ("빈칸", ["밑줄빈칸", "네모빈칸"])
        };
        MenuItem Item(string name)
        {
            var item = new MenuItem { Header = name, Tag = "tag:" + name };
            if (name == "쪽바꿈") item.InputGestureText = "Ctrl+Enter";
            item.Click += Action_Click; return item;
        }
        // Keep the documented insertion order; retain the additional supported tags.
        var existing = AllTagsMenu.Items.Cast<object>().ToArray();
        AllTagsMenu.Items.Clear();
        AllTagsMenu.Items.Add(Item("쪽바꿈")); AllTagsMenu.Items.Add(Item("장바꿈")); AllTagsMenu.Items.Add(new Separator());
        AllTagsMenu.Items.Add(Item("머리말"));
        foreach (var entry in existing.OfType<MenuItem>().Where(m => m.Tag is "footerInsert" or "footer")) AllTagsMenu.Items.Add(entry);
        AllTagsMenu.Items.Add(new Separator());
        foreach (var group in groups)
        {
            var menu = new MenuItem { Header = group.Name };
            foreach (var tag in group.Tags) menu.Items.Add(Item(tag));
            AllTagsMenu.Items.Add(menu);
        }
        var note = Item("주"); note.Header = "점역자 주"; AllTagsMenu.Items.Add(note);
        AllTagsMenu.Items.Add(new Separator());
        foreach (var entry in existing.OfType<MenuItem>().Where(m => m.Tag is "math" or "symbol").OrderBy(m => m.Tag is "math" ? 0 : 1)) AllTagsMenu.Items.Add(entry);
    }
    private async Task ShowAlternativesAsync()
    {
        if (session.ActiveBlock is not { } block) return;
        if (block.Metadata.Alternatives?.Length > 0 && engine is LocalBrailleEngine local)
        {
            var dialog = new AlternativeDialog(block, session.Options, local) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Translation is null) return;
            var decision = ChooseTranslation(block);
            if (decision == ManualEditDecision.Cancel) return;
            EditStructure(() =>
            {
            block.Metadata = block.Metadata with { SelectedIndex = dialog.SelectedIndex, OriginalSource = block.Metadata.OriginalSource ?? block.Source };
            block.Source = dialog.SourceText;
            if (decision == ManualEditDecision.Overwrite)
            { block.Braille = dialog.Translation.Cells; block.Translation = dialog.Translation; block.HasManualBraille = false; block.State = "선택한 대체 초안"; }
            });
            translatedRevisions[block] = block.SourceRevision;
            session.Status = "대체 초안을 적용했습니다. Ctrl+S로 저장해 주세요.";
        }
        else if (block.Translation?.Drafts.Length > 0)
        {
            var dialog = new DraftDialog(block.Translation.Drafts) { Owner = this };
            if (dialog.ShowDialog() != true || ChooseTranslation(block) != ManualEditDecision.Overwrite) return;
            var draft = block.Translation.Drafts[dialog.SelectedIndex];
            block.Metadata = block.Metadata with { RenderMode = draft.RenderMode };
            block.Braille = draft.Cells; block.Translation = block.Translation with { Cells = draft.Cells, Breaks = draft.Breaks, RenderMode = draft.RenderMode };
            block.HasManualBraille = false; block.State = "표 초안 · " + draft.Label;
        }
        else MessageBox.Show(this, "이 블록에는 대체 초안이 없습니다. 그림·표 등의 초안이 제공된 블록을 선택해 주세요.", "대체 초안");
        await Task.CompletedTask;
    }
    private async Task InsertSymbolAsync()
    {
        if (engine is not LocalBrailleEngine local) return;
        var panel = ActiveSearchPanel;
        var dialog = new SymbolDialog(await local.SymbolsAsync(documentLifetime.Token)) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Selected is not { } symbol) return;
        var editor = SearchEditor(panel);
        if (!editor.IsReadOnly) { editor.SelectedText = panel == SearchPanel.Text ? symbol.Char : symbol.Cells; editor.Focus(); }
    }
    private async Task EditSnippetsAsync()
    {
        var dialog = new SnippetDialog(accountPreferences) { Owner = this };
        if (dialog.ShowDialog() == true) { accountPreferences = dialog.Result; await PersistAccountPreferencesAsync(); }
    }
    private void InsertSnippet(int slot, SearchPanel panel)
    {
        if (ActiveSearchPanel != panel) { session.Status = panel == SearchPanel.Text ? "텍스트 패널에서 Ctrl+숫자를 눌러 주세요." : "점자 패널에서 Alt+숫자를 눌러 주세요."; return; }
        var value = (panel == SearchPanel.Text ? accountPreferences.TextSnippets : accountPreferences.BrailleSnippets)?[slot];
        if (string.IsNullOrEmpty(value)) { session.Status = $"{slot}번 상용구가 비어 있습니다. 설정 → 상용구에서 등록해 주세요."; return; }
        var editor = SearchEditor(panel); if (!editor.IsReadOnly) { editor.SelectedText = value; editor.Focus(); }
    }
    private void ShowRules()
    {
        if (session.ActiveBlock is not { } block) return;
        var editor = SearchEditor(SearchPanel.Braille);
        var rows = AppliedRules.Select(block, editor.SelectionStart, editor.SelectionLength);
        var at = editor.Document.GetLocation(editor.SelectionStart);
        new AppliedRulesDialog(api, rows, block.HasManualBraille || block.Translation?.SourceText is { } source && source != block.Source,
            block.Source, editor.SelectionLength > 0 ? editor.SelectedText : block.Braille, $"{at.Line}줄 {at.Column}칸") { Owner = this }.ShowDialog();
    }
    private void RuleSearch_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        if (EnsureAccount()) new RuleDialog(api, [], "", "", RuleSearch.Text.Trim()) { Owner = this }.ShowDialog();
    }
}
