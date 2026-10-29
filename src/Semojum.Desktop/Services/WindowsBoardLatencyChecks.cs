using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsBoardLatencyChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        internal bool BlockList;
        internal bool Cancelled;
        internal string? LastQuery;
        internal int Total;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login"))
                return Json("{\"isSuccess\":true,\"result\":{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}}");
            LastQuery = request.RequestUri.Query;
            if (BlockList)
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { Cancelled = true; throw; }
            }
            var rows = string.Join(',', Enumerable.Range(1, Total == 0 ? 0 : 10).Select(i => $"{{\"postId\":{i},\"title\":\"제목{i}\",\"answerCount\":0,\"author\":\"test\",\"secret\":false,\"createdAt\":\"2026-10-29\",\"viewCount\":0}}"));
            return Json($"{{\"isSuccess\":true,\"result\":{{\"page\":1,\"totalCount\":{Total},\"notices\":[],\"items\":[{rows}]}}}}");
        }
        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var total in new[] { 0, 1_000_000 })
        {
            var handler = new Handler { Total = total };
            using var http = new HttpClient(handler);
            var api = new SemojumApiClient(http);
            await api.LoginAsync("editor", "fixture");
            var dialog = new BoardDialog(new BoardClient(api), "fixture", initiallyMine: true);
            dialog.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = Descendants(dialog).OfType<DataGrid>().Single();
            check(grid.Items.Count == (total == 0 ? 0 : 10) && handler.LastQuery!.Contains("mine=true") && handler.LastQuery.Contains("page=1"),
                $"Inquiry list displays the server page for {total} total results");
            dialog.Close();
        }
        {
            var handler = new Handler { BlockList = true };
            using var http = new HttpClient(handler);
            var api = new SemojumApiClient(http);
            await api.LoginAsync("editor", "fixture");
            var dialog = new BoardDialog(new BoardClient(api), "fixture", initiallyMine: true);
            dialog.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            dialog.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!dialog.IsVisible && handler.Cancelled, "Closing a slow inquiry list cancels its outstanding request");
            if (dialog.IsVisible) { handler.BlockList = false; http.CancelPendingRequests(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.Close(); }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
