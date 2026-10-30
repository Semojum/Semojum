using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private readonly IAccountCredentialStore accountCredentials = new WindowsAccountCredentialStore();
    private string? automaticLoginMessage;
    private string WorkInformation() => $"세모점 {CurrentVersion.ToString(3)}\n운영체제: {Environment.OSVersion}\n작업 ID: {session.JobId ?? "로컬 초안"}\n상태: {session.JobStatus}\n원본 쪽: {session.ActiveBlock?.Page}\n블록 수: {session.Blocks.Count}\n판면: {session.Layout.Columns}칸 × {session.Layout.Lines}줄\n점역 오류 블록: {string.Join(", ", session.Blocks.Where(b => b.IsBrailleLocked).Select(b => b.Id))}";
    private AppVersionInfo? availableVersion;
    private bool downloadingUpdate;
    private bool accountStarting;
    internal Func<AppVersionInfo, IProgress<string>, CancellationToken, Task>? UpdateDownloadForChecks { get; set; }
    internal Func<string, MessageBoxResult>? UpdateConfirmationForChecks { get; set; }
    internal Action<string>? UpdateRequiredNoticeForChecks { get; set; }
    internal Func<Version, Version, Task<bool>>? UpdateReadyForChecks { get; set; }
    internal Func<Version, string[], Task<bool>>? UpdateActivateForChecks { get; set; }
    internal Func<Version, string[], Task<Func<bool>?>>? UpdatePrepareForChecks { get; set; }
    private UpdateReadyDialog? updateReadyDialog;
    private readonly CancellationTokenSource appLifetime = new();
    private string updateStatus = "";
    private async Task DownloadUpdateAsync()
    {
        if ((smoke && UpdateDownloadForChecks is null) || availableVersion is null || downloadingUpdate) return;
        if (availableVersion.DownloadUrl is null) { updateStatus = "업데이트 다운로드 주소가 아직 등록되지 않았습니다. 관리자에게 문의해 주세요."; return; }
        downloadingUpdate = true;
        try
        {
            var version = availableVersion;
            await (UpdateDownloadForChecks ?? AppUpdates.DownloadAsync)(version, new Progress<string>(s => { updateStatus = s; session.Status = s; }), appLifetime.Token);
            if (!version.Required(CurrentVersion) && (!smoke || UpdateReadyForChecks is not null)
                && await (UpdateReadyForChecks ?? AppUpdates.IsReadyAsync)(CurrentVersion, version.Latest)
                && !resourcesReleased && !appLifetime.IsCancellationRequested)
                ShowUpdateReady();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { updateStatus = "업데이트를 준비하지 못했습니다: " + ex.Message; session.Status = updateStatus; }
        finally { downloadingUpdate = false; }
    }
    private void ShowUpdateReady()
    {
        if (updateReadyDialog is { IsVisible: true }) { updateReadyDialog.Activate(); return; }
        var dialog = new UpdateReadyDialog(RestartIntoUpdateAsync) { Owner = this };
        updateReadyDialog = dialog; dialog.Closed += (_, _) => { if (ReferenceEquals(updateReadyDialog, dialog)) updateReadyDialog = null; };
        dialog.Show();
    }
    private async Task<bool> RestartIntoUpdateAsync()
    {
        if (!IsEnabled || resourcesReleased) return false;
        CompleteSourceInput();
        var locked = new[] { this }.Concat(OwnedWindows.Cast<Window>().Where(w => w != updateReadyDialog))
            .Select(w => (Window: w, WasEnabled: w.IsEnabled)).ToArray();
        foreach (var item in locked) item.Window.IsEnabled = false;
        try
        {
            var previous = openDocument;
            var confirmed = new List<(OpenDocument Context, long Version)>();
            foreach (var context in openDocuments.ToArray())
            {
                SetSession(context.Model, context.Path, context.ShowOriginal);
                if (!await ConfirmDiscardAsync()) return false;
                confirmed.Add((context, context.Model.ChangeVersion));
            }
            if (previous is not null && openDocuments.Contains(previous)) SetSession(previous.Model, previous.Path, previous.ShowOriginal);
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            Func<bool>? launch;
            if (UpdatePrepareForChecks is not null) launch = await UpdatePrepareForChecks(CurrentVersion, args);
            else if (UpdateActivateForChecks is not null)
            {
                var result = await UpdateActivateForChecks(CurrentVersion, args);
                launch = result ? () => true : null;
            }
            else launch = await AppUpdates.PrepareActivationAsync(CurrentVersion, args);
            if (launch is null)
            { session.Status = "준비된 업데이트를 확인할 수 없습니다. 버전 정보에서 다시 확인해 주세요."; return false; }
            if (resourcesReleased || openDocuments.Count != confirmed.Count || confirmed.Any(c => !openDocuments.Contains(c.Context) || c.Context.Model.ChangeVersion != c.Version))
            { session.Status = "업데이트를 확인하는 동안 문서가 바뀌었습니다. 변경 내용을 확인한 뒤 다시 시작해 주세요."; return false; }
            if (!launch())
            { session.Status = "업데이트를 시작하지 못했습니다. 버전 정보에서 다시 확인해 주세요."; return false; }
            allowClose = true; Close(); return true;
        }
        finally
        {
            if (!resourcesReleased) foreach (var item in locked) item.Window.IsEnabled = item.WasEnabled;
        }
    }
    internal static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new(1, 0, 0);
    private async Task StartAccountAsync()
    {
        accountStarting = true;
        var content = (UIElement)Content; content.IsEnabled = false;
        var windowWasEnabled = IsEnabled; IsEnabled = false;
        try
        {
            // Missing/unreachable version metadata must not prevent sign-in.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { if (preferences.AutoCheckUpdates) availableVersion = AppVersionInfo.Read(await api.GetPublicAsync("/api/public/app-version", timeout.Token)); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or SemojumApiException or System.IO.InvalidDataException or JsonException or KeyNotFoundException) { }
            if (availableVersion?.Required(CurrentVersion) == true)
            {
                await DownloadUpdateAsync();
                var notice = "필수 업데이트가 필요합니다.\n" + updateStatus;
                if (UpdateRequiredNoticeForChecks is { } showNotice) showNotice(notice); else MessageBox.Show(this, notice, "업데이트");
                if (!await ConfirmAllDocumentsAsync())
                {
                    session.Status = "필수 업데이트가 필요합니다. 작업을 저장한 뒤 다시 실행해 주세요.";
                    return;
                }
                allowClose = true; Close(); return;
            }
            await TryAutomaticLoginAsync();
            if (!EnsureAccount(allowInstitution: true)) { allowClose = true; Close(); return; }
            LoadAccountPreferences();
            if (preferences.WorkFolder is not null) await OpenWorkFolderAsync(false);
            await RestoreActiveJobAsync();
            if (availableVersion?.Available(CurrentVersion) == true)
                session.Status = $"새 버전 {availableVersion.Latest}이 있습니다. 버전 정보에서 확인해 주세요.";
        }
        catch (Exception ex) { ShowError(ex); }
        finally { accountStarting = false; IsEnabled = windowWasEnabled; content.IsEnabled = true; }
    }
    private async Task TryAutomaticLoginAsync()
    {
        if (smoke || api.Account is not null) return;
        var result = await AutomaticLogin.RestoreAsync(api, accountCredentials);
        automaticLoginMessage = result.Message;
        if (result.Account is not null) RefreshAccount();
    }
    private void ShowVersion()
    {
        var message = $"세모점 {CurrentVersion.ToString(3)}\n";
        if (availableVersion is not { } info) { MessageBox.Show(this, message + "버전 정보가 등록되지 않았거나 서버에 연결할 수 없습니다.", "버전 정보"); return; }
        message += $"최신 버전: {info.Latest}\n{info.ReleaseNotes}\n{updateStatus}";
        if (info.Required(CurrentVersion)) message += "\n이 버전은 더 이상 지원되지 않습니다. 업데이트 후 실행해 주세요.";
        if (!info.Available(CurrentVersion)) { MessageBox.Show(this, message + "\n최신 버전입니다.", "버전 정보"); return; }
        if (info.DownloadUrl is null) { MessageBox.Show(this, message + "\n업데이트 다운로드 주소가 아직 등록되지 않았습니다. 관리자에게 문의해 주세요.", "버전 정보"); return; }
        var question = message + "\n\n업데이트를 다운로드하시겠습니까?";
        if ((UpdateConfirmationForChecks?.Invoke(question) ?? MessageBox.Show(this, question, "버전 정보", MessageBoxButton.YesNo)) == MessageBoxResult.Yes)
            _ = DownloadUpdateAsync();
    }
    private async Task CheckVersionAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        availableVersion = AppVersionInfo.Read(await api.GetPublicAsync("/api/public/app-version", timeout.Token));
        ShowVersion();
    }
    internal Func<Task>? LogoutForChecks { get; set; }
    internal Action<string>? LogoutCredentialErrorForChecks { get; set; }
    private async Task LogoutAccountAsync()
    {
        if (!IsEnabled || resourcesReleased) return;
        CompleteSourceInput();
        var locked = new[] { this }.Concat(OwnedWindows.Cast<Window>())
            .Select(w => (Window: w, WasEnabled: w.IsEnabled)).ToArray();
        foreach (var item in locked) item.Window.IsEnabled = false;
        try
        {
            var previous = openDocument;
            var confirmed = new List<(OpenDocument Context, long Version)>();
            foreach (var context in openDocuments.ToArray())
            {
                SetSession(context.Model, context.Path, context.ShowOriginal);
                if (!await ConfirmDiscardAsync()) return;
                confirmed.Add((context, context.Model.ChangeVersion));
            }
            bool DocumentsUnchanged() => openDocuments.Count == confirmed.Count
                && confirmed.All(c => openDocuments.Contains(c.Context) && c.Context.Model.ChangeVersion == c.Version);
            if (!DocumentsUnchanged())
            { session.Status = "로그아웃 확인 중 문서가 바뀌었습니다. 내용을 확인한 뒤 다시 시도해 주세요."; return; }
            if (previous is not null && openDocuments.Contains(previous)) SetSession(previous.Model, previous.Path, previous.ShowOriginal);
            try
            {
                if (LogoutForChecks is not null) await LogoutForChecks();
                else if (smoke) await api.LogoutAsync();
                else await AutomaticLogin.LogoutAsync(api, accountCredentials);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
            {
                var message = "저장된 로그인 정보를 삭제하지 못해 로그아웃하지 않았습니다.\n" + ex.Message;
                if (LogoutCredentialErrorForChecks is { } report) report(message); else MessageBox.Show(this, message, "로그아웃");
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or SemojumApiException) { } // Local credentials are already cleared, even if remote revocation is unavailable.
            finally { RefreshAccount(); }
            if (resourcesReleased) return;
            if (!DocumentsUnchanged())
            { session.Status = "로그아웃 중 문서가 바뀌어 열어 둔 내용을 유지했습니다. 저장하려면 다시 로그인해 주세요."; return; }
            foreach (var d in openDocuments.ToArray()) RemoveDocument(d);
            SetSession(BlankDocument(), null, false);
            if (!EnsureAccount(allowInstitution: true)) { allowClose = true; Close(); return; }
            LoadAccountPreferences();
        }
        finally
        {
            if (!resourcesReleased) foreach (var item in locked) item.Window.IsEnabled = item.WasEnabled;
        }
    }
    private async Task NewTextDocumentAsync(string text = "", string title = "제목 없음.smj")
    {
        if (fileBusy || !smoke && !EnsureAccount()) return;
        var model = BlankDocument(); model.Title = title; model.ActiveBlock!.Source = text;
        await ConfigureTextJobAsync(model);
        model.IsDirty = text.Length > 0;
        SetSession(model, null, true, showUpload: true);
        session.Status = "새 텍스트 작업 · AI·크레딧 사용 없음 · Ctrl+S로 저장";
    }
    private async Task ConfigureTextJobAsync(DocumentSession model, DocumentSession? selectedSettings = null, string? formId = null)
    {
        model.IsTextJob = true;
        if (!smoke)
        {
            if (selectedSettings is null)
            {
                var defaults = await new SettingsClient(api).DefaultsAsync();
                model.Options = defaults.Options; formId = defaults.FormId;
                model.Layout = model.Layout with { PageNumbers = defaults.PageNumberLine == "every" ? "모든 페이지" : "홀수 페이지" };
            }
            else { model.Options = selectedSettings.Options; model.Layout = selectedSettings.Layout; }
            var created = await jobs.CreateTextAsync(System.IO.Path.GetFileNameWithoutExtension(model.Title), model, formId: formId);
            model.JobId = created.JobId; model.OwnerLoginId = api.Account!.LoginId;
            await jobs.ReadSettingsAsync(model, uploadSettings: selectedSettings);
        }
    }
    private async Task RestoreActiveJobAsync()
    {
        var active = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/jobs/active");
        if (active.ValueKind != JsonValueKind.Array || active.GetArrayLength() == 0) return;
        var item = active.EnumerateArray().OrderByDescending(x => x.TryGetProperty("lastModifiedAt", out var t) ? t.GetString() : "").First();
        var id = item.GetProperty("jobId").GetString()!;
        var page = item.TryGetProperty("lastEditedPage", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var n) ? Math.Max(1, n) : 1;
        await LoadJobAsync(id, (item.GetProperty("originalFileName").GetString() ?? "복구 작업") + ".smj", restorePage: page);
    }
}
