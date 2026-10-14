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
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value) || !await ConfirmDiscardAsync()) return;
        await LoadJobAsync(dialog.Value.Trim(), "서버 작업.smj");
    }

    private async Task UploadAsync()
    {
        if (fileBusy || !EnsureAccount()) return;
        var dialog = new OpenFileDialog { Title = "AI 변환할 문서 선택", Multiselect = true, Filter = "문서와 이미지|*.pdf;*.hwp;*.hwpx;*.txt;*.jpg;*.jpeg;*.png" };
        if (dialog.ShowDialog(this) != true || !await ConfirmDiscardAsync()) return;
        if (dialog.FileNames.Sum(p => new FileInfo(p).Length) > JobClient.MaximumUploadBytes)
            throw new InvalidDataException("업로드 전체 크기는 100 MB까지입니다.");
        if (MessageBox.Show(this, $"{dialog.FileNames.Length}개 파일을 서버에 업로드해 AI 변환합니다. 성공한 쪽에 크레딧이 사용됩니다.\n\n진행하시겠습니까?", "AI 변환", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        fileBusy = true;
        try
        {
            var files = new List<UploadFile>();
            foreach (var path in dialog.FileNames) files.Add(new(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
            session.Status = "파일 업로드 중…";
            var job = await jobs.UploadAsync(files, session, documentLifetime.Token);
            await LoadJobAsync(job.JobId, Path.GetFileNameWithoutExtension(dialog.FileNames[0]) + ".smj", files);
        }
        finally { fileBusy = false; }
    }

    private async Task LoadJobAsync(string id, string title, IReadOnlyList<UploadFile>? originals = null)
    {
        fileBusy = true;
        var model = new DocumentSession { JobId = id, OwnerLoginId = api.Account!.LoginId, Title = title, JobStatus = "PENDING" };
        try
        {
            // Do not replace the open document if the job is inaccessible.
            await jobs.ReadSettingsAsync(model, documentLifetime.Token);
            var status = await jobs.StatusAsync(id, documentLifetime.Token);
            if (originals is not null)
                foreach (var file in originals) model.OriginalFiles[file.FileName] = file.Bytes;
            model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "변환 대기 중", State = "대기" });
            SetSession(model, null, false);
            var ct = documentLifetime.Token;
            var loadedPages = new HashSet<int>();
            var nextId = 1;
            while (true)
            {
                model.JobStatus = status.OverallStatus;
                foreach (var pair in status.Pages) model.PageStatuses[pair.Key] = pair.Value;
                model.Status = $"AI 변환 {status.CompletedPages}/{status.TotalPages}쪽 · {status.OverallStatus}";
                foreach (var page in status.Pages.Where(p => p.Value is "COMPLETED" or "NEEDS_REVIEW").OrderBy(p => p.Key))
                {
                    if (loadedPages.Contains(page.Key)) continue;
                    var result = await jobs.PageAsync(id, page.Key, nextId, ct);
                    if (result.Blocks.Length == 0) continue;
                    if (loadedPages.Count == 0) { model.ActiveBlock = null; model.Blocks.Clear(); }
                    foreach (var block in result.Blocks) { model.Add(block); nextId = Math.Max(nextId, block.Id + 1); translatedRevisions[block] = block.Braille.Length > 0 ? block.SourceRevision : -1; }
                    loadedPages.Add(page.Key);
                    await CacheOriginalAsync(model, result, ct);
                    foreach (var block in result.Blocks.Where(b => b.Braille.Length == 0))
                    {
                        var outcome = await translator.TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(35), ct);
                        if (outcome == TranslationOutcome.Applied) translatedRevisions[block] = block.SourceRevision;
                    }
                }
                if (status.IsTerminal) break;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                status = await jobs.StatusAsync(id, ct);
            }
            model.IsDirty = true;
            var blocked = status.Pages.Count(p => p.Value is "BLOCKED" or "FAILED");
            model.Status = $"변환 {status.OverallStatus} · 확인할 쪽 {blocked}개 · Ctrl+S로 저장하세요.";
            ShowActiveBlock();
        }
        catch (OperationCanceledException) { }
        finally { fileBusy = false; }
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
            model.OriginalFiles[name] = buffer.ToArray();
            model.OriginalPages[page.Number] = new(page.OriginalType ?? "image", name, Width: page.Width, Height: page.Height);
        }
        catch (HttpRequestException) { model.Status = $"{page.Number}쪽 원본을 받지 못했습니다. 작업을 다시 열어 URL을 갱신할 수 있습니다."; }
    }
}
