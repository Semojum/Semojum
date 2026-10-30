using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsInstitutionDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var handler = new Fixture(); using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var api = new SemojumApiClient(http) { RefreshRequiresIdentity = a => a.Role == "ROLE_ORG_ADMIN" };
        await api.LoginAsync("org", "fixture"); var allowLogout = false; var logoutCalls = 0;
        var window = new InstitutionWindow(api, () => { logoutCalls++; return Task.FromResult(allowLogout); }) { Width = 960, Height = 650 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await WaitFor(() => window.Refresh.IsEnabled && window.Accounts.Items.Count > 0);
            check(window.Identity.Text == "테스트 기관 · O1" && window.Allocated.Text == "1,000" && window.Balance.Text == "700" && window.UsageBar.Value == 30,
                "Institution dashboard displays actual server organization and credit values");
            check(window.UsagePercent.Text == "30%" && window.StatusSummary.Text.Contains("테스트 기관") && window.StatusSummary.Text.Contains("700") && window.ContractRemaining.Text != "정보 없음",
                "Institution percentage, contract days and status line use the actual dashboard response");
            check(InstitutionWindow.UsageRatio(0, 0) == "정보 없음" && InstitutionWindow.UsageRatio(-1, 0) == "정보 없음" && InstitutionWindow.UsageRatio(100, -1) == "정보 없음"
                && InstitutionWindow.UsageRatio(100, 0) == "0%" && InstitutionWindow.UsageRatio(100, 120) == "120%", "Usage percentage handles zero, negative and over-allocation inputs without inventing data");
            var date = new DateOnly(2026, 10, 30);
            check(InstitutionWindow.RemainingDays("2026-10-31", date) == "1일 남음" && InstitutionWindow.RemainingDays("2026-10-30", date) == "오늘 만료"
                && InstitutionWindow.RemainingDays("2026-10-29", date) == "1일 경과" && InstitutionWindow.RemainingDays("2026-02-30", date) == "정보 없음"
                && InstitutionWindow.RemainingDays("2026-10-30T00:00:00", date) == "정보 없음" && InstitutionWindow.RemainingDays(null, date) == "정보 없음",
                "Contract date uses strict ISO dates and handles future, expiry, past and invalid dates");
            check(window.Accounts.Items.Count == 2 && window.Requests.Items.Count == 2 && window.Orders.Items.Count == 1 && window.Notices.Items.Count == 1,
                "Institution lists display only the API-provided accounts, requests, orders and notices");
            check(InstitutionStatusTextConverter.Label("ACTIVE", "account") == "활성" && InstitutionStatusTextConverter.Label("INACTIVE", "account") == "잠김"
                && InstitutionStatusTextConverter.Label("ORG", "notice") == "우리 기관" && InstitutionStatusTextConverter.Label("FUTURE_STATUS", "account") == "FUTURE_STATUS"
                && InstitutionStatusTextConverter.Label("FUTURE_SCOPE", "notice") == "FUTURE_SCOPE", "Known institution status labels are translated while unknown server codes remain intact");
            check(!window.RequestsExpander.IsExpanded && window.Orders.Items.Count == 1, "Request history starts collapsed while the order list retains actual data");
            var peer = UIElementAutomationPeer.CreatePeerForElement(window.RequestsExpander);
            var expand = peer?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
            expand?.Expand(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(expand is not null && window.RequestsExpander.IsExpanded && window.Requests.IsVisible && window.Requests.Items.Count == 2 && window.Requests.ActualHeight > 0,
                "The accessible expand action reveals the complete real request list");
            expand?.Collapse(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!window.RequestsExpander.IsExpanded && window.Requests.Items.Count == 2, "The accessible collapse action preserves the request data");
            var toggle = Descendants(window.RequestsExpander).OfType<ToggleButton>().FirstOrDefault();
            if (toggle is not null && PresentationSource.FromVisual(toggle) is { } source)
            {
                toggle.Focus();
                toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
                toggle.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                check(window.RequestsExpander.IsExpanded && window.Requests.Items.Count == 2, "The request expander opens through its keyboard Space handler");
                expand?.Collapse();
            }
            else check(false, "The request expander has a keyboard-focusable header");
            foreach (var scroll in Descendants(window).OfType<ScrollViewer>().Where(s => s.Content is StackPanel)) scroll.ScrollToTop();
            check(window.AccountRequest.IsEnabled && window.CreditRequest.IsEnabled && window.ReceiptAddress.Text.Contains("billing@example.test"),
                "Available request actions and receipt recipient reflect the current institution response");
            check(window.CreditRequest.Background is SolidColorBrush { Color: var orange } && orange == Color.FromRgb(232, 115, 12)
                && window.Accounts.RowHeight == 40 && window.Accounts.GridLinesVisibility == DataGridGridLinesVisibility.Horizontal
                && window.UsageBar.Template.FindName("InstitutionTrack", window.UsageBar) is Border { Background: SolidColorBrush { Color: var track } } && track == Color.FromRgb(237, 239, 243),
                "Institution controls use the Figma orange request button, light table rows and rounded credit track");
            var icons = Descendants(window).OfType<SvgViewbox>().Where(i => i.Source?.ToString().Contains("institution-caption-") == true).ToArray();
            check(icons.Length == 3 && icons.All(i => i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false }), "Institution caption renders all three original Figma SVGs");
            window.Requests.SelectedIndex = 1; check(!window.CancelRequest.IsEnabled, "Unknown open request types cannot be cancelled");
            window.Requests.SelectedIndex = 0; check(window.CancelRequest.IsEnabled, "A supported open institution request can be cancelled");
            if (Environment.GetEnvironmentVariable("INSTITUTION_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
            if (Environment.GetEnvironmentVariable("INSTITUTION_FULL_CAPTURE") is { Length: > 0 } fullCapture)
            {
                window.Width = 1440; window.Height = 1000; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var orderTop = window.Orders.TransformToAncestor(window).Transform(new Point()).Y;
                check(window.Orders.IsVisible && window.Orders.Items.Count == 1 && orderTop >= 0 && orderTop + window.Orders.ActualHeight <= window.ActualHeight - 28,
                    "At the 1440px Figma window size the real order list is visible without scrolling");
                var actionTop = window.Receipt.TransformToAncestor(window).Transform(new Point()).Y;
                check(actionTop + window.Receipt.ActualHeight <= window.ActualHeight - 28 && window.Receipt.IsVisible,
                    "The order action buttons fit above the status bar at the 1440px window size");
                await WindowsSmoke.RenderElementAsync(window, fullCapture); window.Width = 960; window.Height = 650; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            window.Orders.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.OrderDetail.IsEnabled && !window.Receipt.IsEnabled, "Selecting an order enables its detail while withholding an unavailable receipt");
            window.OrderDetail.BringIntoView(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var narrowTop = window.OrderDetail.TransformToAncestor(window).Transform(new Point()).Y;
            check(window.ActualWidth <= 960 && narrowTop >= 32 && narrowTop + window.OrderDetail.ActualHeight <= window.ActualHeight - 28,
                "At 960px the selected order action can be brought into view without being covered by the status bar");
            window.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
            check(logoutCalls == 1 && window.IsVisible && !window.LogoutRequested, "Cancelled or failed logout keeps the institution window open");

            handler.FailDashboard = true; window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => window.Refresh.IsEnabled && window.ErrorText.Text.Contains("기관 정보"));
            check(window.Identity.Text == "정보 없음" && window.Balance.Text == "—" && window.UsagePercent.Text == "정보 없음" && window.ContractRemaining.Text == "정보 없음"
                && window.StatusSummary.Text == "기관 정보 없음" && window.UsageBar.Visibility == Visibility.Collapsed,
                "A dashboard refresh error clears old credit, date, graph and status values");
            handler.FailDashboard = false; window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => window.Refresh.IsEnabled && window.Identity.Text.Contains("테스트 기관"));

            handler.FailAccountsOnRefresh = true;
            window.Refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WaitFor(() => window.Refresh.IsEnabled && api.Account is null);
            check(api.Account is null && window.Identity.Text == "정보 없음" && window.Balance.Text == "—" && window.Accounts.Items.Count == 0 && window.Orders.Items.Count == 0
                && !window.AccountRequest.IsEnabled && !window.CreditRequest.IsEnabled && !window.CancelRequest.IsEnabled && !window.Receipt.IsEnabled
                && window.SessionIdentity.Text == "로그인 정보 없음" && window.StatusSummary.Text == "기관 정보 없음" && window.ErrorText.Text.Contains("다시 로그인"),
                "A role-unverified refresh clears previously loaded institution data and disables changes");
            allowLogout = true; window.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
            check(logoutCalls == 2 && window.LogoutRequested && !window.IsVisible, "The logout callback can return an expired institution session to login");
        }
        finally { if (window.IsVisible) window.Close(); }

        var attempts = 0; string? submitted = null;
        var dialog = new InstitutionRequestDialog(true, message => { attempts++; if (attempts == 1) throw new InvalidOperationException("fixture failure"); submitted = message; return Task.CompletedTask; });
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(async () =>
        {
            dialog.First.Text = "국어 담당"; dialog.Second.Text = "reader@example.test";
            dialog.Submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            check(dialog.IsVisible && dialog.First.Text == "국어 담당" && dialog.Second.Text == "reader@example.test", "A failed account request preserves entered fields for retry");
            dialog.Submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; watchdog.Tick += (_, _) => { watchdog.Stop(); if (dialog.IsVisible) dialog.Close(); };
        watchdog.Start(); var result = dialog.ShowDialog(); watchdog.Stop();
        check(result == true && attempts == 2 && submitted?.Contains("reader@example.test") == true, "An account request closes successfully only after its actual callback succeeds");
        var detail = new InstitutionAccountDetailDialog(new("self", "본인", "ACTIVE", "ROLE_ORG_ADMIN", null, 0, true), new("self", "본인", "2026-01-01", "2026-01-31", [], 0, 0), _ => Task.CompletedTask);
        check(!detail.LockButton.IsEnabled, "The institution administrator cannot lock their own account"); detail.Close();
    }
    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(20);
        if (!predicate()) throw new TimeoutException("Institution fixture did not reach the expected state.");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Fixture : HttpMessageHandler
    {
        internal bool FailAccountsOnRefresh;
        internal bool FailDashboard;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"old\",\"refreshToken\":\"refresh\",\"role\":\"ROLE_ORG_ADMIN\"}}");
            if (path.EndsWith("/refresh")) return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"new\"}}");
            if (path.EndsWith("/dashboard") && FailDashboard) return Json(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"TEST\",\"message\":\"조회 실패\"}");
            if (path.EndsWith("/accounts") && FailAccountsOnRefresh) return Json(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}");
            var result = path switch
            {
                "/api/org/dashboard" => "{\"orgName\":\"테스트 기관\",\"orgCode\":\"O1\",\"contractType\":\"ANNUAL\",\"contractStartedAt\":\"2026-01-01\",\"contractExpiresAt\":\"2026-12-31\",\"creditAllocated\":1000,\"creditUsed\":300,\"creditRemaining\":700,\"monthlyUsage\":[{\"month\":\"2026-10\",\"credits\":300}]}",
                "/api/org/accounts" => "{\"usageSince\":\"2026-10-01\",\"items\":[{\"loginId\":\"org\",\"alias\":\"관리자\",\"status\":\"ACTIVE\",\"role\":\"ROLE_ORG_ADMIN\",\"lastLoginAt\":null,\"usedCredits\":100,\"self\":true},{\"loginId\":\"worker\",\"alias\":null,\"status\":\"ACTIVE\",\"role\":\"ROLE_USER\",\"lastLoginAt\":null,\"usedCredits\":200,\"self\":false}]}",
                "/api/org/requests" => "[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198072\",\"type\":\"CREDIT_ADD\",\"status\":\"OPEN\",\"message\":\"요청\",\"createdAt\":\"2026-10-01\"},{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198073\",\"type\":\"OTHER\",\"status\":\"OPEN\",\"message\":null,\"createdAt\":\"2026-10-02\"}]",
                "/api/org/notices" => "[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198074\",\"scope\":\"ORG\",\"title\":\"공지\",\"body\":\"내용\",\"createdAt\":\"2026-10-03\"}]",
                "/api/org/orders" => "{\"receiptEmail\":\"billing@example.test\",\"items\":[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198075\",\"orderDate\":\"2026-01-01\",\"description\":\"계약\",\"amountKrw\":1000,\"creditAmount\":1000,\"paidAt\":null,\"invoiceStatus\":\"PENDING\",\"receiptFileName\":null}]}",
                _ => "{}"
            };
            return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":" + result + "}");
        }
        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
