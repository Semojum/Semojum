using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsFileEdgeChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api); window.Show(); await window.ExecuteAsync("new");
        var original = window.Session; original.ActiveBlock!.Source = "미저장 교정"; original.ActiveBlock.Braille = "⠿"; original.ActiveBlock.HasManualBraille = true;
        var count = window.DocumentTabs.Items.Count; var pickers = 0; var errors = 0; var cancelSettings = false;
        window.FilePicker = _ => { pickers++; return null; };
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == window && n.IsVisible);
            if (notice is not null) { errors++; notice.Close(); }
            var settings = Application.Current.Windows.OfType<OpenDocumentDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (settings is not null) { cancelSettings = true; settings.DialogResult = false; }
        };
        timer.Start();
        try
        {
            var missing = Path.Combine(directory, "missing-drop-" + Guid.NewGuid().ToString("N") + ".smj");
            var data = new DataObject(DataFormats.FileDrop, new[] { missing });
            var ctor = typeof(DragEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
            var drop = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, window.OriginalUpload.UploadArea, new Point(1, 1)]);
            drop.RoutedEvent = UIElement.DropEvent; window.OriginalUpload.UploadArea.RaiseEvent(drop);
            var until = DateTime.UtcNow.AddSeconds(5);
            while (errors == 0 && pickers == 0 && DateTime.UtcNow < until) await Task.Delay(20);
            check(errors == 1 && pickers == 0, "A missing dropped SMJ reports the file error without opening an unrelated picker");
            check(ReferenceEquals(original, window.Session) && original.IsDirty && original.ActiveBlock!.Source == "미저장 교정"
                && original.ActiveBlock.Braille == "⠿" && original.ActiveBlock.HasManualBraille && window.DocumentTabs.Items.Count == count,
                "A missing dropped work file preserves unsaved text and manual braille");
            var invalid = Path.Combine(directory, "invalid-utf8-" + Guid.NewGuid().ToString("N") + ".txt");
            var valid = Path.Combine(directory, "utf8-bom-" + Guid.NewGuid().ToString("N") + ".txt");
            var utf16 = Path.Combine(directory, "utf16-bom-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                await File.WriteAllBytesAsync(invalid, [0xC3, 0x28]);
                var rejected = false;
                try { await window.OpenAsync(invalid); } catch (DecoderFallbackException) { rejected = true; }
                check(rejected && ReferenceEquals(original, window.Session) && original.ActiveBlock!.Source == "미저장 교정"
                    && original.ActiveBlock.Braille == "⠿" && window.DocumentTabs.Items.Count == count,
                    "An invalid UTF-8 text file is rejected without replacing unsaved work");
                var text = "한글과 점자 ⠿\r\n둘째 줄";
                var sample = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
                await File.WriteAllBytesAsync(valid, sample); await window.OpenAsync(valid);
                check(window.Session.ActiveBlock?.Source == text && window.Session.OriginalFiles[Path.GetFileName(valid)].SequenceEqual(sample)
                    && window.DocumentTabs.Items.Count == count + 1 && original.ActiveBlock!.Source == "미저장 교정" && original.ActiveBlock.Braille == "⠿",
                    "Opening a UTF-8 BOM text sample preserves its exact original bytes and the unsaved tab");
                var utf16Sample = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
                await File.WriteAllBytesAsync(utf16, utf16Sample); await window.OpenAsync(utf16);
                check(window.Session.ActiveBlock?.Source == text && window.Session.OriginalFiles[Path.GetFileName(utf16)].SequenceEqual(utf16Sample)
                    && window.DocumentTabs.Items.Count == count + 2,
                    "Opening a UTF-16 BOM text sample preserves the previously supported encoding and original bytes");
            }
            finally { if (File.Exists(invalid)) File.Delete(invalid); if (File.Exists(valid)) File.Delete(valid); if (File.Exists(utf16)) File.Delete(utf16); }
            var imagePath = Path.Combine(directory, "sample-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                await File.WriteAllBytesAsync(imagePath, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/a9sAAAAASUVORK5CYII="));
                window.DocumentTabs.SelectedIndex = count - 1;
                var imageData = new DataObject(DataFormats.FileDrop, new[] { imagePath });
                var imageDrop = (DragEventArgs)ctor.Invoke([imageData, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, window.OriginalUpload.UploadArea, new Point(1, 1)]);
                imageDrop.RoutedEvent = UIElement.DropEvent; window.OriginalUpload.UploadArea.RaiseEvent(imageDrop);
                until = DateTime.UtcNow.AddSeconds(5); while (!cancelSettings && DateTime.UtcNow < until) await Task.Delay(20);
                check(cancelSettings && ReferenceEquals(original, window.Session) && original.ActiveBlock!.Source == "미저장 교정" && original.ActiveBlock.Braille == "⠿"
                    && window.DocumentTabs.Items.Count == count + 2 && pickers == 0,
                    "Dropping a PNG sample and cancelling settings retains both existing tabs without opening a picker");
                cancelSettings = false; fixture.HoldForms = true; var before = fixture.FormsRequests; pickers = 0;
                window.FilePicker = _ => { pickers++; return [imagePath]; };
                try
                {
                    window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    until = DateTime.UtcNow.AddSeconds(5); while (fixture.FormsRequests <= before && DateTime.UtcNow < until) await Task.Delay(20);
                    await Task.Delay(100);
                    check(fixture.FormsRequests == before + 1 && pickers == 1 && ReferenceEquals(original, window.Session),
                        "A second upload click during delayed settings does not start another picker or request");
                }
                finally { fixture.ReleaseForms(); }
                until = DateTime.UtcNow.AddSeconds(5); while (!cancelSettings && DateTime.UtcNow < until) await Task.Delay(20);
                check(cancelSettings && ReferenceEquals(original, window.Session) && original.ActiveBlock!.Braille == "⠿",
                    "Cancelling after delayed settings preserves the unsaved document");
            }
            finally { if (File.Exists(imagePath)) File.Delete(imagePath); }
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        public bool HoldForms { get; set; }
        public int FormsRequests { get; private set; }
        private readonly TaskCompletionSource<HttpResponseMessage> pendingForms = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ReleaseForms() => pendingForms.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"items\":[]}}", Encoding.UTF8, "application/json") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/braille-forms")) { FormsRequests++; if (HoldForms) return pendingForms.Task.WaitAsync(cancellationToken); }
            var result = path.EndsWith("/login") ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}" : path.EndsWith("/braille-forms") ? "{\"items\":[]}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
}
