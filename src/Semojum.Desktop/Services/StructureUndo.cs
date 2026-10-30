using ICSharpCode.AvalonEdit.Document;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal sealed class StructureUndo(DocumentSession session, StructureUndo.Snapshot before, StructureUndo.Snapshot after) : IUndoableOperation
{
    internal sealed record State(EditorBlock Block, string Source, string Braille, BlockMetadata Metadata, BrailleTranslation? Translation, bool Manual, string Memo, LineMemo[] Memos, ReviewMark[] Reviews, string DisplayState, int SourceRevision, int BrailleRevision, int ConfirmationRevision);
    internal sealed record Snapshot(int Page, State[] States, EditorBlock? Active);
    internal static Snapshot Capture(DocumentSession session, int page) => new(page, session.Blocks.Where(b => b.Page == page)
        .Select(b => new State(b, b.Source, b.Braille, b.Metadata, b.Translation, b.HasManualBraille, b.Memo, b.Memos.ToArray(), b.ReviewMarks.ToArray(), b.State, b.SourceRevision, b.BrailleRevision, b.BrailleConfirmationRevision)).ToArray(), session.ActiveBlock);
    private readonly Snapshot afterState = after;
    private Snapshot expected = after;
    public void Undo() => Restore(before);
    public void Redo() => Restore(afterState);
    private void Restore(Snapshot state)
    {
        var expectedBlocks = expected.States.ToDictionary(item => item.Block);
        var pageBlocks = session.Blocks.Where(b => b.Page == state.Page).ToArray();
        var index = pageBlocks.Length > 0 ? session.Blocks.IndexOf(pageBlocks[0]) : session.Blocks.TakeWhile(b => b.Page < state.Page).Count();
        foreach (var b in pageBlocks) session.Blocks.Remove(b);
        foreach (var saved in state.States)
        {
            var b = saved.Block;
            var prior = expectedBlocks.TryGetValue(b, out var anticipated) ? anticipated : saved;
            var sourceChanged = b.SourceRevision != prior.SourceRevision;
            var brailleChanged = b.BrailleRevision != prior.BrailleRevision;
            var contextChanged = sourceChanged || brailleChanged || b.BrailleConfirmationRevision != prior.ConfirmationRevision || !ReferenceEquals(b.Translation, prior.Translation);
            b.Source = saved.Source; b.Braille = saved.Braille; b.Metadata = saved.Metadata;
            b.Translation = saved.Translation; b.HasManualBraille = saved.Manual; b.Memo = saved.Memo; b.State = saved.DisplayState;
            b.Memos.Clear(); foreach (var memo in saved.Memos) b.Memos.Add(memo);
            EditorDocuments.RestoreReviews(b, saved.Reviews, contextChanged, sourceChanged, brailleChanged);
            session.Blocks.Insert(index++, b);
        }
        session.ActiveBlock = state.Active is not null && session.Blocks.Contains(state.Active) ? state.Active : state.States.FirstOrDefault()?.Block;
        expected = Capture(session, state.Page);
    }
}
