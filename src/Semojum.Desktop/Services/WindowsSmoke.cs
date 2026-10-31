using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Runs on Windows only. Captures real WPF output and checks document sharing.</summary>
internal static class WindowsSmoke
{
    public static async Task RunAsync(MainWindow window, string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException(name);
            checks.Add("PASS " + name);
        }
        window.WindowState = WindowState.Normal; window.Width = 1440; window.Height = 1000;
        window.PageEditor.Editor.Focus();
        // Figma 1:112 shows lines 2–3 selected with the caret at the start of line 4 (the visible panel is the continuous page editor).
        var mainDocument = window.PageEditor.Editor.Document;
        if (mainDocument.LineCount >= 4) window.PageEditor.FollowActive(mainDocument.GetLineByNumber(2).Offset, mainDocument.GetLineByNumber(4).Offset - mainDocument.GetLineByNumber(2).Offset);
        await RenderAsync(window, Path.Combine(directory, "01-main.png"));
        window.PageEditor.FollowActive(0, 0);
        Check(window.PageEditor.Editor.ActualWidth > 250 && window.Braille.ActualWidth > 250, "Three-panel layout has usable editor widths");
        Check(window.LineStrip.ActualHeight == 64 && Math.Abs(window.LineStrip.ActualWidth - window.PanelGrid.ActualWidth) < 2,
            "Figma line translation strip spans all panels at 64px");
        Check(window.SourceHeader.ActualHeight == 36 && window.StatusZoom.ActualHeight > 0 && window.ZoomSlider.ActualWidth == 106,
            "Figma panel headers and status-bar zoom controls retain compact geometry");
        Check(window.Braille.GridView.Items.Count > 0 && window.Braille.GridView.ActualHeight > 100,
            "Service plan braille grid renders editable block coordinates");
        foreach (var icon in new[] { window.CutIcon, window.DraftIcon, window.LinenoIcon })
        {
            // Stretch=None keeps the painted SVG bounds (the cut glyph is 12×14 inside its 16×16 viewBox).
            Check(icon.Width == 16 && icon.Height == 16 && icon.ActualWidth > 0 && icon.ActualWidth <= 16
                && icon.ActualHeight > 0 && icon.ActualHeight <= 16 && icon.Drawings is { Bounds.IsEmpty: false },
                $"Local Figma SVG loaded without scaling in its 16px slot: {icon.Name}");
        }
        var toolbarIcons = new (string Tag, string Asset)[]
        {
            ("new", "new"), ("open", "open"), ("save", "save"), ("cut", "cut"), ("copy", "copy"), ("paste", "paste"),
            ("undo", "undo"), ("redo", "redo"), ("find", "find"), ("goto", "goto"), ("translate", "recon"), ("export", "export"),
            ("tag:쪽바꿈", "pagebreak"), ("footer", "footer"), ("tag:가운데정렬", "align"), ("tag:강조", "emph"),
            ("tag:밑줄빈칸", "blank"), ("table", "table"), ("memo", "note"), ("math", "math"), ("drafts", "draft"),
            ("formatMarks", "mark"), ("lineNumbers", "lineno")
        };
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        var toolbarButtons = Descendants(window.EditorToolbarScroll).Concat(Descendants(window.FormatToolbarScroll)).OfType<Button>().ToArray();
        foreach (var (tag, asset) in toolbarIcons)
        {
            var button = toolbarButtons.Single(b => Equals(b.Tag, tag));
            var svg = button.Content as SharpVectors.Converters.SvgViewbox;
            Check(svg is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false }
                && svg.Source is { } iconSourceUri && iconSourceUri.ToString().EndsWith("/main-toolbar-" + asset + ".svg", StringComparison.Ordinal)
                && svg.ActualWidth > 0 && svg.ActualWidth <= 16 && svg.ActualHeight > 0 && svg.ActualHeight <= 16,
                $"Final Figma toolbar icon is rendered in its 16px slot: {tag}");
        }
        var initialVersion = window.Session.ChangeVersion;
        window.HidePanel("hideOriginal"); window.UpdateLayout();
        Check(window.OriginalPanel.Visibility == Visibility.Collapsed && window.PageEditor.Editor.ActualWidth > 350,
            "Hiding original redistributes editor space");
        window.OriginalToggle.IsChecked = true; window.ToggleOriginalPanel(); window.ToggleOriginalPanel();
        window.UpdateLayout();
        Check(window.Session.ChangeVersion == initialVersion, "Panel visibility changes do not dirty the document");
        var beforeNavigation = window.Session.ChangeVersion;
        await window.NavigateAsync(NavigationTarget.OriginalPage, "+1", SearchPanel.Text);
        Check(window.Session.ActiveBlock!.Page == 13 && window.SourceEditor.CaretOffset == 0,
            "Relative original-page navigation synchronizes source and braille");
        await window.NavigateAsync(NavigationTarget.OriginalPage, "12", SearchPanel.Text);
        await window.NavigateAsync(NavigationTarget.Line, "2", SearchPanel.Text);
        Check(window.SourceEditor.TextArea.Caret.Line == 2 && window.Session.ChangeVersion == beforeNavigation,
            "Go to line moves caret without modifying document");
        await CaptureDialogAsync(new GoToDialog(SearchPanel.Text,
            (target, number) => window.NavigateAsync(target, number, SearchPanel.Text)) { Owner = window }, directory, "15-go-to.png");
        var block = window.Session.ActiveBlock!;
        var source = block.Source;
        window.PageEditor.FollowActive(0, 2); window.InsertTag("강조");
        Check(block.Source.StartsWith("<!강조>"), "Source tag insertion reaches model");
        ApplicationCommands.Undo.Execute(null, window.PageEditor.Editor.TextArea); Check(block.Source == source, "Tag insertion has one-step undo");

        window.ToggleDetached();
        Check(ReferenceEquals(window.Braille.Editor.Document, window.DetachedPane!.Editor.Document), "Detached window shares the same document");
        // Figma 1:1023 shows untouched braille scrolled to line 5 with the caret on line 6; capture that state before the manual edit below.
        window.DetachedPane.Editor.TextArea.Caret.Line = Math.Min(6, window.DetachedPane.Editor.LineCount);
        window.DetachedPane.Editor.ScrollToVerticalOffset(4 * 28);
        await RenderAsync(window.DetachedWindow!, Path.Combine(directory, "02-detached.png"));
        window.DetachedPane.Editor.Document.Insert(0, "⠿");
        Check(block.Braille.StartsWith("⠿") && block.HasManualBraille, "Detached edits update model and manual-correction state");
        window.ToggleDetached();
        Check(window.Braille.Editor.Text.StartsWith("⠿"), "Reattached panel retains detached edits");
        var versionBeforeDetach = window.Session.ChangeVersion;
        var sourceDocument = window.SourceEditor.Document;
        window.ToggleSourcePanel(); window.ToggleOriginalPanel(); window.UpdateLayout();
        Check(Math.Abs(window.BrailleContainer.ActualWidth - window.PanelGrid.ActualWidth) < 2,
            "Remaining panel fills main window after source and original detach");
        Check(ReferenceEquals(sourceDocument, window.SourceEditor.Document) && window.DetachedSourceWindow!.IsVisible,
            "Source detachment retains editor document and undo history");
        Check(window.Session.ChangeVersion == versionBeforeDetach, "Detaching panels does not change document state");
        window.SourceEditor.Document.Insert(0, "분리 편집 ");
        Check(block.Source.StartsWith("분리 편집 "), "Detached source edits reach shared model");
        ApplicationCommands.Undo.Execute(null, window.PageEditor.Editor.TextArea);
        Check(block.Source == source, "Detached source edit can be undone");
        window.Session.ActiveBlock = window.Session.Blocks[1];
        Check(window.SourceEditor.Text == window.Session.Blocks[1].Source
            && window.Braille.Editor.Text == window.Session.Blocks[1].Braille
            && window.Original.PageLabel.Text.StartsWith(window.Session.Blocks[1].Page + " /"),
            "Active block stays synchronized across independently detached panels");
        window.Session.ActiveBlock = block;
        await RenderAsync(window.DetachedSourceWindow!, Path.Combine(directory, "09-detached-source.png"));
        await RenderAsync(window.DetachedOriginalWindow!, Path.Combine(directory, "10-detached-original.png"));
        await RenderAsync(window, Path.Combine(directory, "11-remaining-panel.png"));
        var versionBeforeRestore = window.Session.ChangeVersion;
        window.DetachedSourceWindow!.Close(); window.DetachedOriginalWindow!.Close(); window.UpdateLayout();
        Check(window.PanelGrid.Children.Contains(window.SourcePanel) && window.PanelGrid.Children.Contains(window.OriginalPanel)
            && window.PageEditor.Editor.ActualWidth > 250 && window.Braille.ActualWidth > 250
            && window.Session.ChangeVersion == versionBeforeRestore,
            "Closing detached windows restores usable panels without changing document state");
        block.IsBrailleLocked = true;
        Check(window.Braille.Editor.IsReadOnly && !window.SourceEditor.IsReadOnly, "Translation lock affects only braille editor");
        block.IsBrailleLocked = false;

        window.Session.ActiveBlock = window.Session.Blocks[1];
        Check(window.SourceEditor.Text == window.Session.Blocks[1].Source, "Active block updates source editor");
        Check(window.Braille.Editor.Text == window.Session.Blocks[1].Braille, "Active block updates braille editor");
        window.Session.ActiveBlock = block;
        window.SourceEditor.CaretOffset = 8; window.Braille.Editor.CaretOffset = 3;
        window.Session.ActiveBlock = window.Session.Blocks[1]; window.Session.ActiveBlock = block;
        Check(window.SourceEditor.CaretOffset == 8 && window.Braille.Editor.CaretOffset == 3, "Block navigation restores each panel caret");
        window.SourceEditor.CaretOffset = 8; window.Braille.Editor.CaretOffset = 3;
        await CaptureDialogAsync(new LayoutDialog(window.Session.Layout) { Owner = window }, directory, "03-page-settings.png");
        await CaptureDialogAsync(new SixDotDialog("F D S J K L", true, true) { Owner = window }, directory, "04-six-dot-settings.png");
        await CaptureDialogAsync(new MathDialog((_, _) => Task.FromResult(BrailleTranslation.Plain("⠁⠃"))) { Owner = window }, directory, "05-math.png");
        await CaptureDialogAsync(new ManualEditDialog { Owner = window }, directory, "06-manual-correction.png");
        checks.Add("PASS Layout, six-dot, math, and manual-correction dialogs render");
        var path = Path.Combine(directory, "roundtrip.smj");
        await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
        var loaded = await SmjFile.LoadAsync(path);
        Check(loaded.Blocks[0].Braille == block.Braille && loaded.Blocks[0].HasManualBraille, "Saved file preserves detached manual edits");
        await WindowsQa4Checks.RunAsync(window, directory, Check, RenderAsync);
        window.Session.IsDirty = false;
        var originalDocument = window.Session;
        await window.OpenAsync(path);
        Check(window.DocumentTabs.Items.Count == 2, "Opening a work file retains the previous document tab");
        Check(window.SourceEditor.CaretOffset == 8 && window.Braille.Editor.CaretOffset == 3, "Reopening SMJ restores saved editor carets");
        window.SourceEditor.Document.Insert(0, "편집 중 ");
        var unsaved = window.SourceEditor.Text;
        await window.OpenAsync(path);
        Check(window.SourceEditor.Text == unsaved && window.Session.IsDirty, "Opening an already open path retains unsaved edits");
        await CaptureDialogAsync(new WorkFolderDialog(directory) { Owner = window }, directory, "08-work-folder.png");
        await CaptureDialogAsync(new ExportRangeDialog([1, 2, 3, 4], () => Task.FromResult(7)) { Owner = window }, directory, "13-export-range.png");
        var statusDocument = new DocumentSession(); statusDocument.PageStatuses[1] = "COMPLETED";
        statusDocument.PageStatuses[2] = "FAILED"; statusDocument.PageStatuses[3] = "BLOCKED";
        statusDocument.Add(new EditorBlock { Id = 1, Page = 1, ServerId = "status-fixture", Source = "수정된 원문", Metadata = new(OriginalSource: "원문") });
        statusDocument.QualityReports[1] = [new("error", "unsupported_char", "status-fixture", "지원하지 않는 문자가 있습니다"),
            new("review", "low_confidence", "deleted-block", "인식 신뢰도가 낮습니다"), new("review", "layout", null, "쪽 전체 검토")];
        await CaptureDialogAsync(new PageStatusDialog(statusDocument) { Owner = window }, directory, "14-page-status.png");
        var searchWindow = new MainWindow(smoke: true);
        try
        {
            searchWindow.Show();
            var locked = searchWindow.Session.Blocks[0]; var editable = searchWindow.Session.Blocks[1];
            locked.Braille = "⠿⠯"; locked.IsBrailleLocked = true; editable.Braille = "⠿⠯⠿⠯";
            searchWindow.Session.Blocks[2].Braille = "⠁";
            await searchWindow.FindNextAsync("⠿⠯", SearchPanel.Braille);
            await searchWindow.FindNextAsync("⠿⠯", SearchPanel.Braille);
            Check(searchWindow.Session.ActiveBlock == editable && searchWindow.Braille.Editor.SelectedText == "⠿⠯",
                "Find next traverses blocks in the requested panel");
            await searchWindow.ReplaceMatchesAsync("⠿⠯", "⠃", SearchPanel.Braille, true);
            Check(locked.Braille == "⠿⠯" && editable.Braille == "⠃⠃" && editable.HasManualBraille,
                "Replace all protects locked braille and marks corrections in editable blocks");
            ApplicationCommands.Undo.Execute(null, searchWindow.Braille.GridView);
            Check(editable.Braille == "⠿⠯⠿⠯", "Replace all in a block is one undo operation");
            await searchWindow.ReplaceMatchesAsync("⠿⠯", "abc", SearchPanel.Braille, true);
            Check(editable.Braille == "⠿⠯⠿⠯", "Replace rejects non-braille content without changing buffers");
            searchWindow.Braille.Editor.Select(0, 2);
            await searchWindow.ReplaceMatchesAsync("⠿⠯", "⠃", SearchPanel.Braille, false);
            Check(editable.Braille == "⠃⠿⠯", "Single replace changes only the selected occurrence");
            await CaptureDialogAsync(new FindDialog(SearchPanel.Braille, "⠿⠯", true,
                (q, target) => searchWindow.FindNextAsync(q, target),
                (q, r, target, all) => searchWindow.ReplaceMatchesAsync(q, r, target, all)) { Owner = searchWindow },
                directory, "12-find-replace.png");
        }
        finally { searchWindow.PrepareSmokeShutdown(); searchWindow.Close(); }
        var editing = new MainWindow(smoke: true);
        try
        {
            editing.Show();
            var count = editing.Session.Blocks.Count;
            await editing.AddBlockAsync();
            Check(editing.Session.Blocks.Count == count + 1, "Add-block menu adds a paired block");
            ApplicationCommands.Undo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(editing.Session.Blocks.Count == count, "Page history undoes structural insertion");
            ApplicationCommands.Redo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(editing.Session.Blocks.Count == count + 1, "Page history redoes structural insertion");
            ApplicationCommands.Undo.Execute(null, editing.PageEditor.Editor.TextArea);
            var rangeBlocks = editing.Session.Blocks.Take(2).ToArray();
            var beforeRange = rangeBlocks.Select(b => b.Source).ToArray();
            rangeBlocks[1].Memos.Add(new("range-memo", null, "TEXT", 0, beforeRange[1].Split('\n')[0], "붙여넣기 전 메모", DateTimeOffset.UtcNow));
            editing.EditStructure(() => BlockRange.Replace(editing.Session, rangeBlocks.Select(b => new BlockSpan(b, 0, b.Source.Length)).ToArray(), SearchPanel.Text, "선택 구간 대체"));
            Check(editing.Session.Blocks.Count == count - 1 && editing.Session.ActiveBlock!.Memos.Any(m => m.LocalId == "range-memo"), "Cross-block replacement retains memo provenance");
            ApplicationCommands.Undo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(editing.Session.Blocks.Count == count && editing.Session.Blocks.Take(2).Select(b => b.Source).SequenceEqual(beforeRange), "Shared page history restores text and blocks after range replacement");
            ApplicationCommands.Redo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(editing.Session.ActiveBlock!.Source == "선택 구간 대체", "Shared page history redoes range replacement");
            var restored = editing.Session.ActiveBlock!;
            editing.SourceEditor.Document.Insert(0, "입력 ");
            restored.Braille = "⠫"; // An asynchronous engine/model update must preserve typing history.
            ApplicationCommands.Undo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(restored.Source == "선택 구간 대체" && restored.Braille == "⠫", "Engine buffer update preserves the previous source undo step");
            ApplicationCommands.Redo.Execute(null, editing.PageEditor.Editor.TextArea);
            Check(restored.Source == "입력 선택 구간 대체", "Source redo survives engine updates without stale text offsets");
            editing.ToggleDetached();
            editing.DetachedPane!.Editor.Document.Insert(0, "⠿");
            ApplicationCommands.Undo.Execute(null, editing.DetachedPane.GridView);
            Check(restored.Braille == "⠫" && !restored.HasManualBraille, "Detached undo restores braille text and manual-edit state");
            editing.ToggleDetached();
            var revision = editing.Session.ChangeVersion;
            editing.ApplyAppearance(new(HighContrast: true, CursorWidth: 4, TextSize: 18, Bold: true));
            editing.UpdateLayout();
            Check(editing.SourceEditor.FontSize == 18 && editing.SourceEditor.Background == Brushes.Black && editing.Session.ChangeVersion == revision,
                "Appearance changes are visible without editing the document");
            await RenderAsync(editing, Path.Combine(directory, "16-high-contrast.png"));
            await CaptureDialogAsync(new AppearanceDialog(new(), editing.ApplyAppearance) { Owner = editing }, directory, "17-appearance.png");
        }
        finally { editing.PrepareSmokeShutdown(); editing.Close(); }
        var invalidPdfRejected = false;
        try { await PdfPreview.RenderAsync("not a PDF"u8.ToArray(), 1, CancellationToken.None); }
        catch (InvalidDataException) { invalidPdfRejected = true; }
        Check(invalidPdfRejected, "Invalid PDF is reported without terminating the editor");
        var pdf = await PdfPreview.RenderAsync(PdfFixture(), 1, CancellationToken.None);
        Check(pdf.PixelWidth == 1800 && pdf.PixelHeight == 1800, "PDF preview recovers after invalid input and preserves page aspect ratio");
        var pixels = new byte[4]; pdf.CopyPixels(new Int32Rect(900, pdf.PixelHeight / 2, 1, 1), pixels, 4, 0);
        Check(pixels[2] > 180 && pixels[1] < 80, "PDF fallback preserves page contents instead of an empty placeholder");
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(pdf));
        using (var file = File.Create(Path.Combine(directory, "18-pdf-original.png"))) png.Save(file);
        await CaptureDialogAsync(new BraillePreviewDialog([["⠼⠁", "⠫⠉⠊"], ["⠼⠃", "⠱⠑⠘⠃"]], "#a\r\ngnd\r\n\f#b\r\n\f", new(), "조판 검증") { Owner = window }, directory, "19-braille-pages.png");
        await CaptureDialogAsync(new FooterDialog(window.Session) { Owner = window }, directory, "20-footer-ranges.png");
        await CaptureDialogAsync(new RangeSelectionDialog(() => window.Session.Blocks.Where(b => b.Page == window.Session.ActiveBlock!.Page).ToArray(), SearchPanel.Text, (_, _) => Task.CompletedTask) { Owner = window }, directory, "21-range-selection.png");
        window.Session.Progress = new(128, 47, 1, 2, "잔여 약 2분 (처리 속도 기준)");
        await RenderAsync(window, Path.Combine(directory, "22-conversion-progress.png"));
        Check(!window.ConversionBanner.IsVisible && window.Session.Progress.Finished == 48 && window.Session.Progress.Remaining == 80,
            "Conversion progress keeps disjoint page counts without a duplicate fixed panel");
        window.Session.Progress = null;
        // Exercise the production constructor and input debounce against the bundled engine.
        // The earlier window deliberately isolates visual/buffer checks with fixed sample data.
        var real = new MainWindow(testWithoutAccount: true);
        try
        {
            real.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(real.WindowState == WindowState.Maximized,
                "The editor starts maximized for a new user session");
            real.WindowState = WindowState.Normal;
            real.PageEditor.Editor.Focus();
            real.PageEditor.Editor.Text = "가";
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (real.Session.ActiveBlock!.Braille.Trim('⠀') != "⠫" && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            Check(real.Session.ActiveBlock!.Braille.Trim('⠀') == "⠫", "Production text input automatically translates with packaged engine");
            while (real.LineStrip.Cells.Text.Trim('⠀') != "⠫" && DateTime.UtcNow < deadline) await Task.Delay(100);
            Check(real.LineStrip.Cells.Text.Trim('⠀') == "⠫", "Production line translation reaches shared bottom strip");
            real.ToggleDetached();
            Check(real.DetachedPane!.PreviewStrip.Cells.Text.Trim('⠀') == "⠫", "Detached braille retains latest line translation");
            real.ToggleDetached();
            var actualPath = Path.Combine(directory, "real-engine.smj");
            await SmjFile.SaveAsync(actualPath, SmjFile.Snapshot(real.Session));
            var actual = await SmjFile.LoadAsync(actualPath);
            Check(actual.Blocks[0].Translation?.SourceText == "가", "Production translation and original source survive SMJ round trip");
            await RenderAsync(real, Path.Combine(directory, "07-real-engine.png"));
            await real.ReplaceMatchesAsync("가", "나", SearchPanel.Text, true);
            Check(real.Session.ActiveBlock.Source == "나" && real.Session.ActiveBlock.Translation?.SourceText == "나"
                && real.Session.ActiveBlock.Braille.Trim('⠀') != "⠫", "Source replacement retranslates with production engine");
            real.Session.Layout = new(Columns: 16, Lines: 6, PageNumbers: "모든 페이지", Footer: "모든 페이지");
            real.Session.FooterText = "검수";
            var firstBlock = real.Session.ActiveBlock;
            await real.AddBlockAsync(); real.SourceEditor.Text = "다";
            var secondBlock = real.Session.ActiveBlock!;
            deadline = DateTime.UtcNow.AddSeconds(15);
            while ((secondBlock.Translation?.SourceText != "다" || real.Braille.GridView.IsReadOnly) && DateTime.UtcNow < deadline) await Task.Delay(100);
            var grid = real.Braille.GridView;
            Check(!grid.IsReadOnly && grid.Items.Count > 0, "Production composed grid is available after translation");
            var faces = grid.Items.Cast<BrailleGridFace>().ToArray();
            Check(faces.All(f => f.Text is not null) && faces.SelectMany(f => f.Kinds!).Contains("page"), "Grid uses exported BRF and protected generated page rows");
            var beforeFirst = firstBlock!.Braille; var beforeSecond = secondBlock.Braille;
            grid.Point(new(firstBlock, 0, '⠀'), false);
            grid.Point(new(secondBlock, Math.Min(1, secondBlock.Braille.Length), '⠀'), true);
            await grid.InsertAsync("⠿");
            Check(firstBlock.Braille == "⠿" && secondBlock.Braille == beforeSecond[1..], "Grid selection edits across blocks without generated rows entering text");
            real.ReplayHistory();
            Check(firstBlock.Braille == beforeFirst && secondBlock.Braille == beforeSecond, "One undo restores cross-block grid edit");
            await Task.Delay(300);
            grid.Point(new(firstBlock, 0, '⠀'), false);
            for (var i = 0; i < 12; i++) await grid.InsertAsync("⠿");
            Check(firstBlock.Braille.StartsWith(new string('⠿', 12)), "Rapid grid input is retained while asynchronous layout refreshes");
            await Task.Delay(300);
            await RenderAsync(real, Path.Combine(directory, "23-composed-grid.png"));
            Check(real.CaretStatus.Text.Contains("줄"), "Grid status refreshes line and cell coordinates after asynchronous composition");
            real.ToggleDetached(); await Task.Delay(300);
            Check(real.DetachedPane!.GridView.Items.Cast<BrailleGridFace>().SelectMany(f => f.Text ?? []).SequenceEqual(
                real.Braille.GridView.Items.Cast<BrailleGridFace>().SelectMany(f => f.Text ?? [])), "Detached grid composes the same shared document");
            real.ToggleDetached();
            await Task.Delay(300);
            grid.Point(new(firstBlock, 1, '⠀'), false); await grid.InsertPageBreakAsync();
            Check(firstBlock.Braille.Contains("<!쪽바꿈>"), "Grid page break preserves a structural marker");
            await Task.Delay(300);
            grid.Point(new(firstBlock, 0, '⠀'), false); grid.Point(new(firstBlock, firstBlock.Braille.Length, '⠀'), true);
            Check(grid.HasSelection, "Grid selection is available before a render pass");
            grid.Copy(false);
            Check(!Clipboard.GetText().Contains("<!"), "Grid clipboard omits generated layout markers");
            await grid.InsertAsync("⠿");
            Check(firstBlock.Braille.Contains("<!쪽바꿈>"), "Replacing grid body preserves selected layout markers");
            real.ReplayHistory(); real.ReplayHistory(); await Task.Delay(300);
            real.Session.ActiveBlock = secondBlock; real.PageEditor.Editor.Focus(); real.SelectTextRange();
            var oldSource = BlockRange.Text(real.Session.Blocks.ToArray(), SearchPanel.Text);
            real.PageEditor.Editor.CaretOffset = real.PageEditor.Editor.Document.TextLength;
            real.PageEditor.SelectContext();
            Check(real.PageEditor.Editor.SelectedText == secondBlock.Source, "First Ctrl+A selects current source block in continuous editor");
            real.PageEditor.SelectContext();
            Check(real.PageEditor.Editor.SelectedText == oldSource, "Second Ctrl+A selects original page in continuous editor");
            real.PageEditor.Editor.Select(real.PageEditor.Editor.Document.TextLength, 0);
            Check(real.SourceEditor.CaretOffset == secondBlock.Source.Length, "Continuous source caret reaches block-based memo and preview commands");
            real.PageEditor.Editor.Document.Insert(real.PageEditor.Editor.Document.TextLength, "연속 편집");
            Check(secondBlock.Source.EndsWith("연속 편집"), "Inline page editor writes through to original block");
            real.ReplayHistory(); await Task.Delay(100);
            Check(BlockRange.Text(real.Session.Blocks.ToArray(), SearchPanel.Text) == oldSource && real.PageEditor.Editor.Text == oldSource,
                "Inline page editor synchronizes structural undo");
            real.PageEditor.Editor.Select(real.PageEditor.Editor.Document.TextLength, 0);
            real.PageEditor.Editor.Focus();
            // Exercise committed text through AvalonEdit's actual text-input handler.
            // This guards model/caret synchronization; native IME composition is verified separately by direct keyboard input.
            foreach (var syllable in new[] { "한", "글", " ", "입", "력", " ", "테", "스", "트" })
            {
                real.PageEditor.Editor.TextArea.PerformTextInput(syllable);
                await Dispatcher.Yield(DispatcherPriority.DataBind);
            }
            var committedSource = oldSource + "한글 입력 테스트";
            Check(real.PageEditor.Editor.Text == committedSource && BlockRange.Text(real.Session.Blocks.ToArray(), SearchPanel.Text) == committedSource,
                "Korean committed syllables and spaces survive continuous editor refreshes");
            real.RuleSearch.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(real.PageEditor.Editor.Text == committedSource,
                "Moving focus preserves committed Korean text and spaces");
            real.ReplayHistory(); await Dispatcher.Yield(DispatcherPriority.DataBind);
            Check(real.PageEditor.Editor.Text == committedSource[..^1], "Korean committed text undo removes the last input without dropping spaces");
            real.ReplayHistory(true); await Dispatcher.Yield(DispatcherPriority.DataBind);
            Check(real.PageEditor.Editor.Text == committedSource, "Korean committed text redo restores the last syllable");
            await RenderAsync(real, Path.Combine(directory, "24-continuous-source.png"));
            real.Session.IsReadOnly = true; await Task.Delay(100);
            Check(real.PageEditor.Editor.IsReadOnly, "Continuous source honors read-only documents");
            real.Session.IsReadOnly = false;
            real.SourceEditor.Text = "종료 직전 자동 점역 대기";
            await real.ReleaseResourcesAsync();
            var shutdownBraille = real.Session.ActiveBlock!.Braille;
            real.SourceEditor.CaretOffset = 0; // late editor notification after tokens/buffers have been released
            await Task.Delay(600); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(real.Session.ActiveBlock!.Braille == shutdownBraille, "Shutdown cancels pending translation and ignores late caret events");
        }
        finally { await real.ReleaseResourcesAsync(); real.PrepareSmokeShutdown(); real.Close(); }
        var memoModel = new DocumentSession();
        var memoBlock = new EditorBlock { Id = 1, Page = 1, Source = "회귀 검사" }; memoModel.Add(memoBlock);
        memoBlock.Memos.Add(new("open", null, "TEXT", 0, "회귀 검사", "미해결 회신", DateTimeOffset.Now));
        memoBlock.Memos.Add(new("resolved", null, "TEXT", 0, "회귀 검사", "해결된 메모", DateTimeOffset.Now, true));
        var thread = new MemoThreadDialog(memoBlock, "TEXT", 0, "회귀 검사", action => action(), memoModel, author: "검수자") { Owner = window };
        thread.Show();
        try
        {
            Check(thread.MemoItems.Items.Count == 1, "Resolved memos are hidden from the default thread");
            thread.ReplyInput.Text = "원 작성자를 유지하는 답글";
            thread.ReplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(memoBlock.Memos.Count == 2 && memoBlock.Memos[0].Replies is [{ Author: "검수자", Body: "원 작성자를 유지하는 답글" }], "Reply stays in its memo thread with its author");
            thread.MemoItems.SelectedIndex = 0; thread.ResolveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!thread.IsVisible, "Resolving a memo closes its conversation");
            Check(memoBlock.Memos.Count == 2 && memoBlock.Memos.All(m => m.Resolved), "Resolving hides a memo without deleting its body or replies");
            Check(memoBlock.Memos[0].ResolvedAt.HasValue && memoBlock.Memos[0].Replies?.Length == 1, "Resolve records its timestamp and preserves replies");
            thread = new MemoThreadDialog(memoBlock, "TEXT", 0, "회귀 검사", action => action(), memoModel, showResolved: true, author: "검수자") { Owner = window };
            thread.Show();
            Check(thread.MemoItems.Items.Count == 2, "Resolved memos can be retrieved explicitly");
            thread.MemoItems.SelectedIndex = 0; thread.ResolveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            thread.ShowResolved.IsChecked = false;
            Check(thread.MemoItems.Items.Count == 1 && !memoBlock.Memos[0].Resolved, "Reopening a resolved memo restores its default visibility");
            Check(memoBlock.Memos[0].ResolvedAt is null, "Reopening clears the resolution timestamp");
            await RenderAsync(thread, Path.Combine(directory, "25-memo-reopened.png"));
        }
        finally { thread.Close(); }
        // Figma 1:1093 example thread, rendered by the same component for a same-data comparison.
        var figmaMemoModel = new DocumentSession();
        var figmaMemoBlock = new EditorBlock { Id = 1, Page = 12, Source = "원문이 「작다」인데 그래프를 보면 「크다」가 맞습니다." }; figmaMemoModel.Add(figmaMemoBlock);
        figmaMemoBlock.Memos.Add(new("figma-1", null, "TEXT", 0, figmaMemoBlock.Source, "원문이 「작다」인데 그래프를 보면 「크다」가 맞습니다.", DateTimeOffset.Now, Author: "김점역"));
        figmaMemoBlock.Memos.Add(new("figma-2", null, "TEXT", 0, figmaMemoBlock.Source, "확인했습니다. 출판사에 알려 두겠습니다.", DateTimeOffset.Now, Author: "이교정"));
        await CaptureDialogAsync(new MemoThreadDialog(figmaMemoBlock, "TEXT", 0, figmaMemoBlock.Source, action => action(), figmaMemoModel, author: "김점역") { Owner = window }, directory, "85-memo-thread-figma.png");
        var memoList = new MemoListDialog(memoModel) { Owner = window };
        Check(memoList.MemoItems.Items.Count == 1, "Memo overview hides resolved items by default");
        memoList.ShowResolved.IsChecked = true;
        Check(memoList.MemoItems.Items.Count == 2, "Memo overview exposes resolved items on request");
        memoList.Close();
        IEnumerable<MenuItem> MenuItems(MenuItem root) => root.Items.OfType<MenuItem>().SelectMany(item => new[] { item }.Concat(MenuItems(item)));
        var tags = MenuItems(window.AllTagsMenu).Where(m => m.Tag is string tag && tag.StartsWith("tag:")).Select(m => ((string)m.Tag)[4..]).ToArray();
        Check(window.RuleSearch.IsVisible && tags.Length == 22 && MenuItems(window.AllTagsMenu).Any(m => Equals(m.Tag, "headerInsert")) && !tags.Any(t => t is "상자" or "상자끝" or "상자2" or "상자3" or "네모글" or "기울임" or "밑줄빈칸" or "네모빈칸"), "Insertion menu retains the header command and excludes removed or automatic-only tags");
        Check(window.AllTagsMenu.Items.OfType<MenuItem>().Count(m => m.Items.Count > 0) == 3 && !window.AllTagsMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "빈칸")), "Insertion tags have three named submenus and no blank submenu");
        var brailleLines = window.AllTagsMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "점자 줄"));
        var lineTags = brailleLines.Items.OfType<MenuItem>().Select(m => m.Tag as string).ToArray();
        Check(lineTags.SequenceEqual(new[] { "tag:상단줄", "tag:중단줄", "tag:하단줄", "tag:상단굵은줄", "tag:하단굵은줄", "tag:표시작", "tag:표중앙", "tag:표끝", "tag:표구분선", "tag:표지시작", "tag:표지세로", "tag:표지끝" }) &&
              !window.AllTagsMenu.Items.OfType<MenuItem>().Any(m => Equals(m.Header, "표 테두리")),
            "The braille-line submenu follows Figma order while retaining the additional supported table-indicator tags");
        var margin = new Controls.MemoMargin(() => memoBlock, "TEXT", _ => { });
        Check(margin.Preview(0).Contains("미해결 회신") && margin.Preview(0).Contains("검수자") && !margin.Preview(0).Contains("해결된 메모"), "QA4 memo hover includes author, body and replies while hiding resolved items");
        Check(thread.Title == "메모", "QA4 memo title omits block and line coordinates");
        var keyboardMemo = new MemoInputDialog("검수자") { Owner = window };
        var saveGesture = keyboardMemo.InputBindings.OfType<KeyBinding>().SingleOrDefault(b => b.Key == Key.Enter && b.Modifiers == ModifierKeys.Control);
        Check(saveGesture is not null, "Memo entry offers Ctrl+Enter to save");
        var blankMemoStayedOpen = false;
        _ = keyboardMemo.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            keyboardMemo.Input.Text = "   ";
            ((RoutedCommand)saveGesture!.Command).Execute(saveGesture.CommandParameter, keyboardMemo);
            blankMemoStayedOpen = keyboardMemo.IsVisible;
            keyboardMemo.Input.Text = "  단축키 메모  ";
            ((RoutedCommand)saveGesture.Command).Execute(saveGesture.CommandParameter, keyboardMemo);
        }));
        Check(keyboardMemo.ShowDialog() == true && keyboardMemo.Value == "단축키 메모", "Memo Ctrl+Enter saves the entered text");
        Check(blankMemoStayedOpen, "Memo Ctrl+Enter leaves a blank entry open for correction");
        var memoEditor = new MainWindow(smoke: true);
        try
        {
            memoEditor.Show();
            var editedBlock = memoEditor.Session.ActiveBlock!;
            var originalMemo = new LineMemo("undo-memo", null, "TEXT", 0, "원문", "작성한 메모", DateTimeOffset.Now, Author: "작성자");
            memoEditor.EditStructure(() => editedBlock.Memos.Add(originalMemo));
            var historyThread = new MemoThreadDialog(editedBlock, "TEXT", 0, "원문", memoEditor.EditStructure, memoEditor.Session, author: "답글 작성자") { Owner = memoEditor };
            historyThread.Show();
            historyThread.ReplyInput.Text = "답글 내용";
            historyThread.ReplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            historyThread.Close();
            memoEditor.ReplayHistory();
            Check(editedBlock.Memos.Single(m => m.LocalId == "undo-memo").Replies?.Length is null or 0, "Undo removes a newly written memo reply");
            memoEditor.ReplayHistory(true);
            Check(editedBlock.Memos.Single(m => m.LocalId == "undo-memo").Replies is [{ Author: "답글 작성자", Body: "답글 내용" }], "Redo restores the reply and its author");
            historyThread = new MemoThreadDialog(editedBlock, "TEXT", 0, "원문", memoEditor.EditStructure, memoEditor.Session, author: "답글 작성자") { Owner = memoEditor };
            historyThread.Show();
            historyThread.ResolveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            memoEditor.ReplayHistory();
            var undoneMemo = editedBlock.Memos.Single(m => m.LocalId == "undo-memo");
            Check(!undoneMemo.Resolved && undoneMemo.Author == "작성자" && undoneMemo.Replies is [{ Author: "답글 작성자", Body: "답글 내용" }], "Undo of memo resolution preserves the original author and replies");
            memoEditor.ReplayHistory(true);
            var memoPath = Path.Combine(directory, "memo-history.smj");
            await SmjFile.SaveAsync(memoPath, SmjFile.Snapshot(memoEditor.Session));
            var reopenedMemo = (await SmjFile.LoadAsync(memoPath)).Blocks.SelectMany(b => b.Memos).Single(m => m.LocalId == "undo-memo");
            Check(reopenedMemo.Resolved && reopenedMemo.Author == "작성자" && reopenedMemo.Replies is [{ Author: "답글 작성자", Body: "답글 내용" }], "Redo of memo resolution survives SMJ save and reopen with replies");
        }
        finally { await memoEditor.ReleaseResourcesAsync(); memoEditor.PrepareSmokeShutdown(); memoEditor.Close(); }
        await CaptureDialogAsync(new MemoInputDialog { Owner = window }, directory, "26-memo-input.png");
        await using (var engineConnection = EngineRuntime.Create())
        {
            var local = new Semojum.Core.Integration.LocalBrailleEngine(engineConnection);
            var math = new MathDialog((value, ct) => local.TranslateElementAsync(value, new(Type: "formula"), new(), ct)) { Owner = window };
            math.Show();
            try
            {
                await math.RefreshAsync();
                Check(math.SymbolButtons.Children.Count == 23 && math.Translation is not null && math.BrailleInput.Text.Any(c => c >= 0x2800 && c <= 0x28ff), "QA4 math editor offers 23 symbols and previews actual engine cells");
                math.BrailleInput.AppendText("⠿"); Check(math.Edited, "QA4 math result can be corrected before applying");
                await RenderAsync(math, Path.Combine(directory, "27-math-cells.png"));
            }
            finally { math.Close(); }
            var about = new AboutDialog("1.0.0", async () => { await engineConnection.InvokeAsync("ping"); return engineConnection.EngineVersion!; }) { Owner = window };
            about.Show(); await RenderAsync(about, Path.Combine(directory, "28-about.png"));
            Check(about.EngineVersion.Text == engineConnection.EngineVersion && about.EngineVersion.Text == "3.4.41", "QA4 about dialog shows the running bundled engine version 3.4.41, not a hardcoded build label"); about.Close();
        }
        var layoutDialog = new LayoutDialog(new()) { Owner = window };
        layoutDialog.Show(); await RenderAsync(layoutDialog, Path.Combine(directory, "29-page-settings.png"));
        Check(layoutDialog.Top >= SystemParameters.WorkArea.Top && layoutDialog.Top + layoutDialog.ActualHeight <= SystemParameters.WorkArea.Bottom + 1,
            "QA4 page settings fit the current work area with actions reachable"); layoutDialog.Close();
        Check(window.StatusOptions.Text.Contains("2024") && !window.CaretStatus.Text.Contains("저장됨"), "QA4 status shows option summary without obsolete save-state columns");
        window.Session.IsDirty = false;
        File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
    }
    private static byte[] PdfFixture()
    {
        var b = new System.Text.StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        var objects = new[] { "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] /Contents 4 0 R >>", "<< /Length 24 >>\nstream\n1 0 0 rg 0 0 200 200 re f\nendstream" };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(b.Length); b.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
        var xref = b.Length; b.Append("xref\n0 5\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) b.Append(offset.ToString("0000000000") + " 00000 n \n");
        b.Append($"trailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(b.ToString());
    }

    private static async Task CaptureDialogAsync(Window dialog, string directory, string name)
    {
        dialog.Show();
        try { await RenderAsync(dialog, Path.Combine(directory, name)); }
        finally { dialog.Close(); }
    }
    private static async Task RenderAsync(Window window, string path)
        => await RenderElementAsync(window, path);
    internal static async Task CaptureComponentAsync(FrameworkElement element, Window owner, double width, double height, string path)
    {
        var parent = VisualTreeHelper.GetParent(element);
        var index = parent is Panel panel ? panel.Children.IndexOf(element) : -1;
        var previousContext = element.ReadLocalValue(FrameworkElement.DataContextProperty);
        var context = element.DataContext;
        if (parent is Panel container) container.Children.Remove(element);
        else if (parent is Decorator decorator) decorator.Child = null;
        else throw new InvalidOperationException("Unsupported capture host: " + parent?.GetType().Name);
        element.DataContext = context;
        var host = new Window { Owner = owner, Width = width, Height = height, WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Brushes.White, Content = element,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        try { host.Show(); await RenderAsync(host, path); }
        finally
        {
            host.Content = null; host.Close();
            if (previousContext == DependencyProperty.UnsetValue) element.ClearValue(FrameworkElement.DataContextProperty);
            else element.SetValue(FrameworkElement.DataContextProperty, previousContext);
            if (parent is Panel restore) restore.Children.Insert(index, element);
            else ((Decorator)parent).Child = element;
        }
    }
    internal static async Task RenderElementAsync(FrameworkElement window, string path)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(200);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        if (window is Window) bitmap.Render(window);
        else
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
                dc.DrawRectangle(new VisualBrush(window) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect((Point)VisualTreeHelper.GetOffset(window), window.RenderSize) }, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
            bitmap.Render(drawing);
        }
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
