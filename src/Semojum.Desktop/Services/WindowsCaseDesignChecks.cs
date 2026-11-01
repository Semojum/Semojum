using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsCaseDesignChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var invalid = FileNotices.UnsupportedFormat(); invalid.Show();
        try
        {
            invalid.UpdateLayout(); Capture(invalid, "unsupported.png");
            // DOCX is not converted, so Case 1 lists the formats the app really opens (JPEG and SMJ too) and widens from 471 to the 520 message size.
            check(Math.Abs(invalid.Width - 520) <= 1 && Math.Abs(invalid.Height - 196) <= 1, "Unsupported file notice uses the 520x196 message size that fits the real format list");
            check(invalid.DetailText == "PDF · HWP · HWPX · TXT · JPG · JPEG · PNG · SMJ 만 열 수 있습니다." && invalid.MessageText == "이 형식은 열 수 없습니다.",
                "Unsupported file notice keeps the Case 1 wording with the actually supported formats and no DOCX");
            var detail = Descendants(invalid).OfType<TextBlock>().Single(t => t.Text == invalid.DetailText);
            check(detail.ActualHeight <= 21, "Supported formats fit on one line in the 520px notice");
            var icon = Descendants(invalid).OfType<SvgViewbox>().Single();
            check(icon.Source?.ToString().EndsWith("case-error.svg", StringComparison.Ordinal) == true && icon.Drawings is { Bounds.IsEmpty: false } && icon.Width == 16 && icon.Height == 16 && icon.ActualWidth > 0 && icon.ActualWidth <= 16 && icon.ActualHeight > 0 && icon.ActualHeight <= 16,
                "Error notice renders the original Figma warning in its 16px slot");
            check(invalid.ActionButtons.Count == 2 && invalid.ActionButtons[0].IsDefault &&
                  Math.Abs(invalid.ActionButtons[0].ActualWidth - invalid.ActionButtons[1].ActualWidth) < 2 && invalid.ActionButtons.All(b => Math.Abs(b.ActualHeight - 32) <= 1),
                "Error notice uses equal-width Figma action buttons and preserves the default action");
        }
        finally { invalid.Close(); }
        var credit = FileNotices.NoCredits(); credit.Show();
        try
        {
            credit.UpdateLayout(); Capture(credit, "no-credits.png");
            check(credit.ActionButtons.Select(button => button.Content as string).SequenceEqual(["크레딧 구매", "문의하기", "닫기"]),
                "Credit notice offers the final purchase, inquiry and close actions");
            check(credit.DetailText == "크레딧을 충전한 뒤 상단 알림 창의 「이어서 처리」를 눌러 재개할 수 있습니다.",
                "Credit notice points to the resume action in the top notice");
        }
        finally { credit.Close(); }
        var partial = FileNotices.PartialConversion(10, 8, [2, 4]); partial.Show();
        try
        {
            partial.UpdateLayout(); Capture(partial, "partial-credit.png");
            check(partial.DetailText == "2 · 4쪽입니다. 나머지 8쪽은 사용할 수 있습니다." && Math.Abs(partial.Height - 196) <= 1,
                "Partial conversion reports actual pages in the final Case 4 size");
        }
        finally { partial.Close(); }
        var login = FileNotices.LoginRejected(new(System.Net.HttpStatusCode.Unauthorized, "AUTH4001", "Bad credentials"));
        check(login.MessageText == "아이디 또는 비밀번호가 맞지 않습니다." && login.ActionButtons[1].Content as string == "비밀번호 찾기", "Login notice follows the final Case 14 wording");
        var locked = FileNotices.LoginRejected(new(System.Net.HttpStatusCode.Unauthorized, "AUTH4006", "계정이 잠겼습니다."));
        check(locked.MessageText == "계정이 잠겼습니다.", "A locked-account notice keeps the server message");
        var disconnected = FileNotices.Disconnected(47); var oversized = FileNotices.OversizedFile();
        check(disconnected.DetailText == "47쪽까지 변환한 내용은 보관했습니다." && oversized.MessageText == "파일이 100 MB 를 넘습니다.",
            "Disconnected and oversized notices follow the final Case 7 and Case 2 wording");
        foreach (var notice in new[] { login, locked, disconnected, oversized }) notice.Close();
        foreach (var index in new[] { 0, 1, 2 })
        {
            var dialog = FileNotices.NoCredits();
            var timeout = false;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timeout = true; timer.Stop(); dialog.Close(); };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
                dialog.ActionButtons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent))), DispatcherPriority.Normal);
            timer.Start(); dialog.ShowDialog(); timer.Stop();
            check(!timeout && dialog.Choice == index, $"Credit notice action {index} returns its own choice");
        }
        var manual = new ManualEditDialog(); manual.Show();
        try
        {
            manual.UpdateLayout(); Capture(manual, "manual.png");
            check(manual.ActionButtons.Count == 3 && manual.ActionButtons[1].Background is SolidColorBrush brush && brush.Color == Color.FromRgb(196, 43, 28),
                "Manual braille overwrite is shown as the red destructive Figma action");
        }
        finally { manual.Close(); }
        foreach (var index in new[] { 0, 1, 2 })
        {
            var dialog = new ManualEditDialog();
            var timeout = false; Exception? failure = null;
            var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) => { timeout = true; timer.Stop(); dialog.Close(); };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { dialog.ActionButtons[index].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }), DispatcherPriority.Normal);
            timer.Start(); var result = dialog.ShowDialog(); timer.Stop();
            var expected = index switch { 0 => ManualEditDecision.Keep, 1 => ManualEditDecision.Overwrite, _ => ManualEditDecision.Cancel };
            check(!timeout && failure is null && dialog.Decision == expected && (index == 2 ? result != true : result == true),
                $"Manual braille action {index} retains its original decision");
        }
    }
    private static void Capture(Window window, string filename)
    {
        var directory = Environment.GetEnvironmentVariable("NOTICE_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, filename)); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
