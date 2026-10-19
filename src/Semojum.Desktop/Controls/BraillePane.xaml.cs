using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Controls;

public partial class BraillePane : UserControl, IDisposable
{
    private DocumentSession? session;
    private EditorDocuments? documents;
    private EditorBlock? active;
    private bool switchingBlock;
    private readonly HashSet<EditorBlock> gridBlocks = [];
    private readonly System.Windows.Threading.DispatcherTimer gridTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    public Func<GridCell, Task>? NavigateGridAsync { get; set; }
    private readonly SixDotInput chord = new();
    public Brush NormalForeground { get; set; } = Brushes.Black;
    public void RefreshAppearance() => RefreshIndicators();
    public bool SixDotEnabled { get; set; }
    public bool SpaceEnabled { get; set; } = true;
    public bool EnterEnabled { get; set; } = true;
    public event EventHandler? DetachRequested;
    public event EventHandler? HideRequested;
    public event EventHandler? PreviewCloseRequested;
    public BraillePane()
    {
        InitializeComponent();
        gridTimer.Tick += (_, _) => { gridTimer.Stop(); RefreshGrid(); };
        GridView.CellSelected += async cell =>
        {
            if (NavigateGridAsync is not null) await NavigateGridAsync(cell);
            else if (session is not null) { session.ActiveBlock = cell.Block; Editor.CaretOffset = Math.Min(cell.Offset, Editor.Document.TextLength); }
            Editor.Focus();
        };
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(255, 245, 163));
        Editor.TextArea.TextView.CurrentLineBorder = null;
        Editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(204, 228, 247));
        Editor.Options.ShowColumnRuler = true;
        Editor.TextArea.TextView.ColumnRulerPen = new Pen(new SolidColorBrush(Color.FromRgb(242, 93, 93)), 1);
        PreviewStrip.CloseRequested += (_, _) => PreviewCloseRequested?.Invoke(this, EventArgs.Empty);
        Editor.PreviewKeyDown += HandleKeyDown;
        Editor.PreviewKeyUp += HandleKeyUp;
        Editor.LostKeyboardFocus += (_, _) => chord.Reset();
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!switchingBlock && active is not null) active.BrailleCaret = Editor.CaretOffset;
            RefreshIndicators();
        };
        Editor.TextArea.TextEntering += (_, e) =>
        {
            if (!DocumentSearch.IsBrailleText(e.Text))
            {
                e.Handled = true;
                if (session is not null) session.Status = "점자는 6점 입력을 켜거나 유니코드 점자를 붙여 넣어 주세요.";
            }
        };
        DataObject.AddPastingHandler(Editor, (_, e) =>
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !DocumentSearch.IsBrailleText(text))
                e.CancelCommand();
        });
    }
    public void Attach(DocumentSession model, EditorDocuments store)
    {
        Dispose(); session = model; documents = store;
        session.PropertyChanged += SessionChanged;
        session.Blocks.CollectionChanged += GridBlocksChanged; ObserveGridBlocks(); RefreshGrid();
        SwitchBlock();
    }
    public void ConfigureKeys(string keys) => chord.Configure(keys);
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) SwitchBlock();
        if (e.PropertyName == nameof(DocumentSession.Layout)) RefreshGrid();
        RefreshIndicators();
    }
    private void GridBlocksChanged(object? sender, NotifyCollectionChangedEventArgs e) { ObserveGridBlocks(); QueueGrid(); }
    private void ObserveGridBlocks()
    {
        foreach (var b in gridBlocks.Where(b => session?.Blocks.Contains(b) != true).ToArray()) { b.PropertyChanged -= GridBlockChanged; gridBlocks.Remove(b); }
        if (session is not null) foreach (var b in session.Blocks.Where(b => !gridBlocks.Contains(b))) { b.PropertyChanged += GridBlockChanged; gridBlocks.Add(b); }
    }
    private void GridBlockChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(EditorBlock.Braille) or nameof(EditorBlock.HasManualBraille) or nameof(EditorBlock.Metadata)) QueueGrid(); }
    private void QueueGrid() { gridTimer.Stop(); gridTimer.Start(); }
    private void RefreshGrid()
    {
        if (session is null) return;
        GridView.ItemsSource = BrailleGrid.Project(session.Blocks, session.Layout);
        GridView.ActiveBlock = session.ActiveBlock; GridView.CaretOffset = Editor.CaretOffset;
    }
    private void GridToggle_Click(object sender, RoutedEventArgs e)
    { GridView.Visibility = GridToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; GridHeight.Height = GridToggle.IsChecked == true ? new GridLength(2, GridUnitType.Star) : new GridLength(0); }
    private void SwitchBlock()
    {
        chord.Reset();
        if (active is not null) active.PropertyChanged -= BlockChanged;
        active = session?.ActiveBlock;
        if (active is not null && documents is not null)
        {
            switchingBlock = true;
            try
            {
                Editor.Document = documents.Braille(active);
                Editor.CaretOffset = Math.Clamp(active.BrailleCaret, 0, Editor.Document.TextLength);
                Editor.ScrollToLine(Editor.TextArea.Caret.Line);
                active.PropertyChanged += BlockChanged;
            }
            finally { switchingBlock = false; }
        }
        RefreshIndicators();
    }
    private void BlockChanged(object? sender, PropertyChangedEventArgs e) => RefreshIndicators();
    private void RefreshIndicators()
    {
        if (session is null || active is null) return;
        Editor.IsReadOnly = active.IsBrailleLocked || session.IsReadOnly;
        Editor.Options.ColumnRulerPosition = session.Layout.Columns;
        LayoutLabel.Text = $"{session.Layout.Columns}칸 × {session.Layout.Lines}줄 · {Editor.TextArea.Caret.Line}줄 {Editor.TextArea.Caret.Column}칸";
        GridView.ActiveBlock = active; GridView.CaretOffset = Editor.CaretOffset; GridView.InvalidateFaces();
        StateLabel.Text = active.State;
        var lineNumber = Math.Clamp(Editor.TextArea.Caret.Line, 1, Editor.Document.LineCount);
        var line = Editor.Document.GetLineByNumber(lineNumber);
        Editor.Foreground = active.HasManualBraille ? Brushes.Firebrick : NormalForeground;
        if (line.Length > session.Layout.Columns) StateLabel.Text += $" · 판면 초과 +{line.Length - session.Layout.Columns}칸";
    }
    public void SetLinePreview(int line, string cells) => PreviewStrip.SetPreview(line, cells);
    public void SetLinePreviewVisible(bool visible) => PreviewStrip.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    private void Hide_Click(object sender, RoutedEventArgs e) => HideRequested?.Invoke(this, EventArgs.Empty);
    private void HandleKeyDown(object sender, KeyEventArgs e)
    {
        if (!SixDotEnabled || Editor.IsReadOnly || Keyboard.Modifiers != ModifierKeys.None) return;
        var key = e.Key.ToString();
        if (key.Length == 1 && chord.Press(key[0])) e.Handled = true;
        else if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (SpaceEnabled && !e.IsRepeat) Editor.SelectedText = "⠀";
        }
        else if (e.Key == Key.Enter && !EnterEnabled) e.Handled = true;
    }
    private void HandleKeyUp(object sender, KeyEventArgs e)
    {
        if (!SixDotEnabled) { chord.Reset(); return; }
        var key = e.Key.ToString();
        if (key.Length != 1 || !chord.IsMapped(key[0])) return;
        e.Handled = true;
        var cell = chord.Release(key[0]);
        if (cell is not null && !Editor.IsReadOnly) Editor.SelectedText = cell;
    }
    private void Detach_Click(object sender, RoutedEventArgs e) => DetachRequested?.Invoke(this, EventArgs.Empty);
    public void Dispose()
    {
        gridTimer.Stop();
        if (session is not null) { session.PropertyChanged -= SessionChanged; session.Blocks.CollectionChanged -= GridBlocksChanged; }
        foreach (var b in gridBlocks) b.PropertyChanged -= GridBlockChanged;
        gridBlocks.Clear();
        if (active is not null) active.PropertyChanged -= BlockChanged;
        chord.Reset();
    }
}
