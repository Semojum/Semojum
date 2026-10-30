using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.ComponentModel;
using SharpVectors.Converters;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private DocumentSession? draftSession;
    private EditorBlock? draftBlock;
    private DocumentSession? dockSession;
    private EditorBlock? dockBlock;
    private TableDraftSnapshot? dockTable;
    private BrailleOptions? dockOptions;
    private CancellationTokenSource? dockTranslationCancellation;
    private int dockGeneration, dockSelectedIndex;
    private string dockSource = "";
    private BrailleTranslation? dockTranslation;
    private bool dockApplying;

    private void InitializeDraftAvailability()
    {
        DataContextChanged += (_, _) => ObserveDraftSession();
        Loaded += (_, _) => { UpdateDraftAvailability(); UpdateDraftDivider(); };
        SourcePanel.SizeChanged += (_, _) => UpdateDraftDivider();
        DraftDock.SizeChanged += (_, _) => UpdateDraftDivider();
        PanelGrid.LayoutUpdated += (_, _) => UpdateDraftDivider();
        PreviewKeyDown += (_, e) =>
        {
            if (DraftDock.Visibility != Visibility.Visible) return;
            if (e.Key == Key.Escape) { CloseDraftDock(); e.Handled = true; }
            else if (e.Key == Key.Enter && DraftDock.IsKeyboardFocusWithin) { ApplyDraftDock(); e.Handled = true; }
        };
        ObserveDraftSession();
    }

    private void ObserveDraftSession()
    {
        if (dockSession is not null && !ReferenceEquals(dockSession, session)) CloseDraftDock();
        if (draftSession is not null) draftSession.PropertyChanged -= DraftSessionChanged;
        draftSession = session;
        if (draftSession is not null) draftSession.PropertyChanged += DraftSessionChanged;
        ObserveDraftBlock();
    }

    private void DraftSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) ObserveDraftBlock();
        else if (e.PropertyName == nameof(DocumentSession.IsReadOnly))
        { if (draftSession?.IsReadOnly == true) CloseDraftDock(); UpdateDraftAvailability(); }
        else if (e.PropertyName == nameof(DocumentSession.Options) && dockSession is not null)
        { CloseDraftDock(); if (draftSession is not null) draftSession.Status = "점역 설정이 바뀌어 대체 초안을 다시 선택해 주세요."; }
    }

    private void ObserveDraftBlock()
    {
        if (dockBlock is not null && !ReferenceEquals(dockBlock, draftSession?.ActiveBlock)) CloseDraftDock();
        if (draftBlock is not null) draftBlock.PropertyChanged -= DraftBlockChanged;
        draftBlock = draftSession?.ActiveBlock;
        if (draftBlock is not null) draftBlock.PropertyChanged += DraftBlockChanged;
        UpdateDraftAvailability();
    }

    private void DraftBlockChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!dockApplying && dockBlock is not null && e.PropertyName is nameof(EditorBlock.Source) or nameof(EditorBlock.Braille) or nameof(EditorBlock.Metadata) or nameof(EditorBlock.Translation) or nameof(EditorBlock.IsBrailleLocked))
            CloseDraftDock();
        if (e.PropertyName is nameof(EditorBlock.Metadata) or nameof(EditorBlock.Translation) or nameof(EditorBlock.IsBrailleLocked))
            UpdateDraftAvailability();
    }

    private bool CanSelectDraft() => session is { IsReadOnly: false, ActiveBlock: { IsBrailleLocked: false } block }
        && !block.Metadata.IsBlocked && (block.Metadata.Alternatives?.Length > 0 || block.Translation?.Drafts.Length > 0);

    private void UpdateDraftAvailability()
    {
        var enabled = CanSelectDraft();
        DependencyObject? parent = DraftIcon;
        while (parent is not null && parent is not Button) parent = VisualTreeHelper.GetParent(parent);
        if (parent is Button button) button.IsEnabled = enabled;
        foreach (var menu in new[] { SourceEditor.ContextMenu, PageEditor.Editor.ContextMenu, Braille.Editor.ContextMenu,
                     Braille.GridView.ContextMenu, detachedPane?.Editor.ContextMenu, detachedPane?.GridView.ContextMenu })
            if (menu is not null)
                foreach (var item in menu.Items.OfType<MenuItem>().Where(item => Equals(item.Header, "대체 초안 보기"))) item.IsEnabled = enabled;
    }

    private void UpdateDraftDivider()
    {
        if (DraftDock.Visibility != Visibility.Visible) return;
        var sourceVisible = SourcePanel.Visibility == Visibility.Visible && PanelGrid.Children.Contains(SourcePanel);
        var brailleVisible = BrailleContainer.Visibility == Visibility.Visible && PanelGrid.Children.Contains(BrailleContainer);
        if (!sourceVisible && !brailleVisible) { CloseDraftDock(); return; }
        var sourceColumn = sourceVisible ? Grid.GetColumn(SourcePanel) : Grid.GetColumn(BrailleContainer);
        var brailleColumn = brailleVisible ? Grid.GetColumn(BrailleContainer) : sourceColumn;
        var start = Math.Min(sourceColumn, brailleColumn); var span = Math.Abs(brailleColumn - sourceColumn) + 1;
        if (Grid.GetColumn(DraftDock) != start) Grid.SetColumn(DraftDock, start);
        if (Grid.GetColumnSpan(DraftDock) != span) Grid.SetColumnSpan(DraftDock, span);
        if (DraftDock.ActualWidth <= 0) return;
        var width = sourceVisible && brailleVisible ? Math.Clamp(SourcePanel.ActualWidth, 0, DraftDock.ActualWidth) : DraftDock.ActualWidth / 2;
        if (Math.Abs(DraftTextColumn.Width.Value - width) > 0.5) DraftTextColumn.Width = new GridLength(width);
    }

    private void OpenDraftDock(EditorBlock block, TableDraftSnapshot? table = null)
    {
        CloseDraftDock();
        var drafts = table is null ? block.Metadata.Alternatives ?? [] : table.Drafts.Select(d => new AlternativeDraft(d.Label, d.SourceText!, null, d.Cells, null)).ToArray();
        if (drafts.Length == 0) return;
        dockSession = session; dockBlock = block; dockTable = table; dockOptions = session.Options;
        DraftDockTitle.Text = table is null ? $"대체 초안 · 그림 설명 {drafts.Length}안" : $"대체 초안 · 표 점역 {drafts.Length}안";
        var group = "DraftDock" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < drafts.Length; i++)
        {
            var index = i; var draft = drafts[i];
            var normal = DraftIconAsset("draft-dock-radio.svg", 20);
            var selected = DraftIconAsset("draft-dock-radio-selected.svg", 20);
            var dot = DraftIconAsset("draft-dock-radio-dot.svg", 8);
            selected.Visibility = dot.Visibility = Visibility.Collapsed;
            var icon = new Grid { Width = 20, Height = 20 }; icon.Children.Add(normal); icon.Children.Add(selected); icon.Children.Add(dot);
            var label = new TextBlock { Text = $"{(i < 20 ? char.ConvertFromUtf32(0x2460 + i) : (i + 1).ToString())} {draft.Label}", FontFamily = new FontFamily("Malgun Gothic"), FontSize = 14, Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(icon); content.Children.Add(label);
            var radio = new RadioButton { GroupName = group, Content = content, Height = 38, Margin = new(15, 0, 0, 0), ToolTip = draft.Reason,
                Template = new ControlTemplate(typeof(RadioButton)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) } };
            System.Windows.Automation.AutomationProperties.SetName(radio, draft.Label);
            radio.Checked += async (_, _) =>
            {
                normal.Visibility = Visibility.Collapsed; selected.Visibility = dot.Visibility = Visibility.Visible; label.FontWeight = FontWeights.Bold;
                await PreviewDraftAsync(index);
            };
            radio.Unchecked += (_, _) => { normal.Visibility = Visibility.Visible; selected.Visibility = dot.Visibility = Visibility.Collapsed; label.FontWeight = FontWeights.Normal; };
            DraftChoiceGrid.Children.Add(radio);
        }
        DraftDock.Visibility = Visibility.Visible; UpdateDraftDivider();
        var selectedIndex = table is null ? block.Metadata.SelectedIndex ?? 0 : Array.FindIndex(table.Drafts, d => d.RenderMode == block.Metadata.RenderMode);
        ((RadioButton)DraftChoiceGrid.Children[Math.Clamp(selectedIndex, 0, drafts.Length - 1)]).IsChecked = true;
    }

    private static SvgViewbox DraftIconAsset(string name, double size) => new()
    { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}"), Width = size, Height = size, Stretch = Stretch.None };

    private async Task PreviewDraftAsync(int index)
    {
        if (dockTable is { } table && dockBlock is { } tableBlock)
        {
            if (index < 0 || index >= table.Drafts.Length) return;
            var tableDraft = table.Drafts[index]; dockSelectedIndex = index; dockSource = tableDraft.SourceText!;
            dockTranslation = table.Translation with { Cells = tableDraft.Cells, Breaks = tableDraft.Breaks, RenderMode = tableDraft.RenderMode, SourceText = tableBlock.Source };
            DraftTextPreview.Text = tableDraft.SourceText!; DraftTextPreview.ToolTip = null; DraftBraillePreview.Text = tableDraft.Cells;
            return;
        }
        if (dockBlock is not { } block || block.Metadata.Alternatives is not { } drafts || index >= drafts.Length) return;
        dockTranslationCancellation?.Cancel(); dockTranslationCancellation?.Dispose();
        dockTranslationCancellation = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
        var token = dockTranslationCancellation.Token; var generation = ++dockGeneration;
        var draft = drafts[index]; var source = draft.TaggedText ?? draft.Text;
        dockSelectedIndex = index; dockSource = source; dockTranslation = null;
        DraftTextPreview.Text = source; DraftTextPreview.ToolTip = draft.Reason; DraftBraillePreview.Text = "점역 중…";
        try
        {
            var result = draft.TaggedText is null && draft.LegacyBraille is not null ? BrailleTranslation.Plain(draft.LegacyBraille)
                : engine is LocalBrailleEngine local ? await local.TranslateElementAsync(source, block.Metadata with { Type = source.Contains("<!표>") ? "table" : "caption" }, session.Options, token)
                : await engine.TranslateBlockAsync(new EditorBlock { Source = source, Metadata = block.Metadata }, token);
            if (token.IsCancellationRequested || generation != dockGeneration || !ReferenceEquals(dockBlock, block)) return;
            dockTranslation = result with { SourceText = source }; DraftBraillePreview.Text = dockTranslation.Cells;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { if (generation == dockGeneration && ReferenceEquals(dockBlock, block)) DraftBraillePreview.Text = "점역 미리보기 실패: " + ex.Message; }
    }

    private void DraftDockClose_Click(object sender, RoutedEventArgs e) => ApplyDraftDock();

    private void ApplyDraftDock()
    {
        if (dockBlock is not { } block || dockSession != session || session.ActiveBlock != block || !session.Blocks.Contains(block))
        { CloseDraftDock(); return; }
        if (dockTable is { } table)
        {
            ManualEditDecision? selected = null;
            dockApplying = true;
            try { ApplyTableDraft(table, dockSelectedIndex, b => { selected = ChooseTranslation(b); return selected.Value; }); }
            finally { dockApplying = false; }
            if (selected == ManualEditDecision.Keep) session.Status = "직접 수정한 점자와 기존 표 초안을 유지했습니다.";
            if (selected != ManualEditDecision.Cancel) CloseDraftDock();
            return;
        }
        if (dockTranslation is null)
        {
            session.Status = DraftBraillePreview.Text.StartsWith("점역 미리보기 실패:", StringComparison.Ordinal)
                ? "점역 미리보기에 실패했습니다. 오류를 확인하거나 다른 초안을 선택해 주세요."
                : "선택한 초안의 점역이 끝난 뒤 적용할 수 있습니다.";
            return;
        }
        var owner = session; var options = dockOptions; var generation = dockGeneration;
        var sourceRevision = block.SourceRevision; var brailleRevision = block.BrailleRevision; var metadataRevision = block.MetadataRevision;
        var manual = block.HasManualBraille; var translation = dockTranslation;
        var selectedIndex = dockSelectedIndex; var selectedSource = dockSource;
        var decision = ChooseTranslation(block);
        if (decision == ManualEditDecision.Cancel) return;
        if (resourcesReleased || !ReferenceEquals(owner, session) || !ReferenceEquals(dockSession, owner) || !ReferenceEquals(dockBlock, block)
            || !ReferenceEquals(owner.ActiveBlock, block) || !owner.Blocks.Contains(block) || owner.IsReadOnly || block.IsBrailleLocked || block.Metadata.IsBlocked
            || owner.Options != options || dockGeneration != generation || !ReferenceEquals(dockTranslation, translation)
            || block.SourceRevision != sourceRevision || block.BrailleRevision != brailleRevision || block.MetadataRevision != metadataRevision || block.HasManualBraille != manual)
        { owner.Status = "대체 초안 확인 중 문서가 바뀌어 적용하지 않았습니다."; return; }
        dockApplying = true;
        try
        {
            EditStructure(() =>
            {
                var previousSource = block.Source;
                block.Metadata = block.Metadata with { SelectedIndex = selectedIndex, OriginalSource = block.Metadata.OriginalSource ?? block.Source,
                    Rules = string.Equals(previousSource, selectedSource, StringComparison.Ordinal) ? block.Metadata.Rules : null };
                block.Source = selectedSource;
                if (decision == ManualEditDecision.Overwrite)
                { block.Braille = translation.Cells; block.Translation = translation; block.HasManualBraille = false; block.State = "선택한 대체 초안"; }
                else if (!string.Equals(previousSource, selectedSource, StringComparison.Ordinal))
                { block.Translation = null; block.State = "직접 수정 점자 유지"; }
            });
            translatedRevisions[block] = block.SourceRevision;
            session.Status = "대체 초안을 적용했습니다. Ctrl+S로 저장해 주세요.";
        }
        finally { dockApplying = false; CloseDraftDock(); }
    }

    private void CloseDraftDock()
    {
        ++dockGeneration; dockTranslationCancellation?.Cancel(); dockTranslationCancellation?.Dispose(); dockTranslationCancellation = null;
        dockSession = null; dockBlock = null; dockTable = null; dockOptions = null; dockTranslation = null;
        DraftDock.Visibility = Visibility.Collapsed; DraftChoiceGrid.Children.Clear();
    }

    private async Task EditMathAsync()
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } active || engine is not LocalBrailleEngine local) return;
        if (active.Metadata.IsBlocked || active.IsBrailleLocked) { session.Status = "잠긴 블록은 수식 편집을 할 수 없습니다."; return; }
        var existing = active.Metadata.Type == "formula";
        if (!existing)
        {
            var inline = SelectedInlineFormula(active);
            if (inline.Status == FormulaRangeStatus.Malformed)
            { session.Status = "수식 태그가 중첩되었거나 닫히지 않았습니다. 원문을 확인해 주세요."; return; }
            if (inline.Status == FormulaRangeStatus.Selected) { await EditInlineMathAsync(active, inline, local); return; }
        }
        string? latex = null;
        if (existing)
        {
            latex = active.Source;
            if (latex.Contains("<!수식", StringComparison.Ordinal) || latex.Contains("<!/수식", StringComparison.Ordinal))
            {
                var full = FormulaRanges.Find(latex, 0, latex.Length);
                if (full.Status != FormulaRangeStatus.Selected || full.Start != 0 || full.Length != latex.Length)
                { session.Status = "수식 태그가 중첩되었거나 닫히지 않았습니다. 원문을 확인해 주세요."; return; }
                latex = full.Latex;
            }
        }
        var owner = session; var sourceRevision = active.SourceRevision; var brailleRevision = active.BrailleRevision; var metadataRevision = active.MetadataRevision; var options = owner.Options;
        var dialog = new MathDialog((value, ct) => local.TranslateElementAsync(value, new(Type: "formula"), session.Options, ct), latex,
            existing && active.HasManualBraille ? active.Braille : null) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Translation is null) return;
        if (dialog.Latex.Contains("<!수식", StringComparison.Ordinal) || dialog.Latex.Contains("<!/수식", StringComparison.Ordinal))
        { owner.Status = "수식 안에 수식 태그를 중첩할 수 없습니다."; return; }
        var decision = existing ? ChooseTranslation(active) : ManualEditDecision.Overwrite;
        if (decision == ManualEditDecision.Cancel) return;
        if (resourcesReleased || owner.IsReadOnly || !ReferenceEquals(owner, session) || !ReferenceEquals(owner.ActiveBlock, active) || !owner.Blocks.Contains(active) || active.SourceRevision != sourceRevision
            || active.BrailleRevision != brailleRevision || active.MetadataRevision != metadataRevision || owner.Options != options)
        { owner.Status = "수식 편집 중 문서가 바뀌어 적용하지 않았습니다."; return; }
        EditorBlock block = active;
        EditStructure(() =>
        {
            if (!existing)
            {
                block = new EditorBlock { Id = session.Blocks.Max(b => b.Id) + 1, Page = active.Page, Heading = "수식", Metadata = new(Type: "formula") };
                session.Blocks.Insert(session.Blocks.IndexOf(active) + 1, block);
            }
            var source = $"<!수식>{dialog.Latex}<!/수식>";
            var sourceChanged = !string.Equals(block.Source, source, StringComparison.Ordinal);
            block.Source = source;
            if (sourceChanged) block.Metadata = block.Metadata with { Rules = null };
            if (decision == ManualEditDecision.Keep)
            {
                if (sourceChanged) block.Translation = null;
                block.State = "직접 수정 점자 유지";
            }
            else
            {
                block.Braille = dialog.BrailleInput.Text;
                block.Translation = dialog.Translation with { Cells = block.Braille, SourceText = block.Source,
                    RuleTrail = dialog.Edited ? null : dialog.Translation.RuleTrail, Breaks = dialog.Edited ? [] : dialog.Translation.Breaks };
                block.HasManualBraille = dialog.Edited;
            }
        });
        session.ActiveBlock = block; translatedRevisions[block] = block.SourceRevision;
        await Task.CompletedTask;
    }
    private async Task EditInlineMathAsync(EditorBlock block, FormulaRange range, LocalBrailleEngine local)
    {
        var owner = session; var original = block.Source;
        var sourceRevision = block.SourceRevision; var brailleRevision = block.BrailleRevision; var metadataRevision = block.MetadataRevision; var options = owner.Options;
        var dialog = new MathDialog((value, ct) => local.TranslateElementAsync(value, new(Type: "formula"), owner.Options, ct), range.Latex, inlinePreview: true) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Translation is null) return;
        if (owner.IsReadOnly || !ReferenceEquals(owner, session) || !ReferenceEquals(owner.ActiveBlock, block) || !owner.Blocks.Contains(block) || block.SourceRevision != sourceRevision
            || block.BrailleRevision != brailleRevision || block.MetadataRevision != metadataRevision || owner.Options != options)
        { owner.Status = "수식 편집 중 문서가 바뀌어 적용하지 않았습니다."; return; }
        if (dialog.Latex.Contains("<!수식", StringComparison.Ordinal) || dialog.Latex.Contains("<!/수식", StringComparison.Ordinal))
        { owner.Status = "수식 안에 수식 태그를 중첩할 수 없습니다."; return; }
        var decision = ChooseTranslation(block);
        if (decision == ManualEditDecision.Cancel) return;
        var replacement = $"<!수식>{dialog.Latex}<!/수식>";
        var source = original[..range.Start] + replacement + original[(range.Start + range.Length)..];
        if (source == original) return;
        BrailleTranslation? translated = null;
        if (decision == ManualEditDecision.Overwrite)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(35));
                translated = await (InlineMathTranslationForChecks ?? local.TranslateElementAsync)(source, block.Metadata, options, timeout.Token);
                if (translated.NeedsReview) { owner.Status = "블록 전체 점역 결과를 확인할 수 없어 수식 변경을 적용하지 않았습니다."; return; }
            }
            catch (OperationCanceledException) { owner.Status = "수식 점역이 취소되어 기존 원문과 점자를 유지했습니다."; return; }
            catch (Exception ex) { owner.Status = "수식 점역에 실패해 기존 원문과 점자를 유지했습니다: " + ex.Message; return; }
        }
        if (resourcesReleased || owner.IsReadOnly || !ReferenceEquals(owner, session) || !ReferenceEquals(owner.ActiveBlock, block) || !owner.Blocks.Contains(block) || block.Source != original
            || block.SourceRevision != sourceRevision || block.BrailleRevision != brailleRevision || block.MetadataRevision != metadataRevision || owner.Options != options)
        { owner.Status = "점역 중 문서가 바뀌어 수식 변경을 적용하지 않았습니다."; return; }
        EditStructure(() =>
        {
            block.Source = source; block.Metadata = block.Metadata with { Rules = null };
            if (decision == ManualEditDecision.Keep) { block.Translation = null; block.State = "직접 수정 점자 유지"; }
            else { block.Braille = translated!.Cells; block.Translation = translated with { SourceText = source }; block.HasManualBraille = false; }
            block.SourceCaret = range.Start + replacement.Length;
        });
        session.ActiveBlock = block; translatedRevisions[block] = block.SourceRevision;
        owner.Status = decision == ManualEditDecision.Keep ? "수식 원문을 바꾸고 기존 점자를 유지했습니다. 점자를 확인해 주세요." : "수식을 바꾸고 블록 전체를 다시 점역했습니다.";
    }
    private async void ShowLineMemos(SearchPanel panel, int? requestedLine = null, bool showResolved = false)
    {
        if (session.IsReadOnly || session.ActiveBlock is not { } block) return;
        var editor = SearchEditor(panel);
        var resolved = panel == SearchPanel.Text && lastEditor == PageEditor.Editor
            ? PageEditor.ResolveLine(PageEditor.Editor.TextArea.Caret.Line - 1) : (Block: (EditorBlock?)null, Line: 0);
        var line = requestedLine ?? (resolved.Block == block ? resolved.Line : editor.TextArea.Caret.Line - 1);
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
    private void ShowHeaderInsert()
    {
        if (resourcesReleased || accountStarting || session.IsReadOnly || SourceEditor.IsReadOnly) return;
        CompleteSourceInput();
        var target = PageEditor.Visibility == System.Windows.Visibility.Visible ? PageEditor.Editor : SourceEditor;
        var owner = session; var document = target.Document; var version = owner.ChangeVersion;
        var start = target.SelectionStart; var length = target.SelectionLength;
        var dialog = new HeaderInputDialog(target.SelectedText) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!ReferenceEquals(owner, session) || !ReferenceEquals(document, target.Document) || owner.IsReadOnly || SourceEditor.IsReadOnly || owner.ChangeVersion != version)
        { owner.Status = "머리말 편집 중 문서가 바뀌어 적용하지 않았습니다."; return; }
        target.Select(start, length); target.SelectedText = BrailleTags.Get("머리말").Insert(dialog.Result);
        target.Select(start + BrailleTags.Get("머리말").Open.Length, dialog.Result.Length); target.Focus();
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
        var header = new MenuItem { Header = "머리말", Tag = "headerInsert" }; header.Click += (_, _) => ShowHeaderInsert(); AllTagsMenu.Items.Add(header);
        foreach (var entry in existing.OfType<MenuItem>().Where(m => m.Tag is "footerInsert" or "footer")) AllTagsMenu.Items.Add(entry);
        AllTagsMenu.Items.Add(new Separator());
        foreach (var group in groups)
        {
            var menu = new MenuItem { Header = group.Name };
            foreach (var tag in group.Tags)
            {
                var child = Item(tag); child.Style = (Style)FindResource("FinalDesktopNestedItem");
                child.Header = tag switch
                {
                    "왼쪽정렬" => "왼쪽", "가운데정렬" => "가운데", "오른쪽정렬" => "오른쪽",
                    "상단줄" => "상단 줄", "중단줄" => "중단 줄", "하단줄" => "하단 줄",
                    "상단굵은줄" => "상단 굵은 줄", "하단굵은줄" => "하단 굵은 줄",
                    "표시작" => "표 시작 테두리 선", "표중앙" => "표 중앙 테두리 선", "표끝" => "표 끝 테두리 선", "표구분선" => "표 구분선",
                    "강조" => "밑줄", "굵은" => "굵게", "색강조" => "색상 또는 모양", _ => tag
                };
                menu.Items.Add(child);
            }
            AllTagsMenu.Items.Add(menu);
        }
        var note = Item("주"); note.Header = "점역자 주"; AllTagsMenu.Items.Add(note);
        AllTagsMenu.Items.Add(new Separator());
        foreach (var entry in existing.OfType<MenuItem>().Where(m => m.Tag is "math" or "symbol").OrderBy(m => m.Tag is "math" ? 0 : 1)) AllTagsMenu.Items.Add(entry);
    }
    private async Task ShowAlternativesAsync()
    {
        if (!CanSelectDraft() || session.ActiveBlock is not { } block) return;
        if (block.Metadata.Alternatives?.Length > 0) OpenDraftDock(block);
        else if (block.Translation?.Drafts.Length > 0)
        {
            var selection = CaptureTableDraft(block);
            if (HasTableDraftText(selection)) OpenDraftDock(block, selection);
            else
            {
                var dialog = new DraftDialog(selection.Drafts) { Owner = this };
                if (dialog.ShowDialog() == true) ApplyTableDraft(selection, dialog.SelectedIndex);
            }
        }
        else MessageBox.Show(this, "이 블록에는 대체 초안이 없습니다. 그림·표 등의 초안이 제공된 블록을 선택해 주세요.", "대체 초안");
        await Task.CompletedTask;
    }
    internal sealed record TableDraftSnapshot(DocumentSession Owner, EditorBlock Block, BrailleTranslation Translation,
        int SourceRevision, int BrailleRevision, int MetadataRevision, bool Manual, BrailleOptions Options, BrailleDraft[] Drafts);
    internal TableDraftSnapshot CaptureTableDraft(EditorBlock block)
    {
        var translation = block.Translation ?? throw new InvalidOperationException("표 점역 초안이 없습니다.");
        return new(session, block, translation, block.SourceRevision, block.BrailleRevision, block.MetadataRevision, block.HasManualBraille, session.Options,
            translation.Drafts.Select(d => d with { Breaks = d.Breaks.ToArray() }).ToArray());
    }
    private static bool HasTableDraftText(TableDraftSnapshot selection)
    {
        if (selection.Block.Metadata.Type != "table" || selection.Drafts.Length == 0 || selection.Translation.SourceText != selection.Block.Source) return false;
        try
        {
            foreach (var draft in selection.Drafts)
            {
                if (string.IsNullOrWhiteSpace(draft.SourceText) || string.IsNullOrEmpty(draft.Cells)) return false;
                _ = TaggedTable.Read(draft.SourceText);
            }
            return true;
        }
        catch (ArgumentException) { return false; }
    }
    internal void ApplyTableDraft(TableDraftSnapshot selection, int index, Func<EditorBlock, ManualEditDecision>? confirm = null)
    {
        var (owner, block, translation, sourceRevision, brailleRevision, metadataRevision, manual, options, drafts) = selection;
        bool StillCurrent() => ReferenceEquals(session, owner) && ReferenceEquals(owner.ActiveBlock, block) && owner.Blocks.Contains(block)
            && !owner.IsReadOnly && !block.IsBrailleLocked && !block.Metadata.IsBlocked && block.HasManualBraille == manual && owner.Options == options
            && block.SourceRevision == sourceRevision && block.BrailleRevision == brailleRevision && block.MetadataRevision == metadataRevision
            && ReferenceEquals(block.Translation, translation) && translation.Drafts.Length == drafts.Length
            && translation.Drafts.Zip(drafts).All(pair => pair.First.RenderMode == pair.Second.RenderMode && pair.First.Label == pair.Second.Label
                && pair.First.Cells == pair.Second.Cells && pair.First.SourceText == pair.Second.SourceText && pair.First.Breaks.SequenceEqual(pair.Second.Breaks));
        bool RequireCurrent()
        {
            if (StillCurrent() && index >= 0 && index < drafts.Length) return true;
            owner.Status = "블록 또는 초안이 변경되었습니다. 다시 선택해 주세요."; return false;
        }
        if (!RequireCurrent()) return;
        if ((confirm?.Invoke(block) ?? ChooseTranslation(block)) != ManualEditDecision.Overwrite || !RequireCurrent()) return;
        var draft = drafts[index];
        var sameResult = translation.RenderMode == draft.RenderMode && translation.Cells == draft.Cells && translation.Breaks.SequenceEqual(draft.Breaks);
        EditStructure(() =>
        {
            block.Metadata = block.Metadata with { RenderMode = draft.RenderMode };
            block.Braille = draft.Cells; block.Translation = translation with { Cells = draft.Cells, Breaks = draft.Breaks, RenderMode = draft.RenderMode,
                RuleTrail = sameResult ? translation.RuleTrail : null, Spacing = sameResult ? translation.Spacing : null };
            block.HasManualBraille = false; block.State = "표 초안 · " + draft.Label;
        });
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
