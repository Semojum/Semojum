using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private BrailleGridView? lastGrid;
    private DocumentSession? gridOwner;
    private long composedVersion = -1;
    private Task<ComposedBraille>? composing;
    private static string DescribeSpacingBlocks(int[] ids) => string.Join(", ", ids.Take(10))
        + (ids.Length > 10 ? $" 외 {ids.Length - 10}개" : "");
    private void ConnectGrid(BraillePane pane)
    {
        pane.GridView.ShowMemos = MemoToggle.IsChecked; pane.GridView.ShowReviews = ReviewToggle.IsChecked;
        async Task<bool> NavigateMarkerAsync(EditorBlock block, int line, DocumentSession owner)
        {
            if (resourcesReleased || session != owner || !owner.Blocks.Contains(block)) return false;
            var offsets = DocumentNavigation.LineOffsets(block.Braille);
            if (line < 0 || line >= offsets.Length) return false;
            var revision = block.BrailleRevision;
            CompleteSourceInput();
            await NavigateToAsync(new(block, SearchPanel.Braille, offsets[line]));
            return !resourcesReleased && session == owner && owner.ActiveBlock == block
                && owner.Blocks.Contains(block) && block.BrailleRevision == revision;
        }
        pane.GridView.MemoRequested = async (block, line) =>
        {
            var owner = session;
            if (owner.IsReadOnly) return;
            try
            {
                if (!await NavigateMarkerAsync(block, line, owner) || owner.IsReadOnly) return;
                SetActiveEditor(pane.Editor); lastGrid = pane.GridView;
                ShowLineMemos(SearchPanel.Braille, line);
            }
            catch (Exception ex) { if (!resourcesReleased && session == owner) ShowError(ex); }
        };
        pane.GridView.ReviewRequested = async block =>
        {
            var owner = session;
            try
            {
                if (!await NavigateMarkerAsync(block, 0, owner)) return;
                ShowLocalTranslationReview(block, Window.GetWindow(pane) ?? this);
            }
            catch (Exception ex) { if (!resourcesReleased && session == owner) ShowError(ex); }
        };
        foreach (var (command, redo) in new[] { (ApplicationCommands.Undo, false), (ApplicationCommands.Redo, true) })
            pane.GridView.CommandBindings.Add(new CommandBinding(command,
                (_, e) => { ReplayHistory(redo); e.Handled = true; },
                (_, e) => { e.CanExecute = CanReplayHistory(redo); e.Handled = true; }));
        pane.GridRefreshed += () => { RefreshLinkedSource(); if (lastGrid == pane.GridView) RefreshEditorStatus(); };
        var menu = CreatePanelContextMenu(true); pane.GridView.ContextMenu = menu;
        menu.Opened += (_, _) => { SetActiveEditor(pane.Editor); lastGrid = pane.GridView; RefreshEditorStatus(); UpdateDraftAvailability(); };
        pane.GridView.GotKeyboardFocus += (_, _) => { SetActiveEditor(pane.Editor); lastGrid = pane.GridView; RefreshEditorStatus(); };
        pane.NavigateGridAsync = async cell =>
        {
            await NavigateToAsync(new(cell.Block, SearchPanel.Braille, cell.Offset));
            lastGrid = pane.GridView; RefreshEditorStatus();
        };
        pane.ComposeGridAsync = model =>
        {
            if (engine is not LocalBrailleEngine local) return Task.FromResult(new ComposedBraille("", []));
            if (gridOwner == model && composedVersion == model.ChangeVersion && composing is not null && !composing.IsFaulted && !composing.IsCanceled) return composing;
            gridOwner = model; composedVersion = model.ChangeVersion;
            composing = ComposeAndReportAsync();
            return composing;

            async Task<ComposedBraille> ComposeAndReportAsync()
            {
                var result = await local.ComposeAsync(SmjFile.Snapshot(model, false).Manifest, documentLifetime.Token);
                if (session == model && result.SpacingUnknownBlockIds is { Length: > 0 } ids)
                    model.Status = "조판 빈 줄 확인 필요: 블록 " + DescribeSpacingBlocks(ids) + " (확인 정보가 부족합니다).";
                if (session == model && result.AlignmentUnknownBlockIds is { Length: > 0 } alignmentIds)
                    model.Status = "줄별 정렬 확인 필요: 블록 " + DescribeSpacingBlocks(alignmentIds) + " (정렬 위치를 확인할 수 없습니다)."
                        + (result.SpacingUnknownBlockIds is { Length: > 0 } ? " 빈 줄도 확인해 주세요." : "");
                return result;
            }
        };
        pane.ReplaceGridAsync = (spans, text) =>
        {
            if (session.IsReadOnly || spans.Any(s => s.Block.IsBrailleLocked)) return Task.CompletedTask;
            if (spans.Any(s => s.Block.Page != spans[0].Block.Page)) throw new ArgumentException("같은 원본 쪽 안에서 선택해 주세요.");
            session.ActiveBlock = spans[0].Block;
            EditStructure(() =>
            {
                for (var i = 0; i < spans.Length; i++)
                {
                    var span = spans[i];
                    var preserved = BrailleMarkup.Markers(span.Block.Braille.Substring(span.Start, span.Length));
                    var replacement = (i == 0 ? text : "") + (preserved.Length > 0 ? "\n" + preserved + "\n" : "");
                    BlockRange.Replace(session, [span], SearchPanel.Braille, replacement, allowLayoutMarkers: true);
                }
                session.ActiveBlock = spans[0].Block;
            });
            return Task.CompletedTask;
        };
        pane.GridView.MoveBlock = (moving, target) =>
        {
            if (moving == target || !session.Blocks.Contains(moving) || !session.Blocks.Contains(target)) return;
            if (moving.Page != target.Page) { session.Status = "블록은 같은 원본 쪽 안에서 이동해 주세요."; return; }
            if (session.IsReadOnly || moving.IsBrailleLocked || target.IsBrailleLocked) return;
            session.ActiveBlock = moving;
            EditStructure(() =>
            {
                var index = session.Blocks.IndexOf(target);
                if (session.Blocks.IndexOf(moving) < index) index--;
                session.Blocks.Move(session.Blocks.IndexOf(moving), index);
            });
        };
        pane.GridView.ReplayHistory = redo => ReplayHistory(redo);
    }
    private void ShowLocalTranslationReview(EditorBlock block, Window owner)
    {
        if (block.Translation is not { NeedsReview: true } result) return;
        NoticeDialog.Choose(owner, "점역 검토 필요", MemoMargin.DescribeReview(result), ["확인"], error: true);
    }

    private void RefreshLinkedSource()
    {
        BlockSpan[] linked = [];
        if (lastEditor == PageEditor.Editor && session?.ActiveBlock is { } block && session.Blocks.Contains(block)
            && block.Source.Length > 0
            && !block.HasManualBraille && !block.IsBrailleLocked && !block.Metadata.IsBlocked
            && block.Translation is { SourceText: { } source } translation && source == block.Source
            && translation.Cells == block.Braille && block.Braille.Length > 0
            && PageEditor.Editor.Text == BlockRange.Text(session.Blocks.Where(b => b.Page == block.Page), SearchPanel.Text)
            && PageEditor.ResolveLine(PageEditor.Editor.TextArea.Caret.Line - 1) is { Block: var current, Line: var line } && current == block)
        {
            var offsets = DocumentNavigation.LineOffsets(block.Source);
            if (line >= 0 && line < offsets.Length)
            {
                var start = offsets[line]; var end = line + 1 < offsets.Length ? offsets[line + 1] : block.Source.Length;
                while (end > start && block.Source[end - 1] is '\n' or '\r') end--;
                var wholeLine = PageEditor.Editor.SelectionLength == 0 || PageEditor.Selection is [{ Block: var selected, Start: var selectionStart, Length: var length }]
                    && selected == block && selectionStart == start && length == end - start;
                if (wholeLine && translation.SourceLineMap is { Length: > 0 } map
                    && translation.SourceMapStamp is { } stamp && stamp.SourceRevision == block.SourceRevision
                    && stamp.BrailleRevision == block.BrailleRevision && stamp.MetadataRevision == block.MetadataRevision)
                    linked = map.Where(m => m.SourceStart == start && m.SourceEnd == end && m.BrailleStart >= 0
                            && m.BrailleEnd > m.BrailleStart && m.BrailleEnd <= block.Braille.Length)
                        .Select(m => new BlockSpan(block, m.BrailleStart, m.BrailleEnd - m.BrailleStart)).ToArray();
                else if (wholeLine && translation.SourceLineMap is not { Length: > 0 }
                    && !block.Source.Contains('\n') && !block.Source.Contains('\r'))
                    linked = [new BlockSpan(block, 0, block.Braille.Length)];
            }
        }
        Braille.GridView.SetLinkedRanges(linked);
        detachedPane?.GridView.SetLinkedRanges(linked);
    }
}
