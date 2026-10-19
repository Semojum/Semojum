using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private void ShowLineMemos(SearchPanel panel, int? requestedLine = null)
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } block) return;
        var editor = SearchEditor(panel); var line = requestedLine ?? editor.TextArea.Caret.Line - 1;
        if (line < 0 || line >= editor.Document.LineCount) return;
        var text = editor.Document.GetText(editor.Document.GetLineByNumber(line + 1));
        new MemoThreadDialog(block, panel == SearchPanel.Text ? "TEXT" : "BRAILLE", line, text, EditStructure) { Owner = this }.ShowDialog();
        foreach (var margin in editor.TextArea.LeftMargins.OfType<Controls.MemoMargin>()) margin.InvalidateVisual();
    }
    private async Task ShowMemoListAsync()
    {
        var dialog = new MemoListDialog(session) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Destination is { } d) { await NavigateToAsync(d); ShowLineMemos(d.Panel); }
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
        foreach (var tag in BrailleTags.All)
        {
            var item = new MenuItem { Header = tag.Name, Tag = "tag:" + tag.Name }; item.Click += Action_Click; AllTagsMenu.Items.Add(item);
        }
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
        else session.Status = "이 블록에 전달된 대체 초안이 없습니다.";
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
        if (!EnsureAccount() || session.ActiveBlock is not { } block) return;
        var evidence = block.Metadata.Rules ?? [];
        new RuleDialog(api, evidence, SourceEditor.SelectedText.Length > 0 ? SourceEditor.SelectedText : block.Source,
            Braille.Editor.SelectedText.Length > 0 ? Braille.Editor.SelectedText : block.Braille) { Owner = this }.ShowDialog();
    }
}
