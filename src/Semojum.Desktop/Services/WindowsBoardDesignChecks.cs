using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsBoardDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture"); var client = new BoardClient(api);
        var board = new BoardDialog(client, "검수"); board.Show();
        try
        {
            await WaitAsync(() => Descendants(board).OfType<DataGrid>().Any(g => g.Items.Count == 1));
            check(board.Width == 1440 && board.Height == 1000, "Board window uses the final Figma geometry");
            check(HasIcon(board, "board-caption-close.svg") && HasIcon(board, "board-chevron-down.svg"), "Board title bar and search use the final Figma SVGs");
            check(HasIcon(board, "board-chevron-left.svg") && HasIcon(board, "board-chevron-right.svg"), "Board pagination renders both Figma arrow icons");
            check(Descendants(board).OfType<Button>().Any(b => Equals(b.Content, "알림")), "Board retains access to notifications");
            if (Environment.GetEnvironmentVariable("BOARD_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(board, capture);
        }
        finally { board.Close(); }
        var editor = new PostEditorDialog(client, null, "검수"); editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(editor.Width == 1440 && editor.Height == 1000, "Post editor uses the final Figma geometry");
            check(Descendants(editor).OfType<TextBox>().Any(t => t.AcceptsReturn && t.Height == 220), "Post editor offers the final Figma multiline field");
            check(Descendants(editor).OfType<CheckBox>().Any(c => Equals(c.Content, "현재 작업 정보 첨부") && c.IsChecked == true), "New post retains optional current-work attachment");
            check(HasIcon(editor, "board-check.svg"), "The checked work-information option renders the Figma check icon");
            var workOption = Descendants(editor).OfType<CheckBox>().Single(c => Equals(c.Content, "현재 작업 정보 첨부"));
            var checkContainer = (FrameworkElement)VisualTreeHelper.GetParent(workOption);
            var checkRect = workOption.TransformToAncestor(checkContainer).TransformBounds(new Rect(0, 0, workOption.ActualWidth, workOption.ActualHeight));
            check(workOption.ActualHeight >= 20 && checkRect.Top >= 0 && checkRect.Bottom <= checkContainer.ActualHeight, "The work-information checkbox fits completely within its row");
            check(Descendants(editor).OfType<Button>().Where(b => Equals(b.Content, "취소") || Equals(b.Content, "등록")).Select(b => b.Content).SequenceEqual(new object[] { "취소", "등록" }), "Cancel precedes registration in the post editor");
            if (Environment.GetEnvironmentVariable("BOARD_EDITOR_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(editor, capture);
        }
        finally { editor.Close(); }
        var post = new BoardPost(42, "문의 제목", "서버의 본문", "작성자", true, false, false, "2026-10-21", "", 4, [new(3, "첨부.pdf", 1024, "https://example.invalid/attachment.pdf")], [new(1, "운영자 답변 내용", false, "2026-10-22", "")]);
        var detail = new PostDialog(client, post, "검수"); detail.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(detail.Width == 1440 && detail.Height == 1000, "Post detail uses the final Figma geometry");
            check(Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "운영자 답변 내용"), "Post detail retains the actual operator answer");
            check(HasIcon(detail, "board-export.svg"), "Post attachments render the Figma download icon");
            if (Environment.GetEnvironmentVariable("BOARD_DETAIL_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(detail, capture);
        }
        finally { detail.Close(); }
        var alerts = new NotificationsDialog(client, "검수"); alerts.Show();
        try
        {
            await WaitAsync(() => Descendants(alerts).OfType<TextBlock>().Any(t => t.Text.Contains("문의 제목", StringComparison.Ordinal)));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(alerts.Width == 400 && alerts.Height == 278 && HasIcon(alerts, "board-notification-close.svg") && HasIcon(alerts, "board-note.svg"), "Notifications match Figma geometry and original icons");
            if (Environment.GetEnvironmentVariable("BOARD_ALERT_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(alerts, capture);
        }
        finally { alerts.Close(); }
    }
    private static bool HasIcon(DependencyObject root, string asset) => Descendants(root).OfType<SvgViewbox>().Any(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true && i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static async Task WaitAsync(Func<bool> predicate)
    { var until = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20); if (!predicate()) throw new InvalidOperationException("Board fixture did not load"); }
    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" },
                "/api/board/posts" => new { page = 1, totalCount = 1, notices = Array.Empty<object>(), items = new[] { new { postId = 42, title = "문의 제목", answerCount = 1, author = "작성자", secret = false, createdAt = "2026-10-21", viewCount = 4 } } },
                "/api/board/notifications" => new { unreadCount = 1, items = new[] { new { notificationId = 1, postId = 42, title = "문의 제목", answerCount = 1, lastAnsweredAt = "2026-10-22", read = false } } },
                _ => new { }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
    }
}
