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
            check(Math.Abs(invalid.Width - 471) <= 1 && Math.Abs(invalid.Height - 196) <= 1, "Unsupported file notice follows the final Figma dimensions");
            check(invalid.DetailText.Contains("JPEG", StringComparison.Ordinal) && invalid.DetailText.Contains("SMJ", StringComparison.Ordinal),
                "Unsupported file notice includes supported JPEG and local SMJ files");
            var detail = Descendants(invalid).OfType<TextBlock>().Single(t => t.Text == invalid.DetailText);
            check(detail.ActualHeight <= 21, "Supported formats fit on one line in the Figma-sized notice");
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
            check(credit.ActionButtons.Select(button => button.Content as string).SequenceEqual(["구매 문의", "문의하기", "닫기"]),
                "Credit notice offers separate purchase inquiry, general inquiry and close actions");
            check(credit.DetailText.Contains("구매 문의", StringComparison.Ordinal) && credit.DetailText.Contains("확인되지 않았습니다", StringComparison.Ordinal)
                && credit.DetailText.Contains("마이페이지 사용량", StringComparison.Ordinal),
                "Credit notice identifies the purchase inquiry and unverified resumed-charge policy");
        }
        finally { credit.Close(); }
        var partial = FileNotices.PartialConversion(10, 8, [2, 4]); partial.Show();
        try
        {
            partial.UpdateLayout(); Capture(partial, "partial-credit.png");
            check(partial.DetailText.Contains("2 · 4쪽", StringComparison.Ordinal) && partial.DetailText.Contains("8쪽", StringComparison.Ordinal)
                && partial.DetailText.Contains("중복 차감 방지 여부는 확인되지 않았습니다", StringComparison.Ordinal)
                && partial.DetailText.Contains("마이페이지 사용량", StringComparison.Ordinal),
                "Partial conversion reports actual pages and the unverified retry-charge policy before retry");
        }
        finally { partial.Close(); }
        var login = FileNotices.LoginRejected(new(System.Net.HttpStatusCode.Unauthorized, "AUTH4001", "아이디 또는 비밀번호가 맞지 않습니다."));
        check(login.ActionButtons[1].Content as string == "비밀번호 문의", "Login notice describes its inquiry action accurately");
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
