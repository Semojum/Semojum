using System.Reflection;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsSourceMapChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        try
        {
            var mapProperty = typeof(BrailleTranslation).GetProperty("SourceLineMap");
            var stampProperty = typeof(BrailleTranslation).GetProperty("SourceMapStamp");
            check(mapProperty is not null && stampProperty is not null, "Translation exposes verified ephemeral source-line mapping");
            var model = window.Session; model.Blocks.Clear(); model.ActiveBlock = null;
            model.Layout = new(Columns: 8, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함");
            var block = new EditorBlock { Id = 90, Page = 1, Source = "첫째 줄\n둘째 줄", Braille = new string('⠁', 16) + "\n" + new string('⠃', 16) };
            var translation = BrailleTranslation.Plain(block.Braille) with { SourceText = block.Source };
            var itemType = mapProperty!.PropertyType.GetElementType()!;
            var mapping = Array.CreateInstance(itemType, 2);
            mapping.SetValue(Activator.CreateInstance(itemType, 0, 4, 0, 16), 0);
            mapping.SetValue(Activator.CreateInstance(itemType, 5, 9, 17, 33), 1);
            mapProperty.SetValue(translation, mapping);
            stampProperty!.SetValue(translation, Activator.CreateInstance(stampProperty.PropertyType, block.SourceRevision, block.BrailleRevision, block.MetadataRevision));
            block.Translation = translation; model.Add(block); model.ActiveBlock = block;
            window.PageEditor.Refresh(); window.PageEditor.FollowActive(0); window.Activate(); window.PageEditor.Editor.Focus();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(100);
            var grid = window.Braille.GridView;
            var allCells = grid.Items.Cast<BrailleGridFace>().SelectMany(face => face.Rows).SelectMany(row => row).Where(c => c?.Block == block).ToArray();
            check(allCells.Any(c => c!.Offset < 16 && grid.IsLinkedCell(c)) && allCells.Where(c => c!.Offset >= 17).All(c => !grid.IsLinkedCell(c!)),
                "The first source line highlights only its mapped braille cells across wrapped rows");
            check(!grid.HasSelection, "Linked highlights do not create an editable selection");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var originalCompose = window.Braille.ComposeGridAsync;
            try
            {
                window.Braille.ComposeGridAsync = async owner => { await gate.Task; return await originalCompose!(owner); };
                model.Layout = model.Layout with { Columns = 9 };
                await Task.Delay(150);
                window.PageEditor.FollowActive(5); window.PageEditor.FollowActive(0);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(!grid.CoordinatesCurrent && allCells.All(c => !grid.IsLinkedCell(c!)),
                    "A source caret move cannot highlight old cells while composition coordinates are stale");
                gate.TrySetResult();
                await WaitForGrid();
                check(grid.CoordinatesCurrent && allCells.Any(c => c!.Offset < 16 && grid.IsLinkedCell(c))
                    && allCells.Where(c => c!.Offset >= 17).All(c => !grid.IsLinkedCell(c!)),
                    "Completed composition restores the correct line highlight on its new coordinates");
            }
            finally { gate.TrySetResult(); window.Braille.ComposeGridAsync = originalCompose; }
            window.PageEditor.FollowActive(5);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(50);
            check(allCells.Where(c => c!.Offset < 16).All(c => !grid.IsLinkedCell(c!)) && allCells.Any(c => c!.Offset >= 17 && grid.IsLinkedCell(c)),
                "Moving to the second line highlights only its mapped cells");
            window.PageEditor.Editor.Select(6, 1);
            check(allCells.All(c => !grid.IsLinkedCell(c!)), "Partial source selections do not claim whole-line correspondence");
            window.PageEditor.FollowActive(0); block.Source += "수정"; block.Source = "첫째 줄\n둘째 줄";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(50);
            check(allCells.All(c => !grid.IsLinkedCell(c!)), "Source edits invalidate mapping even if the original text is restored");
            check(SmjFile.Snapshot(model).Manifest.Blocks.Single().Translation is { } saved && mapProperty.GetValue(saved) is null,
                "Ephemeral line maps do not alter SMJ version or persist unverifiable revisions");
            void ApplyFreshMap()
            {
                var fresh = BrailleTranslation.Plain(block.Braille) with { SourceText = block.Source };
                mapProperty.SetValue(fresh, mapping);
                stampProperty.SetValue(fresh, Activator.CreateInstance(stampProperty.PropertyType, block.SourceRevision, block.BrailleRevision, block.MetadataRevision));
                block.Translation = fresh;
            }
            async Task WaitForGrid()
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    allCells = grid.Items.Cast<BrailleGridFace>().SelectMany(face => face.Rows).SelectMany(row => row).Where(c => c?.Block == block).ToArray();
                    if (grid.CoordinatesCurrent && allCells.Any(c => grid.IsLinkedCell(c!))) return;
                    await Task.Delay(25);
                }
                throw new TimeoutException("Current grid and linked highlighting did not become available.");
            }
            ApplyFreshMap(); window.PageEditor.FollowActive(5); window.PageEditor.FollowActive(0); window.PageEditor.Editor.Focus();
            await WaitForGrid();
            check(allCells.Any(c => grid.IsLinkedCell(c!)), "Fresh mapping restores highlighting before a manual correction");
            var corrected = block.Braille + "⠿"; block.EditBraille(corrected);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(50);
            check(block.Braille == corrected && block.HasManualBraille && allCells.All(c => !grid.IsLinkedCell(c!)),
                "A manual braille correction remains intact and immediately clears mapped highlighting");
            block.Braille = new string('⠁', 16) + "\n" + new string('⠃', 16); block.HasManualBraille = false;
            ApplyFreshMap(); window.PageEditor.FollowActive(5); window.PageEditor.FollowActive(0); window.PageEditor.Editor.Focus();
            await WaitForGrid();
            check(allCells.Any(c => grid.IsLinkedCell(c!)), "Fresh mapping restores highlighting before a draft change");
            var metadata = block.Metadata; block.Metadata = metadata with { SelectedIndex = 1 }; block.Metadata = metadata;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(50);
            check(allCells.All(c => !grid.IsLinkedCell(c!)), "Draft metadata changes invalidate the previous line mapping");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
