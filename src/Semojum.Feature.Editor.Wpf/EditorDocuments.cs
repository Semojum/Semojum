using System.ComponentModel;
using ICSharpCode.AvalonEdit.Document;
using Semojum.Core;

namespace Semojum.Desktop.Services;

/// <summary>One text buffer per block/representation, shared across main and detached windows.</summary>
public sealed class EditorDocuments : IDisposable
{
    private readonly Dictionary<EditorBlock, Buffers> buffers = [];
    private readonly Dictionary<int, UndoStack> history = [];
    public UndoStack History(int page)
    { if (!history.TryGetValue(page, out var stack)) history[page] = stack = new UndoStack { SizeLimit = 100 }; return stack; }
    // Keep model history independent of AvalonEdit's offset-based document history. Engine
    // output and structural restoration replace entire buffers and must not clear user steps.
    public EditorBlock? Replay(int page, bool redo)
    {
        replayedBlock = null;
        if (redo) History(page).Redo(); else History(page).Undo();
        return replayedBlock;
    }
    private EditorBlock? replayedBlock;
    private sealed class EditUndo(Action before, Action after) : IUndoableOperation
    {
        public void Undo() => before();
        public void Redo() => after();
    }
    internal sealed record ReviewSnapshot(ReviewMark[] Marks, int SourceRevision, int BrailleRevision, int ConfirmationRevision, BrailleTranslation? Translation)
    {
        internal static ReviewSnapshot Capture(EditorBlock block) => new(block.ReviewMarks.ToArray(), block.SourceRevision, block.BrailleRevision, block.BrailleConfirmationRevision, block.Translation);
        internal bool BrailleContextChanged(EditorBlock block) => block.SourceRevision != SourceRevision || block.BrailleRevision != BrailleRevision
            || block.BrailleConfirmationRevision != ConfirmationRevision || !ReferenceEquals(block.Translation, Translation);
    }
    internal static void RestoreReviews(EditorBlock block, IEnumerable<ReviewMark> marks, bool invalidateBrailleConfirmations = false,
        bool invalidateText = false, bool invalidateBraille = false)
    {
        block.ReviewMarks.Clear();
        foreach (var mark in marks)
        {
            var text = mark.Panel == ReviewPanel.Text ? block.Source : block.Braille;
            var start = Math.Clamp(mark.Start, 0, text.Length);
            if (start > 0 && start < text.Length && char.IsHighSurrogate(text[start - 1]) && char.IsLowSurrogate(text[start])) start--;
            var matches = mark.Length > 0 && mark.Start == start && mark.Length <= text.Length - start
                && text.AsSpan(start, mark.Length).SequenceEqual(mark.OriginalText.AsSpan());
            var stale = mark.IsStale || !matches || invalidateText && mark.Panel == ReviewPanel.Text || invalidateBraille && mark.Panel == ReviewPanel.Braille
                || invalidateBrailleConfirmations && mark.Panel == ReviewPanel.Braille && mark.Kind == ReviewKind.HumanConfirmed;
            block.ReviewMarks.Add(stale ? mark with { Start = start, Length = 0, IsStale = true } : mark);
        }
    }
    internal void RecordReviewEdit(EditorBlock focus, IReadOnlyDictionary<EditorBlock, ReviewMark[]> before,
        IReadOnlyDictionary<EditorBlock, ReviewMark[]> after)
    {
        var contexts = before.Keys.ToDictionary(block => block, ReviewSnapshot.Capture);
        void Restore(IReadOnlyDictionary<EditorBlock, ReviewMark[]> snapshot)
        {
            foreach (var (block, marks) in snapshot)
            {
                var context = contexts[block];
                RestoreReviews(block, marks, context.BrailleContextChanged(block), block.SourceRevision != context.SourceRevision, block.BrailleRevision != context.BrailleRevision);
            }
            replayedBlock = focus;
        }
        History(focus.Page).Push(new EditUndo(() => Restore(before), () => Restore(after)));
    }
    private sealed record BrailleState(string Text, bool Manual, BrailleTranslation? Translation, string State, ReviewSnapshot Reviews)
    {
        public static BrailleState Capture(EditorBlock block) => new(block.Braille, block.HasManualBraille, block.Translation, block.State, ReviewSnapshot.Capture(block));
        public void Restore(EditorBlock block)
        {
            var sourceChanged = block.SourceRevision != Reviews.SourceRevision || block.BrailleConfirmationRevision != Reviews.ConfirmationRevision;
            block.Braille = Text; block.HasManualBraille = Manual; block.Translation = Translation; block.State = State;
            RestoreReviews(block, Reviews.Marks, sourceChanged);
        }
    }
    private static void Sync(TextDocument document, string text) => document.Replace(0, document.TextLength, text);
    private sealed class Buffers
    {
        public required TextDocument Source { get; init; }
        public required TextDocument Braille { get; init; }
        public required PropertyChangedEventHandler Listener { get; init; }
        public bool Updating;
    }
    public TextDocument Source(EditorBlock block) => Get(block).Source;
    public TextDocument Braille(EditorBlock block) => Get(block).Braille;
    private Buffers Get(EditorBlock block)
    {
        if (buffers.TryGetValue(block, out var existing)) return existing;
        Buffers? entry = null;
        PropertyChangedEventHandler listener = (_, args) =>
        {
            if (entry is null || entry.Updating) return;
            entry.Updating = true;
            try
            {
                if (args.PropertyName == nameof(EditorBlock.Source) && entry.Source.Text != block.Source) Sync(entry.Source, block.Source);
                if (args.PropertyName == nameof(EditorBlock.Braille) && entry.Braille.Text != block.Braille) Sync(entry.Braille, block.Braille);
            }
            finally { entry.Updating = false; }
        };
        entry = new Buffers { Source = new(block.Source), Braille = new(block.Braille), Listener = listener };
        entry.Source.UndoStack.SizeLimit = 0; entry.Braille.UndoStack.SizeLimit = 0;
        entry.Source.TextChanged += (_, _) =>
        {
            if (entry.Updating) return;
            entry.Updating = true;
            try
            {
                var before = block.Source; var after = entry.Source.Text; var beforeReviews = ReviewSnapshot.Capture(block);
                if (before == after) return;
                block.Source = after;
                var afterReviews = ReviewSnapshot.Capture(block);
                var expected = afterReviews;
                var expectedText = after;
                void RestoreSource(string text, ReviewSnapshot target)
                {
                    var textChanged = block.Source != expectedText || block.SourceRevision != expected.SourceRevision;
                    var changed = block.BrailleRevision != expected.BrailleRevision || block.BrailleConfirmationRevision != expected.ConfirmationRevision
                        || !ReferenceEquals(block.Translation, expected.Translation);
                    block.Source = text;
                    RestoreReviews(block, target.Marks, changed, textChanged);
                    expected = ReviewSnapshot.Capture(block); expectedText = text; replayedBlock = block;
                }
                History(block.Page).Push(new EditUndo(
                    () => RestoreSource(before, beforeReviews),
                    () => RestoreSource(after, afterReviews)));
            }
            finally { entry.Updating = false; }
        };
        entry.Braille.TextChanged += (_, _) =>
        {
            if (entry.Updating) return;
            entry.Updating = true;
            try
            {
                var before = BrailleState.Capture(block);
                block.EditBraille(entry.Braille.Text);
                var after = BrailleState.Capture(block);
                if (before.Text == after.Text && before.Manual == after.Manual && before.Translation == after.Translation
                    && before.State == after.State && before.Reviews.Marks.SequenceEqual(after.Reviews.Marks)) return;
                History(block.Page).Push(new EditUndo(
                    () => { before.Restore(block); replayedBlock = block; },
                    () => { after.Restore(block); replayedBlock = block; }));
            }
            finally { entry.Updating = false; }
        };
        block.PropertyChanged += listener;
        buffers.Add(block, entry);
        return entry;
    }
    public void Dispose()
    {
        foreach (var (block, entry) in buffers) block.PropertyChanged -= entry.Listener;
        buffers.Clear(); history.Clear();
    }
}
