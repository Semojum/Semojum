using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsEmbeddedScreenChecks
{
    private sealed class Fixture(string role) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $"{{\"isSuccess\":true,\"result\":{{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"{role}\"}}}}", Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"isSuccess\":false,\"message\":\"fixture offline\"}", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class DelayedLogin : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<bool> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<HttpResponseMessage> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/auth/login") { Started.TrySetResult(true); return Response.Task; }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":null}", Encoding.UTF8, "application/json") });
        }
    }

    private sealed class InstitutionThenUser : HttpMessageHandler
    {
        private int loginCount;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath;
            if (path == "/api/auth/login")
            {
                var role = Interlocked.Increment(ref loginCount) == 1 ? "ROLE_ORG_ADMIN" : "ROLE_USER";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                    $"{{\"isSuccess\":true,\"result\":{{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"{role}\"}}}}", Encoding.UTF8, "application/json") });
            }
            if (path == "/api/auth/logout") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":null}", Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"isSuccess\":false,\"message\":\"fixture offline\"}", Encoding.UTF8, "application/json") });
        }
    }

    internal static async Task RunAsync(Action<bool, string> check)
    {
        var owner = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 800, Height = 600 };
        owner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            owner.Session.ActiveBlock!.Source = "저장 전 작업";
            var originalState = owner.WindowState; var originalTitle = owner.Title; var shortcutCount = owner.InputBindings.Count;
            var visibleWindows = Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible);
            using var http = new HttpClient(new Fixture("ROLE_USER")); var api = new SemojumApiClient(http);
            var login = new AccountDialog(api, null, null);
            var signingIn = login.ShowEmbeddedAsync(owner);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); owner.UpdateLayout();
            var loginInput = Descendants(owner).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디");
            var password = Descendants(owner).OfType<PasswordBox>().Single(t => AutomationProperties.GetName(t) == "비밀번호");
            check(!login.IsVisible && owner.WindowState == WindowState.Maximized && owner.Title == "누리닷 로그인"
                && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == visibleWindows && owner.InputBindings.Count == 0,
                "Login appears maximized inside the existing window without opening a second visible window or editor shortcuts");
            loginInput.Text = "fixture"; password.Password = "password";
            Descendants(owner).OfType<Button>().Single(b => Equals(b.Content, "로그인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(await signingIn.WaitAsync(TimeSpan.FromSeconds(8)) && api.Account?.LoginId == "fixture"
                && owner.InputBindings.Count == shortcutCount && owner.WindowState == originalState && owner.Title == originalTitle
                && owner.Session.ActiveBlock?.Source == "저장 전 작업",
                "Successful embedded login restores the editor window and its shortcuts");

            using var adminHttp = new HttpClient(new Fixture("ROLE_ORG_ADMIN")); var adminApi = new SemojumApiClient(adminHttp);
            await adminApi.LoginAsync("admin", "password");
            var institution = new InstitutionWindow(adminApi, () => Task.FromResult(true));
            var managing = institution.ShowEmbeddedAsync(owner);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); owner.UpdateLayout();
            check(!institution.IsVisible && owner.WindowState == WindowState.Maximized && owner.Title == "누리닷 기관 관리"
                && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == visibleWindows,
                "Institution management appears maximized inside the same existing window");
            institution.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await managing.WaitAsync(TimeSpan.FromSeconds(8));
            check(institution.LogoutRequested && owner.InputBindings.Count == shortcutCount && owner.WindowState == originalState,
                "Institution logout closes the embedded view and restores the editor shell");
        }
        finally { owner.PrepareSmokeShutdown(); owner.Close(); }

        var delayed = new DelayedLogin(); using var delayedHttp = new HttpClient(delayed); var delayedApi = new SemojumApiClient(delayedHttp);
        var closingOwner = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); closingOwner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var login = new AccountDialog(delayedApi, null, null);
            var signingIn = login.ShowEmbeddedAsync(closingOwner);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Descendants(closingOwner).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디").Text = "fixture";
            Descendants(closingOwner).OfType<PasswordBox>().Single(t => AutomationProperties.GetName(t) == "비밀번호").Password = "password";
            Descendants(closingOwner).OfType<Button>().Single(b => Equals(b.Content, "로그인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await delayed.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            closingOwner.PrepareSmokeShutdown(); closingOwner.Close();
            var accepted = await signingIn.WaitAsync(TimeSpan.FromSeconds(5));
            delayed.Response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"isSuccess\":true,\"result\":{\"accessToken\":\"late\",\"refreshToken\":\"late\",\"role\":\"ROLE_USER\"}}", Encoding.UTF8, "application/json") });
            await Task.Delay(250);
            check(!accepted && delayedApi.Account is null, "Closing the only window during login cannot accept a late session");
        }
        finally { if (closingOwner.IsVisible) { closingOwner.PrepareSmokeShutdown(); closingOwner.Close(); } }

        using var flowHttp = new HttpClient(new InstitutionThenUser()); var flowApi = new SemojumApiClient(flowHttp);
        var flowOwner = new MainWindow(flowApi); flowOwner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var existingWindows = Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible);
            var flow = flowOwner.EnsureAccountAsync(allowInstitution: true);
            await WaitForAsync(() => Descendants(flowOwner).OfType<PasswordBox>().Any(p => p.IsVisible));
            SubmitLogin(flowOwner, "institution");
            await WaitForAsync(() => flowOwner.Title == "누리닷 기관 관리");
            check(flowOwner.WindowState == WindowState.Maximized && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == existingWindows,
                "An institution login routes to management in the same maximized window");
            var fileMenu = Descendants(flowOwner).OfType<MenuItem>().Single(m => Equals(m.Header, "파일(_F)") && m.IsEnabled);
            fileMenu.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "로그아웃")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitForAsync(() => Descendants(flowOwner).OfType<PasswordBox>().Any(p => p.IsVisible));
            SubmitLogin(flowOwner, "editor");
            check(await flow.WaitAsync(TimeSpan.FromSeconds(8)) && flowApi.Account is { LoginId: "editor", Role: "ROLE_USER" }
                && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == existingWindows,
                "Institution logout returns to login and a user enters the editor without opening another window");
        }
        finally { await flowOwner.ReleaseResourcesAsync(); flowOwner.PrepareSmokeShutdown(); flowOwner.Close(); }
    }

    private static void SubmitLogin(Window owner, string id)
    {
        Descendants(owner).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디").Text = id;
        Descendants(owner).OfType<PasswordBox>().Single(t => AutomationProperties.GetName(t) == "비밀번호").Password = "password";
        Descendants(owner).OfType<Button>().Single(b => Equals(b.Content, "로그인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (!condition() && DateTime.UtcNow < deadline) { await Task.Delay(25); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        if (!condition()) throw new TimeoutException("화면 전환을 기다리는 시간이 초과되었습니다.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
