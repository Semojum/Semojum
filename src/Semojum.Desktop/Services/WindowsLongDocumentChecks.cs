using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsLongDocumentChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "long-document-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            await window.ExecuteAsync("new");
            var model = window.Session; var block = model.ActiveBlock!;
            var source = string.Join("\n", Enumerable.Range(1, 5000).Select(i => $"{i:D5} 긴 문서 입력 검색 확인을 위한 줄입니다."));
            block.Source = source; window.PageEditor.FollowActive(source.Length);
            var memo = new LineMemo("long-memo", null, "TEXT", 1, "", "긴 문서 메모", DateTimeOffset.UtcNow); block.Memos.Add(memo);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editor = window.PageEditor.Editor; editor.Focus(); editor.CaretOffset = editor.Document.TextLength;
            var samples = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew(); var offset = editor.CaretOffset;
                editor.Document.Insert(offset, "가"); editor.CaretOffset = offset + 1;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            check(block.Source == source + new string('가', 20) && editor.Text == block.Source, $"5,000-line WPF editor retains 20 repeated insertions; median {Median(samples):F1} ms, max {samples.Max():F1} ms");
            var undoWatch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) { ApplicationCommands.Undo.Execute(null, editor.TextArea); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            check(block.Source == source && editor.Text == source, $"20 WPF Undo operations restore exact 5,000-line content in {undoWatch.Elapsed.TotalMilliseconds:F1} ms");
            var redoWatch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) { ApplicationCommands.Redo.Execute(null, editor.TextArea); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            check(block.Source == source + new string('가', 20) && editor.Text == block.Source, $"20 WPF Redo operations restore exact edits in {redoWatch.Elapsed.TotalMilliseconds:F1} ms");
            for (var i = 0; i < 20; i++) { ApplicationCommands.Undo.Execute(null, editor.TextArea); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
            var searchWatch = Stopwatch.StartNew(); var result = await window.FindNextAsync("04999 긴 문서", SearchPanel.Text);
            check(result.Contains("/", StringComparison.Ordinal) && editor.SelectedText == "04999 긴 문서", $"WPF search selects a result near the end in {searchWatch.Elapsed.TotalMilliseconds:F1} ms");
            var saveWatch = Stopwatch.StartNew(); await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
            var reopened = await SmjFile.LoadAsync(path);
            check(reopened.ActiveBlock!.Source == source && reopened.ActiveBlock.Memos is [{ Body: "긴 문서 메모" }], $"SMJ save and reopen retain all 5,000 lines and memo in {saveWatch.Elapsed.TotalMilliseconds:F1} ms");
            using var buffers = new EditorDocuments(); var isolated = new EditorBlock { Source = source }; var buffer = buffers.Source(isolated);
            var syncSamples = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew(); isolated.Source += "가"; syncSamples.Add(watch.Elapsed.TotalMilliseconds);
            }
            check(buffer.Text == isolated.Source, $"Hidden block buffer synchronizes exact content after 20 edits; median {Median(syncSamples):F1} ms, max {syncSamples.Max():F1} ms");
            var other = new EditorBlock { Id = 901, Page = block.Page, Source = "병합할 블록", SourceCaret = 2 };
            var otherMemo = new LineMemo("other-memo", null, "TEXT", 0, "병합할 블록", "두 번째 메모", DateTimeOffset.UtcNow); other.Memos.Add(otherMemo);
            model.Add(other); model.ActiveBlock = other; window.PageEditor.FollowActive(2);
            window.EditStructure(() => BlockEditing.Merge(model, block, other, true));
            check(!model.Blocks.Contains(other) && block.Memos.Select(m => m.LocalId).SequenceEqual(["long-memo", "other-memo"]),
                "Merging blocks retains both memo records in order");
            window.ReplayHistory(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(model.Blocks.Contains(other) && ReferenceEquals(model.ActiveBlock, other) && other.Source == "병합할 블록" && other.Memos.Single().LocalId == "other-memo"
                && window.PageEditor.Editor.CaretOffset == block.Source.Length + 1 + 2, "Undo of a structural merge restores both blocks, memo and caret");
            window.ReplayHistory(true); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!model.Blocks.Contains(other) && block.Memos.Select(m => m.LocalId).SequenceEqual(["long-memo", "other-memo"]),
                "Redo of a structural merge retains both memos in order");
            var complex = "첫 줄 😀\r\n한 e\u0301 둘째 줄\r\n마지막 끝";
            block.Source = complex; model.ActiveBlock = block; window.PageEditor.FollowActive(2);
            block.Memos[1] = block.Memos[1] with { LineIndex = 2 };
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var (oldText, newText) in new[] { ("😀", "😃"), ("한", "가"), ("\r\n", "\n") })
            {
                var before = block.Source;
                var expectedMemos = block.Memos.ToArray();
                var index = before.IndexOf(oldText, StringComparison.Ordinal);
                var expected = before[..index] + newText + before[(index + oldText.Length)..];
                editor.Document.Replace(index, oldText.Length, newText);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(block.Source == expected && editor.Text == expected, $"WPF edit preserves exact text when replacing {oldText.Length} UTF-16 units");
                window.ReplayHistory(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(block.Source == before && editor.Text == before && editor.CaretOffset <= before.Length,
                    "Undo restores exact Unicode and line endings with a valid caret");
                window.ReplayHistory(true); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(block.Source == expected && editor.Text == expected && block.Memos.SequenceEqual(expectedMemos),
                    "Redo restores exact Unicode and line endings without changing memos");
            }
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
            var unicodeReopened = await SmjFile.LoadAsync(path);
            check(unicodeReopened.ActiveBlock!.Source == block.Source && unicodeReopened.ActiveBlock.Memos.SequenceEqual(block.Memos),
                "SMJ reopen preserves edited Unicode, line endings and both memo records");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static double Median(List<double> values) { var sorted = values.Order().ToArray(); return (sorted[9] + sorted[10]) / 2; }
}
