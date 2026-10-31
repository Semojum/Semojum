using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsFileDialogDesignChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var general = new BrailleForm("builtin:GENERAL_BOOK", "일반 도서", true, null, new(), new());
        var saved = new BrailleForm("user:sample", "내 양식", false, null, new(), new());
        var open = new OpenDocumentDialog([general, saved], new AccountDefaults(new(), saved.FormId, "odd"));
        check(!open.FileNames.Text.Contains("DOCX", StringComparison.OrdinalIgnoreCase), "The upload picker does not advertise DOCX");
        try { open.SetPaths(["document.docx"]); check(false, "The upload picker rejects DOCX"); }
        catch (ArgumentException) { check(true, "The upload picker rejects DOCX"); }
        var decomposed = "가나다.txt".Normalize(System.Text.NormalizationForm.FormD);
        open.SetPaths([decomposed]);
        check(open.FileNames.Text == "가나다.txt" && open.Paths.Single() == decomposed,
            "The selected filename displays composed Korean while retaining the original path");
        var imagePath = Path.Combine(Path.GetTempPath(), "가.png".Normalize(System.Text.NormalizationForm.FormD));
        var imageOrder = new ImageOrderDialog([imagePath]); imageOrder.Show(); imageOrder.UpdateLayout();
        try
        {
            var imageList = Descendants(imageOrder).OfType<ListBox>().Single(l => AutomationProperties.GetName(l) == "이미지 순서");
            check(imageList.Items.Cast<string>().Single() == imagePath && Descendants(imageList).OfType<TextBlock>().Any(t => t.Text.Contains("가.png", StringComparison.Ordinal)),
                "Image ordering displays composed Korean without changing the selected file path");
        }
        finally { imageOrder.Close(); }
        var missingName = "missing-" + Guid.NewGuid().ToString("N") + ".txt";
        open.SetPaths([missingName]); open.Show();
        try
        {
            open.UpdateLayout();
            Capture(open, "open-document.png");
            check(open.Width == 560 && open.Height == 392 && Descendants(open).OfType<TextBlock>().Any(t => t.Text == "점역 설정" && t.IsVisible),
                "File options use the final Figma size and show the settings section");
            check(open.FileNames.Text == missingName && open.Forms.IsEnabled,
                "File options retain the selected filename and editable format settings");
            check(open.FileNames.ActualWidth > 490,
                "Selected file names fill the file field and align from its left edge");
            check(open.FormId == saved.FormId && open.Forms.Items.Contains(general) && open.Forms.Items.Contains(saved),
                "File options select the saved default and retain individual custom forms");
            var icons = Descendants(open).OfType<SvgViewbox>().Where(i => i.IsVisible && (i.Source?.ToString().EndsWith("open-options-close.svg", StringComparison.Ordinal) == true || i.Source?.ToString().EndsWith("open-options-chevron.svg", StringComparison.Ordinal) == true)).ToArray();
            check(icons.Length == 5 && icons.All(i => i.Source is not null && i.Drawings is { Bounds.IsEmpty: false }),
                "File options render the final Figma close and four dropdown icons");
            var start = Descendants(open).OfType<Button>().Single(b => Equals(b.Content, "변환 시작"));
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(Descendants(open).OfType<TextBlock>().Any(t => t.IsVisible && t.Foreground == System.Windows.Media.Brushes.Firebrick && t.Text.Length > 0),
                "A file that disappears before conversion shows an error without closing the dialog");
        }
        finally { open.Close(); }
        CheckUploadTotalNotice(general, check);
        var offline = new OpenDocumentDialog([general], new AccountDefaults(new(), null, "odd"), serverSettingsAvailable: false);
        offline.SetPaths(["document.smj"]); offline.Show();
        try
        {
            offline.UpdateLayout();
            check(!offline.Forms.IsEnabled && Descendants(offline).OfType<Button>().Any(b => Equals(b.Content, "열기") && b.IsEnabled),
                "An existing local work file remains openable when server settings are unavailable");
        }
        finally { offline.Close(); }
        var save = new SaveAsDialog("문서.smj", null); save.Show();
        try
        {
            save.UpdateLayout();
            Capture(save, "save-as.png");
            check(save.Width == 520 && save.Height == 284 && Descendants(save).OfType<TextBlock>().Any(t => t.Text == "저장 설정" && t.IsVisible),
                "Save as uses the final Figma size and displays its format settings");
            var icons = Descendants(save).OfType<SvgViewbox>().Where(i => i.IsVisible && (i.Source?.ToString().EndsWith("save-as-close.svg", StringComparison.Ordinal) == true || i.Source?.ToString().EndsWith("save-as-chevron.svg", StringComparison.Ordinal) == true)).ToArray();
            check(icons.Length == 2 && icons.All(i => i.Source is not null && i.Drawings is { Bounds.IsEmpty: false }),
                "Save as renders the final Figma close and dropdown icons");
            var name = Descendants(save).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "파일 이름");
            check(name.Text == "문서", "Save as retains the proposed document name");
            check(name.Template.FindName("PART_ContentHost", name) is ScrollViewer && name.Template.FindName("FocusLine", name) is Border,
                "Save as keeps an editable text host and the Figma focus underline");
            name.SelectAll(); name.SelectedText = "새 이름";
            check(name.Text == "새 이름", "The redesigned filename field remains editable");
        }
        finally { save.Close(); }
    }

    private static void CheckUploadTotalNotice(BrailleForm general, Action<bool, string> check)
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-upload-notice-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var first = Path.Combine(directory, "first.png");
        var second = Path.Combine(directory, "second.png");
        try
        {
            const long mib = 1024L * 1024;
            using (var file = new FileStream(first, FileMode.CreateNew, FileAccess.Write)) file.SetLength(60 * mib);
            using (var file = new FileStream(second, FileMode.CreateNew, FileAccess.Write)) file.SetLength(60 * mib);
            CheckNotice([first, second], "Two 60 MiB images report their total size rather than a single oversized file");
            using (var file = new FileStream(first, FileMode.Open, FileAccess.Write)) file.SetLength(JobClient.MaximumUploadBytes + 1);
            CheckNotice([first], "One oversized file reports the selected total size accurately");
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
            Directory.Delete(directory);
        }

        void CheckNotice(string[] paths, string description)
        {
            var dialog = new OpenDocumentDialog([general], new AccountDefaults(new(), null, "odd"));
            dialog.SetPaths(paths);
            dialog.Show();
            string? message = null, detail = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            var ticks = 0;
            timer.Tick += (_, _) =>
            {
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(n => n.Owner == dialog && n.IsVisible);
                if (notice is not null)
                {
                    message = notice.MessageText; detail = notice.DetailText;
                    Capture(notice, $"oversized-{paths.Length}.png");
                    timer.Stop(); notice.Close();
                }
                else if (++ticks >= 250)
                {
                    timer.Stop();
                    foreach (var owned in dialog.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) owned.Close();
                }
            };
            try
            {
                dialog.UpdateLayout(); timer.Start();
                Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "변환 시작"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(message == "선택한 파일의 총 크기가 100 MB를 넘습니다." &&
                      detail == "파일 크기를 줄이거나 나누어 다시 올려 주세요." && dialog.IsVisible,
                    description);
            }
            finally { timer.Stop(); dialog.Close(); }
        }
    }
    private static void Capture(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("FILE_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename));
        encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
