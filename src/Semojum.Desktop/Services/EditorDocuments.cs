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
    private sealed record BrailleState(string Text, bool Manual, BrailleTranslation? Translation, string State)
    {
        public static BrailleState Capture(EditorBlock block) => new(block.Braille, block.HasManualBraille, block.Translation, block.State);
        public void Restore(EditorBlock block)
        { block.Braille = Text; block.HasManualBraille = Manual; block.Translation = Translation; block.State = State; }
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
                var before = block.Source; var after = entry.Source.Text;
                if (before == after) return;
                block.Source = after;
                History(block.Page).Push(new EditUndo(
                    () => { block.Source = before; replayedBlock = block; },
                    () => { block.Source = after; replayedBlock = block; }));
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
                if (before == after) return;
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
