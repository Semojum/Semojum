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
        store = new MemoryStore { Value = new("institution", "r") };
        using (var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fresh\",\"loginId\":\"institution\",\"role\":\"ROLE_ORG_ADMIN\"}}")))))
        {
            var api = new SemojumApiClient(http); var result = await AutomaticLogin.RestoreAsync(api, store);
            check(result.Account is { Role: "ROLE_ORG_ADMIN" } && store.Value is not null && !AutomaticLogin.IsEditorAllowed(result.Account),
                "Institution account restores for the desktop institution area without granting editor access");
            var editor = new AccountIdentity("institution", "ROLE_USER", null);
            var institution = result.Account!;
            check(!AutomaticLogin.IsSameDesktopArea(editor, institution) && !AutomaticLogin.IsSameDesktopArea(institution, editor)
                && AutomaticLogin.IsSameDesktopArea(institution, institution) && AutomaticLogin.IsSameDesktopArea(editor, editor),
                "A refreshed identity cannot switch between the editor and institution area");
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
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        var observed = "";
        Exception? callbackFailure = null; var timedOut = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timeout.Stop(); timedOut = true; if (dialog.IsVisible) dialog.Close(); };
        _ = dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var controls = Descendants(dialog).ToArray();
                var login = controls.OfType<TextBox>().Single();
                var password = controls.OfType<PasswordBox>().Single();
                var submit = controls.OfType<Button>().Single(b => Equals(b.Content, "로그인"));
                login.Text = "fixture-user"; password.Password = "fixture-password";
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (!dialog.IsVisible) return;
                    observed = string.Join(" ", Descendants(dialog).OfType<TextBlock>().Select(t => t.Text));
                    dialog.Close();
                };
                timer.Start();
            }
            catch (Exception ex) { callbackFailure = ex; if (dialog.IsVisible) dialog.Close(); }
        }));
        timeout.Start();
        try { dialog.ShowDialog(); }
        finally { timeout.Stop(); }
        if (callbackFailure is not null) throw new InvalidOperationException("Login network fixture interaction failed", callbackFailure);
        if (timedOut) throw new TimeoutException("Login network fixture did not close within 5 seconds");
        check(observed.Contains("서버") && observed.Contains("연결"), "Login window explains a network error without treating it as bad credentials");
    }

    internal static void CheckInstitutionLoginRouting(Window owner, Action<bool, string> check)
    {
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
        foreach (var allowInstitution in new[] { false, true })
        {
            using var http = new HttpClient(new Handler(req => Task.FromResult(Json(HttpStatusCode.OK,
                req.RequestUri!.AbsolutePath.EndsWith("/login")
                    ? "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_ORG_ADMIN\"}}"
                    : "{\"isSuccess\":true,\"result\":null}"))));
            var api = new SemojumApiClient(http);
            var dialog = new AccountDialog(api, null, null, allowInstitution) { Owner = owner };
            var errorShown = false; var timedOut = false; Exception? callbackFailure = null;
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timeout.Tick += (_, _) => { timeout.Stop(); timedOut = true; if (dialog.IsVisible) dialog.Close(); };
            _ = dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    var controls = Descendants(dialog).ToArray();
                    controls.OfType<TextBox>().Single().Text = "fixture-institution";
                    controls.OfType<PasswordBox>().Single().Password = "fixture-password";
                    controls.OfType<Button>().Single(b => Equals(b.Content, "로그인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        if (!dialog.IsVisible) return;
                        errorShown = Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("에디터를 사용할 수 없습니다"));
                        dialog.Close();
                    };
                    timer.Start();
                }
                catch (Exception ex) { callbackFailure = ex; if (dialog.IsVisible) dialog.Close(); }
            }));
            bool? result;
            timeout.Start();
            try { result = dialog.ShowDialog(); }
            finally { timeout.Stop(); }
            if (callbackFailure is not null) throw new InvalidOperationException("Institution login fixture interaction failed", callbackFailure);
            if (timedOut) throw new TimeoutException("Institution login fixture did not close within 5 seconds");
            if (allowInstitution)
                check(result == true && api.Account?.Role == "ROLE_ORG_ADMIN", "Institution sign-in is accepted at the desktop startup routing boundary");
            else
                check(result != true && api.Account is null && errorShown, "An institution account cannot enter an existing editor through reauthentication");
        }
    }

    internal static async Task CheckLogoutCancellationAsync(Action<bool, string> check)
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}"))));
        var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture-password");
        var window = new MainWindow(api);
        window.Show();
        var sawNotice = false; var timedOut = false; var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (notice is not null) { sawNotice = true; notice.Close(); return; }
            if (DateTime.UtcNow < deadline) return;
            timedOut = true; timer.Stop();
            foreach (var child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray()) child.Close();
        };
        try
        {
            var model = window.Session;
            model.ActiveBlock!.Source = "아직 저장하지 않은 내용";
            timer.Start(); await window.ExecuteAsync("logout").WaitAsync(TimeSpan.FromSeconds(7));
            if (timedOut) throw new TimeoutException("Logout cancellation fixture did not finish within 5 seconds");
            check(sawNotice && ReferenceEquals(model, window.Session) && model.ActiveBlock!.Source == "아직 저장하지 않은 내용" && model.IsDirty && api.Account?.LoginId == "editor",
                "Cancelling logout preserves the signed-in account and unsaved document");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
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
        var reportedRoleChange = false; var timedOut = false; var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (notice is not null) { reportedRoleChange = notice.DetailText.Contains("권한"); notice.Close(); return; }
            if (DateTime.UtcNow < deadline) return;
            timedOut = true; timer.Stop();
            foreach (var child in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray()) child.Close();
        };
        try
        {
            var model = window.Session; model.JobId = "fixture-job";
            model.ActiveBlock!.Source = "저장하지 않은 교정";
            timer.Start(); await window.ExecuteAsync("cancelJob").WaitAsync(TimeSpan.FromSeconds(7));
            if (timedOut) throw new TimeoutException("Role change fixture did not finish within 5 seconds");
            check(reportedRoleChange && calls == 1 && api.Account is null && ReferenceEquals(model, window.Session)
                && model.IsDirty && model.ActiveBlock!.Source == "저장하지 않은 교정",
                "Changed role stops request retry and preserves unsaved editor contents");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
}
