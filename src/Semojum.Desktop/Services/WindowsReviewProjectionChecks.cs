using System.Windows;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsReviewProjectionChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var source = new EditorBlock { Id = 1, Page = 1, Source = "첫 줄\n둘째 줄\n셋째 줄" };
        source.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Text, source.Source.LastIndexOf('셋'), 1);
        var session = new DocumentSession(); session.Add(source); session.ActiveBlock = source;
        var editor = new ContinuousSourceEditor();
        var margin = new MemoMargin(() => session.ActiveBlock, "TEXT", _ => { }) { ResolveLine = editor.ResolveLine };
        editor.Editor.TextArea.LeftMargins.Add(margin); editor.Attach(session);
        var window = new Window { Width = 500, Height = 260, Content = editor, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(editor.ResolveLine(2).Block == source && margin.ManualKinds(2).Contains(ReviewKind.SuggestCorrection),
                "The synchronized source line maps its review marker to the actual block");
            editor.Editor.Select(source.Source.Length - 2, 1);
            source.Source = "짧음";
            var pending = editor.ResolveLine(2);
            check(pending.Block is null && editor.Selection.Length == 0 && margin.ManualKinds(2).Length == 0,
                "While a shorter source refresh is pending, stale visual offsets are not mapped or selected");
            editor.Editor.TextArea.TextView.Redraw(); margin.InvalidateVisual(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(editor.Editor.Text == "짧음" && editor.ResolveLine(0).Block == source,
                "After the queued source refresh, line mapping resumes on the new text");
            source.Source = "가\n나";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var replacement = new EditorBlock { Id = 2, Page = 1, Source = "새" };
            session.Blocks.Clear(); session.Add(replacement); session.ActiveBlock = replacement;
            check(editor.ResolveLine(1).Block is null && margin.ManualKinds(1).Length == 0,
                "A pending block replacement never maps old visual lines to the new block");
            editor.Editor.TextArea.TextView.Redraw(); margin.InvalidateVisual(); window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(editor.Editor.Text == "새" && editor.ResolveLine(0).Block == replacement,
                "Block replacement restores line mapping after the new projection is ready");
            replacement.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
            replacement.Source = "더";
            check(editor.ResolveLine(0).Block is null && margin.ManualKinds(0).Length == 0,
                "An equal-length source replacement never maps old text to the new review state");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            replacement.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 0, 1);
            check(editor.Editor.Text == "더" && editor.ResolveLine(0).Block == replacement
                && margin.ManualKinds(0).SequenceEqual([ReviewKind.SuggestDeletion]),
                "Reviews reappear on the correct line after an equal-length replacement");
        }
        finally { editor.Dispose(); window.Close(); }
    }
}
