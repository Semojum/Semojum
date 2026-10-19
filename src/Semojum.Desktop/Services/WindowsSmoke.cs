using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
        await RenderAsync(window, Path.Combine(directory, "01-main.png"));
        Check(window.SourceEditor.ActualWidth > 250 && window.Braille.ActualWidth > 250, "Three-panel layout has usable editor widths");
        Check(window.LineStrip.ActualHeight == 64 && Math.Abs(window.LineStrip.ActualWidth - window.PanelGrid.ActualWidth) < 2,
            "Figma line translation strip spans all panels at 64px");
        Check(window.SourceHeader.ActualHeight == 32 && window.ZoomPicker.ActualHeight == 32,
            "Figma panel headers and toolbar controls retain compact geometry");
        Check(window.Braille.GridView.Items.Count > 0 && window.Braille.GridView.ActualHeight > 100,
            "Service plan braille grid renders editable block coordinates");
        foreach (var icon in new[] { window.CutIcon, window.DraftIcon, window.LinenoIcon })
        {
            // Stretch=None keeps the painted SVG bounds (the cut glyph is 12×14 inside its 16×16 viewBox).
            Check(icon.Width == 16 && icon.Height == 16 && icon.ActualWidth > 0 && icon.ActualWidth <= 16
                && icon.ActualHeight > 0 && icon.ActualHeight <= 16 && icon.Drawings is { Bounds.IsEmpty: false },
                $"Local Figma SVG loaded without scaling in its 16px slot: {icon.Name}");
        }
        var initialVersion = window.Session.ChangeVersion;
        window.HidePanel("hideOriginal"); window.UpdateLayout();
        Check(window.OriginalPanel.Visibility == Visibility.Collapsed && window.SourceEditor.ActualWidth > 350,
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
        window.SourceEditor.Select(0, 2); window.InsertTag("강조");
        Check(block.Source.StartsWith("<!강조>"), "Source tag insertion reaches model");
        window.SourceEditor.Undo(); Check(block.Source == source, "Tag insertion has one-step undo");

        window.ToggleDetached();
        Check(ReferenceEquals(window.Braille.Editor.Document, window.DetachedPane!.Editor.Document), "Detached window shares the same document");
        window.DetachedPane.Editor.Document.Insert(0, "⠿");
        Check(block.Braille.StartsWith("⠿") && block.HasManualBraille, "Detached edits update model and manual-correction state");
        await RenderAsync(window.DetachedWindow!, Path.Combine(directory, "02-detached.png"));
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
        window.SourceEditor.Undo();
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
            && window.SourceEditor.ActualWidth > 250 && window.Braille.ActualWidth > 250
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
        await CaptureDialogAsync(new MathDialog { Owner = window }, directory, "05-math.png");
        await CaptureDialogAsync(new ManualEditDialog { Owner = window }, directory, "06-manual-correction.png");
        checks.Add("PASS Layout, six-dot, math, and manual-correction dialogs render");
        var path = Path.Combine(directory, "roundtrip.smj");
        await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
        var loaded = await SmjFile.LoadAsync(path);
        Check(loaded.Blocks[0].Braille == block.Braille && loaded.Blocks[0].HasManualBraille, "Saved file preserves detached manual edits");
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
        statusDocument.Add(new EditorBlock { Id = 1, Page = 1, ServerId = "status-fixture" });
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
            searchWindow.Braille.Editor.Undo();
            Check(editable.Braille == "⠿⠯⠿⠯", "Replace all in a block is one undo operation");
            await searchWindow.ReplaceMatchesAsync("⠿⠯", "abc", SearchPanel.Braille, true);
            Check(editable.Braille == "⠿⠯⠿⠯", "Replace rejects non-braille content without changing buffers");
            searchWindow.Braille.Editor.Select(0, 2);
            await searchWindow.ReplaceMatchesAsync("⠿⠯", "⠃", SearchPanel.Braille, false);
            Check(editable.Braille == "⠃⠿⠯", "Single replace changes only the selected occurrence");
            await CaptureDialogAsync(new FindDialog(SearchPanel.Braille, "⠿⠯", true,
                q => searchWindow.FindNextAsync(q, SearchPanel.Braille),
                (q, r, all) => searchWindow.ReplaceMatchesAsync(q, r, SearchPanel.Braille, all)) { Owner = searchWindow },
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
            editing.SourceEditor.Undo();
            Check(editing.Session.Blocks.Count == count, "Page history undoes structural insertion");
            editing.SourceEditor.Redo();
            Check(editing.Session.Blocks.Count == count + 1, "Page history redoes structural insertion");
            var revision = editing.Session.ChangeVersion;
            editing.ApplyAppearance(new(HighContrast: true, CursorWidth: 4, TextSize: 18, Bold: true));
            editing.UpdateLayout();
            Check(editing.SourceEditor.FontSize == 18 && editing.SourceEditor.Background == Brushes.Black && editing.Session.ChangeVersion == revision,
                "Appearance changes are visible without editing the document");
            await RenderAsync(editing, Path.Combine(directory, "16-high-contrast.png"));
            await CaptureDialogAsync(new AppearanceDialog(new(), editing.ApplyAppearance) { Owner = editing }, directory, "17-appearance.png");
        }
        finally { editing.PrepareSmokeShutdown(); editing.Close(); }
        var pdf = await PdfPreview.RenderAsync(PdfFixture(), 1, CancellationToken.None);
        Check(pdf.PixelWidth == 1800 && pdf.PixelHeight > 0, "Windows PDF fallback renders the requested original page");
        var pixels = new byte[4]; pdf.CopyPixels(new Int32Rect(900, pdf.PixelHeight / 2, 1, 1), pixels, 4, 0);
        Check(pixels[2] > 180 && pixels[1] < 80, "PDF fallback preserves page contents instead of an empty placeholder");
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(pdf));
        using (var file = File.Create(Path.Combine(directory, "18-pdf-original.png"))) png.Save(file);
        await CaptureDialogAsync(new BraillePreviewDialog([["⠼⠁", "⠫⠉⠊"], ["⠼⠃", "⠱⠑⠘⠃"]], "#a\r\ngnd\r\n\f#b\r\n\f", new(), "조판 검증") { Owner = window }, directory, "19-braille-pages.png");
        // Exercise the production constructor and input debounce against the bundled engine.
        // The earlier window deliberately isolates visual/buffer checks with fixed sample data.
        var real = new MainWindow(testWithoutAccount: true);
        try
        {
            real.Show();
            real.SourceEditor.Focus();
            real.SourceEditor.Text = "가";
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
        }
        finally { await real.ReleaseResourcesAsync(); real.PrepareSmokeShutdown(); real.Close(); }
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
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(200);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
