using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Rendering;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsRegionSelectionChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var window = new MainWindow(smoke: true); window.Show();
        try
        {
            window.Braille.ComposeGridAsync = null;
            var model = window.Session; model.Blocks.Clear(); model.ActiveBlock = null;
            model.Layout = new(Columns: 8, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함");
            var first = new EditorBlock { Id = 301, Page = 1, Source = "첫째 줄\n둘째 줄\n셋째 줄", Braille = new string('⠁', 12) + "\n" + new string('⠃', 12), Metadata = new(BoundingBox: [10, 10, 100, 100]) };
            var shared = new EditorBlock { Id = 302, Page = 1, Source = "같은 이미지의 다른 설명\n마지막 줄", Braille = new string('⠉', 12), Metadata = new(BoundingBox: [10, 10, 100, 100]) };
            var neighbour = new EditorBlock { Id = 303, Page = 1, Source = "다른 영역", Braille = "⠙⠙", Metadata = new(BoundingBox: [90, 90, 150, 150]) };
            model.Add(first); model.Add(shared); model.Add(neighbour);
            model.ActiveBlock = first; window.PageEditor.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(180);
            await window.SelectOriginalRegionAsync(first);
            window.Braille.RefreshGrid();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = window.Braille.GridView;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!grid.CoordinatesCurrent && DateTime.UtcNow < deadline) await Task.Delay(20);
            var cells = grid.Items.Cast<BrailleGridFace>().SelectMany(f => f.Rows).SelectMany(r => r).Where(c => c is not null && !c.IsInsertion && c.Offset < c.Block.Braille.Length).Cast<GridCell>().ToArray();
            check(cells.Length > 0 && cells.Where(c => c.Block != neighbour).All(grid.IsLinkedCell), $"Image region highlights every mapped braille cell across lines and faces without a line map (cells={cells.Length}, linked={grid.LinkedBlocks.Count}, current={grid.CoordinatesCurrent}, sourceRanges={window.PageEditor.LinkedRegionRanges().Count})");
            check(cells.Where(c => c.Block == neighbour).All(c => !grid.IsLinkedCell(c)), "Overlapping neighbour region is not included");
            check(window.PageEditor.LinkedRegionRanges().SequenceEqual(new[] { (0, first.Source.Length), (first.Source.Length + 1, shared.Source.Length) }), "All source lines of both equal-box blocks are highlighted");
            check(!grid.HasSelection && window.PageEditor.Editor.SelectionLength == 0, "Image region highlight does not create an editable selection");
            window.Braille.RefreshGrid();
            check(grid.LinkedBlocks.Contains(first) && grid.LinkedBlocks.Contains(shared), "Recomposition restores the entire selected region");
            await WindowsSmoke.RenderElementAsync(window, System.IO.Path.Combine(directory, "region-multiline.png"));
            first.Source += "변경"; window.PageEditor.Refresh(); window.Braille.RefreshGrid();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.PageEditor.LinkedRegionRanges().Count == 0 && grid.LinkedBlocks.Count == 0, "Editing source invalidates the previous region presentation");
            var newline = new GridCell(first, 12, '⠀');
            check(BrailleGridView.IsEnterCell(newline) && !BrailleGridView.IsEnterCell(newline with { IsInsertion = true, Prefix = "⠀" }), "Braille newline marker excludes insertion padding");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); await window.ReleaseResourcesAsync(); }

        var editor = new TextEditor { WordWrap = true, FontSize = 16, Text = "긴 줄입니다 긴 줄입니다 긴 줄입니다\n두 번째\r\n세 번째\r마지막" };
        var host = new Window { Width = 150, Height = 400, Content = editor }; host.Show();
        try
        {
            var original = editor.Text;
            EnterMarkGenerator.Configure(editor, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            int Marks() => editor.TextArea.TextView.VisualLines.Sum(l => l.Elements.OfType<FormattedTextElement>().Count());
            check(Marks() == 3, "LF, CRLF and CR each render one Enter mark; soft wraps and unterminated final line render none");
            await WindowsSmoke.RenderElementAsync(host, System.IO.Path.Combine(directory, "enter-mark.png"));
            EnterMarkGenerator.Configure(editor, false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout(); editor.TextArea.TextView.EnsureVisualLines();
            check(Marks() == 0 && editor.Text == original, "Hiding formatting marks removes glyphs without changing document text");
        }
        finally { host.Close(); }
    }
}
