using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsTabPanelChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        using var http = new HttpClient(new Fixture());
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var window = new MainWindow(api) { Width = 1440, Height = 1000 }; window.Show();
        try
        {
            await window.ExecuteAsync("new");
            check(window.OriginalToggle.IsChecked && window.SourceToggle.IsChecked && window.BrailleToggle.IsChecked,
                "A new blank document starts with original, text and braille panels");
            check(window.OriginalUpload.IsVisible, "The new document original panel offers a file upload area");
            var pickerOpened = false;
            window.FilePicker = _ => { pickerOpened = true; return null; };
            window.OriginalUpload.UploadArea.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(pickerOpened, "Clicking the original upload area opens the file picker");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.OriginalPanel.UpdateLayout();
            var visual = new RenderTargetBitmap((int)window.OriginalPanel.ActualWidth, (int)window.OriginalPanel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            visual.Render(window.OriginalPanel);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(visual));
            using (var image = File.Create(Path.Combine(directory, "original-upload-panel.png"))) encoder.Save(image);
            var icons = Descendants(window.OriginalUpload).OfType<SharpVectors.Converters.SvgViewbox>().Where(i => i.IsVisible).ToArray();
            check(icons.Length == 3 && icons.All(i => i.Drawings is { Bounds.IsEmpty: false }), "Original upload panel renders all three Figma icons");
            var dropPath = Path.Combine(directory, "dropped-text.txt");
            await File.WriteAllTextAsync(dropPath, "드롭 원문");
            var dropSelected = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var settings = Application.Current.Windows.OfType<Dialogs.OpenDocumentDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (settings is null) return;
                dropSelected = settings.FileNames.Text.Contains("dropped-text.txt", StringComparison.Ordinal);
                settings.DialogResult = false;
            };
            timer.Start();
            try
            {
                var data = new DataObject(DataFormats.FileDrop, new[] { dropPath });
                var ctor = typeof(DragEventArgs).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, [typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point)], null)!;
                var drop = (DragEventArgs)ctor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, window.OriginalUpload.UploadArea, new Point(1, 1)]);
                drop.RoutedEvent = UIElement.DropEvent; window.OriginalUpload.UploadArea.RaiseEvent(drop);
                await Task.Delay(250);
            }
            finally { timer.Stop(); }
            check(dropSelected, "Dropping a supported file opens settings for that file");
            var typed = window.Session;
            typed.ActiveBlock!.Source = "새 문서에서 저장하지 않은 원문";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            var typedTab = (System.Windows.Controls.TabItem)window.DocumentTabs.SelectedItem;
            var typedLabel = ((System.Windows.Controls.StackPanel)typedTab.Header).Children.OfType<System.Windows.Controls.TextBlock>().Single();
            check(typed.IsDirty && typed.WindowTitle.StartsWith("*" + typed.Title, StringComparison.Ordinal) && window.Title == typed.WindowTitle && typedLabel.Text == typed.Title &&
                  (typedTab.ToolTip as string)?.Contains("저장되지 않음", StringComparison.Ordinal) == true &&
                  System.Windows.Automation.AutomationProperties.GetName(typedTab).Contains("저장되지 않음", StringComparison.Ordinal),
                "The Figma title marks unsaved work while the tab retains its filename and exposes its unsaved state");
            var txt = Path.Combine(directory, "panel-import.txt");
            await File.WriteAllTextAsync(txt, "TXT 본문");
            await window.OpenAsync(txt);
            check(!window.OriginalToggle.IsChecked && window.SourceToggle.IsChecked && window.BrailleToggle.IsChecked,
                "A TXT file opens with text and braille panels");
            check(!window.OriginalUpload.IsVisible, "A TXT document does not display the original upload area");
            var txtIndex = window.DocumentTabs.SelectedIndex;
            window.DocumentTabs.SelectedIndex = txtIndex - 1;
            check(ReferenceEquals(window.Session, typed) && typed.IsDirty && typed.ActiveBlock!.Source == "새 문서에서 저장하지 않은 원문" && window.OriginalToggle.IsChecked,
                "Switching back to an unsaved new document restores its three panels and edits");
            window.DocumentTabs.SelectedIndex = txtIndex;
            check(!window.OriginalToggle.IsChecked, "Switching back to a TXT document restores its two panels");
            check(typed.IsDirty && typedLabel.Text == typed.Title && (typedTab.ToolTip as string)?.Contains("저장되지 않음", StringComparison.Ordinal) == true,
                "An inactive unsaved tab keeps the Figma filename and exposes its unsaved state on hover");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }

        var savedPath = Path.Combine(directory, "placeholder-open.smj");
        var saved = new DocumentSession { Title = "기존 작업.smj" };
        saved.Add(new EditorBlock { Id = 1, Page = 1, Source = "보존된 원문" });
        await SmjFile.SaveAsync(savedPath, SmjFile.Snapshot(saved));
        var empty = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); empty.Show();
        try
        {
            check(empty.OriginalToggle.IsChecked, "The initial empty document offers an original panel");
            await empty.OpenStartupFileAsync(savedPath);
            check(empty.DocumentTabs.Items.Count == 1 && empty.Session.ActiveBlock?.Source == "보존된 원문", "Opening a file replaces only the untouched startup placeholder");
        }
        finally { await empty.ReleaseResourcesAsync(); empty.PrepareSmokeShutdown(); empty.Close(); }
        var edited = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); edited.Show();
        try
        {
            var original = edited.Session; original.ActiveBlock!.Source = "보존해야 할 교정";
            await edited.OpenStartupFileAsync(savedPath);
            check(edited.DocumentTabs.Items.Count == 2, "Opening a file keeps the edited startup document in its tab");
            edited.DocumentTabs.SelectedIndex = 0;
            check(ReferenceEquals(original, edited.Session) && original.IsDirty && original.ActiveBlock!.Source == "보존해야 할 교정",
                "The previously edited startup document remains available");
        }
        finally { await edited.ReleaseResourcesAsync(); edited.PrepareSmokeShutdown(); edited.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var result = path.EndsWith("/login") ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}" : path.EndsWith("/braille-forms") ? "{\"items\":[]}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
}
