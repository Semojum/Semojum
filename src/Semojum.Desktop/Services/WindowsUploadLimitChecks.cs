using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsUploadLimitChecks
{
    internal static async Task RunAsync(string outputDirectory, Action<bool, string> check)
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var folder = Path.GetFullPath(Path.Combine(tempRoot, "semojum-upload-limit-" + Guid.NewGuid().ToString("N")));
        if (!folder.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("테스트 임시 폴더가 올바르지 않습니다.");
        Directory.CreateDirectory(folder);
        var large = Path.Combine(folder, "큰이미지.png"); var extra = Path.Combine(folder, "추가이미지.png");
        try
        {
            using (var file = new FileStream(large, FileMode.CreateNew, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes + 1);
            await File.WriteAllBytesAsync(extra, [1]);
            using var handler = new Fixture(); using var http = new HttpClient(handler);
            var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
            var window = new MainWindow(api, startEmpty: true) { Width = 1440, Height = 1000 }; window.Show();
            var settingsCount = 0; var modalCount = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (_, _) =>
            {
                var settings = Application.Current.Windows.OfType<OpenDocumentDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (settings is not null) { settingsCount++; settings.DialogResult = false; }
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (notice is not null) { modalCount++; notice.Close(); }
            };
            try
            {
                // A new document no longer shows the upload area, so type into the start screen that does.
                var model = window.Session; model.ActiveBlock!.Source = "보존할 미저장 작업";
                var tabs = window.DocumentTabs.Items.Count;
                timer.Start();
                window.FilePicker = _ => [large];
                window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                var visible = VisibleText(window.OriginalUpload);
                check(settingsCount == 0 && modalCount == 0 && visible.Contains("100MB를 초과") && visible.Contains("큰이미지.png"),
                    "Picking a file above the limit shows its name in the original panel without opening settings");
                check(ReferenceEquals(model, window.Session) && model.IsDirty && model.ActiveBlock!.Source == "보존할 미저장 작업" && window.DocumentTabs.Items.Count == tabs && handler.Uploads == 0,
                    "An oversized selection neither starts conversion nor loses the unsaved document");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                window.OriginalPanel.UpdateLayout();
                var visual = new RenderTargetBitmap((int)window.OriginalPanel.ActualWidth, (int)window.OriginalPanel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                visual.Render(window.OriginalPanel); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(visual));
                using (var image = File.Create(Path.Combine(outputDirectory, "original-upload-oversize.png"))) encoder.Save(image);
                // The limit is per file: drop a small image together with the oversized one; the warning must name only the oversized file.
                window.OriginalUpload.ClearLimitWarning();
                var data = new DataObject(DataFormats.FileDrop, new[] { extra, large });
                var ctor = typeof(DragEventArgs).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
                var drop = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, window.OriginalUpload.UploadArea, new Point(1, 1)]);
                drop.RoutedEvent = UIElement.DropEvent; window.OriginalUpload.UploadArea.RaiseEvent(drop);
                await Task.Delay(200);
                visible = VisibleText(window.OriginalUpload);
                check(settingsCount == 0 && modalCount == 0 && visible.Contains("100MB를 초과") && visible.Contains("큰이미지.png (101MB)") && !visible.Contains("추가이미지.png"),
                    "Dropping several images where one exceeds the per-file limit names that file in the inline warning");
                check(ReferenceEquals(model, window.Session) && model.IsDirty && handler.Uploads == 0, "An oversized drop preserves unsaved work and does not upload");
                using (var file = new FileStream(large, FileMode.Open, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes);
                window.FilePicker = _ => [large];
                window.OriginalUpload.UploadArea.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(200);
                visible = VisibleText(window.OriginalUpload);
                check(settingsCount == 1 && !visible.Contains("100MB를 초과"), "Exactly 100 MiB is accepted and clears an earlier warning");
                async Task DropAsync(params string[] files)
                {
                    var args = (DragEventArgs)ctor.Invoke([new DataObject(DataFormats.FileDrop, files), DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, window.OriginalUpload.UploadArea, new Point(1, 1)]);
                    args.RoutedEvent = UIElement.DropEvent; window.OriginalUpload.UploadArea.RaiseEvent(args);
                    await Task.Delay(200); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.OriginalPanel.UpdateLayout();
                }
                // BE 2026-11-02: several images also share one 100 MB total (JOB4009), named in the same inline warning.
                using (var file = new FileStream(extra, FileMode.Open, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes);
                await DropAsync(large, extra);
                visible = VisibleText(window.OriginalUpload);
                check(settingsCount == 1 && modalCount == 0 && visible.Contains("100MB를 초과") && visible.Contains("이미지 2장 합계 (200MB)") && handler.Uploads == 0,
                    "Two images of exactly 100 MiB each (200 MiB together) exceed the 100 MB total and name it inline without opening settings");
                using (var file = new FileStream(large, FileMode.Open, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes / 2);
                using (var file = new FileStream(extra, FileMode.Open, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes / 2);
                await DropAsync(large, extra);
                check(settingsCount == 2 && !VisibleText(window.OriginalUpload).Contains("100MB를 초과") && handler.Uploads == 0,
                    "Two images of exactly 100 MiB together reach settings, clear the warning and do not upload");
            }
            finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
        }
        finally { Directory.Delete(folder, true); }
    }
    private static string VisibleText(DependencyObject root) => string.Join(" ", Descendants(root).OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        public int Uploads { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.StartsWith("/api/jobs")) Uploads++;
            var result = path.EndsWith("/login") ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}" : path.EndsWith("/braille-forms") ? "{\"items\":[]}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
}
