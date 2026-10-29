using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsUncertainRestartChecks
{
    private sealed class LateRestartFixture : HttpMessageHandler
    {
        internal bool ResumeAttempted, LateAccepted;
        internal int StatusRequests, SaveRequests;
        internal void DeliverLateRequest() => LateAccepted = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Reply("{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}");
            if (path.EndsWith("/resume")) { ResumeAttempted = true; throw new HttpRequestException("connection lost while request is still in flight"); }
            if (path.EndsWith("/status")) { StatusRequests++; return Reply("{\"totalPages\":1,\"completedPages\":1,\"overallStatus\":\"COMPLETED\",\"pages\":{\"1\":\"COMPLETED\"}}"); }
            if (path.EndsWith("/pages/1")) return Reply("{\"status\":\"COMPLETED\",\"original\":{\"type\":\"text\"},\"result\":{\"text_list\":[{\"id\":\"server-old\",\"type\":\"text\",\"contents\":[\"old server result\"]}],\"braille_text_list\":[],\"bounding_box_list\":[]}}");
            if (path.EndsWith("/events")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (path.EndsWith("/save")) { SaveRequests++; throw new InvalidOperationException("A stale server save must not be sent"); }
            throw new InvalidOperationException("Unexpected fixture path: " + path);
        }
        private static Task<HttpResponseMessage> Reply(string result) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
    }
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var fixture = new LateRestartFixture();
        using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show();
        var path = Path.Combine(directory, "late-restart-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            var model = new DocumentSession { Title = "local.smj", JobId = "late-job", OwnerLoginId = "fixture", JobStatus = "FAILED" };
            model.PageStatuses[1] = "BLOCKED";
            model.Add(new EditorBlock { Id = 1, Page = 1, ServerId = "server-old", Source = "local edits", Metadata = new(Type: "text") });
            model.ServerPages[1] = [new("server-old", false)];
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); await window.OpenAsync(path);
            window.Session.CreditNotice = "크레딧이 소진되었습니다.";
            Descendants(window.CreditBanner).OfType<Button>().Single(b => b.Content?.ToString() == "이어서 처리").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var limit = DateTime.UtcNow.AddSeconds(5);
            while (!fixture.ResumeAttempted && DateTime.UtcNow < limit) await Task.Delay(25);
            if (!fixture.ResumeAttempted) throw new InvalidOperationException("Resume request did not start");
            window.CheckPendingRestart.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(250);
            check(window.Session.PendingRestart is { Unconfirmed: true }, "Old complete status cannot prove the uncertain resume was not accepted");
            fixture.DeliverLateRequest();
            await window.ExecuteAsync("save");
            var reopened = await SmjFile.LoadAsync(path);
            check(fixture.LateAccepted && fixture.SaveRequests == 0 && reopened.PendingRestart is { Unconfirmed: true }
                && reopened.Blocks.Single().Source == "local edits", "Late acceptance keeps server save blocked and preserves the local recovery file");
            check(window.Session.ErrorNotice.Contains("운영자"), "An uncertain restart explains the safe next step");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
