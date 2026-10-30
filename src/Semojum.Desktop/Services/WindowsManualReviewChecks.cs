using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsManualReviewChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "manual-review-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = window.Session; model.Blocks.Clear(); model.ActiveBlock = null;
            var first = new EditorBlock { Id = 1, Page = 1, Source = "abcdef", Braille = "⠁⠃⠉" };
            var second = new EditorBlock { Id = 2, Page = 1, Source = "ghijkl", Braille = "⠙⠑⠋" };
            model.Add(first); model.Add(second); model.ActiveBlock = first;
            window.PageEditor.Refresh(); window.UpdateLayout();
            typeof(MainWindow).GetMethod("SetActiveEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window.PageEditor.Editor]);
            // The page refresh is still queued. Never apply the visible selection to
            // a different model snapshot, even if the replacement has the same length.
            first.Source = "uvwxyz";
            window.PageEditor.Editor.Select(1, 2); await window.ExecuteAsync("reviewCorrection");
            check(first.ReviewMarks.Count == 0, "A pending same-length source replacement cannot receive a review for stale visible text");
            first.Source = "xy";
            window.PageEditor.Editor.Select(4, 2); await window.ExecuteAsync("reviewCorrection");
            check(first.ReviewMarks.Count == 0, "A pending shorter source replacement cannot receive a review for stale visible text");
            first.Source = "abcdef"; window.PageEditor.Refresh(); window.UpdateLayout();
            window.PageEditor.Editor.Select(0, 0); await window.ExecuteAsync("reviewCorrection");
            check(first.ReviewMarks.Count == 0 && model.Status.Contains("선택"), "Review action requires an actual nonempty selection");
            window.PageEditor.Editor.Select(1, 2); await window.ExecuteAsync("reviewCorrection");
            check(first.ReviewMarks is [{ Kind: ReviewKind.SuggestCorrection, Panel: ReviewPanel.Text, Start: 1, Length: 2, OriginalText: "bc", IsStale: false }],
                "A synchronized source selection creates a correction proposal even when a refresh remains queued");
            // Rendering intentionally waits for the queued page refresh so that
            // visual line offsets cannot be applied to an obsolete document.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var margin = window.PageEditor.Editor.TextArea.LeftMargins.OfType<MemoMargin>().Single();
            check(margin.ManualKinds(0).Contains(ReviewKind.SuggestCorrection) && !ReviewVisuals.Icon(ReviewKind.SuggestCorrection).Bounds.IsEmpty,
                "Correction proposal produces the Figma icon and a non-color label on its line");
            window.ReplayHistory(); check(first.ReviewMarks.Count == 0, "Review annotation has a standalone undo step");
            window.ReplayHistory(true); check(first.ReviewMarks.Single().Kind == ReviewKind.SuggestCorrection, "Redo restores the annotation");
            window.PageEditor.Editor.Select(1, 2); await window.ExecuteAsync("reviewDeletion");
            check(first.ReviewMarks is [{ Kind: ReviewKind.SuggestDeletion }], "A different state replaces the exact same selection");
            window.PageEditor.Editor.Select(2, 2); await window.ExecuteAsync("reviewConfirmed");
            check(first.ReviewMarks.Count == 2 && first.ReviewMarks.Any(m => m.Kind == ReviewKind.SuggestDeletion)
                && first.ReviewMarks.Any(m => m.Kind == ReviewKind.HumanConfirmed), "Partly overlapping reviews are retained independently");
            window.PageEditor.Editor.Select(4, 4); await window.ExecuteAsync("reviewCorrection");
            check(first.ReviewMarks.Any(m => m.Kind == ReviewKind.SuggestCorrection && m.Start == 4 && m.Length == 2)
                && second.ReviewMarks.Any(m => m.Kind == ReviewKind.SuggestCorrection && m.Start == 0 && m.Length == 1),
                "A selection spanning blocks stores each actual range separately");
            window.PageEditor.Editor.Select(0, 0);
            var capture = Path.Combine(directory, "manual-review-render.png");
            await WindowsSmoke.RenderElementAsync(window, capture);
            using (var stream = File.OpenRead(capture))
            {
                var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                check(ContainsColor(bitmap, Color.FromRgb(255, 244, 206)) && ContainsColor(bitmap, Color.FromRgb(253, 231, 233))
                    && ContainsColor(bitmap, Color.FromRgb(223, 246, 221)), "The actual editor renders all three Figma review backgrounds");
            }
            window.PageEditor.Editor.Focus(); window.PageEditor.Editor.Select(1, 1);
            var selectionCapture = Path.Combine(directory, "manual-review-selected.png");
            await WindowsSmoke.RenderElementAsync(window, selectionCapture);
            using (var stream = File.OpenRead(selectionCapture))
            {
                var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                check(ContainsColor(bitmap, Color.FromRgb(204, 228, 247)) && ContainsColor(bitmap, Color.FromRgb(223, 246, 221)),
                    "Selection remains visible above review colors while other reviews remain visible");
            }
            model.IsReadOnly = true; window.PageEditor.Editor.Select(0, 1); var count = first.ReviewMarks.Count;
            await window.ExecuteAsync("reviewConfirmed");
            check(first.ReviewMarks.Count == count && model.Status.Contains("읽기 전용"), "Read-only documents reject review changes");
            model.IsReadOnly = false;
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); var reopened = await SmjFile.LoadAsync(path);
            check(reopened.Blocks.SelectMany(b => b.ReviewMarks).Count() == first.ReviewMarks.Count + second.ReviewMarks.Count,
                "Manual review annotations survive actual SMJ save and reopen");
            var plan = JobClient.PlanSave(SmjFile.Snapshot(model).Manifest);
            check(plan.LocalOnly.Any(n => n.Contains("검토 표시") && n.Contains("서버에서 다시 열면")),
                "Server save explicitly explains that local SMJ review marks are not restored from the server");

            var braille = new EditorBlock { Id = 3, Page = 2, Source = "가", Braille = "⠁⠃" };
            braille.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            var cell = new GridCell(braille, 0, '⠁'); var face = new BrailleGridFace(1, [[cell]]);
            var grid = new BrailleGridView { Blocks = [braille], ItemsSource = new[] { face } };
            check(grid.ManualReviewKinds(face, 0).SequenceEqual([ReviewKind.HumanConfirmed])
                && !ReviewVisuals.Icon(ReviewKind.HumanConfirmed).Bounds.IsEmpty, "Braille grid reports human confirmation with its actual icon");
            grid.ShowReviews = false; check(grid.ManualReviewKinds(face, 0).Length == 0, "Review toggle hides manual braille review markers");

            var historyBlock = new EditorBlock { Id = 4, Page = 3, Source = "가나", Braille = "⠁⠃", Translation = BrailleTranslation.Plain("⠁⠃") };
            historyBlock.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            using (var docs = new EditorDocuments())
            {
                docs.Source(historyBlock).Insert(0, "새");
                historyBlock.Braille = "⠉⠃"; historyBlock.Translation = BrailleTranslation.Plain("⠉⠃");
                docs.Replay(3, false);
                check(historyBlock.ReviewMarks.Single().IsStale && historyBlock.ReviewMarks.Single().Length == 0,
                    "Source undo cannot resurrect a confirmation after asynchronous braille changes");
                var saved = new DocumentSession(); saved.Add(historyBlock);
                check(SmjFile.Snapshot(saved).Manifest.Blocks[0].ReviewMarks![0].IsStale,
                    "Stale review after asynchronous undo remains valid for SMJ saving");
            }
            var sameCells = new EditorBlock { Id = 5, Page = 4, Source = "가나", Braille = "⠁⠃", Translation = BrailleTranslation.Plain("⠁⠃") };
            sameCells.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            using (var docs = new EditorDocuments())
            {
                docs.Source(sameCells).Insert(0, "새"); sameCells.InvalidateBrailleConfirmations(); sameCells.Translation = BrailleTranslation.Plain("⠁⠃");
                docs.Replay(4, false);
                check(sameCells.ReviewMarks.Single().IsStale, "Equal-cell retranslation still requires braille confirmation again after undo");
            }
            var annotation = new EditorBlock { Id = 6, Page = 5, Source = "가", Braille = "⠁", Translation = BrailleTranslation.Plain("⠁") };
            using (var docs = new EditorDocuments())
            {
                var before = new Dictionary<EditorBlock, ReviewMark[]> { [annotation] = [] };
                annotation.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
                var after = new Dictionary<EditorBlock, ReviewMark[]> { [annotation] = annotation.ReviewMarks.ToArray() };
                docs.RecordReviewEdit(annotation, before, after);
                annotation.InvalidateBrailleConfirmations(); annotation.Translation = BrailleTranslation.Plain("⠁");
                docs.Replay(5, false); docs.Replay(5, true);
                check(annotation.ReviewMarks.Single().IsStale, "Review redo cannot resurrect a pre-retranslation human confirmation");
            }

            first.ReviewMarks.Clear(); second.ReviewMarks.Clear(); first.Source = "abcd"; second.Source = "efgh";
            first.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            second.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            model.ActiveBlock = first;
            window.EditStructure(() => BlockEditing.Merge(model, first, second, true));
            check(first.ReviewMarks.Count(m => m.Panel == ReviewPanel.Braille && m.IsStale) == 2,
                "Merging source context makes both blocks' braille confirmations stale without dropping them");
            window.ReplayHistory();
            check(model.Blocks.Contains(second) && first.ReviewMarks.Any(m => !m.IsStale) && second.ReviewMarks.Any(m => !m.IsStale),
                "Structure undo restores both blocks and their previously valid confirmations");
            window.ReplayHistory(true);
            check(!model.Blocks.Contains(second) && first.ReviewMarks.Count(m => m.IsStale) == 2,
                "Structure redo restores both stale confirmations after merging");

            window.ReplayHistory(); first.ReviewMarks.Clear(); first.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 0, 1);
            model.ActiveBlock = first; window.PageEditor.Refresh();
            await window.ExecuteAsync("deleteBlock");
            check(!model.Blocks.Contains(first) && model.Status.Contains("검토 표시"), "Explicit block deletion announces its removed review marks");
            window.ReplayHistory();
            check(model.Blocks.Contains(first) && first.ReviewMarks.Any(m => m.Kind == ReviewKind.SuggestDeletion), "Undo restores deleted block review marks");

            model.ActiveBlock = first; first.Source = "abcd"; first.ReviewMarks.Clear();
            var mark = first.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
            first.Source = "xbcd";
            window.PageEditor.Refresh(); window.PageEditor.Editor.Select(0, 1);
            typeof(MainWindow).GetMethod("SetActiveEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window.PageEditor.Editor]);
            var dialog = window.CreateReviewListWindow(); dialog.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var list = Descendants(dialog).OfType<ListBox>().Single(); list.SelectedIndex = 0;
                check(list.Items.Cast<object>().Any(item => item.ToString()!.Contains("다시 확인 필요")), "Review list labels stale marks explicitly");
                first.Source = "ybcd";
                Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "다시 지정")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(first.ReviewMarks.Single(m => m.Id == mark.Id).IsStale && first.ReviewMarks.Count == 1,
                    "A document change while the review list is open cannot confirm a different selection");
            }
            finally { dialog.Close(); }
            window.PageEditor.Refresh(); window.PageEditor.Editor.Select(0, 1);
            var reassign = window.CreateReviewListWindow(); reassign.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var list = Descendants(reassign).OfType<ListBox>().Single(); list.SelectedIndex = 0;
                Descendants(reassign).OfType<Button>().Single(b => Equals(b.Content, "다시 지정")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(first.ReviewMarks is [{ Kind: ReviewKind.HumanConfirmed, IsStale: false, OriginalText: "y" }],
                    "A stale mark can be explicitly assigned to the newly selected text");
            }
            finally { reassign.Close(); }
            window.ReplayHistory();
            check(first.ReviewMarks is [{ IsStale: true, OriginalText: "a" }], "Undo restores the original stale mark after reassignment");
            window.ReplayHistory(true);
            check(first.ReviewMarks is [{ IsStale: false, OriginalText: "y" }], "Redo restores the explicitly reassigned mark");
        }
        finally
        {
            await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static bool ContainsColor(BitmapSource source, Color color)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight]; bitmap.CopyPixels(pixels, stride, 0);
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == color.B && pixels[i + 1] == color.G && pixels[i + 2] == color.R && pixels[i + 3] == 255) return true;
        return false;
    }
}
