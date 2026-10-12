using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow : Window
{
    private DocumentSession session = null!;
    private EditorDocuments documents = new();
    private readonly MockTranslationEngine engine = new();
    private TranslationCoordinator translator;
    private readonly Dictionary<EditorBlock, int> translatedRevisions = [];
    private CancellationTokenSource documentLifetime = new();
    private string? filePath;
    private bool switching, fileBusy, closing, allowClose;
    private bool autoTranslate = true;
    private string sixDotKeys = "F D S J K L";
    private Window? detachedWindow;
    private BraillePane? detachedPane;
    private TextEditor? lastEditor;

    internal DocumentSession Session => session;
    internal EditorDocuments Documents => documents;
    internal BraillePane? DetachedPane => detachedPane;
    internal Window? DetachedWindow => detachedWindow;
    internal void PrepareSmokeShutdown() => allowClose = true;

    public MainWindow()
    {
        translator = new(engine);
        InitializeComponent();
        SourceEditor.TextArea.TextView.LineTransformers.Add(new TagColorizer());
        SourceEditor.Options.HighlightCurrentLine = true;
        SourceEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(255, 248, 177));
        SourceEditor.TextArea.TextView.CurrentLineBorder = null;
        SourceEditor.TextArea.Caret.PositionChanged += (_, _) => RefreshSourcePosition();
        SourceEditor.GotKeyboardFocus += (_, _) => lastEditor = SourceEditor;
        Braille.Editor.GotKeyboardFocus += (_, _) => lastEditor = Braille.Editor;
        Braille.DetachRequested += (_, _) => ToggleDetached();
        SetSession(DocumentSession.Sample(), null, true);
        foreach (var (key, action) in new[] { (Key.N, "new"), (Key.O, "open"), (Key.S, "save"), (Key.F, "find") })
            InputBindings.Add(new KeyBinding(new AsyncRelayCommand(() => ExecuteAsync(action)), new KeyGesture(key, ModifierKeys.Control)));
        Closed += (_, _) => { documentLifetime.Cancel(); documentLifetime.Dispose(); Braille.Dispose(); documents.Dispose(); };
    }

    private void SetSession(DocumentSession model, string? path, bool showOriginal)
    {
        switching = true;
        try
        {
            documentLifetime.Cancel(); documentLifetime.Dispose(); documentLifetime = new();
            if (session is not null) session.PropertyChanged -= SessionChanged;
            documents.Dispose(); documents = new(); translator = new(engine); translatedRevisions.Clear();
            session = model; filePath = path;
            foreach (var block in session.Blocks) translatedRevisions[block] = block.SourceRevision;
            DataContext = session;
            session.PropertyChanged += SessionChanged;
            Braille.Attach(session, documents); detachedPane?.Attach(session, documents);
            OriginalEmpty.Visibility = showOriginal ? Visibility.Collapsed : Visibility.Visible;
            ShowActiveBlock();
        }
        finally { switching = false; }
    }

    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) ShowActiveBlock();
        if (detachedWindow is not null) detachedWindow.Title = $"점자 — {session.Title}";
    }

    private void ShowActiveBlock()
    {
        if (session.ActiveBlock is not { } block) return;
        SourceEditor.Document = documents.Source(block);
        Original.SelectBlock(block.Id, block.Page);
        RefreshSourcePosition();
    }

    private void RefreshSourcePosition()
    {
        if (session?.ActiveBlock is { } block)
            SourcePosition.Text = $"{block.Page}쪽 · {SourceEditor.TextArea.Caret.Line}줄 {SourceEditor.TextArea.Caret.Column}칸";
    }

    private async void BlockPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switching || session is null || BlockPicker.SelectedItem is not EditorBlock next || next == session.ActiveBlock) return;
        var previous = session.ActiveBlock;
        var changed = previous is not null && translatedRevisions.GetValueOrDefault(previous, -1) != previous.SourceRevision;
        var decision = changed && autoTranslate ? ChooseTranslation(previous!) : ManualEditDecision.Keep;
        if (decision == ManualEditDecision.Cancel)
        {
            switching = true; BlockPicker.SelectedItem = previous; switching = false; return;
        }
        session.ActiveBlock = next;
        if (changed && autoTranslate) await TranslateAsync(previous!, decision);
    }

    private ManualEditDecision ChooseTranslation(EditorBlock block)
    {
        if (!block.HasManualBraille) return ManualEditDecision.Overwrite;
        var dialog = new ManualEditDialog { Owner = this }; dialog.ShowDialog(); return dialog.Decision;
    }

    private async Task TranslateAsync(EditorBlock block, ManualEditDecision decision)
    {
        var owner = session;
        var revision = block.SourceRevision;
        var result = await translator.TranslateAsync(block, decision, TimeSpan.FromMilliseconds(400), documentLifetime.Token);
        if (owner != session) return;
        if (result is TranslationOutcome.Applied or TranslationOutcome.Kept) translatedRevisions[block] = revision;
        session.Status = result switch
        {
            TranslationOutcome.Applied => $"블록 {block.Id} · 샘플 점자를 표시했습니다. 실제 점역 결과가 아닙니다.",
            TranslationOutcome.Kept => $"블록 {block.Id} · 직접 수정한 점자를 유지했습니다.",
            TranslationOutcome.Superseded => "점역 대기 중 내용이 바뀌었습니다. 현재 편집 내용을 유지합니다.",
            TranslationOutcome.TimedOut => "0.4초 응답 지연 · 이 블록의 점자가 잠겼습니다. 원문 편집과 저장은 가능합니다.",
            TranslationOutcome.Failed => "점역에 실패했습니다. 원문 편집과 저장은 가능합니다.",
            _ => "점역을 취소했습니다."
        };
        if (result is TranslationOutcome.TimedOut or TranslationOutcome.Failed)
        {
            var retry = MessageBox.Show(this, "점자 패널이 잠겼습니다. 원문은 계속 편집하고 저장할 수 있습니다.\n\n지연 체험을 끄고 지금 다시 시도하시겠습니까?", "점역 응답 지연", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (retry == MessageBoxResult.Yes)
            {
                engine.SimulateTimeout = false; TimeoutToggle.IsChecked = false;
                await TranslateAsync(block, ChooseTranslation(block));
            }
        }
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action }) await ExecuteAsync(action);
    }

    private async Task ExecuteAsync(string action)
    {
        try
        {
            if (action.StartsWith("tag:")) { InsertTag(action[4..]); return; }
            switch (action)
            {
                case "new":
                    if (await ConfirmDiscardAsync())
                    {
                        var model = new DocumentSession { Title = "제목 없음.mock.smi" };
                        model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "새 블록" });
                        SetSession(model, null, false);
                    }
                    break;
                case "sample": if (await ConfirmDiscardAsync()) SetSession(DocumentSession.Sample(), null, true); break;
                case "open": await OpenAsync(); break;
                case "save": await SaveAsync(false); break;
                case "saveAs": await SaveAsync(true); break;
                case "export": await ExportAsync(); break;
                case "close": Close(); break;
                case "undo": (lastEditor ?? SourceEditor).Undo(); break;
                case "redo": (lastEditor ?? SourceEditor).Redo(); break;
                case "cut": (lastEditor ?? SourceEditor).Cut(); break;
                case "copy": (lastEditor ?? SourceEditor).Copy(); break;
                case "paste": (lastEditor ?? SourceEditor).Paste(); break;
                case "find": new FindDialog(SourceEditor) { Owner = this }.ShowDialog(); break;
                case "previous": MoveBlock(-1); break;
                case "next": MoveBlock(1); break;
                case "detach": ToggleDetached(); break;
                case "zoomIn": SetFontSize(SourceEditor.FontSize + 2); break;
                case "zoomOut": SetFontSize(SourceEditor.FontSize - 2); break;
                case "zoomReset": SetFontSize(18); break;
                case "translate": if (session.ActiveBlock is { } block) await TranslateAsync(block, ChooseTranslation(block)); break;
                case "memo":
                    if (session.ActiveBlock is { } memoBlock)
                    {
                        var dialog = new MemoDialog(memoBlock) { Owner = this };
                        if (dialog.ShowDialog() == true) { memoBlock.Memo = dialog.Result; session.Status = $"블록 {memoBlock.Id} · 메모를 반영했습니다."; }
                    }
                    break;
                case "math":
                    var math = new MathDialog { Owner = this };
                    if (math.ShowDialog() == true) InsertText($"<!수식>{math.Latex}<!/수식>");
                    break;
                case "layout":
                    var layout = new LayoutDialog(session.Layout) { Owner = this };
                    if (layout.ShowDialog() == true) { session.Layout = layout.Result; session.Status = $"판면 설정 · {layout.Result.Columns}칸 × {layout.Result.Lines}줄"; }
                    break;
                case "sixdot":
                    var sixdot = new SixDotDialog(sixDotKeys, Braille.SpaceEnabled, Braille.EnterEnabled) { Owner = this };
                    if (sixdot.ShowDialog() == true)
                    {
                        sixDotKeys = sixdot.Keys; Braille.ConfigureKeys(sixDotKeys);
                        Braille.SpaceEnabled = sixdot.Space; Braille.EnterEnabled = sixdot.Enter;
                        CopyBrailleSettings(); session.Status = $"6점키 · {sixDotKeys}";
                    }
                    break;
                case "translationOptions":
                    var options = new TranslationOptionsDialog(autoTranslate) { Owner = this };
                    if (options.ShowDialog() == true) autoTranslate = options.AutoTranslate;
                    break;
                case "help":
                    MessageBox.Show(this, "세모점 Windows 목업\n\n• 텍스트 편집, 태그 삽입, 찾아 바꾸기, 블록별 메모\n• 점자 직접 수정, F D S J K L 6점 입력, 창 분리\n• 판면 설정, LaTeX 수식 미리보기\n• .mock.smi 저장/열기, UTF-8 텍스트 열기/내보내기\n\n샘플 점자는 화면 확인용 고정 데이터입니다. 실제 한글·수식 점역, PDF/HWP/HWPX 변환, 인쇄, 서버 연결은 구현하지 않았습니다.\n\n정보 → 점역 응답 지연 체험으로 잠금·재시도 흐름을 확인할 수 있습니다.", "목업 사용 안내", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void MoveBlock(int delta)
    {
        var index = session.ActiveBlock is null ? 0 : session.Blocks.IndexOf(session.ActiveBlock);
        BlockPicker.SelectedItem = session.Blocks[Math.Clamp(index + delta, 0, session.Blocks.Count - 1)];
    }
    internal void InsertTag(string tag)
    {
        var selected = SourceEditor.SelectedText;
        var start = SourceEditor.SelectionStart;
        var open = $"<!{tag}>";
        SourceEditor.SelectedText = open + selected + (tag == "상자" ? "<!상자끝>" : $"<!/{tag}>");
        SourceEditor.Select(start + open.Length, selected.Length); SourceEditor.Focus();
    }
    private void InsertText(string value) { SourceEditor.SelectedText = value; SourceEditor.Focus(); }
    private void SetFontSize(double size)
    {
        SourceEditor.FontSize = Math.Clamp(size, 12, 32); FontSizeLabel.Text = SourceEditor.FontSize.ToString("0");
    }

    internal void ToggleDetached()
    {
        if (detachedWindow is not null) { detachedWindow.Close(); return; }
        detachedPane = new BraillePane(); detachedPane.Attach(session, documents);
        detachedPane.Editor.GotKeyboardFocus += (_, _) => lastEditor = detachedPane?.Editor;
        detachedPane.DetachRequested += (_, _) => detachedWindow?.Close();
        CopyBrailleSettings();
        detachedWindow = new Window { Title = $"점자 — {session.Title}", Owner = this, Width = 680, Height = 760, MinWidth = 420, MinHeight = 420, Content = detachedPane, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        detachedWindow.Closed += (_, _) =>
        {
            detachedPane?.Dispose(); detachedPane = null; detachedWindow = null;
            DetachedPlaceholder.Visibility = Visibility.Collapsed; Braille.Visibility = Visibility.Visible; lastEditor = Braille.Editor;
        };
        DetachedPlaceholder.Visibility = Visibility.Visible; Braille.Visibility = Visibility.Collapsed;
        detachedWindow.Show(); detachedPane.Editor.Focus();
    }
    private void CopyBrailleSettings()
    {
        if (detachedPane is null) return;
        detachedPane.ConfigureKeys(sixDotKeys); detachedPane.SixDotEnabled = Braille.SixDotEnabled;
        detachedPane.SpaceEnabled = Braille.SpaceEnabled; detachedPane.EnterEnabled = Braille.EnterEnabled;
    }
    private void SixDotToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (Braille is null) return;
        Braille.SixDotEnabled = SixDotToggle.IsChecked == true; CopyBrailleSettings();
        if (session is not null) session.Status = Braille.SixDotEnabled ? $"6점 입력 켜짐 · {sixDotKeys} · 점자 편집기에 입력하세요." : "6점 입력 꺼짐";
    }
    private void TimeoutToggle_Click(object sender, RoutedEventArgs e)
    {
        engine.SimulateTimeout = TimeoutToggle.IsChecked;
        session.Status = engine.SimulateTimeout ? "지연 체험 켜짐 · 점역 미리보기를 누르면 0.4초 후 점자 패널이 잠깁니다." : "지연 체험 꺼짐 · 점역 미리보기로 다시 시도하세요.";
    }
    private void OriginalToggle_Click(object sender, RoutedEventArgs e)
    {
        var show = OriginalToggle.IsChecked;
        OriginalPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        OriginalColumn.MinWidth = show ? 260 : 0;
        OriginalColumn.Width = show ? new GridLength(.82, GridUnitType.Star) : new GridLength(0);
        OriginalSplitterColumn.Width = new GridLength(show ? 6 : 0);
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (fileBusy) return false;
        var path = filePath;
        if (saveAs || path is null)
        {
            var dialog = new SaveFileDialog { Title = "세모점 목업 저장", Filter = "세모점 목업 작업 파일|*.mock.smi", DefaultExt = ".mock.smi", FileName = session.Title, AddExtension = true };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        fileBusy = true;
        try
        {
            var owner = session; var version = owner.ChangeVersion; var snapshot = WorkFile.Snapshot(owner);
            await WorkFile.SaveAsync(path, snapshot);
            filePath = path; owner.Title = Path.GetFileName(path);
            owner.IsDirty = owner.ChangeVersion != version;
            owner.Status = owner.IsDirty ? "저장 완료 · 저장 중 편집한 내용은 한 번 더 저장해 주세요." : "저장 완료 · " + path;
            return !owner.IsDirty;
        }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { fileBusy = false; }
    }
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (fileBusy) return false;
        if (!session.IsDirty) return true;
        return MessageBox.Show(this, "변경 내용을 저장하시겠습니까?", "저장하지 않은 내용", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => await SaveAsync(false),
            MessageBoxResult.No => true,
            _ => false
        };
    }
    private async Task OpenAsync()
    {
        if (fileBusy) return;
        var dialog = new OpenFileDialog { Title = "작업 파일 또는 텍스트 열기", Filter = "지원 파일|*.mock.smi;*.txt|세모점 목업|*.mock.smi|UTF-8 텍스트|*.txt" };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardAsync()) return;
        fileBusy = true; IsEnabled = false;
        try
        {
            DocumentSession model;
            if (Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new InvalidDataException("텍스트 파일은 20 MB까지 열 수 있습니다.");
                var text = await File.ReadAllTextAsync(dialog.FileName, new System.Text.UTF8Encoding(false, true));
                model = new DocumentSession { Title = Path.GetFileNameWithoutExtension(dialog.FileName) + ".mock.smi" };
                model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "불러온 텍스트", Source = text, State = "점역 전" });
                model.IsDirty = true; SetSession(model, null, false);
            }
            else { model = await WorkFile.LoadAsync(dialog.FileName); SetSession(model, dialog.FileName, false); }
            session.Status = "문서를 열었습니다. 점역은 고정 샘플로만 제공됩니다.";
        }
        finally { IsEnabled = true; fileBusy = false; }
    }
    private async Task ExportAsync()
    {
        if (session.ActiveBlock is not { } block) return;
        var dialog = new SaveFileDialog { Title = "현재 블록 내보내기", FileName = $"블록-{block.Id}", Filter = "원문 텍스트|*.txt|유니코드 점자 텍스트|*.txt", DefaultExt = ".txt" };
        if (dialog.ShowDialog(this) != true) return;
        await File.WriteAllTextAsync(dialog.FileName, dialog.FilterIndex == 2 ? block.Braille : block.Source);
        session.Status = "현재 블록을 UTF-8 텍스트로 내보냈습니다.";
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        try { if (await ConfirmDiscardAsync()) { allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); } }
        finally { closing = false; }
    }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "작업을 완료하지 못했습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
}
