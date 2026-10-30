using System.Net;
using System.Net.Http;
using System.Globalization;
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
            var track = usageBar?.Template.FindName("UsageTrack", usageBar) as FrameworkElement;
            var balance = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "OrganizationCreditBalance");
            var allocation = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "OrganizationCreditAllocation");
            var purchase = Descendants(dialog).OfType<Button>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "OrganizationCreditPurchase");
            var originalWidth = dialog.Width;
            foreach (var requestedWidth in new[] { originalWidth, 960d, 480d })
            {
                dialog.Width = requestedWidth;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
                var trackWidth = track?.ActualWidth ?? 0;
                var fillWidth = fill?.ActualWidth ?? 0;
                var trackBounds = track?.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, trackWidth, track.ActualHeight)) ?? Rect.Empty;
                check(usageBar is { Value: 17.49, IsVisible: true } && trackWidth > 0 && fillWidth > 0 &&
                      Math.Abs(fillWidth - trackWidth * 0.1749) <= 1.5 &&
                      trackBounds.Left >= -1 && trackBounds.Right <= dialog.ActualWidth + 1,
                    $"The returned 17.49% usage remains proportional and visible at {dialog.ActualWidth:0.#}px (fill={fillWidth:0.##}, track={trackWidth:0.##})");
                var balanceBounds = balance.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, balance.ActualWidth, balance.ActualHeight));
                var allocationBounds = allocation.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, allocation.ActualWidth, allocation.ActualHeight));
                var purchaseBounds = purchase.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, purchase.ActualWidth, purchase.ActualHeight));
                var displayedText = new FormattedText(allocation.Text, CultureInfo.CurrentCulture, allocation.FlowDirection,
                    new Typeface(allocation.FontFamily, allocation.FontStyle, allocation.FontWeight, allocation.FontStretch),
                    allocation.FontSize, allocation.Foreground, VisualTreeHelper.GetDpi(allocation).PixelsPerDip)
                    { MaxTextWidth = Math.Max(1, allocation.ActualWidth) };
                check(balance.IsVisible && allocation.IsVisible && purchase.IsVisible && purchase.IsEnabled &&
                      allocation.ActualWidth > 0 && allocation.ActualHeight + 2 >= displayedText.Height &&
                      !Overlaps(allocationBounds, purchaseBounds) && !Overlaps(balanceBounds, purchaseBounds) &&
                      allocationBounds.Left >= -1 && allocationBounds.Right <= dialog.ActualWidth + 1 &&
                      purchaseBounds.Left >= -1 && purchaseBounds.Right <= dialog.ActualWidth + 1,
                    $"Organization balance, full allocation and purchase action remain readable without overlap at {dialog.ActualWidth:0.#}px (allocation={allocationBounds}, purchase={purchaseBounds})");
            }
            var originalBalance = balance.Text; var originalAllocation = allocation.Text;
            balance.Text = "2,147,483,647";
            allocation.Text = "잔여 / 할당 2,147,483,647 크레딧";
            dialog.Width = 480;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var longBalance = balance.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, balance.ActualWidth, balance.ActualHeight));
            var longAllocation = allocation.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, allocation.ActualWidth, allocation.ActualHeight));
            var longPurchase = purchase.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, purchase.ActualWidth, purchase.ActualHeight));
            FormattedText Measure(TextBlock block) => new(block.Text, CultureInfo.CurrentCulture, block.FlowDirection,
                new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch), block.FontSize,
                block.Foreground, VisualTreeHelper.GetDpi(block).PixelsPerDip) { MaxTextWidth = Math.Max(1, block.ActualWidth) };
            check(balance.ActualWidth > 0 && allocation.ActualWidth > 0 &&
                  balance.ActualHeight + 2 >= Measure(balance).Height && allocation.ActualHeight + 2 >= Measure(allocation).Height &&
                  !Overlaps(longBalance, longAllocation) && !Overlaps(longBalance, longPurchase) && !Overlaps(longAllocation, longPurchase) &&
                  longBalance.Right <= dialog.ActualWidth + 1 && longAllocation.Right <= dialog.ActualWidth + 1 && longPurchase.Right <= dialog.ActualWidth + 1,
                $"Long credit values remain readable and do not overlap at 480px (balance={longBalance}, allocation={longAllocation}, purchase={longPurchase})");
            balance.Text = originalBalance; allocation.Text = originalAllocation;
            dialog.Width = originalWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "내 작업별 크레딧") && fixture.JobRequests > 0,
                "The usage page displays the real job list without switching views");
            check(Descendants(dialog).OfType<DataGrid>().Any(g => g.Items.Cast<object>().Any(x => x.ToString()!.Contains("실제 작업.pdf", StringComparison.Ordinal))), "The job list shows the returned document");
            var badges = Descendants(dialog).OfType<Border>().Where(b => b.Child is TextBlock t && new[] { "완료", "부분 실패 1쪽", "진행 중 2/4", "대기 중" }.Contains(t.Text)).ToArray();
            check(badges.Length == 4 && badges.All(b => b.CornerRadius.TopLeft >= 9 && b.BorderThickness.Left == 1), "Returned job states are shown as four distinct Figma pill badges");
            var expectedColors = new Dictionary<string, Color> { ["완료"] = Color.FromRgb(15, 123, 15), ["부분 실패 1쪽"] = Color.FromRgb(196, 43, 28), ["진행 중 2/4"] = Color.FromRgb(157, 93, 0), ["대기 중"] = Color.FromRgb(95, 95, 95) };
            check(badges.Length == 4 && badges.All(b => b.Child is TextBlock t && b.BorderBrush is SolidColorBrush edge && t.Foreground is SolidColorBrush text && edge.Color == expectedColors[t.Text] && text.Color == edge.Color), "Each real job status uses its corresponding Figma label and color");
            check(badges.Length == 4 && badges.All(b => b.Child is TextBlock t && System.Windows.Automation.AutomationProperties.GetName(b) == t.Text), "Badge accessibility names include the same actual status and counts");
            var jobGrid = Descendants(dialog).OfType<DataGrid>().Single(g => g.Items.Cast<object>().Any(x => x.ToString()!.Contains("실제 작업.pdf", StringComparison.Ordinal)));
            string Row(string filename, string property)
            {
                var row = jobGrid.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("FileName")?.GetValue(x), filename));
                return row.GetType().GetProperty(property)?.GetValue(row)?.ToString() ?? "";
            }
            check(Row("진행 중.pdf", "BadgeText") == "진행 중 2/4" && Row("진행 중.pdf", "Pages") == "2/4", "Actual progress appears in the badge and the separate pages column remains unchanged");
            check(Row("부분 실패.pdf", "BadgeText") == "부분 실패 1쪽", "The partial failure badge uses the actual server-provided count");
            check(Row("완료 일부 실패.pdf", "BadgeText") == "부분 실패 1쪽", "A completed job with failed pages shows partial failure as in the server contract");
            check(Row("진행 0.pdf", "BadgeText") == "진행 중 0/4", "A valid zero progress count is displayed");
            check(new[] { "진행 없음.pdf", "진행 null.pdf", "진행 음수.pdf", "진행 초과.pdf", "진행 문자.pdf", "전체 0.pdf" }.All(n => Row(n, "BadgeText") == "변환 중"), "Missing, null, negative, excessive, nonnumeric or zero-total progress never fabricates a count");
            check(new[] { "실패 없음.pdf", "실패 0.pdf", "실패 초과.pdf", "실패 음수.pdf", "실패 전체.pdf", "실패 소수.pdf", "실패 총수 없음.pdf" }.All(n => Row(n, "BadgeText") == "일부 쪽 실패"), "Partial failure omits unverifiable or inconsistent counts");
            check(Row("완료 0.pdf", "BadgeText") == "완료" && Row("완료 null.pdf", "BadgeText") == "완료", "Completed jobs keep their state for valid zero and server-null failure counts");
            check(Row("완료 오류.pdf", "BadgeText") == "상태 확인 필요", "A malformed completed failure count is not misreported as successful completion");
            check(Row("완전 실패.pdf", "BadgeText") == "변환 실패", "A fully failed job is not described as a partial failure");
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
    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right - .5 && a.Right > b.Left + .5 &&
        a.Top < b.Bottom - .5 && a.Bottom > b.Top + .5;
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
            return new { items = new object[] {
                new { jobId = "job:1", fileName = "실제 작업.pdf", status = "COMPLETED", failedPages = 0, donePages = 4, totalPages = 4, credits = 4, finishedAt = "2026-10-21" },
                new { jobId = "job:2", fileName = "부분 실패.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1, donePages = 3, totalPages = 4, credits = 3, finishedAt = "2026-10-21" },
                new { jobId = "job:3", fileName = "진행 중.pdf", status = "IN_PROGRESS", failedPages = 0, donePages = 2, totalPages = 4, credits = 2, finishedAt = "" },
                new { jobId = "job:4", fileName = "대기.pdf", status = "PENDING", failedPages = 0, donePages = 0, totalPages = 4, credits = 0, finishedAt = "" },
                new { jobId = "job:5", fileName = "진행 0.pdf", status = "IN_PROGRESS", donePages = 0, totalPages = 4 },
                new { jobId = "job:6", fileName = "진행 없음.pdf", status = "IN_PROGRESS", totalPages = 4 },
                new { jobId = "job:7", fileName = "진행 null.pdf", status = "IN_PROGRESS", donePages = (int?)null, totalPages = 4 },
                new { jobId = "job:8", fileName = "진행 음수.pdf", status = "IN_PROGRESS", donePages = -1, totalPages = 4 },
                new { jobId = "job:9", fileName = "진행 초과.pdf", status = "IN_PROGRESS", donePages = 5, totalPages = 4 },
                new { jobId = "job:10", fileName = "진행 문자.pdf", status = "IN_PROGRESS", donePages = "2", totalPages = 4 },
                new { jobId = "job:11", fileName = "전체 0.pdf", status = "IN_PROGRESS", donePages = 0, totalPages = 0 },
                new { jobId = "job:12", fileName = "실패 없음.pdf", status = "PARTIALLY_COMPLETED", totalPages = 4 },
                new { jobId = "job:13", fileName = "실패 0.pdf", status = "PARTIALLY_COMPLETED", failedPages = 0, totalPages = 4 },
                new { jobId = "job:14", fileName = "실패 초과.pdf", status = "PARTIALLY_COMPLETED", failedPages = 5, totalPages = 4 },
                new { jobId = "job:15", fileName = "실패 음수.pdf", status = "PARTIALLY_COMPLETED", failedPages = -1, totalPages = 4 },
                new { jobId = "job:16", fileName = "실패 전체.pdf", status = "PARTIALLY_COMPLETED", failedPages = 4, totalPages = 4 },
                new { jobId = "job:17", fileName = "실패 소수.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1.5, totalPages = 4 },
                new { jobId = "job:18", fileName = "실패 총수 없음.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1 },
                new { jobId = "job:19", fileName = "완료 0.pdf", status = "COMPLETED", failedPages = 0, totalPages = 4 },
                new { jobId = "job:20", fileName = "완료 null.pdf", status = "COMPLETED", failedPages = (int?)null, totalPages = 4 },
                new { jobId = "job:21", fileName = "완료 오류.pdf", status = "COMPLETED", failedPages = -1, totalPages = 4 },
                new { jobId = "job:22", fileName = "완전 실패.pdf", status = "FAILED", failedPages = 4, totalPages = 4 },
                new { jobId = "job:23", fileName = "완료 일부 실패.pdf", status = "COMPLETED", failedPages = 1, totalPages = 4 }
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
