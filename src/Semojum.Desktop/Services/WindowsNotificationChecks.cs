using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsNotificationChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture"); var board = new BoardDialog(new BoardClient(api), ""); board.Show();
        try
        {
            await WaitAsync(() => fixture.BoardLists > 0);
            var entry = Descendants(board).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "알림"));
            check(entry is not null,
                "The board provides a visible entry to notifications");
            var opened = false; var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            close.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<NotificationsDialog>().FirstOrDefault(d => d.Owner == board && d.IsVisible && d.IsEnabled);
                if (dialog is not null) { opened = true; dialog.Close(); }
            };
            close.Start(); try { entry!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); } finally { close.Stop(); }
            check(opened, "The board notification entry opens the notification list");
        }
        finally { board.Close(); }

        var notifications = new NotificationsDialog(new BoardClient(api), ""); notifications.Show();
        try
        {
            await WaitAsync(() => fixture.NotificationLists > 0);
            check(Descendants(notifications).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("알림이 없습니다")),
                "An empty notification list explains that there are no notifications");
            check(fixture.AuthenticatedNotifications, "Notifications use the signed-in account");
        }
        finally { notifications.Close(); }

        var notices = new PublicNoticesDialog(api); notices.Show();
        try
        {
            await WaitAsync(() => fixture.PublicLists > 0);
            check(Descendants(notices).OfType<TextBlock>().Any(t => t.Text.Contains("현재 표시할 공지가 없습니다")),
                "Public notices explain the empty state");
            check(fixture.PublicWasAnonymous, "Public notices do not send account credentials");
        }
        finally { notices.Close(); }

        fixture.HoldPublic = true; var publicBefore = fixture.PublicLists;
        var slowPublic = new PublicNoticesDialog(api); slowPublic.Show();
        try
        {
            await WaitAsync(() => fixture.PublicLists > publicBefore);
            slowPublic.Close();
            check(!slowPublic.IsVisible, "A delayed public notice request does not prevent closing the window");
        }
        finally { fixture.ReleasePublic(); await Task.Delay(50); if (slowPublic.IsVisible) slowPublic.Close(); fixture.HoldPublic = false; }

        fixture.DenyPublic = true; publicBefore = fixture.PublicLists;
        var retryPublic = new PublicNoticesDialog(api); retryPublic.Show();
        try
        {
            await WaitAsync(() => fixture.PublicLists > publicBefore);
            check(Descendants(retryPublic).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("접근할 수 없습니다")),
                "Public notice load errors are visible");
            fixture.DenyPublic = false; fixture.PopulatedPublic = true; publicBefore = fixture.PublicLists;
            Descendants(retryPublic).OfType<Button>().Single(b => Equals(b.Content, "새로고침")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.PublicLists > publicBefore && Descendants(retryPublic).OfType<TextBlock>().Any(t => t.Text == "점검 공지"));
            check(Descendants(retryPublic).OfType<TextBlock>().Any(t => t.Text == "점검 공지") && fixture.PublicWasAnonymous,
                "Public notices can retry without account credentials");
            var cards = Descendants(retryPublic).OfType<Expander>().ToArray();
            check(cards.Length == 3 && cards[0].IsExpanded && !cards[1].IsExpanded && !cards[2].IsExpanded,
                "Public notice entries display one expanded and another collapsed");
            check(Descendants(cards[0]).OfType<TextBlock>().Any(t => t.Text == "2026. 10. 25."),
                "The public notice displays its server-provided creation date");
            check(!Descendants(cards[2]).OfType<TextBlock>().Any(t => t.Text.Contains("2026.")),
                "A missing notice date is not invented");
            var capture = Environment.GetEnvironmentVariable("SEMOJUM_NOTICE_CAPTURE");
            if (!string.IsNullOrWhiteSpace(capture)) await WindowsSmoke.RenderElementAsync(retryPublic, capture);
            var peer = UIElementAutomationPeer.CreatePeerForElement(cards[1]) as ExpanderAutomationPeer;
            var provider = peer?.GetPattern(PatternInterface.ExpandCollapse) as IExpandCollapseProvider;
            check(provider is not null, "The notice can be expanded through the accessibility API");
            provider!.Expand();
            await retryPublic.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            check(cards[1].IsExpanded && cards[1].Content is FrameworkElement { IsVisible: true },
                "Expanding another notice exposes its full body");
            provider.Collapse();
            check(!cards[1].IsExpanded, "The expanded notice can be collapsed");
            fixture.DenyPublic = true; publicBefore = fixture.PublicLists;
            Descendants(retryPublic).OfType<Button>().Single(b => Equals(b.Content, "새로고침")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.PublicLists > publicBefore && Descendants(retryPublic).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("접근할 수 없습니다")));
            check(Descendants(retryPublic).OfType<Expander>().Count() == 3 && cards[0].IsExpanded,
                "A failed notice refresh preserves the previously loaded notices");
            fixture.DenyPublic = false;
        }
        finally { retryPublic.Close(); fixture.PopulatedPublic = false; }

        var unsignedApi = new SemojumApiClient(http); var account = new AccountDialog(unsignedApi); account.Show();
        var sawNotices = false; var sawInquiry = false; var closePublic = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        closePublic.Tick += (_, _) =>
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == account && w.IsVisible).ToArray())
            {
                if (dialog is PublicNoticesDialog) sawNotices = true;
                if (dialog is PublicInquiryDialog) sawInquiry = true;
                dialog.Close();
            }
        };
        closePublic.Start();
        try
        {
            Descendants(account).OfType<Button>().Single(b => Equals(b.Content, "공지사항")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Descendants(account).OfType<Button>().Single(b => Equals(b.Content, "문의하기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(sawNotices && sawInquiry && unsignedApi.Account is null && fixture.PublicWasAnonymous,
                "Login opens public notices and inquiry without creating a session");
        }
        finally { closePublic.Stop(); account.Close(); }
        var inquiry = new PublicInquiryDialog(unsignedApi); inquiry.Show();
        try
        {
            var email = Descendants(inquiry).OfType<TextBox>().Single(t => t.MaxLength == 100);
            var message = Descendants(inquiry).OfType<TextBox>().Single(t => t.AcceptsReturn && t.MaxLength == 2000);
            var organization = Descendants(inquiry).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "기관명");
            var title = Descendants(inquiry).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "제목");
            var submit = Descendants(inquiry).OfType<Button>().Single(b => Equals(b.Content, "문의 보내기"));
            email.Text = "invalid"; message.Text = "작성한 문의"; organization.Text = "테스트 기관"; title.Text = "문의 제목";
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            check(fixture.InquiryPosts == 0 && message.Text == "작성한 문의", "Invalid inquiry is not sent and keeps the typed message");
            email.Text = "user@example.test"; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.InquiryPosts == 1);
            check(fixture.InquiryWasAnonymous && message.Text == "작성한 문의" && organization.Text == "테스트 기관" && title.Text == "문의 제목"
                && Descendants(inquiry).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("접근할 수 없습니다")),
                "A failed anonymous inquiry reports its error and retains all typed fields");
            check(fixture.InquiryMessage == "기관명: 테스트 기관\n제목: 문의 제목\n\n작성한 문의" && fixture.InquiryName == "",
                "Organization and title reach the existing anonymous message contract without replacing the sender name");
            message.Text = new string('가', 1990);
            submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            check(fixture.InquiryPosts == 1 && message.Text.Length == 1990
                && Descendants(inquiry).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("2,000자")),
                "The combined inquiry length limit rejects the request without losing fields");
            message.Text = "작성한 문의";
            var cancel = Descendants(inquiry).OfType<Button>().Single(b => Equals(b.Content, "취소"));
            var sawConfirm = AnswerInquiryClose(inquiry, 0, () => cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            check(sawConfirm && inquiry.IsVisible && organization.Text == "테스트 기관" && title.Text == "문의 제목" && message.Text == "작성한 문의",
                "Choosing to continue after cancelling retains all inquiry fields");
            email.Text = ""; organization.Text = ""; message.Text = "";
            sawConfirm = AnswerInquiryClose(inquiry, 0, inquiry.Close);
            check(sawConfirm && inquiry.IsVisible && title.Text == "문의 제목", "A title alone is protected when closing the inquiry");
            title.Text = ""; organization.Text = "테스트 기관";
            sawConfirm = AnswerInquiryClose(inquiry, 0, () => inquiry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(inquiry), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent }));
            check(sawConfirm && inquiry.IsVisible && organization.Text == "테스트 기관", "Escape protects an inquiry containing only an organization");
            sawConfirm = AnswerInquiryClose(inquiry, 1, inquiry.Close);
            check(sawConfirm && !inquiry.IsVisible, "Confirming discard closes a drafted inquiry");
        }
        finally { if (inquiry.IsVisible) AnswerInquiryClose(inquiry, 1, inquiry.Close); }

        fixture.HoldNotifications = true; var before = fixture.NotificationLists;
        var slow = new NotificationsDialog(new BoardClient(api), ""); slow.Show();
        try
        {
            await WaitAsync(() => fixture.NotificationLists > before);
            slow.Close();
            check(!slow.IsVisible, "A delayed notification list does not prevent closing the window");
        }
        finally { fixture.Release(); await Task.Delay(50); if (slow.IsVisible) slow.Close(); }

        fixture.HoldNotifications = false; fixture.Populated = true;
        var listWindow = new NotificationsDialog(new BoardClient(api), ""); listWindow.Show();
        var closePost = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        closePost.Tick += (_, _) =>
        {
            foreach (var post in Application.Current.Windows.OfType<PostDialog>().Where(p => p.Owner == listWindow && p.IsVisible).ToArray()) post.Close();
        };
        closePost.Start();
        try
        {
            var list = Descendants(listWindow).OfType<ListBox>().Single();
            await WaitAsync(() => list.Items.Count == 2);
            list.SelectedIndex = 0;
            var open = Descendants(listWindow).OfType<Button>().Single(b => Equals(b.Content, "열기"));
            fixture.DenyPost = true; open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.PostRequests == 1);
            check(list.Items[0] is BoardNotification { Read: false } && fixture.PostRequests == 1,
                "A forbidden post does not mark the notification as read");
            fixture.DenyPost = false; open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.PostRequests == 2 && list.Items.Count == 2 && list.Items[0] is BoardNotification { Read: true });
            check(list.Items[0] is BoardNotification { Read: true } && list.Items[1] is BoardNotification { Read: false },
                "Opening a permitted post refreshes only its server-confirmed read status");
            var readAll = Descendants(listWindow).OfType<Button>().Single(b => Equals(b.Content, "모두 읽음"));
            fixture.DenyReadAll = true; readAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.ReadAllRequests == 1);
            check(list.Items[1] is BoardNotification { Read: false } && Descendants(listWindow).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("접근할 수 없습니다")),
                "A failed read-all request reports the error and does not invent a read state");
            fixture.DenyReadAll = false; readAll.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.ReadAllRequests == 2 && list.Items[1] is BoardNotification { Read: true });
            check(fixture.MutationAuthorized && list.Items.Cast<BoardNotification>().All(n => n.Read), "Successful authenticated read-all refreshes server-confirmed statuses");
            fixture.InvalidNotifications = true; var count = fixture.NotificationLists;
            Descendants(listWindow).OfType<Button>().Single(b => Equals(b.Content, "새로고침")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.NotificationLists > count);
            check(list.Items.Count == 2 && Descendants(listWindow).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("응답을 확인할 수 없습니다")),
                "A malformed notification response reports an error without clearing the prior list");
        }
        finally { closePost.Stop(); listWindow.Close(); }
    }

    private static async Task WaitAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!ready() && DateTime.UtcNow < deadline) await Task.Delay(20);
        await Task.Delay(50);
    }

    private static bool AnswerInquiryClose(Window inquiry, int choice, Action attempt)
    {
        var shown = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == inquiry && d.IsVisible);
            if (dialog is null) return;
            shown = true; timer.Stop(); dialog.ActionButtons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start(); try { attempt(); } finally { timer.Stop(); } return shown;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var item in Descendants(child)) yield return item; }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public int BoardLists { get; private set; }
        public int NotificationLists { get; private set; }
        public int PublicLists { get; private set; }
        public bool AuthenticatedNotifications { get; private set; }
        public bool PublicWasAnonymous { get; private set; }
        public int InquiryPosts { get; private set; }
        public bool InquiryWasAnonymous { get; private set; }
        public string? InquiryMessage { get; private set; }
        public string? InquiryName { get; private set; }
        public bool HoldNotifications { get; set; }
        public bool HoldPublic { get; set; }
        public bool DenyPublic { get; set; }
        public bool PopulatedPublic { get; set; }
        public bool Populated { get; set; }
        public bool DenyPost { get; set; }
        public bool DenyReadAll { get; set; }
        public bool InvalidNotifications { get; set; }
        public bool MutationAuthorized { get; private set; }
        public int PostRequests { get; private set; }
        public int ReadAllRequests { get; private set; }
        private bool firstRead, secondRead;
        private readonly TaskCompletionSource<HttpResponseMessage> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<HttpResponseMessage> pendingPublic = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Release() => pending.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"unreadCount\":0,\"items\":[]}}", Encoding.UTF8, "application/json") });
        public void ReleasePublic() => pendingPublic.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"isSuccess\":true,\"result\":[]}", Encoding.UTF8, "application/json") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; string result;
            if (path.EndsWith("/login")) result = "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}";
            else if (path == "/api/board/posts") { BoardLists++; result = "{\"page\":1,\"totalCount\":0,\"notices\":[],\"items\":[]}"; }
            else if (path == "/api/board/notifications")
            {
                NotificationLists++; AuthenticatedNotifications = request.Headers.Authorization?.Scheme == "Bearer";
                if (request.Method == HttpMethod.Patch) throw new InvalidOperationException("Unexpected notification endpoint");
                if (HoldNotifications) return pending.Task.WaitAsync(cancellationToken);
                if (InvalidNotifications) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"unreadCount\":0,\"items\":null}}", Encoding.UTF8, "application/json") });
                result = !Populated ? "{\"unreadCount\":0,\"items\":[]}" : System.Text.Json.JsonSerializer.Serialize(new
                {
                    unreadCount = (firstRead ? 0 : 1) + (secondRead ? 0 : 1), items = new[]
                    {
                        new { notificationId = 1, postId = 77, title = "첫 문의", answerCount = 1, lastAnsweredAt = "2026-10-29", read = firstRead },
                        new { notificationId = 2, postId = 78, title = "둘째 문의", answerCount = 1, lastAnsweredAt = "2026-10-29", read = secondRead }
                    }
                });
            }
            else if (path == "/api/board/posts/77")
            {
                PostRequests++;
                if (DenyPost) return Task.FromResult(Error());
                firstRead = true;
                result = "{\"postId\":77,\"title\":\"첫 문의\",\"content\":\"본문\",\"author\":\"fixture\",\"mine\":true,\"secret\":true,\"hidden\":false,\"createdAt\":\"2026-10-29\",\"updatedAt\":\"2026-10-29\",\"viewCount\":1,\"attachments\":[],\"answers\":[]}";
            }
            else if (path == "/api/board/notifications/read-all")
            {
                if (request.Method != HttpMethod.Patch) throw new InvalidOperationException("Read-all must use PATCH");
                MutationAuthorized = request.Headers.Authorization?.Scheme == "Bearer";
                ReadAllRequests++;
                if (DenyReadAll) return Task.FromResult(Error());
                firstRead = secondRead = true; result = "null";
            }
            else if (path == "/api/public/notices")
            {
                PublicLists++; PublicWasAnonymous = request.Headers.Authorization is null;
                if (HoldPublic) return pendingPublic.Task.WaitAsync(cancellationToken);
                if (DenyPublic) return Task.FromResult(Error());
                result = PopulatedPublic ? "[{\"title\":\"점검 공지\",\"body\":\"안내 본문\",\"createdAt\":\"2026-10-24T15:00:00Z\"},{\"title\":\"두 번째 공지\",\"body\":\"자세한 본문\",\"createdAt\":\"2026-10-01T09:00:00Z\"},{\"title\":\"날짜 없는 공지\",\"body\":\"본문\"}]" : "[]";
            }
            else if (path == "/api/public/inquiries")
            { InquiryPosts++; InquiryWasAnonymous = request.Headers.Authorization is null; return ReceiveInquiryAsync(request, cancellationToken); }
            else throw new InvalidOperationException("Unexpected fixture path " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
        private async Task<HttpResponseMessage> ReceiveInquiryAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            InquiryMessage = payload.RootElement.GetProperty("message").GetString();
            InquiryName = payload.RootElement.GetProperty("name").GetString();
            return Error();
        }
        private static HttpResponseMessage Error() => new(HttpStatusCode.Forbidden)
        { Content = new StringContent("{\"isSuccess\":false,\"code\":\"COMMON4003\",\"message\":\"접근할 수 없습니다\"}", Encoding.UTF8, "application/json") };
    }
}
