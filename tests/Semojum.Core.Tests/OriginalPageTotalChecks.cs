using Semojum.Core;
using Semojum.Core.Integration;

internal static class OriginalPageTotalChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Original page total accepts only the current job's consistent positive response", AcceptServerTotal);
        yield return ("Known original total survives SMJ while older files remain unknown", RoundTrip);
        yield return ("Shrinking job status clears a conflicting total and still saves SMJ", ShrinkingStatus);
        yield return ("Original total changes stay dirty across a pending SMJ snapshot save", SaveRace);
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static Task AcceptServerTotal()
    {
        var doc = new DocumentSession { JobId = "current" };
        doc.Add(new EditorBlock { Id = 1, Page = 3, Source = "원문" });
        doc.OriginalPages[3] = new OriginalPage("text", null, ["원문"]);
        Check(!doc.TryApplyServerOriginalPageTotal("old", 128, [3]), "A previous job response was accepted");
        Check(!doc.TryApplyServerOriginalPageTotal("current", 0, [3]), "A missing or invalid total was accepted");
        Check(!doc.TryApplyServerOriginalPageTotal("current", 2, [3]), "A total smaller than a received original page was accepted");
        Check(!doc.TryApplyServerOriginalPageTotal("current", 128, [129]), "A response with an out-of-range page was accepted");
        Check(doc.TotalOriginalPages is null, "Invalid responses invented an original total");
        Check(doc.TryApplyServerOriginalPageTotal("current", 128, [1, 3]) && doc.TotalOriginalPages == 128,
            "The explicit total in a sparse response was lost");
        var later = new JobStatus("current", 4, 4, "COMPLETED", new() { [1] = "COMPLETED", [3] = "COMPLETED", [4] = "COMPLETED" });
        Check(JobClient.ApplyReceivedStatus(doc, later) && doc.TotalOriginalPages == 4 && doc.PageStatuses[4] == "COMPLETED",
            "A later consistent status did not update the current job's total and pages");
        Check(SmjFile.Snapshot(doc).Manifest.TotalOriginalPages == 4, "Applying a valid new page made the document unsavable");
        Check(!doc.TryApplyServerOriginalPageTotal("current", 3, [1, 3]) && doc.TotalOriginalPages is null,
            "A total smaller than an existing local page was shown as confirmed");
        Check(SmjFile.Snapshot(doc).Manifest.TotalOriginalPages is null, "A smaller total blocked saving existing page status");
        Check(doc.TryApplyServerOriginalPageTotal("current", 4, [1, 3, 4]), "A later valid total was not accepted");
        var invalid = new JobStatus("current", 0, 0, "IN_PROGRESS", new() { [5] = "COMPLETED" });
        Check(JobClient.ApplyReceivedStatus(doc, invalid) && doc.TotalOriginalPages is null && doc.PageStatuses[5] == "COMPLETED",
            "An invalid response left a contradicted total that could prevent saving");
        Check(SmjFile.Snapshot(doc).Manifest.TotalOriginalPages is null, "Unknown total blocked saving received page status");
        Check(doc.TryApplyServerOriginalPageTotal("current", 6, [5]), "A valid original total after an invalid response was lost");
        doc.IsTextJob = true;
        Check(doc.TotalOriginalPages is null && !doc.TryApplyServerOriginalPageTotal("current", 6, [5])
            && SmjFile.Snapshot(doc).Manifest.TotalOriginalPages is null, "A TXT job reused server work pages as original pages");
        doc.IsTextJob = false;
        Check(doc.TryApplyServerOriginalPageTotal("current", 6, [5]), "Switching to an original-backed job did not accept a valid total");
        var old = new JobStatus("old", 9, 1, "FAILED", new() { [9] = "FAILED" });
        Check(!JobClient.ApplyReceivedStatus(doc, old) && !doc.PageStatuses.ContainsKey(9) && doc.TotalOriginalPages == 6,
            "A previous job status changed the current document");
        doc.JobId = "next";
        Check(doc.TotalOriginalPages is null && !doc.TryApplyServerOriginalPageTotal("current", 128, [3]),
            "A previous job's total survived a job change");
        return Task.CompletedTask;
    }

    private static async Task RoundTrip()
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-total-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var doc = new DocumentSession { JobId = "current", JobStatus = "IN_PROGRESS" };
            doc.Add(new EditorBlock { Id = 1, Page = 3, Source = "원문" });
            doc.OriginalPages[3] = new OriginalPage("text", null, ["원문"]);
            doc.PageStatuses[3] = "COMPLETED";
            Check(doc.TryApplyServerOriginalPageTotal("current", 128, [3]), "The confirmed total was not accepted");
            var snapshot = SmjFile.Snapshot(doc);
            var path = Path.Combine(directory, "total.smj");
            await SmjFile.SaveAsync(path, snapshot);
            var loaded = await SmjFile.LoadAsync(path);
            Check(loaded.TotalOriginalPages == 128 && loaded.JobId == "current" && !loaded.IsDirty, "SMJ did not restore the confirmed total as a clean document");
            foreach (var version in Enumerable.Range(1, SmjFile.ReviewVersion))
            {
                var legacy = snapshot with { Manifest = snapshot.Manifest with { Version = version, TotalOriginalPages = null } };
                var oldPath = Path.Combine(directory, $"old-{version}.smj");
                await SmjFile.SaveAsync(oldPath, legacy);
                Check((await SmjFile.LoadAsync(oldPath)).TotalOriginalPages is null, $"Version {version} invented a missing total");
            }
            var invalid = snapshot with { Manifest = snapshot.Manifest with { TotalOriginalPages = 2 } };
            var sentinel = Path.Combine(directory, "sentinel.smj");
            await File.WriteAllTextAsync(sentinel, "unchanged");
            var rejected = false;
            try { await SmjFile.SaveAsync(sentinel, invalid); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected && await File.ReadAllTextAsync(sentinel) == "unchanged", "An inconsistent total overwrote an existing file");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task ShrinkingStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-total-shrink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var doc = new DocumentSession { JobId = "current" };
            doc.Add(new EditorBlock { Id = 1, Page = 4, Source = "로컬 4쪽" });
            doc.OriginalPages[4] = new OriginalPage("text", null, ["로컬 4쪽"]);
            Check(JobClient.ApplyReceivedStatus(doc, new JobStatus("current", 4, 4, "COMPLETED", new() { [4] = "COMPLETED" }))
                && doc.TotalOriginalPages == 4, "Initial 4-page server status was not applied");
            var before = doc.ChangeVersion; doc.IsDirty = false;
            Check(JobClient.ApplyReceivedStatus(doc, new JobStatus("current", 3, 3, "COMPLETED", new() { [3] = "COMPLETED" }))
                && doc.TotalOriginalPages is null && doc.ChangeVersion > before && doc.IsDirty,
                "A 4-to-3 status did not clear the conflicting total and mark the document changed");
            var path = Path.Combine(directory, "shrink.smj");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(doc));
            var loaded = await SmjFile.LoadAsync(path);
            Check(loaded.TotalOriginalPages is null && loaded.PageStatuses.ContainsKey(3) && loaded.PageStatuses.ContainsKey(4)
                && loaded.OriginalPages.ContainsKey(4) && !loaded.IsDirty,
                "The actual SMJ save/reopen did not preserve local and newly received pages without an invented total");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task SaveRace()
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-total-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var doc = new DocumentSession { JobId = "current" };
            doc.Add(new EditorBlock { Id = 1, Page = 1, Source = "원문" });
            doc.OriginalPages[1] = new OriginalPage("text", null, ["원문"]);
            Check(JobClient.ApplyReceivedStatus(doc, new JobStatus("current", 3, 1, "IN_PROGRESS", new() { [1] = "COMPLETED" })), "Initial status was not applied");
            doc.IsDirty = false;
            var snapshot = SmjFile.Snapshot(doc); var version = doc.ChangeVersion;
            var path = Path.Combine(directory, "race.smj");
            var allowDiskWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task SaveCapturedAsync() { await allowDiskWrite.Task; await SmjFile.SaveAsync(path, snapshot); }
            var saving = SaveCapturedAsync();
            Check(!saving.IsCompleted, "The snapshot save fixture was not waiting at the controlled boundary");
            Check(JobClient.ApplyReceivedStatus(doc, new JobStatus("current", 4, 2, "IN_PROGRESS", new() { [4] = "COMPLETED" }))
                && doc.TotalOriginalPages == 4 && doc.ChangeVersion > version && doc.IsDirty,
                "A page total update during the pending save was not tracked as an unsaved change");
            allowDiskWrite.SetResult(); await saving;
            var older = await SmjFile.LoadAsync(path);
            Check(older.TotalOriginalPages == 3 && !older.IsDirty && doc.TotalOriginalPages == 4 && doc.IsDirty,
                "The captured file or live document did not preserve the correct side of the save race");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(doc));
            var latest = await SmjFile.LoadAsync(path);
            Check(latest.TotalOriginalPages == 4 && latest.PageStatuses.ContainsKey(4) && !latest.IsDirty,
                "A second SMJ save did not persist the updated total and pages");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
