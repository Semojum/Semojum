using System.IO;
using System.Windows;
using System.Windows.Media;
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
        await window.OpenAsync(path);
        Check(window.SourceEditor.CaretOffset == 8 && window.Braille.Editor.CaretOffset == 3, "Reopening SMJ restores saved editor carets");
        window.SourceEditor.Document.Insert(0, "편집 중 ");
        var unsaved = window.SourceEditor.Text;
        await window.OpenAsync(path);
        Check(window.SourceEditor.Text == unsaved && window.Session.IsDirty, "Opening an already open path retains unsaved edits");
        await CaptureDialogAsync(new WorkFolderDialog(directory) { Owner = window }, directory, "08-work-folder.png");
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
        // Exercise the production constructor and input debounce against the bundled engine.
        // The earlier window deliberately isolates visual/buffer checks with fixed sample data.
        var real = new MainWindow();
        try
        {
            real.Show();
            real.SourceEditor.Focus();
            real.SourceEditor.Text = "가";
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (real.Session.ActiveBlock!.Braille.Trim('⠀') != "⠫" && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            Check(real.Session.ActiveBlock!.Braille.Trim('⠀') == "⠫", "Production text input automatically translates with packaged engine");
            var actualPath = Path.Combine(directory, "real-engine.smj");
            await SmjFile.SaveAsync(actualPath, SmjFile.Snapshot(real.Session));
            var actual = await SmjFile.LoadAsync(actualPath);
            Check(actual.Blocks[0].Translation?.SourceText == "가", "Production translation and original source survive SMJ round trip");
            await RenderAsync(real, Path.Combine(directory, "07-real-engine.png"));
            await real.ReplaceMatchesAsync("가", "나", SearchPanel.Text, true);
            Check(real.Session.ActiveBlock.Source == "나" && real.Session.ActiveBlock.Translation?.SourceText == "나"
                && real.Session.ActiveBlock.Braille.Trim('⠀') != "⠫", "Source replacement retranslates with production engine");
        }
        finally { real.PrepareSmokeShutdown(); real.Close(); }
        window.Session.IsDirty = false;
        File.WriteAllLines(Path.Combine(directory, "checks.txt"), checks);
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
