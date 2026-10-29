using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsAccountChecks
{
    private sealed class MemoryStore : IAccountCredentialStore
    {
        public SessionCredential? Value = new("editor", "r");
        public bool FailDelete;
        public bool FailWrite;
        public SessionCredential? Read() => Value;
        public void Write(SessionCredential credential) { if (FailWrite) throw new System.IO.IOException("fixture store unavailable"); Value = credential; }
        public void Delete() { if (FailDelete) throw new System.IO.IOException("fixture store unavailable"); Value = null; }
        public void DeleteIfMatches(SessionCredential credential) { if (Value == credential) Delete(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var store = new MemoryStore();
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fresh\",\"loginId\":\"editor\",\"role\":\"ROLE_USER\",\"alias\":\"현재 이름\"}}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is { LoginId: "editor", Role: "ROLE_USER", Alias: "현재 이름" } && store.Value is not null, "Saved session restores current server identity");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(_ => throw new HttpRequestException("offline"))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is null && result.Message!.Contains("연결") && store.Value is not null && api.Account is null, "Offline automatic login reports connectivity and retains saved credential");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is null && result.Message!.Contains("만료") && store.Value is null && api.Account is null, "Expired credential is removed and asks user to sign in");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fresh\"}}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is null && result.Message!.Contains("권한") && store.Value is not null && api.Account is null, "Old server cannot grant an unverified role");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(req => Task.FromResult(req.RequestUri!.AbsolutePath.EndsWith("logout")
            ? Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":null}")
            : Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fresh\",\"loginId\":\"editor\",\"role\":\"ROLE_ADMIN\",\"adminScope\":\"WEB\"}}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is null && result.Message!.Contains("사용할 수 없습니다") && store.Value is null && api.Account is null, "Changed web-only account cannot enter the desktop editor");
        }
        store = new MemoryStore();
        store.Write(new("another-user", "another-refresh"));
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fresh\",\"loginId\":\"another-user\",\"role\":\"ROLE_USER\"}}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account?.LoginId == "another-user" && store.Value?.LoginId == "another-user", "A different successful login replaces the remembered account");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(req => req.RequestUri!.AbsolutePath.EndsWith("login")
            ? Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}"))
            : throw new HttpRequestException("offline"))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            await AutomaticLogin.LogoutAsync(api, store);
            check(api.Account is null && store.Value is null, "Offline logout removes local credentials and signs out");
        }
        store = new MemoryStore { FailDelete = true };
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}")))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            try { await AutomaticLogin.LogoutAsync(api, store); throw new Exception("Expected credential delete failure"); }
            catch (System.IO.IOException) { }
            check(api.Account?.LoginId == "editor" && store.Value is not null, "Failed credential deletion does not falsely report logout");
        }
        store = new MemoryStore { Value = new("another-user", "another-refresh") };
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}")))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            await AutomaticLogin.LogoutAsync(api, store);
            check(api.Account is null && store.Value?.LoginId == "another-user", "Signing out an older account preserves a newer remembered account");
        }
        store = new MemoryStore { Value = new("another-user", "another-refresh") };
        using (var http = new HttpClient(new Handler(_ => throw new Exception("Anonymous logout must not call server"))))
        {
            var api = new SemojumApiClient(http); await AutomaticLogin.LogoutAsync(api, store);
            check(store.Value?.LoginId == "another-user", "Signing out after session expiry preserves another window's remembered account");
        }
        store = new MemoryStore();
        using (var http = new HttpClient(new Handler(req => Task.FromResult(req.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}")
            : Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"AUTH4004\",\"message\":\"inactive\"}")))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            try { await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage"); } catch (SemojumApiException) { }
            await AutomaticLogin.LogoutAsync(api, store);
            check(api.Account is null && store.Value is null, "Explicit logout removes this window's matching credential after the session is cleared");
        }
        store = new MemoryStore { Value = new("another-user", "another-refresh") };
        using (var http = new HttpClient(new Handler(req => Task.FromResult(req.RequestUri!.AbsolutePath.EndsWith("login")
            ? Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}")
            : Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"AUTH4004\",\"message\":\"inactive\"}")))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            try { await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage"); } catch (SemojumApiException) { }
            await AutomaticLogin.LogoutAsync(api, store);
            check(store.Value?.LoginId == "another-user", "Explicit logout after expiry retains a different window's credential");
        }
        store = new MemoryStore { Value = new("another-user", "another-refresh"), FailWrite = true };
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}")))))
        {
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
            var message = await AutomaticLogin.RememberAsync(api, store);
            check(api.Account is null && store.Value?.LoginId == "another-user" && message?.Contains("저장") == true,
                "Credential write failure cancels the new login and preserves another remembered account");
        }
    }

    internal static void CheckLoginNetworkMessage(Window owner, Action<bool, string> check)
    {
        using var http = new HttpClient(new Handler(_ => throw new HttpRequestException("fixture network detail")));
        var dialog = new AccountDialog(new SemojumApiClient(http)) { Owner = owner };
        var fields = (StackPanel)dialog.Content;
        var login = fields.Children.OfType<TextBox>().Single();
        var password = fields.Children.OfType<PasswordBox>().Single();
        var submit = fields.Children.OfType<Button>().Single(b => Equals(b.Content, "로그인"));
        var observed = "";
        _ = dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            login.Text = "fixture-user"; password.Password = "fixture-password";
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                observed = string.Join(" ", fields.Children.OfType<TextBlock>().Select(t => t.Text));
                dialog.Close();
            };
            timer.Start();
        }));
        dialog.ShowDialog();
        check(observed.Contains("서버") && observed.Contains("연결"), "Login window explains a network error without treating it as bad credentials");
    }

    internal static async Task CheckLogoutCancellationAsync(Action<bool, string> check)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}"))));
        var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
        var window = new MainWindow(api);
        window.Show();
        try
        {
            var model = window.Session;
            model.ActiveBlock!.Source = "아직 저장하지 않은 내용";
            _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                Application.Current.Windows.OfType<NoticeDialog>().Single().Close()));
            await window.ExecuteAsync("logout");
            check(ReferenceEquals(model, window.Session) && model.ActiveBlock!.Source == "아직 저장하지 않은 내용" && model.IsDirty && api.Account?.LoginId == "editor",
                "Cancelling logout preserves the signed-in account and unsaved document");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    internal static async Task CheckRoleChangeAsync(Action<bool, string> check)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"old\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}"));
            if (path.EndsWith("/refresh")) return Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"new\",\"loginId\":\"editor\",\"role\":\"ROLE_ORG_ADMIN\"}}"));
            calls++;
            return Task.FromResult(Json(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}"));
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
        var window = new MainWindow(api); window.Show();
        var reportedRoleChange = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (notice is null) return;
            reportedRoleChange = notice.DetailText.Contains("권한"); notice.Close();
        };
        try
        {
            var model = window.Session; model.JobId = "fixture-job";
            model.ActiveBlock!.Source = "저장하지 않은 교정";
            timer.Start(); await window.ExecuteAsync("cancelJob");
            check(reportedRoleChange && calls == 1 && api.Account is null && ReferenceEquals(model, window.Session)
                && model.IsDirty && model.ActiveBlock!.Source == "저장하지 않은 교정",
                "Changed role stops request retry and preserves unsaved editor contents");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
