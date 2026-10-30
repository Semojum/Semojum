using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private const string UnconfirmedRestartNotice = "재개 요청이 서버에서 처리됐는지 확인할 수 없습니다. 서버 저장은 차단됩니다. 로컬 .smj 파일을 저장하고 작업 ID와 함께 운영자에게 문의해 주세요.";
    internal Func<Window, string[]?>? FilePicker { get; set; }
    private bool uploadFlowBusy;
    internal Func<string?>? PendingBackupPicker { get; set; }
    private bool HasOtherTabForJob(OpenDocument context) => context.Model.JobId is { } id
        && (openDocuments.Any(other => !ReferenceEquals(other, context) && other.Model.JobId == id) || HasClosedJobRecovery(context));
    // The same window and commands can be exercised against an isolated HTTP service in Windows smoke checks.
    internal MainWindow(SemojumApiClient service) : this(smoke: true, testWithoutAccount: true)
    { api = service; api.RefreshedIdentityAllowed = account => AutomaticLogin.IsSameDesktopArea(api.Account, account); api.RefreshRequiresIdentity = account => account.Role == "ROLE_ORG_ADMIN"; jobs = new(service); }

    private void CreditNotice_Close(object sender, RoutedEventArgs e) => session.CreditNotice = "";
    private async Task TranslateImportedTextAsync(OpenDocument context)
    {
        if (smoke) return;
        var model = context.Model; var ct = context.JobLifetime.Token;
        model.ErrorNotice = "";
        var local = new TranslationCoordinator(sidecar is null ? engine : new LocalBrailleEngine(sidecar, () => model.Options));
        try
        {
            var blocks = model.Blocks.ToArray(); var count = 0;
            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();
                var revision = block.SourceRevision;
                var result = await local.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(35), ct);
                if (result is TranslationOutcome.Applied or TranslationOutcome.Kept)
                { context.Translated[block] = revision; if (session == model) translatedRevisions[block] = revision; }
                model.Status = $"TXT 로컬 점역 {++count}/{blocks.Length} · AI·크레딧 사용 없음";
            }
            model.Status = "TXT 점역 완료 · 오류 블록은 상태를 확인해 주세요 · Ctrl+S로 저장";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { model.ErrorNotice = model.Status = "TXT 점역 실패 · 원문은 유지됩니다: " + ex.Message; }
    }
    private void RefreshAccount() => AccountLabel.Text = api.Account is { } account ? account.LoginId : "로그인 안 됨";
    private bool EnsureAccount(bool allowInstitution = false)
    {
        while (true)
        {
            if (api.Account is null)
            {
                var signedIn = new AccountDialog(api, smoke ? null : accountCredentials, automaticLoginMessage, allowInstitution) { Owner = this }.ShowDialog() == true;
                automaticLoginMessage = null;
                if (!signedIn) { RefreshAccount(); return false; }
            }
            if (api.Account is { Role: "ROLE_ORG_ADMIN" })
            {
                if (!allowInstitution) return false;
                var institution = new InstitutionWindow(api, LogoutInstitutionAsync) { Owner = this };
                institution.ShowDialog();
                if (institution.LogoutRequested) continue;
                return false;
            }
            RefreshAccount();
            return api.Account is { } account && AutomaticLogin.IsEditorAllowed(account);
        }
    }

    private async Task<bool> LogoutInstitutionAsync()
    {
        try
        {
            if (smoke) await api.LogoutAsync();
            else await AutomaticLogin.LogoutAsync(api, accountCredentials);
            RefreshAccount();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("저장된 로그인 정보를 삭제하지 못해 로그아웃하지 않았습니다.\n" + ex.Message, "로그아웃");
            return false;
        }
    }

    private async Task OpenServerAsync()
    {
        if (fileBusy || !EnsureAccount()) return;
        var dialog = new TextValueDialog("서버 작업 열기", "작업 ID") { Owner = this };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
        await LoadJobAsync(dialog.Value.Trim(), "서버 작업.smj");
    }

    private async Task UploadAsync(string[]? previousPaths = null)
    {
        if (fileBusy || uploadFlowBusy) return;
        uploadFlowBusy = true;
        try
        {
            var selectedPaths = previousPaths is { Length: > 0 }
                ? previousPaths : FilePicker is null ? OpenDocumentDialog.SelectPaths(this) : FilePicker(this);
            if (selectedPaths is not { Length: > 0 }) return;
            if (selectedPaths.Length == 1 && Path.GetExtension(selectedPaths[0]).Equals(".smj", StringComparison.OrdinalIgnoreCase))
            { await OpenAsync(selectedPaths[0]); return; }
            if (!EnsureAccount()) return;
            var client = new SettingsClient(api);
            BrailleForm[] forms = []; var defaults = new AccountDefaults(session.Options, null, "odd"); var available = true;
            using var settingsTimeout = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
            settingsTimeout.CancelAfter(TimeSpan.FromSeconds(8));
            try { forms = await client.FormsAsync(settingsTimeout.Token); defaults = await client.DefaultsAsync(settingsTimeout.Token); }
            catch (Exception ex) when (ex is HttpRequestException or SemojumApiException || ex is OperationCanceledException && !documentLifetime.IsCancellationRequested) { available = false; }
            var dialog = new OpenDocumentDialog(forms, defaults, available) { Owner = this };
            dialog.SetPaths(selectedPaths);
            if (dialog.ShowDialog() != true) return;
            var orderedFiles = dialog.Paths;
            var settings = dialog.Settings;
            if (orderedFiles.Length == 1 && Path.GetExtension(orderedFiles[0]).ToLowerInvariant() is ".smj" or ".txt")
            { await OpenAsync(orderedFiles[0], settings, dialog.FormId); return; }
            if (orderedFiles.Length > 1) { var order = new ImageOrderDialog(orderedFiles) { Owner = this }; if (order.ShowDialog() != true) return; orderedFiles = order.Paths; }
            fileBusy = true;
            try
            {
                var files = await UploadFileReader.ReadAsync(orderedFiles, documentLifetime.Token);
                session.Status = "파일 업로드 중…";
                var job = await jobs.UploadAsync(files, settings, documentLifetime.Token, dialog.FormId);
                await LoadJobAsync(job.JobId, Path.GetFileNameWithoutExtension(orderedFiles[0]) + ".smj", files, uploadSettings: settings);
                if (openDocument?.Model.JobId == job.JobId) openDocument.UploadPaths = orderedFiles;
            }
            finally { fileBusy = false; }
        }
        finally { uploadFlowBusy = false; }
    }

    private async Task LoadJobAsync(string id, string title, IReadOnlyList<UploadFile>? originals = null, int? restorePage = null, DocumentSession? uploadSettings = null)
    {
        var existing = openDocuments.FirstOrDefault(d => d.Model.JobId == id);
        if (existing is not null) { SetSession(existing.Model, existing.Path, existing.ShowOriginal); return; }
        fileBusy = true;
        try
        {
            var model = new DocumentSession { JobId = id, OwnerLoginId = api.Account!.LoginId, Title = title, JobStatus = "PENDING" };
            if (originals is not null) model.IsTextJob = originals.All(f => Path.GetExtension(f.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase));
            await jobs.ReadSettingsAsync(model, documentLifetime.Token, uploadSettings);
            var status = await jobs.StatusAsync(id, documentLifetime.Token);
            if (originals is not null) foreach (var file in originals) model.OriginalFiles[file.FileName] = file.Bytes;
            model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "변환 대기 중", State = "대기", IsBrailleLocked = true, Metadata = new(IsBlocked: true) });
            SetSession(model, null, originals?.Any(f => !Path.GetExtension(f.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)) ?? false);
            openDocument!.OriginalKindKnown = originals is not null;
            _ = MonitorJobAsync(openDocument!, status, restorePage, applyInitialChoices: uploadSettings is not null);
        }
        finally { fileBusy = false; }
    }
    // replacePages: pages being re-run by retry/resume/re-attach. Only their blocks are replaced in place; every other block stays as edited.
    private static EditorBlock[] UniqueReceivedBlocks(DocumentSession model, EditorBlock[] blocks, ref int nextId)
    {
        var used = model.Blocks.Select(b => b.Id).Concat(model.PendingServerPages.Values.SelectMany(p => p.Blocks.Select(b => b.Id))).ToHashSet();
        var result = new EditorBlock[blocks.Length];
        for (var i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            if (used.Contains(block.Id))
            {
                while (used.Contains(nextId)) nextId++;
                var saved = SmjFile.CaptureBlocks([block])[0] with { Id = nextId++ };
                var replacement = SmjFile.RestoreBlock(saved);
                replacement.State = block.State; replacement.IsBrailleLocked = block.IsBrailleLocked;
                block = replacement;
            }
            used.Add(block.Id); nextId = Math.Max(nextId, block.Id + 1); result[i] = block;
        }
        return result;
    }

    private async Task MonitorJobAsync(OpenDocument context, JobStatus status, int? restorePage, bool applyInitialChoices = false, HashSet<int>? replacePages = null)
    {
        var model = context.Model; var ct = context.JobLifetime.Token;
        context.PageSyncInProgress++;
        var holdingSync = true;
        void ReleaseSync() { if (holdingSync) { context.PageSyncInProgress--; holdingSync = false; } }
        void MarkPageChecked(int page)
        {
            if (model.PendingRestart is not { } pending || !pending.Pages.Contains(page)) return;
            var remaining = pending.Pages.Where(n => n != page).ToArray();
            model.PendingRestart = remaining.Length == 0 ? null : new PendingRestart(false, remaining);
            if (model.PendingRestart is null && !model.HasPendingServerPages) model.ErrorNotice = "";
        }
        model.ErrorNotice = model.HasPendingServerPages || model.HasPendingRestart ? "서버 재개 결과 또는 요청 상태를 먼저 확인해 주세요. 확인 전에는 서버 저장을 할 수 없습니다." : "";
        var jobTranslator = new TranslationCoordinator(sidecar is null ? engine : new LocalBrailleEngine(sidecar, () => model.Options));
        var started = System.Diagnostics.Stopwatch.StartNew(); var initialFinished = status.FinishedPages;
        void RememberRevision(EditorBlock b, int revision)
        {
            context.Translated[b] = revision;
            if (session == model) translatedRevisions[b] = revision;
        }
        try
        {
            var loadedPages = replacePages is null ? new HashSet<int>() : status.Pages.Keys.Where(p => !replacePages.Contains(p)).ToHashSet();
            var waiting = replacePages is null;
            await using var events = new JobEventWatch(api, model.JobId!, ct);
            var nextId = replacePages is null ? 1 : model.Blocks.Select(b => b.Id)
                .Concat(model.PendingServerPages.Values.SelectMany(p => p.Blocks.Select(b => b.Id))).DefaultIfEmpty(0).Max() + 1;
            while (true)
            {
                model.JobStatus = status.OverallStatus;
                model.CreditNotice = status.CreditExhausted ? $"크레딧이 소진되었습니다. ({status.SucceededPages}/{status.TotalPages}쪽 변환 완료)" : "";
                foreach (var pair in status.Pages) model.PageStatuses[pair.Key] = pair.Value;
                // Server completedPages already includes failed pages; the rate uses finished pages so failures neither
                // count twice nor shorten the estimate a second time (C-02).
                var finishedSinceOpen = status.FinishedPages - initialFinished;
                var remainingSeconds = finishedSinceOpen > 0 ? started.Elapsed.TotalSeconds / finishedSinceOpen * status.RemainingPages : 0;
                model.Progress = status.IsTerminal ? null : status.ToProgress(
                    remainingSeconds > 0 ? $"잔여 약 {Math.Max(1, Math.Ceiling(remainingSeconds / 60))}분 (처리 속도 기준)" : "남은 시간 계산 중");
                model.Status = $"AI 변환 {status.FinishedPages}/{status.TotalPages}쪽 처리 · 성공 {status.SucceededPages} · 확인 필요 {status.FailedPages} · {status.Label}";
                foreach (var page in status.Pages.Where(p => JobStatus.IsSucceededPage(p.Value) || JobStatus.IsFailedPage(p.Value)).OrderBy(p => p.Key))
                {
                    if (loadedPages.Contains(page.Key)) continue;
                    RemotePage result; var missingCreditResult = false;
                    try { result = await jobs.PageAsync(model.JobId!, page.Key, nextId, ct); }
                    catch (SemojumApiException ex) when (status.CreditExhausted && page.Value == "BLOCKED"
                        && ex.Status == System.Net.HttpStatusCode.NotFound && ex.Code == "JOB4001")
                    {
                        // A page stopped before processing has no result yet. Keep any existing edits, and
                        // continue receiving completed pages instead of treating credit exhaustion as a connection error.
                        missingCreditResult = true;
                        if (model.Blocks.Any(b => b.Page == page.Key && (!b.Metadata.IsBlocked || b.ServerId is not null)))
                        { loadedPages.Add(page.Key); MarkPageChecked(page.Key); continue; }
                        result = new RemotePage(page.Key, page.Value, [], null, null, null, 0, 0);
                    }
                    if (applyInitialChoices) foreach (var block in result.Blocks) VisualDraftSelection.ApplyInitial(block, model.Options);
                    var keepLocalPage = replacePages?.Contains(page.Key) == true && model.Blocks.Any(b => b.Page == page.Key
                        && (b.ServerId is not null || !b.Metadata.IsBlocked || b.Source.Length > 0 || b.Braille.Length > 0 || b.Memos.Count > 0));
                    if (keepLocalPage && !missingCreditResult)
                    {
                        // Both versions remain available: never replace local work or record the new server baseline as if it had been applied.
                        foreach (var block in result.Blocks.Where(b => b.Braille.Length == 0 && !b.Metadata.IsBlocked))
                            await jobTranslator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(35), ct);
                        await CacheOriginalAsync(model, result, ct);
                        result = result with { Blocks = UniqueReceivedBlocks(model, result.Blocks, ref nextId) };
                        model.SetPendingServerPage(new PendingServerPage(page.Key, SmjFile.CaptureBlocks(result.Blocks),
                            result.ServerBlocks ?? [], result.Quality ?? [], result.OcrConfidenceAverage, result.OriginalType, result.Status));
                        model.ErrorNotice = "서버 재개 결과와 로컬 교정이 충돌합니다. 결과를 확인하기 전에는 서버 저장을 할 수 없습니다.";
                        if (result.Blocks.Length > 0) nextId = Math.Max(nextId, result.Blocks.Max(b => b.Id) + 1);
                        loadedPages.Add(page.Key); MarkPageChecked(page.Key);
                        continue;
                    }
                    if (!missingCreditResult)
                    {
                        JobClient.Remember(model, result);
                        await CacheOriginalAsync(model, result, ct);
                    }
                    if (!context.OriginalKindKnown && result.OriginalType is not null)
                    {
                        context.OriginalKindKnown = true;
                        context.ShowOriginal = result.OriginalType != "text";
                        if (session == model) { OriginalToggle.IsChecked = context.ShowOriginal; UpdatePanelLayout(); }
                    }
                    if (!waiting) result = result with { Blocks = UniqueReceivedBlocks(model, result.Blocks, ref nextId) };
                    if (result.Blocks.Length == 0 && page.Value is "FAILED" or "BLOCKED")
                        result = result with { Blocks = [new EditorBlock { Id = nextId++, Page = page.Key, Heading = missingCreditResult ? "크레딧 소진 · 변환 대기" : "변환 실패 · 원본 확인", State = page.Value, IsBrailleLocked = true, Metadata = new(IsBlocked: true) }] };
                    if (result.Blocks.Length == 0) { loadedPages.Add(page.Key); MarkPageChecked(page.Key); continue; }
                    if (waiting) { model.ActiveBlock = null; model.Blocks.Clear(); waiting = false; }
                    // First-time results may arrive in different polls out of page order.
                    // Insert the whole page before later pages without disturbing their edits
                    // or the server's order of blocks within this page.
                    int? insertAt = replacePages is null ? model.Blocks.TakeWhile(b => b.Page < page.Key).Count() : null;
                    if (replacePages?.Contains(page.Key) == true)
                    {
                        foreach (var old in model.Blocks.Where(b => b.Page == page.Key).ToArray()) { if (model.ActiveBlock == old) model.ActiveBlock = null; model.Blocks.Remove(old); }
                        insertAt = model.Blocks.TakeWhile(b => b.Page < page.Key).Count();
                    }
                    foreach (var block in result.Blocks)
                    {
                        if (insertAt is { } at) { model.Blocks.Insert(at, block); insertAt = at + 1; model.ActiveBlock ??= block; } else model.Add(block);
                        nextId = Math.Max(nextId, block.Id + 1); RememberRevision(block, block.Braille.Length > 0 ? block.SourceRevision : -1);
                    }
                    loadedPages.Add(page.Key); MarkPageChecked(page.Key);
                    if (restorePage == page.Key) { model.ActiveBlock = result.Blocks[0]; restorePage = null; }
                    foreach (var block in result.Blocks.Where(b => b.Braille.Length == 0 && !b.Metadata.IsBlocked))
                    {
                        var outcome = await jobTranslator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(35), ct);
                        if (outcome == TranslationOutcome.Applied) RememberRevision(block, block.SourceRevision);
                    }
                }
                if (status.IsTerminal) break;
                await events.WaitAsync(ct);
                await Task.Delay(150, ct);
                status = await jobs.StatusAsync(model.JobId!, ct);
            }
            if (waiting && status.TotalPages == 0 && status.OverallStatus == "COMPLETED")
            { model.ActiveBlock = null; model.Blocks.Clear(); model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "새 문서" }); }
            model.IsDirty = status.TotalPages > 0;
            var issues = model.QualityReports.Values.Sum(q => q.Length);
            model.Status = $"{status.Label} · 성공 {status.SucceededPages}쪽 · 확인할 쪽 {status.FailedPages}개"
                + (issues > 0 ? $" · 서버 검토 항목 {issues}건(쪽별 변환 상태에서 확인)" : "") + " · Ctrl+S로 저장하세요.";
            if (model.CancellationRequested)
            {
                model.CancellationRequested = false;
                model.ErrorNotice = $"변환 중단 처리가 끝났습니다. 완료 {status.SucceededPages}쪽 · 확인 필요 {status.FailedPages}쪽. 처리된 쪽은 계속 편집할 수 있습니다.";
            }
            if (session == model) ShowActiveBlock();
            model.Progress = null;
            ReleaseSync();
            await ShowJobOutcomeAsync(model, status);
            if (applyInitialChoices) await ShowUnreadableNoticeAsync(model);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            model.ErrorNotice = model.Status = "작업 수신을 완료하지 못했습니다. 문서를 닫고 서버 작업에서 다시 열어 주세요: " + ex.Message;
            if (ex is HttpRequestException && session == model && !resourcesReleased)
            {
                var disconnected = FileNotices.Disconnected(model.Blocks.Where(b => !b.Metadata.IsBlocked).Select(b => b.Page).Distinct().Count());
                disconnected.Owner = this; disconnected.ShowDialog();
                if (disconnected.Choice == 0) await ReattachAsync(context);
            }
        }
        finally { ReleaseSync(); model.Progress = null; }
    }

    internal async Task ShowJobOutcomeAsync(DocumentSession model, JobStatus status)
    {
        if (resourcesReleased || session != model || status.OverallStatus is "CANCELLED" or "CANCELED") return;
        NoticeDialog? notice = status.CreditExhausted ? FileNotices.NoCredits() :
            status.FailedPages > 0 && status.SucceededPages > 0 ? FileNotices.PartialConversion(status.TotalPages, status.SucceededPages, status.Pages.Where(p => JobStatus.IsFailedPage(p.Value)).Select(p => p.Key).Order().ToArray()) :
            status.OverallStatus == "FAILED" ? FileNotices.ConversionFailed() : null;
        if (notice is null) return;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (resourcesReleased || session != model) return;
        notice.Owner = this; notice.ShowDialog();
        if (status.CreditExhausted)
        {
            if (notice.Choice is 0 or 1) new PublicInquiryDialog(api) { Owner = this }.ShowDialog();
        }
        else if (status.FailedPages > 0 && status.SucceededPages > 0)
        {
            if (notice.Choice == 0) await RestartJobAsync(model, retry: true);
        }
        else if (status.OverallStatus == "FAILED")
        {
            if (notice.Choice == 0) await UploadAsync(openDocument?.UploadPaths);
            else if (notice.Choice == 1 && EnsureAccount()) new DefaultsDialog(new(api)) { Owner = this }.ShowDialog();
            else if (notice.Choice == 2) new PublicInquiryDialog(api) { Owner = this }.ShowDialog();
        }
    }

    // BE 2026-10-27: retry re-queues AI-failed pages, resume re-queues credit-exhausted pages (기획 10-1). Only those pages are replaced.
    private async Task RestartJobAsync(DocumentSession model, bool retry)
    {
        if (resourcesReleased || session != model || openDocument is null || model.JobId is null || !EnsureAccount()) return;
        var context = openDocument;
        if (HasOtherTabForJob(context))
        { model.ErrorNotice = "같은 서버 작업의 다른 탭을 닫거나, 닫힌 재개 작업의 원본 파일을 다시 열어 확인한 뒤 재개해 주세요."; return; }
        if (context.ServerSaveInProgress || context.PageSyncInProgress > 0)
        { model.Status = "저장 또는 쪽 확인이 끝난 뒤 다시 시도해 주세요."; return; }
        if (model.HasPendingServerPages || model.HasPendingRestart)
        { model.ErrorNotice = "보관된 재개 결과를 먼저 확인해 주세요. 로컬 교정과 서버 결과는 유지됩니다."; return; }
        var ct = context.JobLifetime.Token;
        var candidatePages = model.PageStatuses.Where(p => JobStatus.IsFailedPage(p.Value)).Select(p => p.Key).Order().ToArray();
        model.PendingRestart = new PendingRestart(true, candidatePages);
        model.ErrorNotice = "서버 재개 요청의 결과를 확인하는 중입니다. 확인 전에는 서버 저장을 할 수 없습니다.";
        context.PageSyncInProgress++;
        var requestAccepted = false;
        try
        {
            var restart = retry ? await jobs.RetryAsync(model.JobId, ct) : await jobs.ResumeAsync(model.JobId, ct);
            requestAccepted = true;
            if (restart.Pages.Length == 0)
            {
                model.PendingRestart = null;
                if (!model.HasPendingServerPages) model.ErrorNotice = "";
                model.Status = retry ? "다시 변환할 쪽이 없습니다. 취소했거나 크레딧 소진으로 멈춘 쪽은 대상이 아닙니다." : "이어서 처리할 쪽이 없습니다.";
                return;
            }
            model.PendingRestart = new PendingRestart(false, restart.Pages);
            model.CreditNotice = "";
            model.Status = $"{(retry ? "실패한 쪽 다시 변환" : "이어서 처리")} 요청 · {restart.Pages.Length}쪽 ({string.Join(", ", restart.Pages)}) · 성공한 쪽만 크레딧이 차감됩니다.";
            var status = await jobs.StatusAsync(model.JobId, ct);
            _ = MonitorJobAsync(context, status, null, replacePages: restart.Pages.ToHashSet());
        }
        catch (OperationCanceledException) { if (model.PendingRestart is { Unconfirmed: true }) model.ErrorNotice = UnconfirmedRestartNotice; }
        catch (SemojumApiException ex)
        {
            if (!requestAccepted && (int)ex.Status is >= 400 and < 500) { model.PendingRestart = null; if (!model.HasPendingServerPages) model.ErrorNotice = ""; }
            if (model.PendingRestart is { Unconfirmed: true }) model.ErrorNotice = UnconfirmedRestartNotice;
            var notice = ex.Code == "CREDIT4001" ? FileNotices.NoCredits()
                : new NoticeDialog(retry ? "실패한 쪽을 다시 변환하지 못했습니다." : "이어서 처리하지 못했습니다.", $"{ex.Message} ({ex.Code})", ["닫기"], error: true);
            notice.Owner = this; notice.ShowDialog();
            if (ex.Code == "CREDIT4001" && notice.Choice is 0 or 1) new PublicInquiryDialog(api) { Owner = this }.ShowDialog();
        }
        catch (HttpRequestException)
        { model.ErrorNotice = model.PendingRestart is { Unconfirmed: true } ? UnconfirmedRestartNotice : "재개 상태를 확인하지 못했습니다. 확인 전에는 서버 저장을 할 수 없습니다."; }
        catch (Exception ex)
        { model.ErrorNotice = model.PendingRestart is { Unconfirmed: true } ? UnconfirmedRestartNotice : "재개 상태를 확인하지 못했습니다. 확인 전에는 서버 저장을 할 수 없습니다: " + ex.Message; }
        finally { context.PageSyncInProgress--; }
    }

    private async void ReviewPendingServerPages_Click(object sender, RoutedEventArgs e)
    {
        if (fileBusy || !session.HasPendingServerPages) return;
        var owner = session;
        var pages = owner.PendingServerPages.Values.OrderBy(p => p.Page).ToArray();
        var choice = NoticeDialog.Choose(this, "재개 결과와 로컬 교정 확인",
            "재개 결과가 돌아온 쪽에 로컬 교정이나 메모가 있습니다. 서버 결과를 별도 탭에서 비교하거나, 현재 교정본을 별도 파일로 보관한 뒤 서버 결과를 적용할 수 있습니다. 해결 전에는 서버 저장이 차단됩니다.",
            ["서버 결과 별도 탭", "교정본 백업 후 적용", "닫기"], width: 560);
        if (choice == 0)
        {
            pages = owner.PendingServerPages.Values.OrderBy(p => p.Page).ToArray();
            var copy = new DocumentSession { Title = "재개된 서버 결과.smj", Layout = owner.Layout, Options = owner.Options, IsTextJob = owner.IsTextJob };
            var nextId = pages.SelectMany(p => p.Blocks).Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
            var usedIds = new HashSet<int>();
            foreach (var page in pages)
            {
                if (page.Blocks.Length == 0) { usedIds.Add(nextId); copy.Add(new EditorBlock { Id = nextId++, Page = page.Page, Heading = "서버 결과 없음", IsBrailleLocked = true, Metadata = new(IsBlocked: true) }); }
                else foreach (var block in page.Blocks)
                {
                    var id = block.Id;
                    if (usedIds.Contains(id)) { while (usedIds.Contains(nextId)) nextId++; id = nextId++; }
                    usedIds.Add(id); copy.Add(SmjFile.RestoreBlock(block with { Id = id }));
                }
            }
            copy.IsDirty = true; SetSession(copy, null, false);
            copy.Status = "서버 재개 결과의 별도 로컬 사본 · 원래 교정은 이전 탭에 보존됩니다.";
            return;
        }
        if (choice != 1 || !ReferenceEquals(owner, session)) return;
        if (owner.IsReadOnly) { ShowError(new InvalidOperationException("읽기 전용 작업은 다른 이름으로 저장한 뒤 서버 결과를 적용해 주세요.")); return; }
        string? backupPath;
        if (PendingBackupPicker is not null) backupPath = PendingBackupPicker();
        else
        {
            var picker = new SaveFileDialog { Title = "현재 교정본을 별도 작업 파일로 보관", Filter = "세모점 작업 파일|*.smj", DefaultExt = ".smj", AddExtension = true,
                FileName = Path.GetFileNameWithoutExtension(owner.Title) + "_재개전_교정본.smj", OverwritePrompt = true };
            backupPath = picker.ShowDialog(this) == true ? picker.FileName : null;
        }
        if (backupPath is null) return;
        if (filePath is not null && LocalWorkspace.SamePath(filePath, backupPath)
            || openDocuments.Any(d => d.Path is not null && LocalWorkspace.SamePath(d.Path, backupPath)))
        { ShowError(new IOException("현재 열려 있는 작업 파일과 다른 이름으로 교정본을 보관해 주세요.")); return; }
        using var lease = WorkFileLease.TryAcquire(backupPath);
        if (lease is null) { ShowError(new IOException("교정본을 보관할 수 없는 경로입니다. 다른 이름을 선택해 주세요.")); return; }
        fileBusy = true; IsEnabled = false;
        try
        {
            pages = owner.PendingServerPages.Values.OrderBy(p => p.Page).ToArray();
            var version = owner.ChangeVersion;
            await SmjFile.SaveAsync(backupPath, SmjFile.Snapshot(owner));
            if (!ReferenceEquals(owner, session) || owner.ChangeVersion != version)
                throw new InvalidOperationException("교정본을 저장하는 동안 문서가 변경됐습니다. 보관된 파일을 확인한 뒤 다시 시도해 주세요.");
            var conflictPageNumbers = pages.Select(p => p.Page).ToHashSet();
            var usedIds = owner.Blocks.Where(b => !conflictPageNumbers.Contains(b.Page)).Select(b => b.Id).ToHashSet();
            var nextBlockId = usedIds.DefaultIfEmpty(0).Max() + 1;
            foreach (var page in pages)
            {
                foreach (var old in owner.Blocks.Where(b => b.Page == page.Page).ToArray())
                { if (owner.ActiveBlock == old) owner.ActiveBlock = null; owner.Blocks.Remove(old); }
                var at = owner.Blocks.TakeWhile(b => b.Page < page.Page).Count();
                var restored = page.Blocks.Select(saved =>
                {
                    var id = saved.Id;
                    if (usedIds.Contains(id)) { while (usedIds.Contains(nextBlockId)) nextBlockId++; id = nextBlockId++; }
                    usedIds.Add(id);
                    return SmjFile.RestoreBlock(saved with { Id = id });
                }).ToArray();
                if (restored.Length == 0)
                {
                    while (usedIds.Contains(nextBlockId)) nextBlockId++;
                    restored = [new EditorBlock { Id = nextBlockId++, Page = page.Page, Heading = "서버 결과 없음", IsBrailleLocked = true, Metadata = new(IsBlocked: true) }];
                    usedIds.Add(restored[0].Id);
                }
                foreach (var block in restored)
                {
                    owner.Blocks.Insert(at++, block); owner.ActiveBlock ??= block;
                    translatedRevisions[block] = block.Braille.Length > 0 ? block.SourceRevision : -1;
                    if (openDocument is not null) openDocument.Translated[block] = translatedRevisions[block];
                }
                JobClient.Remember(owner, new RemotePage(page.Page, page.Status, restored, page.OriginalType, null, null, 0, 0,
                    page.ServerBlocks, page.Quality, page.OcrConfidenceAverage));
            }
            owner.ClearPendingServerPages();
            owner.ErrorNotice = owner.HasPendingRestart ? "서버 재개 요청의 남은 쪽 상태를 확인해 주세요. 확인 전에는 서버 저장을 할 수 없습니다." : "";
            owner.Status = "교정본을 별도 파일에 보관하고 서버 결과를 적용했습니다. Ctrl+S로 저장해 주세요.";
            ShowActiveBlock();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { IsEnabled = true; fileBusy = false; }
    }

    // After a dropped connection: read the job status again and fetch only the pages this window never received.
    private async Task ReattachAsync(OpenDocument context)
    {
        if (resourcesReleased || session != context.Model || context.Model.JobId is null) return;
        // A status snapshot cannot prove that an earlier request will not be accepted later.
        // Keep the on-disk guard until a server contract can identify that exact request.
        if (context.Model.PendingRestart is { Unconfirmed: true }) { context.Model.ErrorNotice = UnconfirmedRestartNotice; return; }
        if (HasOtherTabForJob(context)) { context.Model.ErrorNotice = "같은 서버 작업의 다른 탭을 닫거나, 닫힌 재개 작업의 원본 파일을 다시 열어 확인해 주세요."; return; }
        if (context.ServerSaveInProgress) { context.Model.ErrorNotice = "저장이 끝난 뒤 재개 상태를 확인해 주세요."; return; }
        context.PageSyncInProgress++;
        try
        {
            var model = context.Model;
            var status = await jobs.StatusAsync(model.JobId!, context.JobLifetime.Token);
            var missing = status.Pages.Keys.Where(p => !model.Blocks.Any(b => b.Page == p && b.ServerId is not null)).ToHashSet();
            if (model.PendingRestart is { } pending)
            {
                bool Known(string value) => value is "PENDING" or "RUNNING" or "PROCESSING" or "COMPLETED" or "NEEDS_REVIEW" or "FAILED" or "BLOCKED" or "CANCELLED" or "CANCELED" or "SAVED";
                var complete = status.TotalPages > 0 && status.Pages.Count == status.TotalPages
                    && Enumerable.Range(1, status.TotalPages).All(n => status.Pages.TryGetValue(n, out var value) && Known(value));
                var knownTargets = status.TotalPages > 0 && pending.Pages.Length > 0
                    && pending.Pages.All(n => n >= 1 && n <= status.TotalPages && status.Pages.TryGetValue(n, out var value) && Known(value));
                if (pending.Unconfirmed && !complete || !pending.Unconfirmed && !knownTargets)
                { model.ErrorNotice = "서버가 쪽별 상태를 완전히 제공하지 않았습니다. 재개 상태를 다시 확인하기 전에는 서버 저장을 할 수 없습니다."; return; }
                missing.UnionWith(pending.Pages);
                if (pending.Unconfirmed) missing.UnionWith(status.Pages.Keys);
                model.PendingRestart = missing.Count == 0 ? null : new PendingRestart(false, missing.Order().ToArray());
            }
            _ = MonitorJobAsync(context, status, null, replacePages: missing);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        { context.Model.ErrorNotice = "재개 상태를 확인하지 못했습니다. 로컬 파일을 저장하고 다시 확인해 주세요: " + ex.Message; }
        finally { context.PageSyncInProgress--; }
    }

    private async void CheckPendingRestart_Click(object sender, RoutedEventArgs e)
    {
        if (openDocument is { } context && context.Model.HasPendingRestart && context.PageSyncInProgress == 0) await ReattachAsync(context);
    }

    private async void CreditResume_Click(object sender, RoutedEventArgs e) => await RestartJobAsync(session, retry: false);

    // Shown once per fresh conversion; reopening a saved job does not repeat it. Counts the ■ markers still in the text.
    private async Task ShowUnreadableNoticeAsync(DocumentSession model)
    {
        var count = UnreadableText.Count(model);
        if (count == 0 || resourcesReleased || session != model) return;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (resourcesReleased || session != model) return;
        var notice = FileNotices.UnreadableCharacters(count); notice.Owner = this; notice.ShowDialog();
        if (notice.Choice == 0 && UnreadableText.First(model) is { } first) await NavigateToAsync(new(first.Block, SearchPanel.Text, first.Offset));
        session.Status = $"원본을 읽지 못한 자리 {count}군데 · ■ 를 찾아 직접 입력하세요 (Ctrl+F로 ■ 검색)";
    }

    private static async Task CacheOriginalAsync(DocumentSession model, RemotePage page, CancellationToken ct)
    {
        if (page.OriginalUrl is null)
        {
            if (page.OriginalLines is not null) model.OriginalPages[page.Number] = new("text", null, page.OriginalLines);
            return;
        }
        // Presigned URLs never receive API authorization headers, and are not persisted in .smj.
        using var download = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        try
        {
            using var response = await download.GetAsync(page.OriginalUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > JobClient.MaximumUploadBytes) return;
            await using var source = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream(); var chunk = new byte[81920]; int read;
            while ((read = await source.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + read > JobClient.MaximumUploadBytes) return;
                buffer.Write(chunk, 0, read);
            }
            var name = $"preview-{page.Number}.{(page.OriginalType == "pdf" ? "pdf" : "image")}";
            var data = buffer.ToArray();
            if (page.OriginalType == "pdf") name = model.OriginalFiles.FirstOrDefault(p => p.Key.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) && p.Value.AsSpan().SequenceEqual(data)).Key ?? name;
            model.OriginalFiles[name] = data;
            model.OriginalPages[page.Number] = new(page.OriginalType ?? "image", name, Width: page.Width, Height: page.Height);
        }
        catch (HttpRequestException) { model.ErrorNotice = model.Status = $"{page.Number}쪽 원본을 받지 못했습니다. 작업을 다시 열어 URL을 갱신할 수 있습니다."; }
    }
}
