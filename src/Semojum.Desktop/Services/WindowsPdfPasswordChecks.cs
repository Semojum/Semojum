using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Figma Case 3 and the BE 2026-11-01 encrypted PDF upload (JOB4018 no password, JOB4019 wrong password; no job is made).</summary>
internal static class WindowsPdfPasswordChecks
{
    // ASCII values, so a JSON escape of the work file cannot hide them from the check below.
    private const string WrongPassword = "pdf-wrong-1103", RightPassword = "pdf-secret-1103";

    internal static async Task RunAsync(Action<bool, string> check)
    {
        await CheckDesignAsync(check);
        await CheckUploadAsync(check);
    }

    private static async Task CheckDesignAsync(Action<bool, string> check)
    {
        var prompt = FileNotices.EncryptedPdf(); prompt.Show();
        try
        {
            prompt.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); prompt.UpdateLayout();
            check(Math.Abs(prompt.Width - 555) <= 1 && Math.Abs(prompt.Height - 226) <= 1 && prompt.MessageText == "암호가 걸린 PDF 입니다."
                  && prompt.DetailText == "문서 암호를 입력하면 변환을 이어서 합니다. 암호는 저장하지 않습니다."
                  && prompt.ActionButtons.Select(b => b.Content as string).SequenceEqual(["확인", "취소"]),
                "Encrypted PDF notice follows the Figma Case 3 size, wording and actions");
            var field = (Border)((Grid)prompt.PasswordInput.Parent).Parent;
            var label = Descendants(prompt).OfType<TextBlock>().Single(t => t.Text == "문서 암호");
            Point fieldAt = field.TranslatePoint(new Point(), prompt), labelAt = label.TranslatePoint(new Point(), prompt);
            check(Math.Abs(fieldAt.X - 24) <= 1 && Math.Abs(fieldAt.Y - 120) <= 3 && Math.Abs(field.ActualWidth - 507) <= 1 && Math.Abs(field.ActualHeight - 32) <= .5
                  && Math.Abs(labelAt.Y - 102) <= 3 && fieldAt.Y + field.ActualHeight <= prompt.ActualHeight - 64 && prompt.PasswordInput.IsVisible,
                $"The document password field sits at the Figma Case 3 place above the button row (field {fieldAt.X:0},{fieldAt.Y:0} {field.ActualWidth:0}x{field.ActualHeight:0}, label {labelAt.Y:0})");
            check(!prompt.ActionButtons[0].IsEnabled && prompt.ActionButtons[1].IsEnabled, "Confirm waits for a password while cancel stays available");
            prompt.PasswordInput.Password = "x";
            check(prompt.ActionButtons[0].IsEnabled && prompt.TakePassword() is null && prompt.PasswordInput.Password.Length == 0,
                "Typing enables confirm, and taking the password without confirming returns nothing and clears the box");
        }
        finally { prompt.Close(); }
        var wrong = FileNotices.EncryptedPdf(wrongPassword: true); wrong.Show();
        try
        {
            wrong.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(wrong.MessageText == "문서 암호가 맞지 않습니다." && wrong.DetailText.Contains("다시 입력", StringComparison.Ordinal) && wrong.PasswordInput.IsVisible
                  && Math.Abs(wrong.Height - 226) <= 1, "A wrong password reopens the same Case 3 window with a password field and says it did not match");
        }
        finally { wrong.Close(); }
    }

    private static async Task CheckUploadAsync(Action<bool, string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "semojum-pdf-password-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var pdf = Path.Combine(folder, "잠긴 문서.pdf");
        try
        {
            await File.WriteAllBytesAsync(pdf, Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF\n"));
            using var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
            var window = new MainWindow(api); window.Show();
            var answers = new Queue<string?>(); var prompts = new List<string>(); var errors = new List<string>();
            PdfPasswordDialog? lastPrompt = null; OpenDocumentDialog? lastSettings = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<PdfPasswordDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
                if (prompt is not null)
                {
                    if (ReferenceEquals(prompt, lastPrompt)) return;
                    lastPrompt = prompt; prompts.Add(prompt.MessageText);
                    var answer = answers.Count > 0 ? answers.Dequeue() : null;
                    if (answer is null) { prompt.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return; }
                    prompt.PasswordInput.Password = answer; prompt.ActionButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n is not PdfPasswordDialog && n.IsVisible && n.Owner == window);
                if (notice is not null) { errors.Add(notice.DetailText); notice.Close(); return; }
                var settings = Application.Current.Windows.OfType<OpenDocumentDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
                if (settings is not null && !ReferenceEquals(settings, lastSettings))
                {
                    lastSettings = settings;
                    Descendants(settings).OfType<Button>().Single(button => Equals(button.Content, "변환 시작")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            try
            {
                await window.ExecuteAsync("new"); var model = window.Session; model.ActiveBlock!.Source = "미저장 원문";
                var tabs = window.DocumentTabs.Items.Count; window.FilePicker = _ => [pdf]; timer.Start();

                // No password → Case 3 → a wrong password → the same window says so → cancel stops the upload without a job.
                answers.Enqueue(WrongPassword); answers.Enqueue(null);
                window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => model.Status.Contains("취소했습니다", StringComparison.Ordinal));
                check(fixture.Passwords.SequenceEqual([null, WrongPassword]) && prompts.SequenceEqual(["암호가 걸린 PDF 입니다.", "문서 암호가 맞지 않습니다."]) && errors.Count == 0,
                    "JOB4018 asks for the password, the retry carries it, and JOB4019 asks again in the same Case 3 window");
                check(ReferenceEquals(model, window.Session) && model.ActiveBlock!.Source == "미저장 원문" && window.DocumentTabs.Items.Count == tabs && fixture.StatusRequests == 0,
                    "Cancelling the password stops the upload and keeps the open document");

                // No password → Case 3 → the right password uploads the same file and opens the job.
                prompts.Clear(); fixture.Passwords.Clear(); answers.Enqueue(RightPassword);
                window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => window.Session.JobId == "job_pdf" && fixture.StatusRequests > 0);
                await Task.Delay(200); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var opened = window.Session; var original = await File.ReadAllBytesAsync(pdf);
                check(fixture.Passwords.SequenceEqual([null, RightPassword]) && prompts.Count == 1 && errors.Count == 0 && fixture.Files.Count == 4 && fixture.Files.All(bytes => bytes.SequenceEqual(original)),
                    "The right password re-sends the same PDF once with the password form part and opens the job");
                var saved = JsonSerializer.Serialize(SmjFile.Snapshot(opened).Manifest);
                check(fixture.Queries.All(query => query.Length == 0) && !saved.Contains(RightPassword, StringComparison.Ordinal) && !saved.Contains(WrongPassword, StringComparison.Ordinal)
                      && !opened.Status.Contains(RightPassword, StringComparison.Ordinal) && !opened.ErrorNotice.Contains(RightPassword, StringComparison.Ordinal),
                    "The password never goes into a URL, the work file or the status line");
            }
            finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 250 && !condition(); i++) await Task.Delay(20);
        if (!condition()) throw new TimeoutException("Encrypted PDF upload did not complete");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        public List<string?> Passwords { get; } = [];
        public List<byte[]> Files { get; } = [];
        public List<string> Queries { get; } = [];
        public int StatusRequests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; var status = HttpStatusCode.OK;
            string body;
            if (path.EndsWith("/login", StringComparison.Ordinal)) body = Ok("{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}");
            else if (path.EndsWith("/braille-forms", StringComparison.Ordinal)) body = Ok("{\"items\":[]}");
            else if (request.Method == HttpMethod.Post && path == "/api/jobs")
            {
                Queries.Add(request.RequestUri.Query);
                var form = (MultipartFormDataContent)request.Content!;
                foreach (var file in form.Where(p => p.Headers.ContentDisposition?.FileName is not null)) Files.Add(await file.ReadAsByteArrayAsync(cancellationToken));
                var part = form.FirstOrDefault(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "password");
                var password = part is null ? null : await part.ReadAsStringAsync(cancellationToken); Passwords.Add(password);
                (status, body) = password switch
                {
                    null => (HttpStatusCode.BadRequest, "{\"isSuccess\":false,\"code\":\"JOB4018\",\"message\":\"암호가 걸린 PDF입니다.\"}"),
                    RightPassword => (HttpStatusCode.OK, Ok("{\"jobId\":\"job_pdf\",\"totalPages\":0,\"status\":\"PENDING\"}")),
                    _ => (HttpStatusCode.BadRequest, "{\"isSuccess\":false,\"code\":\"JOB4019\",\"message\":\"문서 암호가 틀렸습니다.\"}")
                };
            }
            else if (path.EndsWith("/status", StringComparison.Ordinal))
            { StatusRequests++; body = Ok("{\"jobId\":\"job_pdf\",\"totalPages\":0,\"completedPages\":0,\"overallStatus\":\"COMPLETED\",\"pages\":{},\"estimatedRemainingSec\":null}"); }
            else if (path.EndsWith("/events", StringComparison.Ordinal)) { status = HttpStatusCode.NotFound; body = "{\"isSuccess\":false,\"code\":\"COMMON4004\",\"message\":\"없음\"}"; }
            else body = Ok("{}");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
        private static string Ok(string result) => "{\"isSuccess\":true,\"result\":" + result + "}";
    }
}
