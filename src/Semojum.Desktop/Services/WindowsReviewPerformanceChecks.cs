using System.Diagnostics;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsReviewPerformanceChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var source = new EditorBlock { Source = "Z" + string.Concat(Enumerable.Repeat("가나다라마바사\n", 30000)) };
        var margin = new MemoMargin(() => source, "TEXT", _ => { });
        for (var line = 0; line < 200; line++) margin.ManualKinds(line);
        check(margin.LineOffsetsBuildCount == 0, "Long unannotated source never builds review line offsets");
        source.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Text, 0, 1);
        var timer = Stopwatch.StartNew();
        for (var line = 0; line < 3000; line++) margin.ManualKinds(line % 200);
        timer.Stop();
        check(margin.LineOffsetsBuildCount == 1 && margin.ManualKinds(0).SequenceEqual([ReviewKind.SuggestCorrection]),
            $"Repeated long-source review lookup builds line offsets once ({timer.ElapsedMilliseconds} ms for 3000 lookups)");
        source.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 2, 1);
        check(margin.LineOffsetsBuildCount == 1 && margin.ManualKinds(0).SequenceEqual([ReviewKind.SuggestCorrection, ReviewKind.HumanConfirmed]),
            "Review changes appear immediately without rebuilding unchanged line offsets");
        source.Source += "끝";
        margin.ManualKinds(0);
        check(margin.LineOffsetsBuildCount == 2, "A source edit rebuilds cached line offsets once for the new revision");
        margin.ShowReviews = false;
        check(margin.ManualKinds(0).Length == 0 && margin.LineOffsetsBuildCount == 2, "Hiding review markers does not rebuild source offsets");
        margin.ShowReviews = true;
        check(margin.ManualKinds(0).Contains(ReviewKind.HumanConfirmed), "Showing source reviews restores current annotations");

        var block = new EditorBlock { Braille = new string('⠁', 1024) };
        for (var i = 0; i < 3000; i++)
        {
            var start = (i * 29) % 1000; var length = 1 + i % 24;
            block.ReviewMarks.Add(new ReviewMark(i.ToString(), (ReviewKind)(i % 3), ReviewPanel.Braille, start, length, new string('⠁', length)));
        }
        var rows = Enumerable.Range(0, 8).Select(row => Enumerable.Range(0, 32)
            .Select(col => (GridCell?)new GridCell(block, row * 32 + col, '⠁')).ToArray()).ToArray();
        var insertion = new GridCell(block, 2, '⠀', true);
        rows[0][31] = insertion;
        var face = new BrailleGridFace(1, rows);
        var grid = new BrailleGridView { Blocks = [block], ItemsSource = new[] { face } };
        var detached = new BrailleGridView { Blocks = [block], ItemsSource = new[] { face } };
        timer.Restart();
        var rowKinds = Enumerable.Range(0, rows.Length).Select(row => grid.ManualReviewKinds(face, row)).ToArray();
        foreach (var cell in rows.SelectMany(row => row).OfType<GridCell>()) grid.ManualReviewForCell(face, cell);
        timer.Stop();
        var correct = true;
        for (var row = 0; row < rows.Length; row++)
        {
            var expectedKinds = new List<ReviewKind>();
            foreach (var cell in rows[row].OfType<GridCell>())
            {
                var matches = block.ReviewMarks.Where(mark => !mark.IsStale && mark.Panel == ReviewPanel.Braille && cell.Offset >= mark.Start && cell.Offset < mark.Start + mark.Length).ToArray();
                correct &= grid.ManualReviewForCell(face, cell) == matches.LastOrDefault();
                if (!cell.IsInsertion)
                    foreach (var mark in matches) if (!expectedKinds.Contains(mark.Kind)) expectedKinds.Add(mark.Kind);
            }
            correct &= rowKinds[row].SequenceEqual(expectedKinds);
        }
        check(correct && grid.ManualReviewCacheBuildCount == 1,
            $"Face review index preserves overlap priority and row icon order across 3000 marks ({timer.ElapsedMilliseconds} ms)");
        check(detached.ManualReviewForCell(face, rows[0][0]!) == grid.ManualReviewForCell(face, rows[0][0]!) && detached.ManualReviewCacheBuildCount == 1,
            "Detached and main grids independently render identical manual reviews");
        var added = block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
        check(grid.ManualReviewForCell(face, rows[0][0]!) == added && detached.ManualReviewForCell(face, rows[0][0]!) == added
            && grid.ManualReviewCacheBuildCount == 2 && detached.ManualReviewCacheBuildCount == 2,
            "Editing reviews invalidates the main and detached face caches");
        block.ReviewMarks.Remove(added);
        check(grid.ManualReviewForCell(face, rows[0][0]!) != added && grid.ManualReviewCacheBuildCount == 3,
            "Removing a review restores the preceding overlapping state");
        grid.CoordinatesCurrent = false;
        check(grid.ManualReviewKinds(face, 0).Length == 0 && grid.ManualReviewForCell(face, rows[0][0]!) is null,
            "Stale grid coordinates hide both review icons and cell backgrounds");
        grid.CoordinatesCurrent = true;
        check(grid.ManualReviewForCell(face, rows[0][0]!) is not null, "Current coordinates restore the correct face review");
        grid.ShowReviews = false;
        check(grid.ManualReviewForCell(face, rows[0][0]!) is null && grid.ManualReviewKinds(face, 0).Length == 0,
            "Hiding reviews suppresses cached cell backgrounds and icons");
        grid.ShowReviews = true;
        check(grid.ManualReviewForCell(face, rows[0][0]!) is not null, "Showing reviews rebuilds the current face index");
        var beforeRevision = grid.ManualReviewCacheBuildCount;
        block.Braille += "⠃";
        grid.ManualReviewForCell(face, rows[0][0]!);
        check(grid.ManualReviewCacheBuildCount == beforeRevision + 1, "Editing braille invalidates the face review index");
        var nextFace = new BrailleGridFace(2, [[new GridCell(block, 1, '⠁')]]);
        grid.ItemsSource = new[] { nextFace };
        var nextBuild = grid.ManualReviewCacheBuildCount;
        check(grid.ManualReviewForCell(nextFace, nextFace.Rows[0][0]!) is not null && grid.ManualReviewCacheBuildCount == nextBuild + 1,
            "Replacing composed faces builds the index for the new coordinates");
        var translationBuild = grid.ManualReviewCacheBuildCount;
        block.Translation = BrailleTranslation.Plain(block.Braille);
        grid.ManualReviewForCell(nextFace, nextFace.Rows[0][0]!);
        check(grid.ManualReviewCacheBuildCount == translationBuild + 1, "Retranslation invalidates the face review index");
        var replacement = new EditorBlock { Braille = "⠉" };
        replacement.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Braille, 0, 1);
        var replacementFace = new BrailleGridFace(3, [[new GridCell(replacement, 0, '⠉')]]);
        grid.Blocks = [replacement]; grid.ItemsSource = new[] { replacementFace };
        check(grid.ManualReviewForCell(replacementFace, replacementFace.Rows[0][0]!)?.Kind == ReviewKind.SuggestDeletion,
            "Replacing the document's blocks binds reviews to the replacement face");
        var replacementBuild = grid.ManualReviewCacheBuildCount;
        block.ReviewMarks.Clear();
        grid.ManualReviewForCell(replacementFace, replacementFace.Rows[0][0]!);
        check(grid.ManualReviewCacheBuildCount == replacementBuild,
            "Changes to removed blocks do not invalidate the replacement document's cache");
        replacement.ReviewMarks.Clear();
        check(grid.ManualReviewForCell(replacementFace, replacementFace.Rows[0][0]!) is null && grid.ManualReviewCacheBuildCount == replacementBuild + 1,
            "Changes to replacement blocks immediately invalidate their review cache");
    }
}
