using System.Net;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsUpdateSafetyChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var result = path.EndsWith("/login") ? "{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}"
                : path.EndsWith("/app-version") ? "{\"latestVersion\":\"99.0.0\",\"minSupportedVersion\":\"0.0.1\",\"downloadUrl\":\"https://fixture.invalid/update.zip\"}"
                : path.EndsWith("/jobs/active") ? "[]" : throw new InvalidOperationException("Unexpected fixture request: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var http = new HttpClient(new Handler());
        var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture");
        var window = new MainWindow(api); window.Show();
        var calls = 0;
        window.UpdateDownloadForChecks = (_, _, _) => { calls++; throw new IOException("fixture update failure"); };
        try
        {
            var model = window.Session; model.ActiveBlock!.Source = "저장하지 않은 업데이트 검증 내용";
            var startup = typeof(MainWindow).GetMethod("StartAccountAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)startup.Invoke(window, null)!;
            bool Preserved() => ReferenceEquals(model, window.Session) && model.IsDirty && model.ActiveBlock!.Source == "저장하지 않은 업데이트 검증 내용";
            check(calls == 0 && Preserved() && model.Status.Contains("새 버전"), "Startup announces the optional update and preserves unsaved edits without downloading");
            window.UpdateConfirmationForChecks = _ => MessageBoxResult.No;
            await window.ExecuteAsync("version");
            check(calls == 0 && Preserved(), "Declining an optional update does not start a download or change unsaved edits");
            window.UpdateConfirmationForChecks = _ => MessageBoxResult.Yes;
            await window.ExecuteAsync("version");
            check(calls == 1 && Preserved() && model.Status.Contains("업데이트를 준비하지 못했습니다"),
                "A failed optional update reports the failure and keeps unsaved edits");
            window.UpdateDownloadForChecks = (_, _, _) => { calls++; return Task.CompletedTask; };
            await window.ExecuteAsync("version");
            check(calls == 2 && Preserved(), "Accepting an optional update requests a download without closing or changing the document");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
