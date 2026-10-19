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
    public TextEditor Editor { get; } = new() { ShowLineNumbers = true, WordWrap = true, FontSize = 16, Padding = new(8) };
    public Action<BlockSpan[], string>? Replace { get; set; }
    public Action<bool>? Replay { get; set; }
    public Action? InsertPageBreak { get; set; }
    public event Action? CloseRequested;
    private DocumentSession? model;
    private readonly HashSet<EditorBlock> observed = [];
    private bool syncing, queued, selectedBlock;
    private int page;
    public ContinuousSourceEditor()
    {
        var header = new DockPanel { Margin = new(8, 4, 8, 4) };
        var close = new Button { Content = "블록 보기", HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => CloseRequested?.Invoke(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = "쪽 연속 편집 · 드래그 / Shift+클릭", VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(header, Dock.Top); Children.Add(header); Children.Add(Editor);
        Editor.TextArea.TextView.LineTransformers.Add(new TagColorizer());
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
            span.Block.SourceCaret = span.Start;
            model.ActiveBlock = span.Block;
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
    public BlockSpan[] Selection => BlockRange.Select(Blocks(), SearchPanel.Text, Editor.SelectionStart, Editor.SelectionLength);
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
    private void BlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) { Observe(); QueueRefresh(); }
    private void Observe()
    {
        foreach (var b in observed.Where(b => model?.Blocks.Contains(b) != true).ToArray()) { b.PropertyChanged -= Changed; observed.Remove(b); }
        if (model is not null) foreach (var b in model.Blocks.Where(b => !observed.Contains(b))) { b.PropertyChanged += Changed; observed.Add(b); }
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
        finally { syncing = false; }
    }
    public void Dispose()
    {
        if (model is not null) { model.PropertyChanged -= SessionChanged; model.Blocks.CollectionChanged -= BlocksChanged; }
        foreach (var b in observed) b.PropertyChanged -= Changed;
        observed.Clear(); model = null;
    }
}
