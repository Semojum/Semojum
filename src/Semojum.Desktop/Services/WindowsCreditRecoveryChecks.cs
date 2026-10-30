using System.Net;
using System.Net.Http;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Exercise server job opening through the window command and a controlled HTTP boundary.</summary>
internal static class WindowsCreditRecoveryChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var missing = await OpenAsync("missing", directory);
        check(missing.Credit && !missing.Error && missing.First && missing.Later && missing.BlockedPlaceholder
            && missing.DialogDetail.Contains("이어서 처리", StringComparison.Ordinal),
            "Credit exhaustion retains successful pages on both sides of a missing blocked result without a generic error");
        var available = await OpenAsync("available", directory);
        check(!available.Error && available.BlockedResult,
            "A blocked page with a server result remains available when another page exhausts credits");
        var unavailable = await OpenAsync("blocked-500", directory);
        check(unavailable.Credit && unavailable.Error && unavailable.First,
            "Credit exhaustion does not hide a server failure while retrieving a blocked page");
        var successMissing = await OpenAsync("success-404", directory);
        check(successMissing.Credit && successMissing.Error,
            "Credit exhaustion does not hide a missing successful page");
        var otherNotFound = await OpenAsync("blocked-other-404", directory);
        check(otherNotFound.Credit && otherNotFound.Error && otherNotFound.First,
            "Credit exhaustion does not hide another not-found error on a blocked page");
        var noCreditFlag = await OpenAsync("missing-without-credit", directory);
        check(!noCreditFlag.Credit && noCreditFlag.Error,
            "A blocked result missing without credit exhaustion remains a real error");
        var resumed = await OpenAsync("resume", directory);
        check(!resumed.Error && resumed.EditedAfterResume && resumed.First,
            "Resuming a credit-stopped page preserves its existing local edits when its new result is not available");
        var resumedSuccess = await OpenAsync("resume-success", directory);
        check(resumedSuccess.EditedAfterResume && resumedSuccess.First && resumedSuccess.Later,
            "A successful resumed page preserves existing local edits and memo alongside completed pages");
        check(resumedSuccess.ConflictSaved && resumedSuccess.ServerSaveBlocked,
            "Both local work and the new server result survive save and reopen, with server saving blocked until resolution");
        check(resumedSuccess.RecoveryWorked,
            "Server results can be compared in a separate tab and applied only after a local backup is saved");
        check(resumedSuccess.FailedBackupPreserved,
            "A failed backup keeps both the local edits and unresolved server result");
        check(resumedSuccess.PartialPendingRecovery,
            "Applying one stored result keeps the remaining unconfirmed-page warning and server-save protection");
        await CheckSaveDuringResumeAsync(directory, check);
        await CheckResumeDuringSaveAsync(directory, check);
        await CheckReopenAndReattachAsync(directory, check);
        await CheckIncompleteAndRejectedAsync(directory, check);
        await CheckSameJobTabsAsync(directory, check);
        await CheckClosedPendingTabAsync(directory, check);
        await CheckInitialArrivalOrderAsync(check);
        await CheckStaleReviewRecoveryAsync(directory, check);
        await CheckCreditInquiryRoutesAsync(check);
    }

    private static async Task CheckCreditInquiryRoutesAsync(Action<bool, string> check)
    {
        var fixture = new HeldResumeFixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        try
        {
            for (var choice = 0; choice < 3; choice++)
            {
                var selected = choice; var ticks = 0; var openedNotice = false; var openedInquiry = false;
                string? inquiryTitle = null; Exception? error = null;
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                timer.Tick += (_, _) =>
                {
                    try
                    {
                        if (!openedNotice && Application.Current.Windows.OfType<NoticeDialog>()
                            .FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText.Contains("크레딧이 모두", StringComparison.Ordinal)) is { } notice)
                        {
                            openedNotice = true;
                            notice.ActionButtons[selected].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        else if (openedNotice && !openedInquiry && Application.Current.Windows.OfType<PublicInquiryDialog>()
                            .FirstOrDefault(d => d.Owner == window && d.IsVisible) is { } inquiry)
                        {
                            openedInquiry = true;
                            inquiryTitle = Descendants(inquiry).OfType<TextBox>().Single(t =>
                                System.Windows.Automation.AutomationProperties.GetName(t) == "제목").Text;
                            inquiry.Close();
                        }
                        if (++ticks >= 400) throw new TimeoutException("Credit inquiry route did not close");
                    }
                    catch (Exception ex)
                    {
                        error = ex; timer.Stop();
                        foreach (var owned in window.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) owned.Close();
                    }
                };
                timer.Start();
                try { await window.ShowJobOutcomeAsync(window.Session, new JobStatus("fixture", 1, 0, "FAILED", new() { [1] = "BLOCKED" }, true)); }
                finally { timer.Stop(); }
                check(error is null && openedNotice && openedInquiry == (choice != 2)
                    && (choice == 0 ? inquiryTitle == "크레딧 구매 문의" : choice == 1 ? inquiryTitle == "" : inquiryTitle is null),
                    $"Credit notice action {choice} opens its intended inquiry or closes without sending");
            }
            check(fixture.InquiryRequests == 0, "Opening or cancelling credit inquiry actions never submits a message");
        }
        finally { fixture.Cancel(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static async Task CheckStaleReviewRecoveryAsync(string directory, Action<bool, string> check)
    {
        var fixture = new Fixture("resume-success"); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); var choice = -1; var notices = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray())
            {
                if (dialog is TextValueDialog)
                {
                    Descendants(dialog).OfType<TextBox>().Single().Text = "review-recovery";
                    Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (dialog is NoticeDialog notice && notice.MessageText == "재개 결과와 로컬 교정 확인" && choice >= 0)
                { var selected = choice; choice = -1; notice.ActionButtons[selected].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else if (dialog is AccountDialog account)
                { check(false, "Recovery fixture unexpectedly requested interactive login"); account.Close(); }
                else if (dialog is NoticeDialog other) { notices++; other.Close(); }
            }
        };
        var backup = Path.Combine(directory, "review-recovery-" + Guid.NewGuid().ToString("N") + ".smj");
        window.Show(); timer.Start();
        try
        {
            check(api.Account is { LoginId: "fixture" }, "Recovery fixture starts with an authenticated account");
            await window.ExecuteAsync("serverOpen");
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (notices == 0 && DateTime.UtcNow < deadline) await Task.Delay(25);
            var model = window.Session;
            if (model.Blocks.FirstOrDefault(b => b.Page == 2) is not { } old)
            { check(false, "Recovery fixture received the blocked second page"); return; }
            old.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Text, 0, 1);
            old.Source = ""; old.Braille = ""; old.ServerId = null;
            old.Metadata = old.Metadata with { IsBlocked = true };
            check(old.ReviewMarks is [{ IsStale: true, Length: 0 }] && old.Memos.Count == 0,
                "Resume fixture contains only a stale review on an empty blocked placeholder");
            Descendants(window.CreditBanner).OfType<Button>().Single(b => Equals(b.Content, "이어서 처리")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(8);
            while (!model.HasPendingServerPages && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(model.HasPendingServerPages && model.Blocks.Contains(old) && old.ReviewMarks.Count == 1,
                "Server resume cannot silently replace an empty placeholder that retains a stale review");
            choice = 2; window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(model.HasPendingServerPages && old.ReviewMarks.Count == 1, "Closing resume conflict retains the original stale review");
            window.PendingBackupPicker = () => null; choice = 1;
            window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(model.HasPendingServerPages && old.ReviewMarks.Count == 1, "Canceling backup cannot replace the original stale review");
            window.PendingBackupPicker = () => backup; choice = 1;
            window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(8);
            while (model.HasPendingServerPages && DateTime.UtcNow < deadline) await Task.Delay(25);
            var saved = File.Exists(backup) ? await SmjFile.LoadAsync(backup) : null;
            check(!model.HasPendingServerPages && saved?.Blocks.Any(b => b.Page == 2 && b.ReviewMarks is [{ IsStale: true }]) == true,
                "Applying a resumed server result first preserves the stale review in the explicit SMJ backup");
        }
        finally
        {
            timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    internal static async Task CheckInitialArrivalOrderAsync(Action<bool, string> check)
    {
        var fixture = new OutOfOrderFixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray())
            {
                if (dialog is TextValueDialog) { Descendants(dialog).OfType<TextBox>().Single().Text = "arrival"; Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else if (dialog is NoticeDialog notice) notice.Close();
            }
        };
        timer.Start();
        try
        {
            await window.ExecuteAsync("serverOpen");
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!window.Session.Blocks.Any(b => b.Page == 3 && b.ServerId == "three-b") && DateTime.UtcNow < deadline) await Task.Delay(20);
            var model = window.Session; var third = model.Blocks.Single(b => b.Page == 3 && b.ServerId == "three-a");
            third.Source = "수신 중 직접 교정"; third.Memos.Add(new("arrival-memo", null, "TEXT", 0, "수신 중 직접 교정", "보존할 메모", DateTimeOffset.UtcNow));
            fixture.Release();
            deadline = DateTime.UtcNow.AddSeconds(8);
            while (!model.Blocks.Any(b => b.Page == 1 && b.ServerId == "one-b") && DateTime.UtcNow < deadline) await Task.Delay(20);
            check(model.Blocks.Select(b => b.Page).SequenceEqual([1, 1, 2, 3, 3]), "Initial server pages arriving in separate polls retain original page order, including blocked pages");
            check(model.Blocks.Where(b => b.Page == 1).Select(b => b.ServerId).SequenceEqual(["one-a", "one-b"])
                && model.Blocks.Where(b => b.Page == 3).Select(b => b.ServerId).SequenceEqual(["three-a", "three-b"]), "Within each page server block order remains unchanged");
            check(ReferenceEquals(third, model.Blocks.Single(b => b.ServerId == "three-a")) && third.Source == "수신 중 직접 교정" && third.Memos.Any(m => m.Body == "보존할 메모"),
                "Inserting an earlier page preserves edits and memos on an already received page");
        }
        finally { fixture.Release(); timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private sealed class OutOfOrderFixture : HttpMessageHandler
    {
        private int statuses; private readonly TaskCompletionSource<HttpResponseMessage> next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => next.TrySetResult(Json("{\"isSuccess\":true,\"result\":{\"totalPages\":3,\"completedPages\":3,\"overallStatus\":\"FAILED\",\"pages\":{\"1\":\"COMPLETED\",\"2\":\"BLOCKED\",\"3\":\"COMPLETED\"}}}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}"));
            if (path.EndsWith("/options")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{}}"));
            if (path.EndsWith("/status")) return ++statuses == 1 ? Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"totalPages\":3,\"completedPages\":2,\"overallStatus\":\"IN_PROGRESS\",\"pages\":{\"1\":\"PENDING\",\"2\":\"BLOCKED\",\"3\":\"COMPLETED\"}}}")) : next.Task;
            if (path.EndsWith("/events")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/pages/2")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"status\":\"BLOCKED\",\"result\":{\"text_list\":[],\"braille_text_list\":[],\"bounding_box_list\":[]}}}"));
            if (path.EndsWith("/pages/1") || path.EndsWith("/pages/3"))
            {
                var prefix = path.EndsWith("/pages/1") ? "one" : "three";
                return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"status\":\"COMPLETED\",\"original\":{\"type\":\"text\"},\"result\":{\"text_list\":[{\"id\":\"" + prefix + "-a\",\"type\":\"text\",\"contents\":[\"" + prefix + " a\"]},{\"id\":\"" + prefix + "-b\",\"type\":\"text\",\"contents\":[\"" + prefix + " b\"]}],\"braille_text_list\":[],\"bounding_box_list\":[]}}}"));
            }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
    }

    private static async Task CheckSaveDuringResumeAsync(string directory, Action<bool, string> check)
    {
        var fixture = new HeldResumeFixture();
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path = Path.Combine(directory, "save-during-resume-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            model.PageStatuses[2] = "BLOCKED";
            model.Add(new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local text", Metadata = new(Type: "text") });
            model.ServerPages[2] = [new("x", false)];
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
            await window.OpenAsync(path);
            window.SourceEditor.Document.Insert(0, "edited ");
            window.Session.CreditNotice = "크레딧이 소진되었습니다.";
            Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!fixture.ResumeStarted && DateTime.UtcNow < deadline) await Task.Delay(25);
            if (!fixture.ResumeStarted) throw new InvalidOperationException("Held resume did not start");
            await window.ExecuteAsync("save");
            var loaded = await SmjFile.LoadAsync(path);
            check(loaded.Blocks[0].Source == "edited local text", "A save during resume keeps the current edits in the local work file");
            check(fixture.SaveRequests == 0, "A save during resume must not send the stale snapshot to the server");
            check(loaded.PendingRestart is { Unconfirmed: true, Pages: [2] } && SmjFile.Snapshot(loaded).Manifest.Version == SmjFile.ConflictVersion,
                "An unconfirmed resume and its target pages survive a local save and reopen");
            fixture.Cancel();
            await Task.Delay(100);
            check(window.Session.PendingRestart is { Unconfirmed: true, Pages: [2] },
                "Cancellation before the server response retains the unconfirmed resume for recovery");
        }
        finally
        {
            fixture.Cancel(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task CheckResumeDuringSaveAsync(string directory, Action<bool, string> check)
    {
        var fixture = new HeldSaveFixture();
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path = Path.Combine(directory, "resume-during-save-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            model.PageStatuses[2] = "BLOCKED"; model.Add(new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local text" });
            model.ServerPages[2] = [new("x", false)];
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); await window.OpenAsync(path);
            window.Session.CreditNotice = "크레딧이 소진되었습니다.";
            var saving = window.ExecuteAsync("save");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!fixture.SaveStarted && DateTime.UtcNow < deadline) await Task.Delay(25);
            if (!fixture.SaveStarted) throw new InvalidOperationException("Held save did not start");
            Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.ResumeRequests == 0, "A resume cannot begin while the same document is being saved to the server");
            fixture.CompleteSave(); await saving;
        }
        finally
        {
            fixture.CompleteSave(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task CheckReopenAndReattachAsync(string directory, Action<bool, string> check)
    {
        var fixture = new ReattachFixture();
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path = Path.Combine(directory, "reattach-resume-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            var local = new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local correction" };
            local.Memos.Add(new("memo", null, "TEXT", 0, "local correction", "local memo", DateTimeOffset.UtcNow));
            model.Add(local); model.ServerPages[2] = [new("x", false)]; model.PageStatuses[2] = "BLOCKED";
            var prior = new EditorBlock { Id = 2, Page = 3, ServerId = "old-pending", Source = "earlier server result" };
            model.SetPendingServerPage(new PendingServerPage(3, SmjFile.CaptureBlocks([prior]), [new("old-pending", false)], [], null, "text", "COMPLETED"));
            model.PendingRestart = new PendingRestart(false, [2]);
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); await window.OpenAsync(path);
            await window.ExecuteAsync("save");
            check(fixture.SaveRequests == 0 && window.Session.PendingRestart is { Pages: [2] },
                "Reopened work with an unresolved resumed page cannot overwrite the server");
            window.CheckPendingRestart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!window.Session.PendingServerPages.ContainsKey(2) && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(window.Session.PendingRestart is null && window.Session.Blocks.Any(b => b.Source == "local correction" && b.Memos.Any(m => m.Body == "local memo"))
                && window.Session.PendingServerPages.TryGetValue(2, out var result) && result.Blocks.Any(b => b.Source == "server result"),
                "Reattaching checks the resumed page even when a local server ID exists and retains both versions");
            var ids = window.Session.PendingServerPages.Values.SelectMany(p => p.Blocks).Select(b => b.Id).ToArray();
            check(ids.Length == 2 && ids.Distinct().Count() == ids.Length,
                "Previously stored and newly reattached server pages have distinct local block IDs");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText == "재개 결과와 로컬 교정 확인");
                if (notice is not null) notice.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try
            {
                window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var copyPath = Path.Combine(directory, "reattach-copy-" + Guid.NewGuid().ToString("N") + ".smj");
                try
                {
                    await SmjFile.SaveAsync(copyPath, SmjFile.Snapshot(window.Session));
                    var copy = await SmjFile.LoadAsync(copyPath);
                    check(copy.Blocks.Select(b => b.Source).Order().SequenceEqual(new[] { "earlier server result", "server result" }),
                        "A separate server-results tab containing several pending pages saves and reopens");
                }
                finally { if (File.Exists(copyPath)) File.Delete(copyPath); }
            }
            finally { timer.Stop(); }
        }
        finally
        {
            await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task CheckIncompleteAndRejectedAsync(string directory, Action<bool, string> check)
    {
        foreach (var mode in new[] { "incomplete", "invalid-status", "out-of-range", "accepted-status-403", "rejected-403", "malformed" })
        {
            var fixture = new ReconcileFailureFixture(mode);
            using var http = new HttpClient(fixture);
            var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
            var window = new MainWindow(api); window.Show();
            var path = Path.Combine(directory, "reconcile-" + Guid.NewGuid().ToString("N") + ".smj");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { foreach (var notice in Application.Current.Windows.OfType<NoticeDialog>().Where(d => d.Owner == window && d.IsVisible).ToArray()) notice.Close(); };
            timer.Start();
            try
            {
                var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
                model.PageStatuses[2] = "BLOCKED"; model.Add(new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local text" });
                model.ServerPages[2] = [new("x", false)];
                if (mode is "incomplete" or "invalid-status") model.PendingRestart = new PendingRestart(true, []);
                if (mode == "out-of-range") model.PendingRestart = new PendingRestart(false, [2]);
                await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); await window.OpenAsync(path);
                if (mode is "incomplete" or "invalid-status" or "out-of-range") window.CheckPendingRestart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                else
                {
                    window.Session.CreditNotice = "크레딧이 소진되었습니다.";
                    Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (mode is not ("incomplete" or "invalid-status") && !fixture.Replied && DateTime.UtcNow < deadline) await Task.Delay(25);
                await Task.Delay(100);
                if (mode == "rejected-403")
                    check(window.Session.PendingRestart is null, "A definitely rejected resume clears its pending state");
                else
                {
                    var pending = window.Session.PendingRestart;
                    var correct = pending is not null && (mode is "accepted-status-403" or "out-of-range" ? !pending.Unconfirmed && pending.Pages.SequenceEqual([2]) : pending.Unconfirmed);
                    check(correct, mode switch { "incomplete" => "Incomplete server page status keeps an unconfirmed request blocked", "invalid-status" => "Malformed status keeps the request unconfirmed without crashing", "out-of-range" => "A status with an out-of-range target keeps its pending pages", "malformed" => "A malformed resume response keeps the request unconfirmed", _ => "A status error after an accepted resume keeps its pending pages" });
                    await window.ExecuteAsync("save");
                    check(fixture.SaveRequests == 0, "An unresolved resume cannot send a stale server save");
                }
            }
            finally
            {
                timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static async Task CheckSameJobTabsAsync(string directory, Action<bool, string> check)
    {
        var fixture = new ReconcileFailureFixture("same-job");
        using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path1 = Path.Combine(directory, "same-job-a-" + Guid.NewGuid().ToString("N") + ".smj");
        var path2 = Path.Combine(directory, "same-job-b-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            model.PageStatuses[2] = "BLOCKED"; model.Add(new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local text" });
            model.ServerPages[2] = [new("x", false)];
            var snapshot = SmjFile.Snapshot(model); await SmjFile.SaveAsync(path1, snapshot); await SmjFile.SaveAsync(path2, snapshot);
            await window.OpenAsync(path1); await window.OpenAsync(path2);
            await window.ExecuteAsync("save");
            window.Session.CreditNotice = "크레딧이 소진되었습니다.";
            Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.SaveRequests == 0 && fixture.ResumeRequests == 0,
                "Two tabs for the same server job cannot independently save or resume it");
        }
        finally
        {
            await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path1)) File.Delete(path1); if (File.Exists(path2)) File.Delete(path2);
        }
    }

    private static async Task CheckClosedPendingTabAsync(string directory, Action<bool, string> check)
    {
        await CheckClosedPendingTabAsync(directory, check, accepted: false);
        await CheckClosedPendingTabAsync(directory, check, accepted: true);
    }

    private static async Task CheckClosedPendingTabAsync(string directory, Action<bool, string> check, bool accepted)
    {
        var fixture = new HeldResumeFixture();
        using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path1 = Path.Combine(directory, "closed-pending-a-" + Guid.NewGuid().ToString("N") + ".smj");
        var path2 = Path.Combine(directory, "closed-pending-b-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "held-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            model.PageStatuses[2] = "BLOCKED"; model.Add(new EditorBlock { Id = 1, Page = 2, ServerId = "x", Source = "local text" });
            model.ServerPages[2] = [new("x", false)];
            var snapshot = SmjFile.Snapshot(model); await SmjFile.SaveAsync(path1, snapshot); await SmjFile.SaveAsync(path2, snapshot);
            await window.OpenAsync(path1);
            window.Session.CreditNotice = "크레딧이 소진되었습니다.";
            Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!fixture.ResumeStarted && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(fixture.ResumeStarted && window.Session.HasPendingRestart, "The held resume has an unresolved request");
            if (accepted)
            {
                fixture.AcceptResume();
                deadline = DateTime.UtcNow.AddSeconds(5);
                while (!fixture.StatusStarted && DateTime.UtcNow < deadline) await Task.Delay(25);
                check(fixture.StatusStarted && window.Session.PendingRestart is { Unconfirmed: false }, "The accepted resume retains its confirmed target pages");
                check(window.Session.Status.Contains("1쪽 (2)", StringComparison.Ordinal)
                    && window.Session.Status.Contains("마이페이지 사용량", StringComparison.Ordinal)
                    && !window.Session.Status.Contains("성공한 쪽만", StringComparison.Ordinal),
                    "The accepted resume reports its actual page and directs users to usage without promising a charge rule");
            }
            await window.ExecuteAsync("save");
            await window.OpenAsync(path2);
            await window.ExecuteAsync("save");
            check(fixture.SaveRequests == 0 && File.Exists(path2), "The second copy saves locally but cannot save to the server during resume");
            var original = window.DocumentTabs.Items.OfType<TabItem>().First(t => (t.ToolTip as string) is { } tooltip && (tooltip == path1 || tooltip == path1 + " · 저장되지 않음"));
            ((StackPanel)original.Header).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (window.DocumentTabs.Items.Contains(original) && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(!window.DocumentTabs.Items.Contains(original), "The original pending tab can close after local save");
            window.DocumentTabs.SelectedItem = window.DocumentTabs.Items.OfType<TabItem>().Single(t => (t.ToolTip as string) is { } tooltip && (tooltip == path2 || tooltip == path2 + " · 저장되지 않음"));
            fixture.HoldSave = true;
            var finalSave = window.ExecuteAsync("save");
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (!finalSave.IsCompleted && fixture.SaveRequests == 0 && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(fixture.SaveRequests == 0 && finalSave.IsCompleted && File.Exists(path2), "Closing the original tab keeps the remaining copy locally writable and server-blocked");
            await finalSave;
            fixture.Cancel(); await Task.Delay(100);
            await window.OpenAsync(path1);
            check(window.Session.HasPendingRestart, "Reopening the original retains the unconfirmed resume");
        }
        finally
        {
            fixture.Cancel(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
            if (File.Exists(path1)) File.Delete(path1); if (File.Exists(path2)) File.Delete(path2);
        }
    }

    private sealed class ReconcileFailureFixture(string mode) : HttpMessageHandler
    {
        public bool Replied { get; private set; }
        public int SaveRequests { get; private set; }
        public int ResumeRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}"));
            if (path.EndsWith("/resume"))
            {
                ResumeRequests++;
                if (mode == "rejected-403") { Replied = true; return Task.FromResult(Error()); }
                if (mode == "malformed") { Replied = true; return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{}}")); }
                return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"jobId\":\"held-job\",\"status\":\"IN_PROGRESS\",\"totalPages\":3,\"resumedPages\":[2]}}"));
            }
            if (path.EndsWith("/status"))
            {
                Replied = true;
                return Task.FromResult(mode switch
                {
                    "incomplete" => Json("{\"isSuccess\":true,\"result\":{\"totalPages\":3,\"completedPages\":3,\"overallStatus\":\"COMPLETED\",\"pages\":{}}}"),
                    "invalid-status" => Json("{invalid-json"),
                    "out-of-range" => Json("{\"isSuccess\":true,\"result\":{\"totalPages\":1,\"completedPages\":1,\"overallStatus\":\"COMPLETED\",\"pages\":{\"2\":\"COMPLETED\"}}}"),
                    _ => Error()
                });
            }
            if (path.EndsWith("/save")) { SaveRequests++; throw new InvalidOperationException("Unexpected server save"); }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
        private static HttpResponseMessage Error() => new(HttpStatusCode.Forbidden)
        { Content = new StringContent("{\"isSuccess\":false,\"code\":\"ACCESS_DENIED\",\"message\":\"denied\"}", Encoding.UTF8, "application/json") };
    }

    private sealed class HeldSaveFixture : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> save = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SaveStarted { get; private set; }
        public int ResumeRequests { get; private set; }
        public void CompleteSave() => save.TrySetResult(Json("{\"isSuccess\":true,\"result\":{\"savedPages\":1,\"failedPages\":[]}}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}"));
            if (path.EndsWith("/save")) { SaveStarted = true; return save.Task; }
            if (path.EndsWith("/resume")) { ResumeRequests++; throw new InvalidOperationException("Unexpected resume"); }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
    }

    private sealed class ReattachFixture : HttpMessageHandler
    {
        public int SaveRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}"));
            if (path.EndsWith("/status")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"totalPages\":2,\"completedPages\":2,\"overallStatus\":\"COMPLETED\",\"pages\":{\"2\":\"COMPLETED\"}}}"));
            if (path.EndsWith("/pages/2")) return Task.FromResult(Json("{\"isSuccess\":true,\"result\":{\"status\":\"COMPLETED\",\"original\":{\"type\":\"text\"},\"result\":{\"text_list\":[{\"id\":\"new\",\"type\":\"text\",\"contents\":[\"server result\"]}],\"braille_text_list\":[],\"bounding_box_list\":[]}}}"));
            if (path.EndsWith("/events")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/save")) { SaveRequests++; throw new InvalidOperationException("Unexpected server save"); }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class HeldResumeFixture : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> status = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> heldSave = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldSave { get; set; }
        public bool ResumeStarted { get; private set; }
        public bool StatusStarted { get; private set; }
        public int SaveRequests { get; private set; }
        public int InquiryRequests { get; private set; }
        public void AcceptResume() => resume.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"jobId\":\"held-job\",\"status\":\"IN_PROGRESS\",\"totalPages\":2,\"resumedPages\":[2]}}", Encoding.UTF8, "application/json") });
        public void Cancel() { resume.TrySetCanceled(); status.TrySetCanceled(); }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Reply("{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}");
            if (path.EndsWith("/resume")) { ResumeStarted = true; return resume.Task; }
            if (path.EndsWith("/status")) { StatusStarted = true; return status.Task; }
            if (path.EndsWith("/public/inquiries")) { InquiryRequests++; return Reply("{\"isSuccess\":true,\"result\":{}}"); }
            if (path.EndsWith("/save")) { SaveRequests++; return HoldSave ? heldSave.Task : Reply("{\"isSuccess\":true,\"result\":{\"savedPages\":1,\"failedPages\":[]}}"); }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
        private static Task<HttpResponseMessage> Reply(string json) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed record Observation(bool Credit, bool Error, bool First, bool Later, bool BlockedPlaceholder,
        bool BlockedResult, bool EditedAfterResume, string DialogDetail, bool ConflictSaved, bool ServerSaveBlocked, bool RecoveryWorked, bool FailedBackupPreserved, bool PartialPendingRecovery);

    private static async Task<Observation> OpenAsync(string mode, string directory)
    {
        var fixture = new Fixture(mode);
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api);
        var dialogs = 0; var dialogDetail = ""; var recoveryChoice = -1;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray())
            {
                if (dialog is TextValueDialog)
                {
                    Descendants(dialog).OfType<TextBox>().Single().Text = "credit-test";
                    Descendants(dialog).OfType<Button>().Single(b => b.Content?.ToString() == "확인").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (dialog is NoticeDialog notice)
                {
                    if (recoveryChoice >= 0 && notice.MessageText == "재개 결과와 로컬 교정 확인")
                    {
                        var selected = recoveryChoice; recoveryChoice = -1;
                        notice.ActionButtons[selected].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        continue;
                    }
                    dialogs++; dialogDetail = notice.DetailText;
                    dialog.Close();
                }
            }
        };
        window.Show(); timer.Start();
        try
        {
            await window.ExecuteAsync("serverOpen");
            var timeout = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < timeout && dialogs == 0 && window.Session.ErrorNotice.Length == 0)
                await Task.Delay(25);
            var resumedEdit = false;
            var conflictSaved = false; var serverSaveBlocked = false; var recoveryWorked = false; var failedBackupPreserved = false; var partialPendingRecovery = false;
            if (mode is "resume" or "resume-success" && dialogs > 0)
            {
                var edited = window.Session.Blocks.Single(b => b.Page == 2 && b.Source == "blocked content");
                edited.Source = "local edit";
                if (mode == "resume-success") edited.Memos.Add(new("local-memo", null, "TEXT", 0, "local edit", "교정 메모", DateTimeOffset.UtcNow));
                Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                timeout = DateTime.UtcNow.AddSeconds(8);
                while (DateTime.UtcNow < timeout && (mode == "resume"
                    ? dialogs < 2
                    : !window.Session.PendingServerPages.ContainsKey(2)))
                    await Task.Delay(25);
                resumedEdit = (mode == "resume" ? dialogs == 2 : fixture.Page2Requests >= 2)
                    && ReferenceEquals(edited, window.Session.Blocks.FirstOrDefault(b => b.Page == 2))
                    && edited.Source == "local edit" && (mode == "resume" || edited.Memos.Any(m => m.Body == "교정 메모"));
                if (mode == "resume-success" && window.Session.PendingServerPages.TryGetValue(2, out var pending))
                {
                    var path = Path.Combine(directory, "resume-conflict-" + Guid.NewGuid().ToString("N") + ".smj");
                    try
                    {
                        var snapshot = SmjFile.Snapshot(window.Session);
                        await SmjFile.SaveAsync(path, snapshot);
                        var loaded = await SmjFile.LoadAsync(path);
                        conflictSaved = snapshot.Manifest.Version == SmjFile.ConflictVersion
                            && loaded.Blocks.Any(b => b.Page == 2 && b.Source == "local edit" && b.Memos.Any(m => m.Body == "교정 메모"))
                            && loaded.PendingServerPages.TryGetValue(2, out var saved) && saved.Blocks.Any(b => b.Source == "new server content")
                            && pending.Blocks.Any(b => b.Source == "new server content") && loaded.ErrorNotice.Length > 0;
                        try { JobClient.PlanSave(SmjFile.Snapshot(loaded).Manifest); }
                        catch (InvalidOperationException) { serverSaveBlocked = true; }
                    }
                    finally { if (File.Exists(path)) File.Delete(path); }
                    var original = window.Session; var originalTab = window.DocumentTabs.SelectedIndex;
                    recoveryChoice = 0;
                    window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var separateTab = window.Session.Blocks.Any(b => b.Page == 2 && b.Source == "new server content")
                        && !ReferenceEquals(window.Session, original) && original.HasPendingServerPages;
                    window.DocumentTabs.SelectedIndex = originalTab;
                    window.PendingBackupPicker = () => Path.Combine(directory, "missing-backup-folder", "backup.smj");
                    recoveryChoice = 1;
                    window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    failedBackupPreserved = original.HasPendingServerPages
                        && original.Blocks.Any(b => b.Page == 2 && b.Source == "local edit" && b.Memos.Any(m => m.Body == "교정 메모"));
                    var backup = Path.Combine(directory, "resume-backup-" + Guid.NewGuid().ToString("N") + ".smj");
                    window.PendingBackupPicker = () => backup;
                    recoveryChoice = 1;
                    window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var finishBy = DateTime.UtcNow.AddSeconds(8);
                    while (DateTime.UtcNow < finishBy && original.HasPendingServerPages) await Task.Delay(25);
                    try
                    {
                        if (File.Exists(backup))
                        {
                            var savedBackup = await SmjFile.LoadAsync(backup);
                            var clean = SmjFile.Snapshot(original);
                            recoveryWorked = separateTab && !original.HasPendingServerPages && clean.Manifest.Version == SmjFile.Version
                                && original.Blocks.Any(b => b.Page == 2 && b.Source == "new server content")
                                && savedBackup.HasPendingServerPages && savedBackup.Blocks.Any(b => b.Page == 2 && b.Source == "local edit" && b.Memos.Any(m => m.Body == "교정 메모"));
                            JobClient.PlanSave(clean.Manifest);
                        }
                    }
                    finally { if (File.Exists(backup)) File.Delete(backup); }
                    if (recoveryWorked)
                    {
                        original.PendingRestart = new PendingRestart(false, [4]);
                        var next = new EditorBlock { Id = 90, Page = 2, ServerId = "later", Source = "another server result" };
                        original.SetPendingServerPage(new PendingServerPage(2, SmjFile.CaptureBlocks([next]), [new("later", false)], [], null, "text", "COMPLETED"));
                        var secondBackup = Path.Combine(directory, "resume-partial-backup-" + Guid.NewGuid().ToString("N") + ".smj");
                        window.PendingBackupPicker = () => secondBackup; recoveryChoice = 1;
                        window.ReviewPendingServerPages.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        finishBy = DateTime.UtcNow.AddSeconds(8);
                        while (DateTime.UtcNow < finishBy && original.HasPendingServerPages) await Task.Delay(25);
                        try
                        {
                            partialPendingRecovery = !original.HasPendingServerPages && original.PendingRestart is { Pages: [4] }
                                && original.ErrorNotice.Length > 0 && window.CheckPendingRestart.IsVisible;
                            try { JobClient.PlanSave(SmjFile.Snapshot(original).Manifest); partialPendingRecovery = false; }
                            catch (InvalidOperationException) { }
                        }
                        finally { if (File.Exists(secondBackup)) File.Delete(secondBackup); }
                    }
                }
            }
            return new(window.Session.CreditNotice.Contains("크레딧이 소진되었습니다", StringComparison.Ordinal),
                window.Session.ErrorNotice.Length > 0,
                window.Session.Blocks.Any(b => b.Page == 1 && b.Source == "completed content"),
                window.Session.Blocks.Any(b => b.Page == 3 && b.Source == "later content"),
                window.Session.Blocks.Any(b => b.Page == 2 && b.Metadata.IsBlocked),
                window.Session.Blocks.Any(b => b.Page == 2 && b.Source == "blocked content"), resumedEdit, dialogDetail, conflictSaved, serverSaveBlocked, recoveryWorked, failedBackupPreserved, partialPendingRecovery);
        }
        finally
        {
            timer.Stop();
            await window.ReleaseResourcesAsync();
            window.PrepareSmokeShutdown(); window.Close();
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class Fixture(string mode) : HttpMessageHandler
    {
        private int page2Requests;
        private bool resumed;
        public int Page2Requests => page2Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}");
            if (path.EndsWith("/options")) return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{}}");
            if (path.EndsWith("/status")) return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"totalPages\":3,\"completedPages\":3,\"overallStatus\":\"" + (mode == "resume-success" && resumed ? "COMPLETED" : "FAILED") + "\",\"creditExhausted\":" + (mode == "missing-without-credit" || mode == "resume-success" && resumed ? "false" : "true") + ",\"pages\":{\"1\":\"COMPLETED\",\"2\":\"" + (mode == "resume-success" && resumed ? "COMPLETED" : "BLOCKED") + "\",\"3\":\"COMPLETED\"}}}");
            if (path.EndsWith("/resume")) { resumed = true; return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"jobId\":\"credit-test\",\"status\":\"IN_PROGRESS\",\"totalPages\":3,\"resumedPages\":[2]}}"); }
            if (path.EndsWith("/events")) return Reply(HttpStatusCode.NotFound, "{}");
            if (path.EndsWith("/pages/2"))
            {
                page2Requests++;
                if (mode == "blocked-500") return Reply(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"COMMON5000\",\"message\":\"server error\"}");
                if (mode == "blocked-other-404") return Reply(HttpStatusCode.NotFound, "{\"isSuccess\":false,\"code\":\"COMMON4003\",\"message\":\"unknown job\"}");
                if (mode is "available" || mode is "resume" or "resume-success" && page2Requests == 1)
                    return Page("BLOCKED", "blocked content");
                if (mode == "resume-success" && resumed) return Page("COMPLETED", "new server content");
                return Reply(HttpStatusCode.NotFound, "{\"isSuccess\":false,\"code\":\"JOB4001\",\"message\":\"page result unavailable\"}");
            }
            if (path.EndsWith("/pages/1") || path.EndsWith("/pages/3"))
            {
                if (path.EndsWith("/pages/1") && mode == "success-404") return Reply(HttpStatusCode.NotFound, "{\"isSuccess\":false,\"code\":\"JOB4001\",\"message\":\"success page unavailable\"}");
                var text = path.EndsWith("/pages/1") ? "completed content" : "later content";
                return Page("COMPLETED", text);
            }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }

        private static Task<HttpResponseMessage> Page(string status, string text) =>
            Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"status\":\"" + status + "\",\"original\":{\"type\":\"text\"},\"result\":{\"text_list\":[{\"id\":\"" + text[0] + "\",\"type\":\"text\",\"contents\":[\"" + text + "\"]}],\"braille_text_list\":[],\"bounding_box_list\":[]}}}");

        private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string json) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
