using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using Semojum.Core;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Controls;

/// <summary>Native text selection/IME across a source page, backed by the same block models.</summary>
public sealed class ContinuousSourceEditor : DockPanel, IDisposable
{
    public TextEditor Editor { get; } = new() { ShowLineNumbers = true, WordWrap = true, FontSize = 16, Padding = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public Action<BlockSpan[], string>? Replace { get; set; }
    public Action<bool>? Replay { get; set; }
    public Action? InsertPageBreak { get; set; }
    public event Action? ProjectionRestored;
    public bool IsSynchronizing => syncing || followingCaret;
    private bool followingCaret;
    private DocumentSession? model;
    private readonly HashSet<EditorBlock> observed = [];
    private bool syncing, queued, selectedBlock, projectionSuppressed;
    private bool showReviews = true;
    public bool ShowReviews { get => showReviews; set { showReviews = value; Editor.TextArea.TextView.Redraw(); } }
    private int page;
    public ContinuousSourceEditor()
    {
        Children.Add(Editor);
        NativeIme.Attach(Editor);
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 245, 163));
        Editor.TextArea.TextView.CurrentLineBorder = null;
        Editor.TextArea.TextView.LineTransformers.Add(new TagColorizer());
        Editor.TextArea.TextView.ElementGenerators.Add(new IndentTagGenerator());
        Editor.TextArea.TextView.BackgroundRenderers.Add(new ReviewRangeRenderer(ReviewRanges, () => ShowReviews, () => (Editor.SelectionStart, Editor.SelectionLength)));
        Editor.Document.UndoStack.SizeLimit = 0;
        Editor.PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            { e.Handled = true; SelectContext(); return; }
            selectedBlock = false;
            if (!Editor.IsReadOnly && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
            { e.Handled = true; InsertPageBreak?.Invoke(); }
        };
        Editor.PreviewMouseDown += (_, _) => selectedBlock = false;
        Editor.Document.Changed += (_, e) =>
        {
            if (syncing || model is null) return;
            syncing = true;
            try
            {
                var spans = BlockRange.Select(Blocks(), SearchPanel.Text, e.Offset, e.RemovalLength);
                if (model.IsReadOnly || spans.Any(s => s.Block.Metadata.IsBlocked)) throw new ArgumentException("읽기 전용 구간입니다.");
                Replace?.Invoke(spans, e.InsertedText.Text);
            }
            catch (Exception ex) { model.Status = ex.Message; }
            finally { syncing = false; QueueRefresh(); }
        };
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (syncing || model is null || Editor.Text != BlockRange.Text(Blocks(), SearchPanel.Text)) return;
            var span = BlockRange.Select(Blocks(), SearchPanel.Text, Editor.CaretOffset, 0).FirstOrDefault();
            if (span is null) return;
            followingCaret = true;
            try { span.Block.SourceCaret = span.Start; model.ActiveBlock = span.Block; }
            finally { followingCaret = false; }
        };
        Editor.AddHandler(CommandManager.PreviewCanExecuteEvent, new CanExecuteRoutedEventHandler((_, e) =>
        {
            if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
            e.CanExecute = model?.IsReadOnly == false; e.Handled = true;
        }));
        Editor.AddHandler(CommandManager.PreviewExecutedEvent, new ExecutedRoutedEventHandler((_, e) =>
        {
            if (e.Command != ApplicationCommands.Undo && e.Command != ApplicationCommands.Redo) return;
            Replay?.Invoke(e.Command == ApplicationCommands.Redo); QueueRefresh(); e.Handled = true;
        }));
    }
    private EditorBlock[] Blocks() => model?.Blocks.Where(b => b.Page == page).ToArray() ?? [];
    internal bool TryGetPageCaretPosition(int expectedPage, out int line, out int column)
    {
        line = column = 0;
        // A queued update may still be showing the previous page or previous
        // contents. Do not present its caret as a position in the new model.
        if (model is null || syncing || page != expectedPage || Editor.Text != BlockRange.Text(Blocks(), SearchPanel.Text))
        {
            projectionSuppressed = true;
            return false;
        }
        line = Editor.TextArea.Caret.Line;
        column = Editor.TextArea.Caret.Column;
        return true;
    }
    private IReadOnlyList<(int Start, ReviewMark Mark)> ReviewRanges()
    {
        // The page model can change before its queued editor refresh runs. Its
        // offsets must not be rendered against the previous document snapshot.
        if (ProjectionPending()) return [];
        var result = new List<(int, ReviewMark)>(); var offset = 0;
        foreach (var block in Blocks())
        {
            result.AddRange(block.ReviewMarks.Where(m => m.Panel == ReviewPanel.Text && !m.IsStale)
                .Select(m => (offset + m.Start, m)));
            offset += block.Source.Length + 1;
        }
        return result;
    }
    public (EditorBlock? Block, int Line) ResolveLine(int line)
    {
        // Visual lines may still belong to the previous document while the
        // model is being replaced or a refresh is waiting on the dispatcher.
        if (ProjectionPending() || line < 0 || line >= Editor.Document.LineCount) return (null, 0);
        var span = BlockRange.Select(Blocks(), SearchPanel.Text, Editor.Document.GetLineByNumber(line + 1).Offset, 0).FirstOrDefault();
        return span is null ? (null, 0) : (span.Block, span.Block.Source[..span.Start].Count(c => c == '\n'));
    }
    public void FollowActive(int? offset = null, int length = 0)
    {
        if (IsSynchronizing || model?.ActiveBlock is not { } block) return;
        page = block.Page; Refresh();
        var start = Blocks().TakeWhile(b => b != block).Sum(b => b.Source.Length + 1);
        var local = Math.Clamp(offset ?? block.SourceCaret, 0, block.Source.Length);
        syncing = true;
        try
        {
            Editor.Select(start + local, Math.Clamp(length, 0, block.Source.Length - local));
            Editor.CaretOffset = start + local + Math.Clamp(length, 0, block.Source.Length - local);
            Editor.ScrollToLine(Editor.Document.GetLineByOffset(start + local).LineNumber);
        }
        finally { syncing = false; RestoreProjectionIfReady(); }
    }
    public BlockSpan[] Selection
    {
        get
        {
            if (syncing || model is null) return [];
            var blocks = Blocks();
            // A refresh can still be queued after an explicit Refresh has already
            // synchronized the editor. Conversely, matching lengths alone cannot
            // prove that a queued refresh is safe to map to the current blocks.
            if (Editor.Text != BlockRange.Text(blocks, SearchPanel.Text)) return [];
            return BlockRange.Select(blocks, SearchPanel.Text, Editor.SelectionStart, Editor.SelectionLength);
        }
    }
    private bool ProjectionPending()
    {
        if (!syncing && !queued) return false;
        projectionSuppressed = true;
        return true;
    }
    private void RestoreProjectionIfReady()
    {
        if (syncing || queued || !projectionSuppressed) return;
        projectionSuppressed = false;
        Editor.TextArea.TextView.Redraw();
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<MemoMargin>()) margin.InvalidateVisual();
        ProjectionRestored?.Invoke();
    }
    internal void SelectContext()
    {
        if (selectedBlock) { Editor.SelectAll(); return; }
        var blocks = Blocks();
        var span = BlockRange.Select(blocks, SearchPanel.Text, Editor.CaretOffset, 0).FirstOrDefault();
        if (span is null) return;
        var start = blocks.TakeWhile(b => b != span.Block).Sum(b => b.Source.Length + 1);
        Editor.Select(start, span.Block.Source.Length); selectedBlock = true;
    }
    public void Attach(DocumentSession session)
    {
        Dispose(); model = session; page = session.ActiveBlock?.Page ?? 1;
        model.PropertyChanged += SessionChanged; model.Blocks.CollectionChanged += BlocksChanged;
        Observe(); Refresh();
    }
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock) && model?.ActiveBlock?.Page is { } p && page != p)
        { page = p; QueueRefresh(); }
        if (e.PropertyName == nameof(DocumentSession.IsReadOnly)) QueueRefresh();
    }
    private void BlocksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Observe(); QueueRefresh();
        if (e.Action == NotifyCollectionChangedAction.Move)
            Dispatcher.BeginInvoke(() => FollowActive(), DispatcherPriority.DataBind);
    }
    private void Observe()
    {
        foreach (var b in observed.Where(b => model?.Blocks.Contains(b) != true).ToArray()) { b.PropertyChanged -= Changed; b.ReviewMarks.CollectionChanged -= ReviewsChanged; observed.Remove(b); }
        if (model is not null) foreach (var b in model.Blocks.Where(b => !observed.Contains(b))) { b.PropertyChanged += Changed; b.ReviewMarks.CollectionChanged += ReviewsChanged; observed.Add(b); }
    }
    private void ReviewsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Editor.TextArea.TextView.Redraw();
        foreach (var margin in Editor.TextArea.LeftMargins.OfType<MemoMargin>()) margin.InvalidateVisual();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    { if (!syncing && e.PropertyName is nameof(EditorBlock.Source) or nameof(EditorBlock.Metadata)) QueueRefresh(); }
    private void QueueRefresh()
    {
        if (queued) return; queued = true;
        Dispatcher.BeginInvoke(() => { queued = false; Refresh(); }, DispatcherPriority.DataBind);
    }
    public void Refresh()
    {
        if (model is null) return;
        syncing = true;
        try
        {
            var text = BlockRange.Text(Blocks(), SearchPanel.Text);
            if (Editor.Text != text)
            {
                var offset = Editor.CaretOffset;
                Editor.Text = text; Editor.CaretOffset = Math.Min(offset, text.Length);
            }
            Editor.IsReadOnly = model.IsReadOnly;
        }
        finally
        {
            syncing = false;
            RestoreProjectionIfReady();
        }
    }
    public void Dispose()
    {
        if (model is not null) { model.PropertyChanged -= SessionChanged; model.Blocks.CollectionChanged -= BlocksChanged; }
        foreach (var b in observed) { b.PropertyChanged -= Changed; b.ReviewMarks.CollectionChanged -= ReviewsChanged; }
        observed.Clear(); model = null;
    }
}
