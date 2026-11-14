using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsIncrementalTranslationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await using var preparation = EngineRuntime.Create();
        var model = new DocumentSession();
        var engine = new LocalBrailleEngine(preparation, () => model.Options);
        for (var i = 0; i < 24; i++)
        {
            var block = new EditorBlock { Id = i + 1, Page = i == 23 ? 2 : 1,
                Source = string.Concat(Enumerable.Repeat("우리나라의 지역과 생활을 알아봅시다. ", 8)) + i };
            var result = await engine.TranslateElementAsync(block.Source, block.Metadata, model.Options);
            block.Braille = result.Cells; block.Translation = result;
            model.Add(block);
        }
        model.ActiveBlock = model.Blocks[0];
        // This is a real non-smoke editor: normal debounce, sidecar, coordinator,
        // grid composition and source change subscriptions remain enabled.
        var window = new MainWindow(testWithoutAccount: true, startEmpty: true) { ShowInTaskbar = false };
        typeof(MainWindow).GetMethod("SetSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [model, null, true, false]);
        window.Show();
        try
        {
            window.LinePreviewToggle.IsChecked = false;
            async Task Until(Func<bool> ready, string label)
            {
                var timeout = Stopwatch.StartNew();
                while (!ready() && timeout.Elapsed < TimeSpan.FromSeconds(45)) await Task.Delay(25);
                check(ready(), label);
            }
            await Until(() => window.Braille.GridView.CoordinatesCurrent, "Incremental: real-engine initial page composition settles");
            window.PageEditor.Editor.Focus();
            window.PageEditor.Editor.CaretOffset = 0;
            var first = model.Blocks[0];
            var unchanged = model.Blocks.Skip(1).Select(b => (b.Source, b.Braille)).ToArray();
            var ticks = 0;
            var clock = Stopwatch.StartNew();
            var previous = clock.Elapsed;
            var largestGap = TimeSpan.Zero;
            var heartbeat = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(25) };
            heartbeat.Tick += (_, _) => { var now = clock.Elapsed; var gap = now - previous; if (gap > largestGap) largestGap = gap; previous = now; ticks++; };
            heartbeat.Start();
            try
            {
                window.PageEditor.Editor.Document.Insert(0, "첫 수정 ");
                await Task.Delay(500);
                window.PageEditor.Editor.Document.Insert(0, "최신 수정 ");
                var latest = first.Source;
                await Until(() => first.Translation?.SourceText == latest && window.Braille.GridView.CoordinatesCurrent,
                    "Incremental: successive visible-editor edits reach latest translation and current grid");
                check(first.Source.StartsWith("최신 수정 첫 수정 ", StringComparison.Ordinal), "Incremental: typing while translation is pending preserves both edits");
                check(ticks >= 10 && largestGap < TimeSpan.FromSeconds(1),
                    $"Incremental: input dispatcher stays responsive (ticks={ticks}, max gap={largestGap.TotalMilliseconds:F0}ms)");
                check(model.Blocks.Skip(1).Select(b => (b.Source, b.Braille)).SequenceEqual(unchanged),
                    "Incremental: editing one block preserves all other source and braille blocks");
                window.PageEditor.Editor.Document.Insert(0, "이동 전 수정 ");
                await window.NavigateAsync(NavigationTarget.OriginalPage, "2", SearchPanel.Text);
                check(model.ActiveBlock?.Page == 2 && first.Translation?.SourceText == first.Source,
                    "Incremental: pending source translation completes before page navigation without losing edits");
            }
            finally { heartbeat.Stop(); }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
