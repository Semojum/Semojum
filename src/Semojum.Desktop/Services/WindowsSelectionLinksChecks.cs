using System.Windows.Threading;
using System.IO;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsSelectionLinksChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "semojum-link-" + Guid.NewGuid() + ".smj");
        try
        {
            var model = window.Session;
            model.Blocks.Clear(); model.ActiveBlock = null;
            model.Layout = new(Columns: 8, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함");
            var first = new EditorBlock { Id = 31, Page = 1, Source = "한 줄", Braille = new string('⠁', 32) };
            first.Translation = BrailleTranslation.Plain(first.Braille) with { SourceText = first.Source };
            var second = new EditorBlock { Id = 32, Page = 1, Source = "다른 줄", Braille = new string('⠃', 16) };
            second.Translation = BrailleTranslation.Plain(second.Braille) with { SourceText = second.Source };
            model.Add(first); model.Add(second); window.PageEditor.Refresh(); window.PageEditor.FollowActive(0);
            window.Activate(); window.PageEditor.Editor.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            var grid = window.Braille.GridView;
            var linkedRows = grid.Items.Cast<BrailleGridFace>().SelectMany(f => f.Rows)
                .Count(row => row.Any(c => c is not null && grid.IsLinkedCell(c)));
            check(linkedRows == 4 && grid.LinkedBlocks.SequenceEqual([first]),
                "A single source line links all four wrapped braille rows and no other block");
            check(!grid.HasSelection, "Linked highlighting does not create an editable selection");
            window.PageEditor.Editor.Select(0, 1);
            check(grid.LinkedBlocks.Count == 0, "A partial text selection does not claim a complete braille correspondence");
            window.PageEditor.FollowActive(0);
            check(grid.LinkedBlocks.SequenceEqual([first]), "Returning to a source caret restores whole-line linkage");
            window.ToggleDetached();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            window.Activate(); window.PageEditor.Editor.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.DetachedPane!.GridView.LinkedBlocks.SequenceEqual([first]),
                "Detached braille panel receives the same source linkage");
            window.ToggleDetached(); window.PageEditor.Editor.Focus();
            first.EditBraille(first.Braille + "⠉");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(grid.LinkedBlocks.Count == 0, "Manual braille edits clear unverified line linkage");
            first.HasManualBraille = false; first.Translation = BrailleTranslation.Plain(first.Braille) with { SourceText = "과거 원문" };
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(grid.LinkedBlocks.Count == 0, "A stale translation is not linked to changed source");
            first.Translation = BrailleTranslation.Plain(first.Braille) with { SourceText = first.Source };
            model.ActiveBlock = second; window.PageEditor.FollowActive(0);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(grid.LinkedBlocks.SequenceEqual([second]), "Changing source blocks moves the linked highlight");
            model.Blocks.Move(1, 0);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(grid.LinkedBlocks.SequenceEqual([second])
                && window.PageEditor.ResolveLine(window.PageEditor.Editor.TextArea.Caret.Line - 1).Block == second,
                "Reordering preserves the selected block and source caret identity");
            model.Blocks.Remove(second); model.ActiveBlock = first;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(!grid.LinkedBlocks.Contains(second), "Deleting a block cannot leave its cells linked");
            first.Source = "첫째 줄\n둘째 줄";
            first.Translation = BrailleTranslation.Plain(first.Braille) with { SourceText = first.Source };
            window.PageEditor.Refresh();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            check(grid.LinkedBlocks.Count == 0, "Multiline source is not guessed without a line mapping");
            first.Source = "한 줄";
            first.Translation = BrailleTranslation.Plain(first.Braille) with { SourceText = first.Source };
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); model.IsDirty = false;
            await window.OpenAsync(path);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(150);
            window.PageEditor.Editor.Focus();
            check(window.Braille.GridView.LinkedBlocks.SequenceEqual([window.Session.ActiveBlock!]),
                "Saved and reopened verified translation retains source-to-braille linkage");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); await window.ReleaseResourcesAsync(); if (File.Exists(path)) File.Delete(path); }
    }
}
