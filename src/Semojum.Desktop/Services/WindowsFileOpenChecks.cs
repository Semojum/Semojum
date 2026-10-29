using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsFileOpenChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var path = Path.Combine(directory, "selected-text.txt");
        await File.WriteAllTextAsync(path, "선택한 문서");
        using var http = new HttpClient(new Fixture());
        var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api);
        var previous = window.Session;
        window.SourceEditor.Document.Insert(0, "저장하지 않은 교정 ");
        var unsavedText = previous.ActiveBlock!.Source;
        bool selected = false, settingsSawSelection = false;
        var errorNotices = 0;
        window.FilePicker = _ => { selected = true; return [path]; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<OpenDocumentDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (dialog is not null)
            {
                settingsSawSelection = selected && dialog.FileNames.Text.Contains("selected-text.txt", StringComparison.Ordinal);
                dialog.DialogResult = false;
            }
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
            if (notice is not null) { errorNotices++; notice.Close(); }
        };
        window.Show(); timer.Start();
        try
        {
            await window.ExecuteAsync("open");
            check(settingsSawSelection, "Selecting a file precedes its settings dialog");
            check(ReferenceEquals(previous, window.Session) && previous.IsDirty && previous.ActiveBlock!.Source == unsavedText,
                "Cancelling file settings preserves the existing unsaved document");

            window.FilePicker = _ => null;
            await window.ExecuteAsync("open");
            check(ReferenceEquals(previous, window.Session) && previous.IsDirty && previous.ActiveBlock!.Source == unsavedText,
                "Cancelling the file picker preserves the existing unsaved document");

            var savedPath = Path.Combine(directory, "selected-work.smj");
            var saved = new DocumentSession { Title = "다른 문서.smj" };
            saved.Add(new EditorBlock { Id = 1, Page = 1, Source = "저장된 교정" });
            await SmjFile.SaveAsync(savedPath, SmjFile.Snapshot(saved));
            settingsSawSelection = false;
            window.FilePicker = _ => [savedPath];
            await window.ExecuteAsync("open");
            check(window.Session.ActiveBlock?.Source == "저장된 교정" && window.DocumentTabs.Items.Count == 2 && !settingsSawSelection,
                "Selecting a saved work file opens its contents without a conversion settings dialog");
            window.DocumentTabs.SelectedIndex = 0;
            check(ReferenceEquals(previous, window.Session) && previous.IsDirty && previous.ActiveBlock!.Source == unsavedText,
                "Opening another file retains the unsaved document in its tab");

            await window.OpenStartupFileAsync(savedPath);
            check(window.Session.ActiveBlock?.Source == "저장된 교정", "A work file supplied at startup opens its saved contents");
            var opened = window.Session;
            await window.OpenStartupFileAsync(path);
            check(errorNotices == 1 && ReferenceEquals(opened, window.Session), "Invalid startup file types are reported without replacing the current document");
            await window.OpenStartupFileAsync(Path.Combine(directory, "missing-work.smj"));
            check(errorNotices == 2 && ReferenceEquals(opened, window.Session), "Missing startup files are reported without replacing the current document");
            var corruptPath = Path.Combine(directory, "corrupt-work.smj");
            await File.WriteAllTextAsync(corruptPath, "invalid work file");
            await window.OpenStartupFileAsync(corruptPath);
            check(errorNotices == 3 && ReferenceEquals(opened, window.Session), "Corrupt startup files are reported without replacing the current document");
        }
        finally
        {
            timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
        }

    }

    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = path.EndsWith("/login")
                ? "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}}"
                : path.EndsWith("/braille-forms") ? "{\"isSuccess\":true,\"result\":{\"items\":[]}}"
                : "{\"isSuccess\":true,\"result\":{}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
