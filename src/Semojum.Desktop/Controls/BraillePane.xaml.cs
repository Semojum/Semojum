using System.ComponentModel;
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
    private readonly SixDotInput chord = new();
    public bool SixDotEnabled { get; set; }
    public bool SpaceEnabled { get; set; } = true;
    public bool EnterEnabled { get; set; } = true;
    public event EventHandler? DetachRequested;
    public BraillePane()
    {
        InitializeComponent();
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(255, 248, 177));
        Editor.TextArea.TextView.CurrentLineBorder = null;
        Editor.PreviewKeyDown += HandleKeyDown;
        Editor.PreviewKeyUp += HandleKeyUp;
        Editor.LostKeyboardFocus += (_, _) => chord.Reset();
        Editor.TextArea.Caret.PositionChanged += (_, _) => RefreshIndicators();
        Editor.TextArea.TextEntering += (_, e) =>
        {
            if (e.Text.Any(c => !IsBraille(c)))
            {
                e.Handled = true;
                if (session is not null) session.Status = "점자는 6점 입력을 켜거나 유니코드 점자를 붙여 넣어 주세요.";
            }
        };
        DataObject.AddPastingHandler(Editor, (_, e) =>
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetData(DataFormats.UnicodeText) is not string text || text.Any(c => !IsBraille(c)))
                e.CancelCommand();
        });
    }
    private static bool IsBraille(char c) => c is >= '\u2800' and <= '\u28ff' || c is ' ' or '\n' or '\r' or '\t';
    public void Attach(DocumentSession model, EditorDocuments store)
    {
        Dispose(); session = model; documents = store;
        session.PropertyChanged += SessionChanged;
        SwitchBlock();
    }
    public void ConfigureKeys(string keys) => chord.Configure(keys);
    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) SwitchBlock();
        RefreshIndicators();
    }
    private void SwitchBlock()
    {
        chord.Reset();
        if (active is not null) active.PropertyChanged -= BlockChanged;
        active = session?.ActiveBlock;
        if (active is not null && documents is not null)
        {
            Editor.Document = documents.Braille(active);
            active.PropertyChanged += BlockChanged;
        }
        RefreshIndicators();
    }
    private void BlockChanged(object? sender, PropertyChangedEventArgs e) => RefreshIndicators();
    private void RefreshIndicators()
    {
        if (session is null || active is null) return;
        Editor.IsReadOnly = active.IsBrailleLocked;
        LayoutLabel.Text = $"{session.Layout.Columns}칸 × {session.Layout.Lines}줄";
        StateLabel.Text = active.State;
        var lineNumber = Math.Clamp(Editor.TextArea.Caret.Line, 1, Editor.Document.LineCount);
        var line = Editor.Document.GetLineByNumber(lineNumber);
        Editor.Foreground = active.HasManualBraille ? Brushes.Firebrick : Brushes.Black;
        if (line.Length > session.Layout.Columns) StateLabel.Text = $"판면 초과 +{line.Length - session.Layout.Columns}칸";
    }
    public void SetLinePreview(int line, string cells) { LineLabel.Text = $"텍스트 줄번역 · {line}줄"; LinePreview.Text = cells; }
    public void SetLinePreviewVisible(bool visible) { LinePreview.Visibility = visible ? Visibility.Visible : Visibility.Collapsed; }
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
        if (session is not null) session.PropertyChanged -= SessionChanged;
        if (active is not null) active.PropertyChanged -= BlockChanged;
        chord.Reset();
    }
}
