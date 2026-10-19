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
    private static void Sync(TextDocument document, string text)
    {
        // Engine/model restoration is not an additional user edit; retain the shared page history.
        var stack = document.UndoStack; document.UndoStack = new UndoStack();
        try { document.Text = text; } finally { document.UndoStack = stack; }
    }
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
        entry.Source.UndoStack = History(block.Page); entry.Braille.UndoStack = History(block.Page);
        entry.Source.TextChanged += (_, _) =>
        {
            if (entry.Updating) return;
            entry.Updating = true;
            try { block.Source = entry.Source.Text; }
            finally { entry.Updating = false; }
        };
        entry.Braille.TextChanged += (_, _) =>
        {
            if (entry.Updating) return;
            entry.Updating = true;
            try { block.EditBraille(entry.Braille.Text); }
            finally { entry.Updating = false; }
        };
        block.PropertyChanged += listener;
        buffers.Add(block, entry);
        return entry;
    }
    public void Dispose()
    {
        foreach (var (block, entry) in buffers) block.PropertyChanged -= entry.Listener;
        buffers.Clear();
    }
}
