using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsInstitutionVisualChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var account = new InstitutionAccount("kblib02", "수학 담당", "ACTIVE", "ROLE_USER", "2026-08-13", 1140, false);
        var jobs = new InstitutionAccountJobs("kblib02", "수학 담당", "2026-08-01", "2026-08-31", [new("job", "문서.pdf", "IMAGE", "DONE", 14, 14, 0, 11, null, null)], 14, 11);
        var notice = new InstitutionNotice(Guid.NewGuid(), "ORG", "크레딧 소진 임박 안내", "운영자가 보낸 실제 공지 내용입니다.", null, null, "2026-08-13");
        var order = new InstitutionOrder(Guid.NewGuid(), "2026-06-02", "크레딧 추가 · 3,000", 780000, 3000, null, "PENDING", null);
        var views = new (Window Window, string Name, double Width, double Height, string Asset)[]
        {
            (new InstitutionRequestDialog(true, _ => Task.CompletedTask), "institution-account-request.png", 520, 344, "institution-account-request-close.svg"),
            (new InstitutionRequestDialog(false, _ => Task.CompletedTask), "institution-credit-request.png", 520, 288, "institution-credit-request-close.svg"),
            (new InstitutionAccountDetailDialog(account, jobs, _ => Task.CompletedTask), "institution-account-detail.png", 560, 564, "institution-account-detail-close.svg"),
            (new InstitutionNoticeDialog(notice, true), "institution-notice-detail.png", 560, 276, "institution-notice-detail-close.svg"),
            (new InstitutionOrderDialog(order, null, () => Task.CompletedTask), "institution-order-detail.png", 560, 448, "institution-order-detail-close.svg")
        };
        foreach (var (window, name, width, height, asset) in views)
        {
            window.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var icon = Descendants(window).OfType<SvgViewbox>().SingleOrDefault(i => i.Source?.ToString().EndsWith("/" + asset, StringComparison.Ordinal) == true);
                check(Math.Abs(window.Width - width) < .1 && Math.Abs(window.Height - height) < .1 && icon is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false },
                    $"{name} has its Figma window dimensions and original close icon");
                var body = Descendants(window).OfType<ScrollViewer>().FirstOrDefault(s => s.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text is "계정" or "결제와 증빙"));
                if (window is InstitutionAccountDetailDialog)
                {
                    var origin = body?.TransformToAncestor(window).Transform(new Point()) ?? new Point(0, 0);
                    check(body is { IsVisible: true, ActualHeight: > 300 } && origin.Y >= 54 && origin.Y + body.ActualHeight <= height - 63,
                        "The account-detail scrollable body renders between the title and footer without clipping");
                    check(Descendants(window).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "수학 담당"), "The account detail actually renders its server-provided alias");
                    var accountDetail = (InstitutionAccountDetailDialog)window;
                    check(accountDetail.Jobs.HeadersVisibility == DataGridHeadersVisibility.None && accountDetail.Jobs.GridLinesVisibility == DataGridGridLinesVisibility.None && accountDetail.Jobs.RowHeight == 32 && accountDetail.Jobs.Items.Count == 1,
                        "Account detail uses the Figma headerless, gridless 32px job rows while retaining actual job data");
                }
                else if (window is InstitutionOrderDialog)
                {
                    var origin = body?.TransformToAncestor(window).Transform(new Point()) ?? new Point(0, 0);
                    check(body is { IsVisible: true, ActualHeight: > 250 } && origin.Y >= 54 && origin.Y + body.ActualHeight <= height - 63,
                        "The order-detail body occupies its own row between title and footer");
                }
                else if (window is InstitutionNoticeDialog)
                {
                    var noticeBody = Descendants(window).OfType<TextBlock>().Single(t => t.Text == notice.Body);
                    var origin = noticeBody.TransformToAncestor(window).Transform(new Point());
                    check(noticeBody.IsVisible && origin.Y >= 55 && origin.Y + noticeBody.ActualHeight <= height - 64,
                        "The notice detail body renders between its title and action row");
                }
                else if (window is InstitutionRequestDialog requestBody)
                {
                    var origin = requestBody.First.TransformToAncestor(window).Transform(new Point());
                    check(requestBody.First.IsVisible && origin.Y >= 55 && origin.Y + requestBody.First.ActualHeight <= height - 64,
                        $"{name} keeps its editable fields between title and action row");
                }
                if (Environment.GetEnvironmentVariable("INSTITUTION_VISUAL_DIR") is { Length: > 0 } directory)
                { Directory.CreateDirectory(directory); await WindowsSmoke.RenderElementAsync(window, Path.Combine(directory, name)); }
                if (window is InstitutionRequestDialog request)
                {
                    if (name == "institution-credit-request.png")
                    {
                        var up = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "크레딧 1 증가");
                        var down = Descendants(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "크레딧 1 감소");
                        var upIcon = up.Content as SvgViewbox; var downIcon = down.Content as SvgViewbox;
                        check(upIcon is { Width: 9.60063, Height: 5.19934, Drawings.Bounds.IsEmpty: false } && downIcon is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false },
                            "Credit input renders the original Figma up and down spin icons at their supplied viewport sizes");
                        up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); up.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        check(request.First.Text == "1", "Credit spin controls increment and decrement the real request quantity without going below one"); request.First.Clear();
                    }
                    request.Option.IsDropDownOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(100); window.UpdateLayout();
                    var popup = request.Option.Template.FindName("PART_Popup", request.Option) as Popup;
                    var surface = popup?.Child as Border;
                    var items = Enumerable.Range(0, request.Option.Items.Count).Select(i => request.Option.ItemContainerGenerator.ContainerFromIndex(i)).OfType<ComboBoxItem>().ToArray();
                    check(request.Option.IsDropDownOpen && popup?.IsOpen == true && Math.Abs(request.Option.ActualWidth - 240) < .5
                        && surface is { CornerRadius.TopLeft: 6 } && items.Length == 3 && items.All(i => Math.Abs(i.ActualHeight - 32) < .5),
                        $"{name} opens the Figma 240px menu with all three 32px choices");
                    if (surface is not null && Environment.GetEnvironmentVariable("INSTITUTION_VISUAL_DIR") is { Length: > 0 } popupDirectory)
                        await WindowsSmoke.RenderElementAsync(surface, Path.Combine(popupDirectory, Path.GetFileNameWithoutExtension(name) + "-menu.png"));
                    request.Option.SelectedIndex = 1; request.Option.IsDropDownOpen = false;
                    check(request.Option.SelectedIndex == 1 && !request.Option.IsDropDownOpen, $"{name} preserves a selected menu choice after closing");
                }
            }
            finally { window.Close(); }
        }
        var absentReceipt = new InstitutionOrderDialog(order, null, () => Task.CompletedTask);
        check(!absentReceipt.ReceiptButton.IsEnabled, "An order with no server receipt cannot offer a document download"); absentReceipt.Close();
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
}
