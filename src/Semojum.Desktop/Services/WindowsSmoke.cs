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
        block.IsBrailleLocked = true;
        Check(window.Braille.Editor.IsReadOnly && !window.SourceEditor.IsReadOnly, "Translation lock affects only braille editor");
        block.IsBrailleLocked = false;

        window.Session.ActiveBlock = window.Session.Blocks[1];
        Check(window.SourceEditor.Text == window.Session.Blocks[1].Source, "Active block updates source editor");
        Check(window.Braille.Editor.Text == window.Session.Blocks[1].Braille, "Active block updates braille editor");
        window.Session.ActiveBlock = block;
        await CaptureDialogAsync(new LayoutDialog(window.Session.Layout) { Owner = window }, directory, "03-page-settings.png");
        await CaptureDialogAsync(new SixDotDialog("F D S J K L", true, true) { Owner = window }, directory, "04-six-dot-settings.png");
        await CaptureDialogAsync(new MathDialog { Owner = window }, directory, "05-math.png");
        await CaptureDialogAsync(new ManualEditDialog { Owner = window }, directory, "06-manual-correction.png");
        checks.Add("PASS Layout, six-dot, math, and manual-correction dialogs render");
        var path = Path.Combine(directory, "roundtrip.mock.smi");
        await WorkFile.SaveAsync(path, WorkFile.Snapshot(window.Session));
        var loaded = await WorkFile.LoadAsync(path);
        Check(loaded.Blocks[0].Braille == block.Braille && loaded.Blocks[0].HasManualBraille, "Saved file preserves detached manual edits");
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
