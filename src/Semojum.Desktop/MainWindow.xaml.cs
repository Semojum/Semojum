using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow : Window
{
    private DocumentSession session = null!;
    private EditorDocuments documents = new();
    private readonly ITranslationEngine engine;
    private readonly BrailleSidecar? sidecar;
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly SemojumApiClient api;
    private readonly JobClient jobs;
    private readonly bool smoke;
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
    private Task? cleanup;
    internal Task ReleaseResourcesAsync() => cleanup ??= ReleaseResourcesCoreAsync();
    private async Task ReleaseResourcesCoreAsync()
    {
        appLifetime.Cancel(); debounce?.Cancel(); dotSpeech.Dispose(); documentLifetime.Cancel(); Braille.Dispose();
        foreach (var opened in openDocuments) { opened.JobLifetime.Cancel(); opened.Lease?.Dispose(); opened.Buffers.Dispose(); }
        if (sidecar is not null) await sidecar.DisposeAsync();
        documentLifetime.Dispose(); http.Dispose();
    }

    internal DocumentSession Session => session;
    internal EditorDocuments Documents => documents;
    internal BraillePane? DetachedPane => detachedPane;
    internal Window? DetachedWindow => detachedWindow;
    internal void PrepareSmokeShutdown() => allowClose = true;

    internal MainWindow(bool smoke = false, bool testWithoutAccount = false)
    {
        this.smoke = smoke;
        api = new(http); jobs = new(api);
        if (smoke) engine = new MockTranslationEngine();
        else { sidecar = EngineRuntime.Create(); engine = new LocalBrailleEngine(sidecar, () => session.Options); }
        translator = new(engine);
        InitializeComponent();
        PopulateTagMenu();
        SourceEditor.TextArea.LeftMargins.Add(new MemoMargin(() => session?.ActiveBlock, "TEXT", line => ShowLineMemos(SearchPanel.Text, line)));
        Braille.Editor.TextArea.LeftMargins.Add(new MemoMargin(() => session?.ActiveBlock, "BRAILLE", line => ShowLineMemos(SearchPanel.Braille, line)));
        InstallEditingKeys(SourceEditor); InstallEditingKeys(Braille.Editor);
        InstallEditorMenu(SourceEditor); InstallEditorMenu(Braille.Editor);
        SourceEditor.TextArea.TextView.LineTransformers.Add(new TagColorizer());
        SourceEditor.Options.HighlightCurrentLine = true;
        SourceEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromRgb(255, 245, 163));
        SourceEditor.TextArea.TextView.CurrentLineBorder = null;
        SourceEditor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!switching && session?.ActiveBlock is { } active) active.SourceCaret = SourceEditor.CaretOffset;
            RefreshSourcePosition(); QueuePreview(false);
        };
        SourceEditor.TextChanged += (_, _) => QueuePreview(true);
        SourceEditor.GotKeyboardFocus += (_, _) => SetActiveEditor(SourceEditor);
        Braille.Editor.GotKeyboardFocus += (_, _) => SetActiveEditor(Braille.Editor);
        Braille.DetachRequested += (_, _) => ToggleDetached();
        Braille.HideRequested += (_, _) => HidePanel("hideBraille");
        Braille.NavigateGridAsync = cell => NavigateToAsync(new(cell.Block, SearchPanel.Braille, cell.Offset));
        Braille.Editor.TextArea.Caret.PositionChanged += (_, _) => { RefreshEditorStatus(); SpeakCaret(Braille.Editor); };
        LineStrip.CloseRequested += (_, _) => SetLinePreviewVisible(false);
        SourceEditor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromRgb(204, 228, 247));
        OriginalPanel.PreviewMouseDown += (_, _) => SetActiveEditor(null);
        ZoomPicker.ItemsSource = new[] { "75%", "100%", "125%", "150%", "175%", "200%" };
        ZoomPicker.SelectedItem = "100%";
        SetSession(smoke ? DocumentSession.Sample() : BlankDocument(), null, smoke);
        RestorePreferences(); ApplyAppearance(new());
        if (!smoke && !testWithoutAccount) Loaded += async (_, _) => await StartAccountAsync();
        TimeoutToggle.Visibility = smoke ? Visibility.Visible : Visibility.Collapsed;
        BindCommonKeys(this);
        UpdatePanelLayout(); RefreshEditorStatus();
        Closed += (_, _) => _ = ReleaseResourcesAsync();
    }

    private static DocumentSession BlankDocument()
    {
        var model = new DocumentSession();
        model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "새 문서" });
        model.IsDirty = false; return model;
    }

    private void SetSession(DocumentSession model, string? path, bool showOriginal)
    {
        switching = true;
        try
        {
            documentLifetime.Cancel(); documentLifetime.Dispose(); documentLifetime = new();
            if (session is not null) session.PropertyChanged -= SessionChanged;
            StoreActiveDocument();
            openDocument = RegisterDocument(model, path, showOriginal);
            documents = openDocument.Buffers; translator = new(engine); translatedRevisions.Clear();
            foreach (var pair in openDocument.Translated) translatedRevisions[pair.Key] = pair.Value;
            previewGeneration++; debounce?.Cancel(); pendingSourceTranslation = false;
            SelectionPane.Visibility = Visibility.Collapsed; BlockSelectionList.UnselectAll();
            session = model; filePath = openDocument.Path;
            tabSwitching = true; DocumentTabs.SelectedItem = openDocument.Tab; tabSwitching = false;
            foreach (var block in session.Blocks.Where(b => !translatedRevisions.ContainsKey(b))) translatedRevisions[block] = block.Braille.Length != 0
                && (block.Translation?.SourceText is null || block.Translation.SourceText == block.Source) ? block.SourceRevision : -1;
            DataContext = session;
            foreach (var window in detachedPanels.Values)
            {
                window.DataContext = session;
                window.Title = $"{window.Tag} — {session.Title}";
            }
            if (detachedWindow is not null) detachedWindow.Title = $"점자 — {session.Title}";
            session.PropertyChanged += SessionChanged;
            Braille.Attach(session, documents); detachedPane?.Attach(session, documents);
            OriginalEmpty.Visibility = showOriginal ? Visibility.Collapsed : Visibility.Visible;
            ShowActiveBlock();
        }
        finally { switching = false; }
    }

    private void SessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSession.IsDirty) && session.IsDirty) Welcome.Visibility = Visibility.Collapsed;
        if (e.PropertyName == nameof(DocumentSession.ActiveBlock)) ShowActiveBlock();
        if (e.PropertyName == nameof(DocumentSession.Options))
            foreach (var block in session.Blocks) { translator.Invalidate(block); translatedRevisions[block] = -1; block.State = "옵션 변경 · 재점역 필요"; }
        RefreshEditorStatus();
        if (detachedWindow is not null) detachedWindow.Title = $"점자 — {session.Title}";
        foreach (var window in detachedPanels.Values) window.Title = $"{window.Tag} — {session.Title}";
    }

    private void ShowActiveBlock()
    {
        Welcome.Visibility = !smoke && session.JobId is null && filePath is null && session.Blocks.Count == 1
            && session.Blocks[0].Source.Length == 0 && session.Blocks[0].Braille.Length == 0 && !session.IsDirty ? Visibility.Visible : Visibility.Collapsed;
        if (session.ActiveBlock is not { } block) return;
        var wasSwitching = switching; switching = true;
        try
        {
            if (SelectionPane.Visibility == Visibility.Visible) PopulateBlockSelection();
            currentPreviewLine = 0; currentPreviewCells = "";
            LineStrip.SetPreview(0, ""); detachedPane?.SetLinePreview(0, "");
            SourceEditor.Document = documents.Source(block);
            SourceEditor.IsReadOnly = session.IsReadOnly || block.Metadata.IsBlocked;
            SourceEditor.CaretOffset = Math.Clamp(block.SourceCaret, 0, SourceEditor.Document.TextLength);
            SourceEditor.ScrollToLine(SourceEditor.TextArea.Caret.Line);
            Original.SelectBlock(block.Id, block.Page);
            ShowOriginal(block.Page);
            RefreshSourcePosition();
        }
        finally { switching = wasSwitching; }
        QueuePreview(false);
    }

    private void RefreshSourcePosition()
    {
        if (session?.ActiveBlock is { } block)
            SourcePosition.Text = $"줄 {SourceEditor.TextArea.Caret.Line} / {SourceEditor.Document.LineCount}";
        RefreshEditorStatus();
    }

    private async void BlockPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (switching || session is null || BlockPicker.SelectedItem is not EditorBlock next || next == session.ActiveBlock) return;
        var previous = session.ActiveBlock;
        var changed = previous is not null && translatedRevisions.GetValueOrDefault(previous, -1) != previous.SourceRevision;
        var decision = changed && autoTranslate ? ChooseTranslation(previous!) : ManualEditDecision.Keep;
        if (decision == ManualEditDecision.Cancel)
        {
            switching = true; BlockPicker.SetCurrentValue(Selector.SelectedItemProperty, previous); switching = false; return;
        }
        session.ActiveBlock = next;
        if (changed && autoTranslate) await TranslateAsync(previous!, decision);
    }

    private ManualEditDecision ChooseTranslation(EditorBlock block)
    {
        if (!block.HasManualBraille) return ManualEditDecision.Overwrite;
        var dialog = new ManualEditDialog { Owner = this }; dialog.ShowDialog(); return dialog.Decision;
    }

    private async Task TranslateAsync(EditorBlock block, ManualEditDecision decision, bool interactive = true)
    {
        var owner = session;
        var revision = block.SourceRevision;
        var result = await translator.TranslateAsync(block, decision, smoke ? TimeSpan.FromMilliseconds(400) : TimeSpan.FromSeconds(35), documentLifetime.Token);
        if (owner != session) return;
        if (result is TranslationOutcome.Applied or TranslationOutcome.Kept) translatedRevisions[block] = revision;
        session.Status = result switch
        {
            TranslationOutcome.Applied => smoke ? "화면 검증용 샘플 점자" : $"블록 {block.Id} · {block.State}" + (block.Translation?.Dropped.Length > 0 ? " · 누락: " + string.Join(", ", block.Translation.Dropped.Select(d => $"{d.Text}({d.Count})")) : ""),
            TranslationOutcome.Kept => $"블록 {block.Id} · 직접 수정한 점자를 유지했습니다.",
            TranslationOutcome.Superseded => "점역 대기 중 내용이 바뀌었습니다. 현재 편집 내용을 유지합니다.",
            TranslationOutcome.TimedOut => "점역 응답 지연 · 이 블록의 점자가 잠겼습니다. 원문 편집과 저장은 가능합니다.",
            TranslationOutcome.Failed => block.State + " · 원문 편집과 저장은 가능합니다.",
            _ => "점역을 취소했습니다."
        };
        if (interactive && result is (TranslationOutcome.TimedOut or TranslationOutcome.Failed))
        {
            var retry = MessageBox.Show(this, "점역 엔진의 응답을 받지 못했습니다. 원문은 계속 편집하고 저장할 수 있습니다.\n\n지금 다시 시도하시겠습니까?", "점역 실패", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (retry == MessageBoxResult.Yes)
            {
                if (engine is MockTranslationEngine mock) mock.SimulateTimeout = false;
                TimeoutToggle.IsChecked = false;
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
            if (session.IsReadOnly && (action.StartsWith("tag:") || new[] { "undo", "redo", "cut", "paste", "replace", "addBlock", "deleteBlock", "moveBlockUp", "moveBlockDown", "indent", "unindent", "clearLine", "clearBlock", "table", "memo", "memos", "drafts", "symbol", "math", "footer", "layout", "translationOptions", "translate", "translateAll" }.Contains(action)))
                throw new InvalidOperationException("읽기 전용 문서입니다. 다른 이름으로 저장한 뒤 편집해 주세요.");
            switch (action)
            {
                case "autoUpdate": preferences = preferences with { AutoCheckUpdates = AutoUpdateToggle.IsChecked }; await PersistPreferencesAsync(); break;
                case "selectRange": SelectTextRange(); break;
                case "showMemos":
                    foreach (var editor in new[] { SourceEditor, Braille.Editor, detachedPane?.Editor }.OfType<TextEditor>())
                        foreach (var margin in editor.TextArea.LeftMargins.OfType<MemoMargin>()) margin.Visibility = MemoToggle.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "showOutlines":
                    Braille.GridView.ShowOutlines = OutlineToggle.IsChecked; Braille.GridView.InvalidateFaces();
                    if (detachedPane is not null) { detachedPane.GridView.ShowOutlines = OutlineToggle.IsChecked; detachedPane.GridView.InvalidateFaces(); }
                    break;
                case "new": await NewTextDocumentAsync(); break;
                case "closeDocument": if (openDocument is not null) await CloseDocumentAsync(openDocument); break;
                case "defaults": if (EnsureAccount()) new DefaultsDialog(new(api)) { Owner = this }.ShowDialog(); break;
                case "forms":
                    if (EnsureAccount()) { var form = new FormDialog(new(api), session.Layout, session.Options) { Owner = this }; if (form.ShowDialog() == true && form.Selected is { } selected) { session.Layout = selected.Layout; session.Options = selected.Options; } }
                    break;
                case "sample": SetSession(DocumentSession.Sample(), null, true); break;
                case "open": await OpenAsync(); break;
                case "workFolder": await OpenWorkFolderAsync(true); break;
                case "lastFolder": await OpenWorkFolderAsync(false); break;
                case "save": await SaveAsync(false); break;
                case "saveAs": await SaveAsync(true); break;
                case "optimize": await OptimizeDocumentAsync(); break;
                case "composition": await ShowCompositionAsync(); break;
                case "export": await ExportAsync(); break;
                case "version": await CheckVersionAsync(); break;
                case "login": if (api.Account is not null) await LogoutAccountAsync(); else { EnsureAccount(); LoadAccountPreferences(); } break;
                case "logout": await LogoutAccountAsync(); break;
                case "serverOpen": await OpenServerAsync(); break;
                case "upload": await UploadAsync(); break;
                case "cancelJob":
                    var cancelling = session;
                    if (cancelling.JobId is { } jobId) { await jobs.CancelAsync(jobId); cancelling.Status = "변환 중단을 요청했습니다. 처리 중인 쪽이 마무리되면 반영됩니다."; }
                    break;
                case "pageStatus":
                    var pageStatus = new PageStatusDialog(session) { Owner = this };
                    if (pageStatus.ShowDialog() == true && session.Blocks.FirstOrDefault(b => b.Page == pageStatus.SelectedPage && b.ServerId is not null) is { } pageBlock)
                        BlockPicker.SetCurrentValue(Selector.SelectedItemProperty, pageBlock);
                    break;
                case "close": Close(); break;
                case "undo": ReplayHistory(); break;
                case "redo": ReplayHistory(true); break;
                case "cut": if (!CopySelectedBlocks(true)) (lastEditor ?? SourceEditor).Cut(); break;
                case "copy": if (!CopySelectedBlocks(false)) (lastEditor ?? SourceEditor).Copy(); break;
                case "paste": if (!await PasteSelectedBlocksAsync()) (lastEditor ?? SourceEditor).Paste(); break;
                case "find": await ShowSearchAsync(false); break;
                case "replace": await ShowSearchAsync(true); break;
                case "findNext":
                    if (searchQuery.Length == 0) await ShowSearchAsync(false);
                    else session.Status = await FindNextAsync(searchQuery, ActiveSearchPanel);
                    break;
                case "goto": ShowGoTo(); break;
                case "addBlock": await AddBlockAsync(); break;
                case "selectBlocks": selectionPanel = ActiveSearchPanel; SelectionPane.Visibility = Visibility.Visible; PopulateBlockSelection(); break;
                case "deleteBlock": case "moveBlockUp": case "moveBlockDown": case "indent": case "unindent": SelectionEdit(action); break;
                case "clearLine": ClearCurrentLine(); break;
                case "clearBlock": if (lastEditor?.IsReadOnly != true) EditStructure(() => { if (ActiveSearchPanel == SearchPanel.Text) session.ActiveBlock!.Source = ""; else session.ActiveBlock!.EditBraille(""); }); break;
                case "table": await EditTableAsync(); break;
                case "board": if (EnsureAccount()) new BoardDialog(new BoardClient(api), WorkInformation()) { Owner = this }.ShowDialog(); break;
                case "notifications": if (EnsureAccount()) new NotificationsDialog(new BoardClient(api), WorkInformation()) { Owner = this }.ShowDialog(); break;
                case "hideOriginal": case "hideSource": case "hideBraille": HidePanel(action); break;
                case "lineNumbers": LineNumbersToggle.IsChecked = !LineNumbersToggle.IsChecked; ApplyLineNumbers(); break;
                case "previous": MoveBlock(-1); break;
                case "next": MoveBlock(1); break;
                case "detach": ToggleDetached(); break;
                case "detachSource": ToggleSourcePanel(); break;
                case "detachOriginal": ToggleOriginalPanel(); break;
                case "zoomIn": SetFontSize(SourceEditor.FontSize + 2); break;
                case "zoomOut": SetFontSize(SourceEditor.FontSize - 2); break;
                case "zoomReset": SetFontSize(14); break;
                case "translate": if (session.ActiveBlock is { } block) await TranslateAsync(block, ChooseTranslation(block)); break;
                case "translateAll":
                    var translationSession = session;
                    foreach (var item in translationSession.Blocks.ToArray())
                    { if (translationSession != session) break; var choice = ChooseTranslation(item); if (choice == ManualEditDecision.Cancel) break; await TranslateAsync(item, choice); }
                    break;
                case "footer":
                    var footer = new FooterDialog(session) { Owner = this };
                    if (footer.ShowDialog() == true) { session.FooterText = footer.Footer; session.FooterRanges = footer.Ranges; session.Layout = session.Layout with { FooterAlign = footer.Alignment }; }
                    break;
                case "memo": ShowLineMemos(ActiveSearchPanel); break;
                case "memos": await ShowMemoListAsync(); break;
                case "drafts": await ShowAlternativesAsync(); break;
                case "rules": ShowRules(); break;
                case "symbol": await InsertSymbolAsync(); break;
                case "appearance": await EditAppearanceAsync(false); break;
                case "colors": await EditAppearanceAsync(true); break;
                case "snippets": await EditSnippetsAsync(); break;
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
                        preferences = preferences with { SixDotKeys = sixDotKeys, SpaceEnabled = sixdot.Space, EnterEnabled = sixdot.Enter };
                        await PersistPreferencesAsync();
                        accountPreferences = accountPreferences with { SixDotKeys = sixDotKeys, SpaceEnabled = sixdot.Space, EnterEnabled = sixdot.Enter };
                        await PersistAccountPreferencesAsync();
                    }
                    break;
                case "translationOptions":
                    var options = new TranslationOptionsDialog(autoTranslate, session.Options) { Owner = this };
                    if (options.ShowDialog() == true) { autoTranslate = options.AutoTranslate; session.Options = options.Result; }
                    break;
                case "help": new HelpWindow(ActiveSearchPanel == SearchPanel.Text ? "텍스트 고치기" : "점자 직접 고치기") { Owner = this }.Show(); break;
                case "usage":
                    if (EnsureAccount()) { var usage = new UsageDialog(api) { Owner = this }; if (usage.ShowDialog() == true && usage.JobId is { } id) await LoadJobAsync(id, Path.GetFileNameWithoutExtension(usage.FileName ?? "서버 작업") + ".smj"); }
                    break;
                case "about": MessageBox.Show(this, $"세모점 {CurrentVersion.ToString(3)}\n운영 기관: 등록 전\n문의: contact-semojum@gmail.com", "세모점 정보"); break;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void MoveBlock(int delta)
    {
        var index = session.ActiveBlock is null ? 0 : session.Blocks.IndexOf(session.ActiveBlock);
        BlockPicker.SetCurrentValue(Selector.SelectedItemProperty, session.Blocks[Math.Clamp(index + delta, 0, session.Blocks.Count - 1)]);
    }
    internal void InsertTag(string tag)
    {
        if (session.IsReadOnly || SourceEditor.IsReadOnly) return;
        var selected = SourceEditor.SelectedText;
        var start = SourceEditor.SelectionStart;
        var definition = BrailleTags.Get(tag);
        var open = definition.Open;
        SourceEditor.SelectedText = definition.Insert(selected);
        SourceEditor.Select(start + open.Length, selected.Length); SourceEditor.Focus();
    }
    private void InsertText(string value) { if (session.IsReadOnly || SourceEditor.IsReadOnly) return; SourceEditor.SelectedText = value; SourceEditor.Focus(); }
    private void SetFontSize(double size)
    {
        SourceEditor.FontSize = Math.Clamp(size, 10, 32);
        Braille.Editor.FontSize = SourceEditor.FontSize * 22 / 14;
        if (detachedPane is not null) detachedPane.Editor.FontSize = Braille.Editor.FontSize;
        FontSizeLabel.Content = SourceEditor.FontSize.ToString("0") + " px";
        var percent = (SourceEditor.FontSize / 14 * 100).ToString("0") + "%";
        if (ZoomPicker.Text != percent) ZoomPicker.Text = percent;
    }

    internal void ToggleDetached()
    {
        if (detachedWindow is not null) { detachedWindow.Close(); return; }
        detachedPane = new BraillePane(); detachedPane.Attach(session, documents); InstallEditingKeys(detachedPane.Editor);
        InstallEditorMenu(detachedPane.Editor);
        detachedPane.NavigateGridAsync = cell => NavigateToAsync(new(cell.Block, SearchPanel.Braille, cell.Offset));
        detachedPane.Editor.TextArea.LeftMargins.Add(new MemoMargin(() => session?.ActiveBlock, "BRAILLE", line => ShowLineMemos(SearchPanel.Braille, line)));
        detachedPane.Editor.GotKeyboardFocus += (_, _) => SetActiveEditor(detachedPane?.Editor);
        detachedPane.Editor.TextArea.Caret.PositionChanged += (_, _) => RefreshEditorStatus();
        detachedPane.HideRequested += (_, _) => detachedWindow?.Close();
        detachedPane.PreviewCloseRequested += (_, _) => SetLinePreviewVisible(false);
        detachedPane.SetLinePreviewVisible(LinePreviewToggle.IsChecked);
        detachedPane.SetLinePreview(currentPreviewLine, currentPreviewCells);
        detachedPane.DetachRequested += (_, _) => detachedWindow?.Close();
        BrailleToggle.IsChecked = true;
        CopyBrailleSettings(); ApplyAppearance(appearance);
        detachedPane.Editor.TextArea.Caret.PositionChanged += (_, _) => { if (detachedPane is not null) SpeakCaret(detachedPane.Editor); };
        detachedWindow = new Window { Title = $"점자 — {session.Title}", Owner = this, Width = 680, Height = 760, MinWidth = 420, MinHeight = 420, Content = detachedPane, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        detachedWindow.Closed += (_, _) =>
        {
            if (detachedPane is not null) wideCarets.Remove(detachedPane.Editor);
            detachedPane?.Dispose(); detachedPane = null; detachedWindow = null;
            DetachedPlaceholder.Visibility = Visibility.Collapsed; Braille.Visibility = Visibility.Visible; lastEditor = Braille.Editor;
            UpdatePanelLayout();
        };
        DetachedPlaceholder.Visibility = Visibility.Visible; Braille.Visibility = Visibility.Collapsed;
        BindCommonKeys(detachedWindow); UpdatePanelLayout();
        detachedWindow.Show(); detachedPane.Editor.Focus();
    }
    private void CopyBrailleSettings()
    {
        if (detachedPane is null) return;
        detachedPane.ConfigureKeys(sixDotKeys); detachedPane.SixDotEnabled = Braille.SixDotEnabled;
        detachedPane.Editor.FontSize = Braille.Editor.FontSize; detachedPane.Editor.ShowLineNumbers = Braille.Editor.ShowLineNumbers;
        detachedPane.SpaceEnabled = Braille.SpaceEnabled; detachedPane.EnterEnabled = Braille.EnterEnabled;
    }
    private void SixDotToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (Braille is null) return;
        Braille.SixDotEnabled = SixDotToggle.IsChecked == true; CopyBrailleSettings();
        RefreshEditorStatus();
        if (session is not null) session.Status = Braille.SixDotEnabled ? $"6점 입력 켜짐 · {sixDotKeys} · 점자 편집기에 입력하세요." : "6점 입력 꺼짐";
    }
    private void TimeoutToggle_Click(object sender, RoutedEventArgs e)
    {
        if (engine is MockTranslationEngine mock) mock.SimulateTimeout = TimeoutToggle.IsChecked;
    }
    private void OriginalToggle_Click(object sender, RoutedEventArgs e)
    {
        if (detachedPanels.TryGetValue(OriginalPanel, out var window)) window.Close();
        UpdatePanelLayout();
    }

    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (fileBusy) return false;
        var path = filePath;
        if (saveAs || session.IsReadOnly || path is null)
        {
            var dialog = new SaveFileDialog { Title = "세모점 작업 저장", Filter = "세모점 작업 파일|*.smj", DefaultExt = ".smj", FileName = session.Title, AddExtension = true };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        WorkFileLease? candidate = null;
        var samePath = filePath is not null && LocalWorkspace.SamePath(filePath, path);
        if (!samePath || openDocument?.Lease is null)
        {
            if (openDocuments.Any(d => d != openDocument && d.Path is not null && LocalWorkspace.SamePath(d.Path, path))) { ShowError(new IOException("다른 탭에서 열어 둔 파일입니다.")); return false; }
            candidate = WorkFileLease.TryAcquire(path);
            if (candidate is null) { ShowError(new IOException("다른 창에서 편집 중이거나 쓸 수 없는 경로입니다. 다른 이름으로 저장해 주세요.")); return false; }
        }
        fileBusy = true;
        try
        {
            var owner = session; var version = owner.ChangeVersion; var snapshot = SmjFile.Snapshot(owner);
            await SmjFile.SaveAsync(path, snapshot);
            if (candidate is not null && openDocument is not null) { openDocument.Lease?.Dispose(); openDocument.Lease = candidate; candidate = null; }
            owner.IsReadOnly = false; ShowActiveBlock();
            filePath = path; if (openDocument is not null) openDocument.Path = path; owner.Title = Path.GetFileName(path);
            await RememberFileAsync(path);
            if (owner.JobId is not null)
            {
                try
                {
                    var result = await jobs.SaveAsync(snapshot);
                    if (!result.Complete(snapshot.Manifest.Blocks.Where(b => !b.Metadata.IsBlocked).Select(b => b.Page).Distinct().Count()))
                        throw new InvalidOperationException($"서버 저장 실패 쪽: {string.Join(", ", result.FailedPages)}. {result.SavedPages}쪽만 저장되었습니다.");
                }
                catch (Exception ex)
                {
                    owner.IsDirty = true;
                    owner.Status = "로컬 저장 완료 · 서버 저장 미완료";
                    MessageBox.Show(this, $"로컬 .smj 파일은 저장했습니다.\n서버에는 저장을 완료하지 못했습니다.\n\n{ex.Message}", "저장 결과", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
            }
            owner.IsDirty = owner.ChangeVersion != version;
            owner.Status = owner.IsDirty ? "저장 완료 · 저장 중 편집한 내용은 한 번 더 저장해 주세요." : owner.JobId is null ? "로컬 초안 저장 완료 · 서버 작업 없음" : "로컬·서버 저장 완료 · " + path;
            if (owner.FooterRanges.Length > 0) owner.Status += " · 꼬리말 범위는 .smj에 보존";
            return !owner.IsDirty;
        }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { candidate?.Dispose(); fileBusy = false; }
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
    internal async Task OpenAsync(string? requestedPath = null)
    {
        if (fileBusy) return;
        if (requestedPath is null)
        {
            var dialog = new OpenFileDialog { Title = "작업 파일 또는 텍스트 열기", Filter = "지원 파일|*.smj;*.mock.smi;*.txt|세모점 작업|*.smj|UTF-8 텍스트|*.txt|이전 목업 파일|*.mock.smi", InitialDirectory = preferences.WorkFolder ?? "" };
            if (dialog.ShowDialog(this) != true) return;
            requestedPath = dialog.FileName;
        }
        if (filePath is not null && LocalWorkspace.SamePath(filePath, requestedPath))
        { Activate(); SourceEditor.Focus(); return; }
        var existing = openDocuments.FirstOrDefault(d => d.Path is not null && LocalWorkspace.SamePath(d.Path, requestedPath));
        if (existing is not null) { SetSession(existing.Model, existing.Path, existing.ShowOriginal); return; }
        if (!smoke && !EnsureAccount()) return;
        fileBusy = true; IsEnabled = false;
        try
        {
            DocumentSession model;
            if (Path.GetExtension(requestedPath).Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(requestedPath).Length > JobClient.MaximumUploadBytes) throw new InvalidDataException("텍스트 파일은 100 MB까지 열 수 있습니다.");
                var text = await File.ReadAllTextAsync(requestedPath, new System.Text.UTF8Encoding(false, true));
                model = new DocumentSession { Title = Path.GetFileNameWithoutExtension(requestedPath) + ".smj" };
                var index = 0;
                foreach (var chunk in TextImport.Split(text)) model.Add(new EditorBlock { Id = ++index, Page = 1, Heading = $"불러온 텍스트 {index}", Source = chunk, State = "점역 전" });
                model.OriginalFiles[Path.GetFileName(requestedPath)] = await File.ReadAllBytesAsync(requestedPath);
                await ConfigureTextJobAsync(model);
                model.IsDirty = true; SetSession(model, null, false);
                OriginalToggle.IsChecked = false; UpdatePanelLayout();
                _ = TranslateImportedTextAsync(openDocument!);
            }
            else if (requestedPath.EndsWith(".mock.smi", StringComparison.OrdinalIgnoreCase))
            { model = await WorkFile.LoadAsync(requestedPath); model.Title = Path.GetFileNameWithoutExtension(requestedPath) + ".smj"; SetSession(model, null, false); }
            else
            {
                var lease = WorkFileLease.TryAcquire(requestedPath);
                try
                {
                    model = await SmjFile.LoadAsync(requestedPath); model.IsReadOnly = lease is null;
                    SetSession(model, requestedPath, false); openDocument!.Lease = lease; lease = null;
                    await RememberFileAsync(requestedPath);
                    if (model.IsReadOnly) MessageBox.Show(this, "다른 창에서 편집 중이거나 쓰기 권한이 없어 읽기 전용으로 열었습니다. 다른 이름으로 저장하면 편집할 수 있습니다.", "읽기 전용");
                }
                finally { lease?.Dispose(); }
            }
            session.Status = "문서를 열었습니다. 자동 저장은 사용하지 않습니다.";
        }
        finally { IsEnabled = true; fileBusy = false; }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        try { if (await ConfirmAllDocumentsAsync()) { await ReleaseResourcesAsync(); allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); } }
        finally { closing = false; }
    }
    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "작업을 완료하지 못했습니다", MessageBoxButton.OK, MessageBoxImage.Warning);
}
