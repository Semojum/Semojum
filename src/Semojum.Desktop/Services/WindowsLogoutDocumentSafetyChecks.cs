using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsLogoutDocumentSafetyChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        await DelayedLogoutAsync(check, addTab: false);
        await DelayedLogoutAsync(check, addTab: true);
        await CancelConfirmationAsync(check);
        await CredentialFailureAsync(check);
        await ExpiredSessionAsync(check);
    }
    private static async Task WaitAsync(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(4);
        while (!done() && DateTime.UtcNow < until) await Task.Delay(10);
        if (!done()) throw new TimeoutException("Logout safety fixture did not reach its expected state.");
    }
    private static Task Logout(MainWindow window) => (Task)typeof(MainWindow).GetMethod("LogoutAccountAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static async Task<(MainWindow Window, SemojumApiClient Api, MemoryStore Store, Handler Handler, HttpClient Http)> CreateAsync()
    {
        var handler = new Handler(); var http = new HttpClient(handler); var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture");
        var store = new MemoryStore { Value = api.GetSessionCredential() };
        var window = new MainWindow(api); window.Show();
        window.LogoutForChecks = () => AutomaticLogin.LogoutAsync(api, store);
        return (window, api, store, handler, http);
    }
    private static async Task CloseAsync(MainWindow window, HttpClient http)
    { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); http.Dispose(); }
    private static async Task DelayedLogoutAsync(Action<bool, string> check, bool addTab)
    {
        var (window, api, store, handler, http) = await CreateAsync();
        var disabled = new Window { Owner = window, IsEnabled = false }; disabled.Show(); window.ToggleDetached(); var detached = window.DetachedWindow!;
        try
        {
            var document = window.Session; var block = document.ActiveBlock!; block.Source = "보존할 원문"; block.Braille = "⠫"; document.IsDirty = false;
            handler.FailLogout = !addTab;
            var pending = Logout(window); await WaitAsync(() => handler.LogoutStarted);
            check(!window.IsEnabled && !window.PageEditor.Editor.IsEnabled && !detached.IsEnabled && !disabled.IsEnabled,
                "Logout confirmation and delayed network request lock both editor windows");
            if (addTab)
            {
                var create = typeof(MainWindow).GetMethod("NewTextDocumentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)create.Invoke(window, ["추가 탭 내용", "새 문서.smj"])!;
            }
            else block.Source = "대기 중 변경된 원문";
            handler.Gate.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(4));
            check(window.IsVisible && api.Account is null && store.Value is null && window.DocumentTabs.Items.Count == (addTab ? 2 : 1)
                && (addTab ? window.Session.ActiveBlock!.Source == "추가 탭 내용" : document.IsDirty && block.Source == "대기 중 변경된 원문" && block.Braille == "⠫"),
                addTab ? "A new tab during logout is retained after logout completes" : "A network failure and background document change preserve the unsaved document after local logout");
            check(window.IsEnabled && detached.IsEnabled && !disabled.IsEnabled, "Logout cancellation restores each owner's prior enabled state");
        }
        finally { handler.Gate.TrySetResult(); disabled.Close(); await CloseAsync(window, http); }
    }
    private static async Task CancelConfirmationAsync(Action<bool, string> check)
    {
        var (window, api, store, handler, http) = await CreateAsync();
        var model = window.Session; model.ActiveBlock!.Source = "저장 취소 보존"; var choice = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText.Contains("저장")) is not { } prompt) return;
            choice = true; timer.Stop(); prompt.ActionButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            timer.Start(); await Logout(window).WaitAsync(TimeSpan.FromSeconds(4));
            check(choice && window.IsEnabled && ReferenceEquals(window.Session, model) && model.IsDirty && api.Account is not null && store.Value is not null && !handler.LogoutStarted,
                "Canceling save confirmation preserves the document and authenticated session");
        }
        finally { timer.Stop(); handler.Gate.TrySetResult(); await CloseAsync(window, http); }
    }
    private static async Task CredentialFailureAsync(Action<bool, string> check)
    {
        var (window, api, store, handler, http) = await CreateAsync(); store.FailDelete = true;
        var model = window.Session; model.ActiveBlock!.Source = "보존"; model.IsDirty = false; string? message = null;
        window.LogoutCredentialErrorForChecks = value => message = value;
        try
        {
            await Logout(window).WaitAsync(TimeSpan.FromSeconds(4));
            check(message is not null && window.IsEnabled && ReferenceEquals(window.Session, model) && model.ActiveBlock!.Source == "보존"
                && api.Account is not null && store.Value is not null && !handler.LogoutStarted,
                "A credential deletion failure keeps the document and current login intact");
        }
        finally { handler.Gate.TrySetResult(); await CloseAsync(window, http); }
    }
    private static async Task ExpiredSessionAsync(Action<bool, string> check)
    {
        var (window, api, store, handler, http) = await CreateAsync();
        try
        {
            try { await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/fixture/expired"); }
            catch (SemojumApiException ex) when (ex.Code == "AUTH4004") { }
            var model = window.Session; model.ActiveBlock!.Source = "기존 원문"; model.IsDirty = false;
            window.LogoutForChecks = async () =>
            {
                await AutomaticLogin.LogoutAsync(api, store);
                model.ActiveBlock!.Source = "만료 후 변경 내용";
            };
            await Logout(window).WaitAsync(TimeSpan.FromSeconds(4));
            check(api.Account is null && store.Value is null && ReferenceEquals(window.Session, model) && model.IsDirty
                && model.ActiveBlock!.Source == "만료 후 변경 내용" && window.IsEnabled,
                "An expired session removes only its remembered credential and preserves a changed document");
        }
        finally { handler.Gate.TrySetResult(); await CloseAsync(window, http); }
    }
    private sealed class MemoryStore : IAccountCredentialStore
    {
        internal SessionCredential? Value; internal bool FailDelete;
        public SessionCredential? Read() => Value;
        public void Write(SessionCredential credential) => Value = credential;
        public void Delete() { if (FailDelete) throw new IOException("fixture credential error"); Value = null; }
        public void DeleteIfMatches(SessionCredential credential) { if (Value == credential) Delete(); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool LogoutStarted, FailLogout;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/login") return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}");
            if (path == "/api/fixture/expired") return Reply(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4004\"}");
            if (path == "/api/auth/logout")
            {
                LogoutStarted = true; await Gate.Task.WaitAsync(ct);
                if (FailLogout) throw new HttpRequestException("fixture network error");
                return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":null}");
            }
            return Reply(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":null}");
        }
        private static HttpResponseMessage Reply(HttpStatusCode status, string value) => new(status) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    }
}
