using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private string WorkInformation() => $"세모점 {CurrentVersion.ToString(3)}\n운영체제: {Environment.OSVersion}\n작업 ID: {session.JobId ?? "로컬 초안"}\n상태: {session.JobStatus}\n원본 쪽: {session.ActiveBlock?.Page}\n블록 수: {session.Blocks.Count}\n판면: {session.Layout.Columns}칸 × {session.Layout.Lines}줄\n점역 오류 블록: {string.Join(", ", session.Blocks.Where(b => b.IsBrailleLocked).Select(b => b.Id))}";
    private AppVersionInfo? availableVersion;
    internal static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new(1, 0, 0);
    private async Task StartAccountAsync()
    {
        var content = (UIElement)Content; content.IsEnabled = false;
        try
        {
            // Missing/unreachable version metadata must not prevent sign-in.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { availableVersion = AppVersionInfo.Read(await api.GetPublicAsync("/api/public/app-version", timeout.Token)); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or SemojumApiException or System.IO.InvalidDataException or JsonException or KeyNotFoundException) { }
            if (availableVersion?.Required(CurrentVersion) == true)
            {
                ShowVersion(); allowClose = true; Close(); return;
            }
            if (!EnsureAccount()) { allowClose = true; Close(); return; }
            LoadAccountPreferences();
            if (availableVersion?.Available(CurrentVersion) == true) session.Status = $"새 버전 {availableVersion.Latest}이 있습니다. 정보 → 버전 정보에서 받을 수 있습니다.";
            if (preferences.WorkFolder is not null) await OpenWorkFolderAsync(false);
            await RestoreActiveJobAsync();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { content.IsEnabled = true; }
    }
    private void ShowVersion()
    {
        var message = $"세모점 {CurrentVersion.ToString(3)}\n";
        if (availableVersion is not { } info) { MessageBox.Show(this, message + "버전 정보가 등록되지 않았거나 서버에 연결할 수 없습니다.", "버전 정보"); return; }
        message += $"최신 버전: {info.Latest}\n{info.ReleaseNotes}";
        if (info.Required(CurrentVersion)) message += "\n이 버전은 더 이상 지원되지 않습니다. 업데이트 후 실행해 주세요.";
        if (MessageBox.Show(this, message + "\n\n다운로드 페이지를 여시겠습니까?", "버전 정보", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            Process.Start(new ProcessStartInfo(info.DownloadUrl.AbsoluteUri) { UseShellExecute = true });
    }
    private async Task LogoutAccountAsync()
    {
        if (!await ConfirmAllDocumentsAsync()) return;
        try { await api.LogoutAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or SemojumApiException) { } // Local credentials are already cleared, even if remote revocation is unavailable.
        finally { RefreshAccount(); }
        foreach (var d in openDocuments.ToArray()) RemoveDocument(d);
        SetSession(BlankDocument(), null, false);
        if (!EnsureAccount()) { allowClose = true; Close(); return; }
        LoadAccountPreferences();
    }
    private async Task NewTextDocumentAsync(string text = "", string title = "제목 없음.smj")
    {
        if (fileBusy || !smoke && !EnsureAccount()) return;
        var model = BlankDocument(); model.Title = title; model.ActiveBlock!.Source = text;
        if (!smoke)
        {
            var defaults = await new SettingsClient(api).DefaultsAsync();
            model.Options = defaults.Options;
            model.Layout = model.Layout with { PageNumbers = defaults.PageNumberLine == "every" ? "모든 페이지" : "홀수 페이지" };
            var created = await jobs.CreateTextAsync(System.IO.Path.GetFileNameWithoutExtension(title), model, formId: defaults.FormId);
            model.JobId = created.JobId; model.OwnerLoginId = api.Account!.LoginId;
            await jobs.ReadSettingsAsync(model);
        }
        model.IsDirty = text.Length > 0;
        SetSession(model, null, false); OriginalToggle.IsChecked = false; UpdatePanelLayout();
        session.Status = "새 텍스트 작업 · AI·크레딧 사용 없음 · Ctrl+S로 저장";
    }
    private async Task RestoreActiveJobAsync()
    {
        var active = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/jobs/active");
        if (active.ValueKind != JsonValueKind.Array || active.GetArrayLength() == 0) return;
        var item = active.EnumerateArray().OrderByDescending(x => x.TryGetProperty("lastModifiedAt", out var t) ? t.GetString() : "").First();
        var id = item.GetProperty("jobId").GetString()!;
        var page = item.TryGetProperty("lastEditedPage", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? Math.Max(1, n) : 1;
        await LoadJobAsync(id, (item.GetProperty("originalFileName").GetString() ?? "복구 작업") + ".smj");
        if (session.Blocks.FirstOrDefault(b => b.Page == page) is { } block) session.ActiveBlock = block;
    }
}
