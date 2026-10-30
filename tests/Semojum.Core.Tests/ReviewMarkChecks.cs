using Semojum.Core;

internal static class ReviewMarkChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Local review annotations survive SMJ saves and old versions remain readable", RoundTrip);
        yield return ("Review anchors move before edits and become stale on overlapping edits", Reanchor);
        yield return ("Repeated characters cannot falsely preserve review anchors", RepeatedText);
        yield return ("Valid review anchors agree with every possible minimal repeated-text edit", ExhaustiveAmbiguity);
        yield return ("Multi-block edits conservatively preserve ambiguous review records", MultiBlockRepeatedText);
        yield return ("Source edits and applied translation invalidate braille confirmations", BrailleConfirmation);
        yield return ("Block merging retains review annotations in both panels", Merge);
        yield return ("Invalid review annotations and incompatible SMJ versions cannot overwrite a file", Invalid);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static DocumentSession Document()
    {
        var doc = new DocumentSession();
        doc.Add(new EditorBlock { Id = 1, Page = 1, Source = "가나다라", Braille = "⠁⠃⠉⠙" });
        doc.IsDirty = false;
        return doc;
    }

    private static async Task RoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-review-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "work.smj");
            var legacy = Document();
            var snapshot = SmjFile.Snapshot(legacy);
            foreach (var version in new[] { 1, 2, 3, 4, 5 })
            {
                await SmjFile.SaveAsync(path, snapshot with { Manifest = snapshot.Manifest with { Version = version } });
                var loaded = await SmjFile.LoadAsync(path);
                Check(loaded.Blocks[0].ReviewMarks.Count == 0 && loaded.Blocks[0].Source == "가나다라", $"v{version} could not be read");
            }
            var block = legacy.Blocks[0];
            var originalVersion = legacy.ChangeVersion;
            block.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Text, 0, 1);
            block.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 1, 1);
            block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
            Check(legacy.IsDirty && legacy.ChangeVersion > originalVersion, "Review edits must mark the document dirty");
            var saved = SmjFile.Snapshot(legacy);
            Check(saved.Manifest.Version == SmjFile.ReviewVersion, "Reviewed files must use the protected v6 format");
            block.ReviewMarks.Clear();
            Check(saved.Manifest.Blocks[0].ReviewMarks?.Length == 3, "Snapshot must be detached from live marks");
            await SmjFile.SaveAsync(path, saved);
            var restored = await SmjFile.LoadAsync(path);
            Check(restored.Blocks[0].ReviewMarks.Select(x => x.Kind).SequenceEqual(new[] { ReviewKind.SuggestCorrection, ReviewKind.SuggestDeletion, ReviewKind.HumanConfirmed }), "Kinds did not round trip");
            Check(restored.Blocks[0].ReviewMarks[2].Panel == ReviewPanel.Braille && !restored.IsDirty, "Panel or saved state did not round trip");
            restored.PendingRestart = new(false, [1]);
            Check(SmjFile.Snapshot(restored).Manifest.Version == SmjFile.ReviewVersion, "v6 must also preserve pending recovery metadata");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static Task Reanchor()
    {
        var doc = Document(); var block = doc.Blocks[0];
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 2);
        block.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Braille, 1, 1);
        block.Source = "앞가나다라";
        Check(block.ReviewMarks[0] is { Start: 2, Length: 2, IsStale: false, OriginalText: "나다" }, "Insertion before the mark must move it");
        block.Source = "앞가나수다라";
        Check(block.ReviewMarks[0] is { Length: 0, IsStale: true, OriginalText: "나다" }, "Edit inside reviewed text must retain a stale record");
        block.Braille = "⠁⠿⠉⠙";
        Check(block.ReviewMarks[1] is { IsStale: true, OriginalText: "⠃" }, "Braille replacement must not preserve a valid review of different cells");
        var restored = SmjFile.RestoreBlock(SmjFile.Snapshot(doc).Manifest.Blocks[0]);
        Check(restored.ReviewMarks.All(x => x.IsStale), "Stale review records must survive save and reopen");
        var unicode = new EditorBlock { Id = 3, Page = 1, Source = "😀가" };
        var rejected = false;
        try { unicode.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 1); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "A UTF-16 surrogate pair cannot be split by a review mark");
        return Task.CompletedTask;
    }

    private static Task Merge()
    {
        var doc = Document(); var first = doc.Blocks[0];
        var second = new EditorBlock { Id = 2, Page = 1, Source = "마바사", Braille = "⠑⠋⠛" };
        doc.Add(second);
        first.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
        second.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 1, 1);
        second.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Braille, 0, 1);
        BlockEditing.Merge(doc, first, second, true);
        Check(first.ReviewMarks[0] is { Start: 0, IsStale: false }, "The first mark changed unexpectedly");
        Check(first.ReviewMarks[1] is { Start: 5, OriginalText: "바", IsStale: false }, "Text mark from second block was not moved");
        Check(first.ReviewMarks[2] is { Start: 4, OriginalText: "⠑", IsStale: false }, "Braille mark from second block was not moved");
        Check(SmjFile.Snapshot(doc).Manifest.Blocks[0].ReviewMarks?.Length == 3, "Merged review marks were not saved");
        return Task.CompletedTask;
    }

    private static Task RepeatedText()
    {
        var block = new EditorBlock { Id = 1, Page = 1, Source = "aaaa", Braille = "⠁⠁⠁⠁" };
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 1);
        block.Source = "aaaaa";
        Check(block.ReviewMarks[0] is { IsStale: true, OriginalText: "a" },
            "An insertion in repeated source could be before or after the reviewed character");
        var braille = new EditorBlock { Id = 6, Page = 1, Braille = "⠁⠁⠁⠁" };
        braille.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 1, 1);
        braille.Braille = "⠁⠁⠁⠁⠁";
        Check(braille.ReviewMarks[0] is { IsStale: true, OriginalText: "⠁" },
            "An insertion in repeated braille must not falsely preserve confirmation");
        var deletion = new EditorBlock { Id = 2, Page = 1, Source = "aaaa" };
        deletion.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Text, 2, 1);
        deletion.Source = "aaa";
        Check(deletion.ReviewMarks[0] is { IsStale: true, OriginalText: "a" },
            "A deletion in repeated source could remove or shift the reviewed character");

        var beforeOnly = new EditorBlock { Id = 3, Page = 1, Source = "aaaXZ" };
        beforeOnly.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 4, 1);
        beforeOnly.Source = "aaaaXZ";
        Check(beforeOnly.ReviewMarks[0] is { Start: 5, IsStale: false, OriginalText: "Z" },
            "A repeated insertion entirely before a mark has an unambiguous shift");
        var afterOnly = new EditorBlock { Id = 4, Page = 1, Source = "Xaaaa" };
        afterOnly.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
        afterOnly.Source = "Xaaaaa";
        Check(afterOnly.ReviewMarks[0] is { Start: 0, IsStale: false, OriginalText: "X" },
            "A repeated insertion entirely after a mark must not invalidate it");
        var unique = new EditorBlock { Id = 5, Page = 1, Source = "aaaa" };
        unique.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 1);
        unique.Source = "Xaaaa";
        Check(unique.ReviewMarks[0] is { Start: 2, IsStale: false }, "An unambiguous insertion must still move the mark");
        return Task.CompletedTask;
    }

    private static Task MultiBlockRepeatedText()
    {
        var doc = new DocumentSession();
        var first = new EditorBlock { Id = 1, Page = 1, Source = "aaaa" };
        var second = new EditorBlock { Id = 2, Page = 1, Source = "aaaa" };
        doc.Add(first); doc.Add(second);
        first.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 1);
        second.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Text, 1, 1);
        var spans = BlockRange.Select(doc.Blocks, SearchPanel.Text, 3, 3);
        BlockRange.Replace(doc, spans, SearchPanel.Text, "a");
        Check(doc.Blocks.Count == 1 && first.Source == "aaaaaaa", "The selected cross-block text was not replaced as expected");
        Check(first.ReviewMarks.Count == 2 && first.ReviewMarks.All(mark => mark.IsStale && mark.OriginalText == "a"),
            "Ambiguous repeated edits must retain both blocks' review records without falsely confirming a target");
        Check(SmjFile.Snapshot(doc).Manifest.Blocks[0].ReviewMarks?.Length == 2,
            "Stale review records from both blocks must survive the save snapshot");
        return Task.CompletedTask;
    }

    private static Task ExhaustiveAmbiguity()
    {
        var words = new List<string> { "" };
        for (var length = 1; length <= 4; length++)
            for (var bits = 0; bits < 1 << length; bits++)
                words.Add(new string(Enumerable.Range(0, length).Select(i => (bits & (1 << i)) == 0 ? 'a' : 'b').ToArray()));
        foreach (var before in words.Where(value => value.Length > 0))
        foreach (var after in words.Where(value => value != before))
        {
            var candidates = new List<(int Start, int Removed, int Inserted)>();
            var best = int.MaxValue;
            for (var start = 0; start <= Math.Min(before.Length, after.Length); start++)
            {
                if (!before.AsSpan(0, start).SequenceEqual(after.AsSpan(0, start))) continue;
                for (var removed = 0; removed <= before.Length - start; removed++)
                for (var inserted = 0; inserted <= after.Length - start; inserted++)
                {
                    if (!before.AsSpan(start + removed).SequenceEqual(after.AsSpan(start + inserted))) continue;
                    var cost = removed + inserted;
                    if (cost < best) { best = cost; candidates.Clear(); }
                    if (cost == best) candidates.Add((start, removed, inserted));
                }
            }
            for (var offset = 0; offset < before.Length; offset++)
            for (var length = 1; length <= before.Length - offset; length++)
            {
                var block = new EditorBlock { Id = 1, Page = 1, Source = before };
                block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, offset, length);
                block.Source = after;
                var mark = block.ReviewMarks[0];
                if (mark.IsStale) continue;
                foreach (var edit in candidates)
                {
                    int? mapped = edit.Start + edit.Removed <= offset
                        ? offset + edit.Inserted - edit.Removed
                        : edit.Start >= offset + length ? offset : null;
                    Check(mapped == mark.Start,
                        $"Valid mark {offset}+{length} for {before}->{after} is ambiguous at edit {edit.Start}/{edit.Removed}/{edit.Inserted}");
                }
            }
        }
        return Task.CompletedTask;
    }

    private sealed class SameCellsEngine : ITranslationEngine
    {
        public Task<string> TranslateAsync(string text, int blockId, CancellationToken token) => Task.FromResult("⠁⠃⠉⠙");
    }
    private sealed class DeferredCellsEngine : ITranslationEngine
    {
        public TaskCompletionSource<string> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> TranslateAsync(string text, int blockId, CancellationToken token) => Completion.Task;
    }

    private static async Task BrailleConfirmation()
    {
        var doc = Document(); var block = doc.Blocks[0];
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
        block.AddReviewMark(ReviewKind.SuggestCorrection, ReviewPanel.Braille, 1, 1);
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 1, 1);
        var beforeSourceRevision = block.BrailleConfirmationRevision;
        block.Source += "추가";
        Check(block.BrailleConfirmationRevision == beforeSourceRevision + 1, "A source edit must advance the confirmation generation");
        Check(block.Braille == "⠁⠃⠉⠙" && block.ReviewMarks[0] is { IsStale: true, OriginalText: "⠁" },
            "Changing source must invalidate braille confirmation even if braille cells remain identical");
        Check(!block.ReviewMarks[1].IsStale && !block.ReviewMarks[2].IsStale,
            "A source edit after the selected text must retain other marks");
        var translated = Document().Blocks[0];
        translated.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
        translated.AddReviewMark(ReviewKind.SuggestDeletion, ReviewPanel.Braille, 1, 1);
        var coordinator = new TranslationCoordinator(new SameCellsEngine());
        var beforeTranslationRevision = translated.BrailleConfirmationRevision;
        var cancelled = await coordinator.TranslateAsync(translated, ManualEditDecision.Cancel, TimeSpan.FromSeconds(1));
        Check(cancelled == TranslationOutcome.Cancelled && !translated.ReviewMarks[0].IsStale && translated.BrailleConfirmationRevision == beforeTranslationRevision,
            "Cancel must not invalidate confirmation");
        translated.HasManualBraille = true;
        var kept = await coordinator.TranslateAsync(translated, ManualEditDecision.Keep, TimeSpan.FromSeconds(1));
        Check(kept == TranslationOutcome.Kept && !translated.ReviewMarks[0].IsStale && translated.BrailleConfirmationRevision == beforeTranslationRevision,
            "Keeping a manual translation must not invalidate confirmation");
        var applied = await coordinator.TranslateAsync(translated, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(1));
        Check(applied == TranslationOutcome.Applied && translated.Braille == "⠁⠃⠉⠙" && translated.ReviewMarks[0].IsStale
            && translated.BrailleConfirmationRevision == beforeTranslationRevision + 1,
            "Applied retranslation must invalidate confirmation even when cells are identical");
        var revisionAfterApply = translated.BrailleConfirmationRevision;
        await coordinator.TranslateAsync(translated, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(1));
        Check(translated.BrailleConfirmationRevision == revisionAfterApply + 1,
            "Each applied translation must advance the generation even when no valid confirmation remains");
        Check(!translated.ReviewMarks[1].IsStale, "Unchanged braille proposals must remain intact");
        Check(SmjFile.Snapshot(doc).Manifest.Blocks[0].ReviewMarks?[0].IsStale == true,
            "Source-invalidated braille confirmation must be saved as stale");

        var deferred = new DeferredCellsEngine(); var superseded = Document().Blocks[0];
        var task = new TranslationCoordinator(deferred).TranslateAsync(superseded, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(1));
        superseded.Source += "변경";
        var afterEdit = superseded.BrailleConfirmationRevision;
        deferred.Completion.SetResult("⠁⠃⠉⠙");
        Check(await task == TranslationOutcome.Superseded && superseded.BrailleConfirmationRevision == afterEdit,
            "A superseded translation must not advance the generation after the source edit");
    }

    private static async Task Invalid()
    {
        var doc = Document(); var block = doc.Blocks[0];
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
        var snapshot = SmjFile.Snapshot(doc);
        var path = Path.Combine(Path.GetTempPath(), "semojum-review-invalid-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "keep original");
        try
        {
            foreach (var invalid in new[]
            {
                snapshot with { Manifest = snapshot.Manifest with { Version = 4 } },
                snapshot with { Manifest = snapshot.Manifest with { Version = 7 } },
                snapshot with { Manifest = snapshot.Manifest with { Blocks = [snapshot.Manifest.Blocks[0] with { ReviewMarks = [new("id", ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1, "다른 글자")] }] } }
            })
            {
                var rejected = false;
                try { await SmjFile.SaveAsync(path, invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected && await File.ReadAllTextAsync(path) == "keep original", "Invalid review data overwrote an existing file");
            }
        }
        finally { File.Delete(path); }
    }
}
