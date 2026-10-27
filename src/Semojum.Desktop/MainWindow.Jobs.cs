using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
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
    private bool EnsureAccount()
    {
        if (api.Account is not null) return true;
        var result = new AccountDialog(api) { Owner = this }.ShowDialog() == true;
        RefreshAccount(); return result;
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
        if (fileBusy || !EnsureAccount()) return;
        var client = new SettingsClient(api);
        BrailleForm[] forms = []; var defaults = new AccountDefaults(session.Options, null, "odd"); var available = true;
        using var settingsTimeout = CancellationTokenSource.CreateLinkedTokenSource(documentLifetime.Token);
        settingsTimeout.CancelAfter(TimeSpan.FromSeconds(8));
        try { forms = await client.FormsAsync(settingsTimeout.Token); defaults = await client.DefaultsAsync(settingsTimeout.Token); }
        catch (Exception ex) when (ex is HttpRequestException or SemojumApiException || ex is OperationCanceledException && !documentLifetime.IsCancellationRequested) { available = false; }
        var dialog = new OpenDocumentDialog(forms, defaults, available) { Owner = this };
        // Figma 1:2276 retries with the same files; the dialog starts from them while they still exist.
        if (previousPaths is { Length: > 0 } && previousPaths.All(File.Exists)) { try { dialog.SetPaths(previousPaths); } catch (ArgumentException) { } }
        if (dialog.ShowDialog() != true) return;
        var orderedFiles = dialog.Paths;
        var settings = dialog.Settings;
        if (orderedFiles.Length == 1 && Path.GetExtension(orderedFiles[0]).ToLowerInvariant() is ".smj" or ".txt")
        { await OpenAsync(orderedFiles[0], settings, dialog.FormId); return; }
        if (orderedFiles.Length > 1) { var order = new ImageOrderDialog(orderedFiles) { Owner = this }; if (order.ShowDialog() != true) return; orderedFiles = order.Paths; }
        fileBusy = true;
        try
        {
            var files = new List<UploadFile>();
            foreach (var path in orderedFiles) files.Add(new(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
            session.Status = "파일 업로드 중…";
            var job = await jobs.UploadAsync(files, settings, documentLifetime.Token, dialog.FormId);
            await LoadJobAsync(job.JobId, Path.GetFileNameWithoutExtension(orderedFiles[0]) + ".smj", files, uploadSettings: settings);
            if (openDocument?.Model.JobId == job.JobId) openDocument.UploadPaths = orderedFiles;
        }
        finally { fileBusy = false; }
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
    private async Task MonitorJobAsync(OpenDocument context, JobStatus status, int? restorePage, bool applyInitialChoices = false, HashSet<int>? replacePages = null)
    {
        var model = context.Model; var ct = context.JobLifetime.Token;
        model.ErrorNotice = "";
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
            var nextId = replacePages is null ? 1 : model.Blocks.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
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
                    var result = await jobs.PageAsync(model.JobId!, page.Key, nextId, ct);
                    if (applyInitialChoices) foreach (var block in result.Blocks) VisualDraftSelection.ApplyInitial(block, model.Options);
                    JobClient.Remember(model, result);
                    await CacheOriginalAsync(model, result, ct);
                    if (!context.OriginalKindKnown && result.OriginalType is not null)
                    {
                        context.OriginalKindKnown = true;
                        context.ShowOriginal = result.OriginalType != "text";
                        if (session == model) { OriginalToggle.IsChecked = context.ShowOriginal; UpdatePanelLayout(); }
                    }
                    if (result.Blocks.Length == 0 && page.Value is "FAILED" or "BLOCKED")
                        result = result with { Blocks = [new EditorBlock { Id = nextId++, Page = page.Key, Heading = "변환 실패 · 원본 확인", State = page.Value, IsBrailleLocked = true, Metadata = new(IsBlocked: true) }] };
                    if (result.Blocks.Length == 0) { loadedPages.Add(page.Key); continue; }
                    if (waiting) { model.ActiveBlock = null; model.Blocks.Clear(); waiting = false; }
                    int? insertAt = null;
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
                    loadedPages.Add(page.Key);
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
        finally { model.Progress = null; }
    }

    private async Task ShowJobOutcomeAsync(DocumentSession model, JobStatus status)
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
        else if (status.FailedPages > 0)
        {
            if (notice.Choice == 0) await RestartJobAsync(model, retry: true);
        }
        else if (status.SucceededPages == 0)
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
        var ct = openDocument.JobLifetime.Token;
        try
        {
            var restart = retry ? await jobs.RetryAsync(model.JobId, ct) : await jobs.ResumeAsync(model.JobId, ct);
            if (restart.Pages.Length == 0)
            {
                model.Status = retry ? "다시 변환할 쪽이 없습니다. 취소했거나 크레딧 소진으로 멈춘 쪽은 대상이 아닙니다." : "이어서 처리할 쪽이 없습니다.";
                return;
            }
            model.CreditNotice = "";
            model.Status = $"{(retry ? "실패한 쪽 다시 변환" : "이어서 처리")} 요청 · {restart.Pages.Length}쪽 ({string.Join(", ", restart.Pages)}) · 성공한 쪽만 크레딧이 차감됩니다.";
            var status = await jobs.StatusAsync(model.JobId, ct);
            _ = MonitorJobAsync(openDocument, status, null, replacePages: restart.Pages.ToHashSet());
        }
        catch (OperationCanceledException) { }
        catch (SemojumApiException ex)
        {
            var notice = ex.Code == "CREDIT4001" ? FileNotices.NoCredits()
                : new NoticeDialog(retry ? "실패한 쪽을 다시 변환하지 못했습니다." : "이어서 처리하지 못했습니다.", $"{ex.Message} ({ex.Code})", ["닫기"], error: true);
            notice.Owner = this; notice.ShowDialog();
            if (ex.Code == "CREDIT4001" && notice.Choice is 0 or 1) new PublicInquiryDialog(api) { Owner = this }.ShowDialog();
        }
    }

    // After a dropped connection: read the job status again and fetch only the pages this window never received.
    private async Task ReattachAsync(OpenDocument context)
    {
        if (resourcesReleased || session != context.Model || context.Model.JobId is null) return;
        var status = await jobs.StatusAsync(context.Model.JobId, context.JobLifetime.Token);
        var missing = status.Pages.Keys.Where(p => !context.Model.Blocks.Any(b => b.Page == p && b.ServerId is not null)).ToHashSet();
        _ = MonitorJobAsync(context, status, null, replacePages: missing);
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
