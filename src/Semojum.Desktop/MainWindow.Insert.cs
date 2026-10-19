using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
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
            translator.Invalidate(block);
            block.Metadata = block.Metadata with { SelectedIndex = dialog.SelectedIndex, OriginalSource = block.Metadata.OriginalSource ?? block.Source };
            documents.Source(block).Text = dialog.SourceText;
            if (decision == ManualEditDecision.Overwrite)
            { block.Braille = dialog.Translation.Cells; block.Translation = dialog.Translation; block.HasManualBraille = false; block.State = "선택한 대체 초안"; }
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
