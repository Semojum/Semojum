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

internal static class WindowsUsageDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture");
        var dialog = new UsageDialog(api); dialog.Show();
        try
        {
            await WaitAsync(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "8,251"));
            await WaitAsync(() => fixture.JobRequests > 0);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var asset in new[] { "account-caption-min.svg", "account-caption-max.svg", "account-caption-close.svg", "account-chevron.svg" })
            {
                var icon = Descendants(dialog).OfType<SvgViewbox>().FirstOrDefault(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true);
                check(icon is { Width: 16, Height: 16, ActualWidth: > 0, ActualHeight: > 0, Drawings.Bounds.IsEmpty: false }, $"{asset} uses the non-empty Figma drawing at its 16px design size");
            }
            var usageBar = Descendants(dialog).OfType<ProgressBar>().FirstOrDefault(b => b.Value == 17.49);
            var fill = usageBar?.Template.FindName("RangeFill", usageBar) as FrameworkElement;
            check(fill is { ActualWidth: > 80 }, $"The returned 17.49% usage fills the visible bar ({fill?.ActualWidth:0.##} px)");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "내 작업별 크레딧") && fixture.JobRequests > 0,
                "The usage page displays the real job list without switching views");
            check(Descendants(dialog).OfType<DataGrid>().Any(g => g.Items.Cast<object>().Any(x => x.ToString()!.Contains("실제 작업.pdf", StringComparison.Ordinal))), "The job list shows the returned document");
            var badges = Descendants(dialog).OfType<Border>().Where(b => b.Child is TextBlock t && new[] { "완료", "일부 쪽 실패", "변환 중", "대기 중" }.Contains(t.Text)).ToArray();
            check(badges.Length == 4 && badges.All(b => b.CornerRadius.TopLeft >= 9 && b.BorderThickness.Left == 1), "Returned job states are shown as four distinct Figma pill badges");
            var expectedColors = new Dictionary<string, Color> { ["완료"] = Color.FromRgb(15, 123, 15), ["일부 쪽 실패"] = Color.FromRgb(196, 43, 28), ["변환 중"] = Color.FromRgb(157, 93, 0), ["대기 중"] = Color.FromRgb(95, 95, 95) };
            check(badges.Length == 4 && badges.All(b => b.Child is TextBlock t && b.BorderBrush is SolidColorBrush edge && t.Foreground is SolidColorBrush text && edge.Color == expectedColors[t.Text] && text.Color == edge.Color), "Each real job status uses its corresponding Figma label and color");
            check(!fixture.Paths.Any(p => p.StartsWith("/api/org/", StringComparison.Ordinal)), "My page does not call privileged organization APIs");
            if (Environment.GetEnvironmentVariable("USAGE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(dialog, capture);
            dialog.Navigation.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("과금 내역을 조회할 수 없습니다", StringComparison.Ordinal)), "Unavailable monthly billing is explained without fabricated transactions");
            if (Environment.GetEnvironmentVariable("BILLING_CAPTURE") is { Length: > 0 } billCapture) await WindowsSmoke.RenderElementAsync(dialog, billCapture);
            dialog.Navigation.SelectedIndex = 3;
            await WaitAsync(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "서버가 돌려준 문의 내용"));
            check(fixture.MineRequested && fixture.PostRequests == 1, "Inquiry history loads the user's posts and selected post from the server");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "실제 운영자 답변"), "Inquiry details show the returned operator answer");
            if (Environment.GetEnvironmentVariable("INQUIRY_CAPTURE") is { Length: > 0 } inquiryCapture) await WindowsSmoke.RenderElementAsync(dialog, inquiryCapture);
        }
        finally { dialog.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new InvalidOperationException("Usage fixture did not load");
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int JobRequests { get; private set; }
        internal int PostRequests { get; private set; }
        internal bool MineRequested { get; private set; }
        internal List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            object result = path switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER", alias = "검수자" },
                "/api/users/usage/daily" => new { from = "2026-10-01", to = "2026-10-27", orgRemaining = 8251, orgAllocated = 10000, usedCredits = 1749, usedPercent = 17.49,
                    daily = new[] { new { date = "2026-10-21", credits = 1749 } }, bySource = new[] { new { source = "PDF_HWP", credits = 1749 } } },
                "/api/users/usage/jobs" => Jobs(),
                "/api/board/posts" => Posts(request.RequestUri.Query),
                "/api/board/posts/42" => Post(),
                _ => new { }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
        private object Jobs()
        {
            JobRequests++;
            return new { items = new[] {
                new { jobId = "job:1", fileName = "실제 작업.pdf", status = "COMPLETED", failedPages = 0, donePages = 4, totalPages = 4, credits = 4, finishedAt = "2026-10-21" },
                new { jobId = "job:2", fileName = "부분 실패.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1, donePages = 3, totalPages = 4, credits = 3, finishedAt = "2026-10-21" },
                new { jobId = "job:3", fileName = "진행 중.pdf", status = "IN_PROGRESS", failedPages = 0, donePages = 2, totalPages = 4, credits = 2, finishedAt = "" },
                new { jobId = "job:4", fileName = "대기.pdf", status = "PENDING", failedPages = 0, donePages = 0, totalPages = 4, credits = 0, finishedAt = "" }
            } };
        }
        private object Posts(string query)
        {
            MineRequested = query.Contains("mine=true", StringComparison.OrdinalIgnoreCase);
            return new { page = 1, totalCount = 1, notices = Array.Empty<object>(), items = new[] { new { postId = 42, title = "서버 문의", answerCount = 1, author = "검수자", secret = false, createdAt = "2026-10-21", viewCount = 1 } } };
        }
        private object Post()
        {
            PostRequests++;
            return new { postId = 42, title = "서버 문의", content = "서버가 돌려준 문의 내용", author = "검수자", mine = true, secret = false, hidden = false, createdAt = "2026-10-21", updatedAt = "2026-10-21", viewCount = 1,
                attachments = Array.Empty<object>(), answers = new[] { new { answerId = 7, content = "실제 운영자 답변", mine = false, answeredAt = "2026-10-22", updatedAt = "2026-10-22" } } };
        }
    }
}
