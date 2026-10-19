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

    private async Task UploadAsync()
    {
        if (fileBusy || !EnsureAccount()) return;
        var dialog = new OpenFileDialog { Title = "AI 변환할 문서 선택", Multiselect = true, Filter = "문서와 이미지|*.pdf;*.hwp;*.hwpx;*.txt;*.jpg;*.jpeg;*.png" };
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileNames.Sum(p => new FileInfo(p).Length) > JobClient.MaximumUploadBytes)
            throw new InvalidDataException("업로드 전체 크기는 100 MB까지입니다.");
        if (dialog.FileNames.Length == 1 && Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)) { await OpenAsync(dialog.FileName); return; }
        var orderedFiles = dialog.FileNames;
        if (orderedFiles.Length > 1) { var order = new ImageOrderDialog(orderedFiles) { Owner = this }; if (order.ShowDialog() != true) return; orderedFiles = order.Paths; }
        var defaults = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/braille-options", cancellationToken: documentLifetime.Token);
        var settings = new DocumentSession { Options = JobClient.ReadOptions(defaults) };
        if (defaults.TryGetProperty("pageNumberLine", out var pageLine))
            settings.Layout = settings.Layout with { PageNumbers = pageLine.GetString() switch { "none" => "출력 안 함", "every" => "모든 페이지", _ => "홀수 페이지" } };
        var form = new FormDialog(new(api), settings.Layout, settings.Options, defaults.TryGetProperty("formId", out var f) ? f.GetString() : null) { Owner = this };
        if (form.ShowDialog() != true) return;
        if (form.Selected is { } selected) { settings.Options = selected.Options; settings.Layout = selected.Layout; }
        var options = new TranslationOptionsDialog(autoTranslate, settings.Options) { Owner = this };
        if (options.ShowDialog() != true) return;
        settings.Options = options.Result;
        var layout = new LayoutDialog(settings.Layout) { Owner = this };
        if (layout.ShowDialog() != true) return;
        settings.Layout = layout.Result;
        if (MessageBox.Show(this, $"{dialog.FileNames.Length}개 파일을 서버에 업로드해 AI 변환합니다. 성공한 쪽에 크레딧이 사용됩니다.\n\n진행하시겠습니까?", "AI 변환", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        fileBusy = true;
        try
        {
            var files = new List<UploadFile>();
            foreach (var path in orderedFiles) files.Add(new(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
            session.Status = "파일 업로드 중…";
            var job = await jobs.UploadAsync(files, settings, documentLifetime.Token, form.Selected?.FormId);
            await LoadJobAsync(job.JobId, Path.GetFileNameWithoutExtension(dialog.FileNames[0]) + ".smj", files);
        }
        finally { fileBusy = false; }
    }

    private async Task LoadJobAsync(string id, string title, IReadOnlyList<UploadFile>? originals = null, int? restorePage = null)
    {
        var existing = openDocuments.FirstOrDefault(d => d.Model.JobId == id);
        if (existing is not null) { SetSession(existing.Model, existing.Path, existing.ShowOriginal); return; }
        fileBusy = true;
        try
        {
            var model = new DocumentSession { JobId = id, OwnerLoginId = api.Account!.LoginId, Title = title, JobStatus = "PENDING" };
            await jobs.ReadSettingsAsync(model, documentLifetime.Token);
            var status = await jobs.StatusAsync(id, documentLifetime.Token);
            if (originals is not null) foreach (var file in originals) model.OriginalFiles[file.FileName] = file.Bytes;
            model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "변환 대기 중", State = "대기", IsBrailleLocked = true, Metadata = new(IsBlocked: true) });
            SetSession(model, null, false);
            _ = MonitorJobAsync(openDocument!, status, restorePage);
        }
        finally { fileBusy = false; }
    }
    private async Task MonitorJobAsync(OpenDocument context, JobStatus status, int? restorePage)
    {
        var model = context.Model; var ct = context.JobLifetime.Token;
        var jobTranslator = new TranslationCoordinator(sidecar is null ? engine : new LocalBrailleEngine(sidecar, () => model.Options));
        void RememberRevision(EditorBlock b, int revision)
        {
            context.Translated[b] = revision;
            if (session == model) translatedRevisions[b] = revision;
        }
        try
        {
            var loadedPages = new HashSet<int>();
            var waiting = true;
            await using var events = new JobEventWatch(api, model.JobId!, ct);
            var nextId = 1;
            while (true)
            {
                model.JobStatus = status.OverallStatus;
                foreach (var pair in status.Pages) model.PageStatuses[pair.Key] = pair.Value;
                model.Status = $"AI 변환 {status.CompletedPages}/{status.TotalPages}쪽 · {status.OverallStatus}";
                foreach (var page in status.Pages.Where(p => p.Value is "COMPLETED" or "NEEDS_REVIEW" or "FAILED" or "BLOCKED").OrderBy(p => p.Key))
                {
                    if (loadedPages.Contains(page.Key)) continue;
                    var result = await jobs.PageAsync(model.JobId!, page.Key, nextId, ct);
                    await CacheOriginalAsync(model, result, ct);
                    if (result.Blocks.Length == 0 && page.Value is "FAILED" or "BLOCKED")
                        result = result with { Blocks = [new EditorBlock { Id = nextId++, Page = page.Key, Heading = "변환 실패 · 원본 확인", State = page.Value, IsBrailleLocked = true, Metadata = new(IsBlocked: true) }] };
                    if (result.Blocks.Length == 0) { loadedPages.Add(page.Key); continue; }
                    if (waiting) { model.ActiveBlock = null; model.Blocks.Clear(); waiting = false; }
                    foreach (var block in result.Blocks) { model.Add(block); nextId = Math.Max(nextId, block.Id + 1); RememberRevision(block, block.Braille.Length > 0 ? block.SourceRevision : -1); }
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
            var blocked = status.Pages.Count(p => p.Value is "BLOCKED" or "FAILED");
            model.Status = $"변환 {status.OverallStatus} · 확인할 쪽 {blocked}개 · Ctrl+S로 저장하세요.";
            if (session == model) ShowActiveBlock();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { model.Status = "작업 수신을 완료하지 못했습니다. 문서를 닫고 서버 작업에서 다시 열어 주세요: " + ex.Message; }
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
        catch (HttpRequestException) { model.Status = $"{page.Number}쪽 원본을 받지 못했습니다. 작업을 다시 열어 URL을 갱신할 수 있습니다."; }
    }
}
