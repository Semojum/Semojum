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

namespace Semojum.Desktop.Services;

internal static class WindowsUsageFeedbackChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        static (double? Personal, double? Organization, decimal? OrganizationUsed) Ratios(string text)
        { using var document = JsonDocument.Parse(text); return UsageDialog.UsagePercentages(document.RootElement); }
        var valid = Ratios("{\"orgAllocated\":1000,\"orgRemaining\":600,\"usedCredits\":100,\"usedPercent\":10}");
        check(valid is { Personal: 10, Organization: 40, OrganizationUsed: 400 }, "Personal and institution bars use their own server-supported numerators");
        check(Ratios("{\"orgAllocated\":null,\"orgRemaining\":null,\"usedCredits\":10,\"usedPercent\":null}") == (null, null, null)
            && Ratios("{\"orgAllocated\":0,\"orgRemaining\":0,\"usedCredits\":0,\"usedPercent\":0}") == (null, null, null),
            "No allocation or zero denominator never invents a usage percentage");
        var inconsistent = Ratios("{\"orgAllocated\":100,\"orgRemaining\":120,\"usedCredits\":20,\"usedPercent\":90}");
        check(inconsistent.Personal is null && inconsistent.Organization is null, "Contradictory personal and institution usage values do not produce bars");
        var zero = Ratios("{\"orgAllocated\":100,\"orgRemaining\":100,\"usedCredits\":0,\"usedPercent\":0}");
        check(zero is { Personal: 0, Organization: 0, OrganizationUsed: 0 }, "Verified zero usage is shown as zero rather than missing data");

        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture"); var dialog = new UsageDialog(api); dialog.Show();
        try
        {
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "600"));
            var bars = Descendants(dialog).OfType<ProgressBar>().ToArray();
            var personal = bars.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "내 크레딧 사용률");
            var organization = bars.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "기관 크레딧 사용률");
            check(personal.Value == 10 && organization.Value == 40 && personal.IsVisible && organization.IsVisible,
                "The visible personal and institution bars display 10% and 40% from distinct real data");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("100 / 1,000 크레딧")) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("400 / 1,000 크레딧")),
                "Both usage cards label their actual credit numerators and common allocation");
            var jobs = Descendants(dialog).OfType<DataGrid>().Single(g => g.Columns.Any(c => Equals(c.Header, "모드")));
            await Wait(() => jobs.Items.Count > 0);
            var job = jobs.Items[0];
            check(Equals(job.GetType().GetProperty("Mode")?.GetValue(job), "통합 변환") && jobs.Columns.Any(c => c is DataGridTemplateColumn && Equals(c.Header, "")),
                "The job list uses the server mode and provides a per-row open action");
            dialog.Navigation.SelectedIndex = 3;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            await Wait(() => Descendants(dialog).OfType<ListBox>().Any());
            var list = Descendants(dialog).OfType<ListBox>().Single(); await Wait(() => list.Items.Count == 10);
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            check(list.Items.Count == 10 && fixture.MineOnly, "A long first inquiry page shows only the current user's server results");
            list.SelectedIndex = 1; await Wait(() => fixture.SecondStarted);
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "문의 내용을 불러오는 중입니다.") && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"),
                "Changing the selected inquiry clears the previous detail while the new response is pending");
            list.SelectedIndex = 0; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            fixture.FailSecond(); await Task.Delay(80);
            check(list.SelectedIndex == 0 && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"), "A late failure from a previous selection cannot replace the current detail");
            list.SelectedIndex = 1; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "문의 내용을 불러오지 못했습니다."));
            check(!Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문") && list.SelectedIndex == 1,
                "A failed selected inquiry keeps the selection without mislabelling the previous content");
            var next = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "다음"));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => fixture.PageTwoRequests >= 1);
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("문의 내역을 불러오지 못했습니다.")));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => fixture.PageTwoRequests >= 2);
            check(fixture.PageTwoRequests == 2 && list.SelectedIndex == 1 && list.Items.Count == 10,
                "A failed next-page request preserves the visible page and retries the same page");
            check(next.IsEnabled && Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "상세 열기")).IsEnabled,
                "A page failure leaves the valid retry and selected inquiry actions available");
            fixture.PageTwoFails = false; next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "보낸 문의가 없습니다."));
            check(list.Items.Count == 0 && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"),
                "An empty server page clears the former inquiry detail");
            check(!next.IsEnabled && !Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "상세 열기")).IsEnabled
                && Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "이전")).IsEnabled,
                "An empty final page disables next and detail actions while allowing a return to the previous page");
            check(!Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal)),
                "A successful retry clears the earlier inquiry error instead of presenting it as current");
            if (Environment.GetEnvironmentVariable("USAGE_EMPTY_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(dialog, capture);
            var status = (TextBlock)typeof(CompactDialog).GetField("Status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
            status.Text = "다른 화면의 상태 메시지";
            var previous = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "이전")); previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => list.Items.Count == 10 && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            check(status.Text == "다른 화면의 상태 메시지", "Successful inquiry loading does not erase unrelated status messages");
            check(next.IsEnabled && Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "상세 열기")).IsEnabled,
                "Returning to a populated page restores valid next-page and selected-detail actions");
        }
        finally { dialog.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static async Task Wait(Func<bool> predicate)
    { for (var i = 0; i < 250 && !predicate(); i++) await Task.Delay(20); if (!predicate()) throw new TimeoutException("Usage feedback fixture did not reach its expected state."); }
    private sealed class Fixture : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool MineOnly; internal bool SecondStarted; internal bool PageTwoFails = true; internal int PageTwoRequests;
        internal void FailSecond() => second.TrySetResult(Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"DENIED\",\"message\":\"접근할 수 없습니다\"}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/board/posts/2") { SecondStarted = true; return second.Task; }
            if (path == "/api/board/posts" && request.RequestUri.Query.Contains("page=2", StringComparison.OrdinalIgnoreCase))
            {
                PageTwoRequests++;
                if (PageTwoFails) return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"ERROR\",\"message\":\"실패\"}"));
                return Ok(new { page = 2, totalCount = 11, notices = Array.Empty<object>(), items = Array.Empty<object>() });
            }
            object result = path switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" },
                "/api/users/usage/daily" => new { from = "2026-10-01", to = "2026-10-30", orgRemaining = 600, orgAllocated = 1000, usedCredits = 100, usedPercent = 10, daily = Array.Empty<object>(), bySource = Array.Empty<object>() },
                "/api/users/usage/jobs" => new { items = new[] { new { jobId = "job", fileName = "작업.pdf", mode = "c", status = "COMPLETED", donePages = 1, totalPages = 1, failedPages = 0, credits = 1, finishedAt = "2026-10-30" } } },
                "/api/board/posts" => Posts(request.RequestUri.Query),
                "/api/board/posts/1" => new { postId = 1, title = "첫 문의", content = "첫 문의 본문", author = "나", mine = true, secret = false, hidden = false, createdAt = "2026-10-01", updatedAt = "2026-10-01", viewCount = 1, attachments = Array.Empty<object>(), answers = Array.Empty<object>() },
                _ => new { }
            };
            return Ok(result);
        }
        private object Posts(string query)
        {
            MineOnly = query.Contains("mine=true", StringComparison.OrdinalIgnoreCase);
            return new { page = 1, totalCount = 11, notices = Array.Empty<object>(), items = Enumerable.Range(1, 10).Select(i => new { postId = i, title = $"문의 {i}", answerCount = 0, author = "나", secret = false, createdAt = "2026-10-01", viewCount = 1 }).ToArray() };
        }
        private static Task<HttpResponseMessage> Ok(object result) => Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { isSuccess = true, result })));
        private static HttpResponseMessage Json(HttpStatusCode status, string text) => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
