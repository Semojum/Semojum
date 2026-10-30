using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsConversionOutcomeChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var result = await ExerciseAsync(choice: 1, partial: false);
        check(result.NoticeClicked && result.SettingsOpened && result.RetryRequests == 0 && result.PickerCalls == 0,
            "The conversion-settings button on an all-failed job opens settings without retrying");
        var retryAll = await ExerciseAsync(choice: 0, partial: false);
        check(retryAll.NoticeClicked && retryAll.PickerCalls == 1 && retryAll.RetryRequests == 0 && !retryAll.SettingsOpened && !retryAll.InquiryOpened,
            "The retry button on an all-failed job reopens the upload flow");
        var contact = await ExerciseAsync(choice: 2, partial: false);
        check(contact.NoticeClicked && contact.InquiryOpened && contact.RetryRequests == 0 && contact.PickerCalls == 0,
            "The inquiry button on an all-failed job opens the inquiry form without retrying");
        var retryPart = await ExerciseAsync(choice: 0, partial: true);
        check(retryPart.NoticeClicked && retryPart.RetryRequests == 1 && retryPart.PickerCalls == 0,
            "The retry button on a partially failed job requests only failed pages");
        var keep = await ExerciseAsync(choice: 1, partial: true);
        check(keep.NoticeClicked && keep.RetryRequests == 0 && keep.PickerCalls == 0 && !keep.SettingsOpened && !keep.InquiryOpened,
            "Opening a partially converted job does not retry or open another flow");
    }
    private sealed record Result(bool NoticeClicked, bool SettingsOpened, bool InquiryOpened, int RetryRequests, int PickerCalls);
    private static async Task<Result> ExerciseAsync(int choice, bool partial)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show(); var pickerCalls = 0; window.FilePicker = _ => { pickerCalls++; return null; };
        var model = window.Session; model.JobId = "case6"; model.PageStatuses[1] = partial ? "COMPLETED" : "FAILED"; model.PageStatuses[2] = "FAILED";
        var status = new JobStatus("case6", 2, 2, "FAILED", new() { [1] = partial ? "COMPLETED" : "FAILED", [2] = "FAILED" });
        var clicked = false; var settings = false; var inquiry = false; var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(w => w.Owner == window && w.IsVisible).ToArray())
            {
                if (dialog is NoticeDialog notice && !clicked)
                { clicked = true; notice.ActionButtons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                else if (dialog is DefaultsDialog options) { settings = true; options.Close(); }
                else if (dialog is PublicInquiryDialog contact) { inquiry = true; contact.Close(); }
                else if (DateTime.UtcNow >= deadline) dialog.Close();
            }
        };
        timer.Start();
        try
        {
            await window.ShowJobOutcomeAsync(model, status).WaitAsync(TimeSpan.FromSeconds(8));
            return new(clicked, settings, inquiry, fixture.RetryRequests, pickerCalls);
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int RetryRequests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath; object result;
            if (path.EndsWith("/login", StringComparison.Ordinal)) result = new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" };
            else if (path.EndsWith("/retry", StringComparison.Ordinal)) { RetryRequests++; result = new { jobId = "case6", status = "FAILED", retriedPages = Array.Empty<int>() }; }
            else if (path.EndsWith("/braille-forms", StringComparison.Ordinal)) result = new { items = new[] { new { formId = "builtin:GENERAL_BOOK", name = "일반 도서", builtin = true, baseFormId = (string?)null, layoutOptions = JobClient.LayoutBody(new()), brailleOptions = JobClient.OptionsBody(new()) } } };
            else if (path.EndsWith("/braille-options", StringComparison.Ordinal)) result = JobClient.OptionsBody(new());
            else throw new InvalidOperationException("Unexpected fixture request: " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
    }
}
