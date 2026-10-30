using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsPublicInquiryFieldsChecks
{
    private sealed class Fixture : HttpMessageHandler
    {
        internal readonly List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/api/public/inquiries") throw new InvalidOperationException("Unexpected fixture request");
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"UNAVAILABLE\",\"message\":\"fixture error\"}", Encoding.UTF8, "application/json") };
        }
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); var dialog = new PublicInquiryDialog(api);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? failure = null;
        dialog.Loaded += async (_, _) =>
        {
            try
            {
                var boxes = Descendants(dialog).OfType<TextBox>().ToArray();
                var organization = boxes.FirstOrDefault(b => AutomationProperties.GetName(b) == "기관명");
                var title = boxes.FirstOrDefault(b => AutomationProperties.GetName(b) == "제목");
                check(organization is not null && title is not null, "Public inquiry accepts distinct organization and title fields");
                if (organization is null || title is null) { dialog.Close(); done.TrySetResult(); return; }
                var email = boxes.Single(b => b.MaxLength == 100);
                var name = boxes.Single(b => b.MaxLength == 50);
                var message = boxes.Single(b => b.AcceptsReturn);
                var send = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
                email.Text = "fixture@example.test"; name.Text = "홍길동"; organization.Text = "기관 A"; title.Text = "문의 제목"; message.Text = "본문";
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (fixture.Bodies.Count < 1 && DateTime.UtcNow < deadline) await Task.Delay(25);
                await Task.Delay(100);
                if (fixture.Bodies.Count > 0)
                {
                    using var json = JsonDocument.Parse(fixture.Bodies[0]); var data = json.RootElement;
                    check(data.GetProperty("name").GetString() == "홍길동" && data.GetProperty("message").GetString() == "기관명: 기관 A\n제목: 문의 제목\n\n본문",
                        "Legacy sender name stays intact and organization and title reach the server in the message");
                }
                else check(false, "Inquiry request reaches the mock server");
                check(organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문" && dialog.IsVisible,
                    "A failed inquiry retains every entered field");
                message.Text = new string('가', 2000);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
                check(fixture.Bodies.Count == 1 && message.Text.Length == 2000,
                    "Combined message over the server limit is rejected without discarding input");
                message.Text = "본문";
                var cancel = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "취소"));
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
                int action = 0;
                timer.Tick += (_, _) =>
                {
                    var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == dialog && n.IsVisible);
                    if (notice is null) return;
                    notice.ActionButtons[action].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                try
                {
                    timer.Start(); cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    check(dialog.IsVisible && organization.Text == "기관 A" && title.Text == "문의 제목" && message.Text == "본문" && fixture.Bodies.Count == 1,
                        "Continuing after cancel preserves the inquiry draft without sending again");
                    action = 1; cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                finally { timer.Stop(); }
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                failure = ex; done.TrySetResult();
                foreach (var input in Descendants(dialog).OfType<TextBox>()) input.Clear();
                if (dialog.IsVisible) dialog.Close();
            }
        };
        dialog.Show(); await done.Task.WaitAsync(TimeSpan.FromSeconds(8));
        if (failure is not null) throw failure;
        var choiceOnly = new PublicInquiryDialog(api); choiceOnly.Show();
        var type = Descendants(choiceOnly).OfType<ComboBox>().Single();
        var cancelOnly = Descendants(choiceOnly).OfType<Button>().Single(b => Equals(b.Content, "취소"));
        var confirmation = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        confirmation.Tick += (_, _) =>
        {
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == choiceOnly && n.IsVisible);
            if (notice is not null) notice.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try
        {
            type.SelectedIndex = 0;
            confirmation.Start(); cancelOnly.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(choiceOnly.IsVisible && type.SelectedIndex == 0 && fixture.Bodies.Count == 1,
                "Continuing after cancel preserves a changed inquiry type even when other fields are empty");
        }
        finally { confirmation.Stop(); if (choiceOnly.IsVisible) { type.SelectedIndex = 2; choiceOnly.Close(); } }
        var untouched = new PublicInquiryDialog(api); untouched.Show(); untouched.Close();
        check(!untouched.IsVisible && fixture.Bodies.Count == 1, "An untouched inquiry closes without confirmation or a request");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
