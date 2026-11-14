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

        // An administrator cannot use the editor: login → separate 기관 관리 window with the editor hidden → logout → login → editor.
        using var flowHttp = new HttpClient(new InstitutionThenUser()); var flowApi = new SemojumApiClient(flowHttp);
        var flowOwner = new MainWindow(flowApi); var flowClosed = false; flowOwner.Closed += (_, _) => flowClosed = true; flowOwner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var existingWindows = Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible); var editorTitle = flowOwner.Title;
            var flow = flowOwner.EnsureAccountAsync(allowInstitution: true);
            await WaitForAsync(() => Descendants(flowOwner).OfType<PasswordBox>().Any(p => p.IsVisible));
            SubmitLogin(flowOwner, "institution");
            await WaitForAsync(() => flowOwner.Institution is { IsVisible: true });
            var institution = flowOwner.Institution!; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!flowOwner.IsVisible && !flow.IsCompleted && institution is { Title: "기관 관리 - 누리닷", Owner: null, ShowInTaskbar: true, ResizeMode: ResizeMode.CanResize }
                && CaptionButtons(institution).SequenceEqual(["최소화", "최대화 / 복원", "닫기"])
                && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == existingWindows
                && !Application.Current.Windows.Cast<Window>().Any(w => w.Title == "누리닷 기관 관리"),
                "An institution login hides the editor and shows only the top-level 기관 관리 - 누리닷 window with minimize, maximize and close");
            check(Descendants(institution).OfType<Menu>().Single().Items.OfType<MenuItem>().SelectMany(m => m.Items.OfType<MenuItem>()).Select(m => m.Header as string)
                    .SequenceEqual(["로그아웃", "닫기", "도움말", "마이페이지 · 사용량"]),
                "The 기관 관리 window offers no editor command, only logout, close, help and my page");
            // 정보 › 마이페이지 · 사용량 opens my page over the admin window; its admin-only 기관 관리 section brings that window back.
            UsageDialog? myPage = null; string[] sections = []; var ticks = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (Application.Current.Windows.OfType<UsageDialog>().FirstOrDefault(d => d.Owner == institution && d.IsLoaded) is { } page)
                {
                    timer.Stop(); myPage = page; sections = page.Navigation.Items.Cast<TabItem>().Select(Section).ToArray();
                    page.Navigation.SelectedItem = page.Navigation.Items.Cast<TabItem>().FirstOrDefault(t => Section(t) == "기관 관리") ?? page.Navigation.Items[0];
                    if (page.IsVisible) page.DialogResult = false;
                }
                else if (++ticks >= 250) { timer.Stop(); foreach (var stray in Application.Current.Windows.OfType<UsageDialog>().Where(d => d.IsVisible).ToArray()) stray.DialogResult = false; }
            };
            timer.Start();
            institution.MyPageItem?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(myPage is { IsVisible: false, LogoutRequested: false, DialogResult: true } && sections.SequenceEqual(["사용량", "과금 이력", "계정 정보", "기관 관리", "문의 내역", "로그아웃"])
                && institution.IsVisible && !flowOwner.IsVisible && !flow.IsCompleted,
                "The administrator's my page lists six sections and its 기관 관리 closes it back to the open 기관 관리 window");
            institution.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitForAsync(() => Descendants(flowOwner).OfType<PasswordBox>().Any(p => p.IsVisible));
            check(flowOwner.IsVisible && flowOwner.Title == "누리닷 로그인" && !institution.IsVisible && flowOwner.Institution is null && flowApi.Account is null && !flow.IsCompleted,
                "Logging out of the 기관 관리 window closes it and returns to the login screen");
            SubmitLogin(flowOwner, "editor");
            check(await flow.WaitAsync(TimeSpan.FromSeconds(8)) && flowApi.Account is { LoginId: "editor", Role: "ROLE_USER" }
                && flowOwner.IsVisible && flowOwner.Title == editorTitle && Application.Current.Windows.Cast<Window>().Count(w => w.IsVisible) == existingWindows,
                "After an institution logout a user login enters the editor normally");
        }
        finally { if (!flowClosed) { await flowOwner.ReleaseResourcesAsync(); flowOwner.PrepareSmokeShutdown(); flowOwner.Close(); } }

        // A restored administrator session (automatic login) takes the same route; × on the 기관 관리 window ends the app.
        using var closeHttp = new HttpClient(new InstitutionThenUser()); var closeApi = new SemojumApiClient(closeHttp);
        await closeApi.LoginAsync("institution", "password");
        var closeOwner = new MainWindow(closeApi); var ownerClosed = false; closeOwner.Closed += (_, _) => ownerClosed = true; closeOwner.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var restored = closeOwner.EnsureAccountAsync(allowInstitution: true);
            await WaitForAsync(() => closeOwner.Institution is { IsVisible: true });
            var admin = closeOwner.Institution!; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!closeOwner.IsVisible && !Descendants(closeOwner).OfType<PasswordBox>().Any(p => p.IsVisible),
                "A restored administrator session opens the 기관 관리 window directly with the editor hidden");
            Descendants(admin).OfType<Button>().First(b => AutomationProperties.GetName(b) == "닫기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!await restored.WaitAsync(TimeSpan.FromSeconds(8)) && !admin.IsVisible && ownerClosed,
                "Closing the 기관 관리 window with × also closes the hidden editor window, ending the app");
        }
        finally { if (!ownerClosed) { await closeOwner.ReleaseResourcesAsync(); closeOwner.PrepareSmokeShutdown(); closeOwner.Close(); } }
    }

    private static string Section(TabItem tab) => tab.Header as string ?? Descendants((Panel)tab.Header).OfType<TextBlock>().Last().Text;

    private static IEnumerable<string?> CaptionButtons(Window window) => Descendants(window).OfType<Button>()
        .Select(b => AutomationProperties.GetName(b)).Where(name => name is "최소화" or "최대화 / 복원" or "닫기");

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
