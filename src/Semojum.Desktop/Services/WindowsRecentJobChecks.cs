using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsRecentJobChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api, startEmpty: true); window.Show();
        try
        {
            await Open(window);
            check(window.Session.Blocks.Single().Source == "서버에 저장한 수정 원문" && window.Session.Blocks.Single().Braille == "⠿⠃"
                && window.Session.Blocks.Single().HasManualBraille,
                "QA1110: MainWindow recent-job open restores saved source and protected manual braille");
            check(window.PageEditor.Editor.Text.Contains("서버에 저장한 수정 원문", StringComparison.Ordinal),
                "QA1110: restored server edits appear in the visible source editor");
            var sameDocument = window.Session;
            sameDocument.ActiveBlock!.Source = "아직 저장하지 않은 현재 편집";
            var requests = fixture.PageRequests;
            await Open(window);
            check(ReferenceEquals(window.Session, sameDocument) && window.Session.ActiveBlock!.Source == "아직 저장하지 않은 현재 편집"
                && fixture.PageRequests == requests,
                "QA1110: reopening an already-open job preserves unsaved edits without refetching pages");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
        var reopened = new MainWindow(api, startEmpty: true); reopened.Show();
        try
        {
            await Open(reopened);
            check(fixture.PageRequests == 2 && reopened.Session.Blocks.Single().Source == "서버에 저장한 수정 원문"
                && reopened.Session.Blocks.Single().Braille == "⠿⠃",
                "QA1110: reopening after editor shutdown restores server save, not discarded local edits");
            check(fixture.Unexpected.Count == 0, "QA1110: recent-job verification uses only expected isolated HTTP requests");
        }
        finally { await reopened.ReleaseResourcesAsync(); reopened.PrepareSmokeShutdown(); reopened.Close(); }
    }
    private static async Task Open(MainWindow window)
    {
        var method = typeof(MainWindow).GetMethod("LoadJobAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)method.Invoke(window, ["saved-job", "저장본.smj", null, null, null])!;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((window.Session.JobId != "saved-job" || !window.Session.Blocks.Any(b => b.ServerId == "saved-block")) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int PageRequests;
        internal List<string> Unexpected { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login", StringComparison.Ordinal)) return Task.FromResult(Ok(new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" }));
            if (path.EndsWith("/options", StringComparison.Ordinal)) return Task.FromResult(Ok(new { }));
            if (path.EndsWith("/events", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/status", StringComparison.Ordinal)) return Task.FromResult(Ok(new { totalPages = 1, completedPages = 1, overallStatus = "COMPLETED", pages = new Dictionary<string, string> { ["1"] = "COMPLETED" } }));
            if (path.EndsWith("/pages/1", StringComparison.Ordinal))
            {
                PageRequests++;
                return Task.FromResult(Ok(new { status = "COMPLETED", result = new
                {
                    text_list = new[] { new { id = "saved-block", type = "text", contents = new[] { "서버에 저장한 수정 원문" } } },
                    braille_text_list = new[] { new { id = "saved-block", contents = new[] { "⠿⠃" } } },
                    bounding_box_list = Array.Empty<object>()
                }, original = new { type = "text", lines = new[] { "업로드 당시 원문" } } }));
            }
            Unexpected.Add(path); return Task.FromResult(Ok(new { }));
        }
        private static HttpResponseMessage Ok(object result) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
    }
}