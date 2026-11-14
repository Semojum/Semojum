using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsLoginLegalChecks
{
    private sealed class Offline : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests++; throw new HttpRequestException("Legal documents must open without the network."); }
    }
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        foreach (var embedded in new[] { false, true })
        {
            var handler = new Offline(); using var http = new HttpClient(handler);
            var login = new AccountDialog(new SemojumApiClient(http));
            MainWindow? main = null; Task<bool>? pending = null; Window owner;
            if (embedded)
            {
                main = new MainWindow(smoke: true, testWithoutAccount: true); main.Show(); owner = main;
                pending = login.ShowEmbeddedAsync(main, allowCancel: true);
            }
            else { owner = login; login.Show(); }
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var input = Descendants(owner).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디");
                var password = Descendants(owner).OfType<PasswordBox>().Single();
                input.Text = "legal-fixture"; password.Password = "unchanged-fixture";
                if (!embedded) Capture(owner, Path.Combine(directory, "login-legal-links.png"));
                foreach (var kind in new[] { LegalDocumentKind.Terms, LegalDocumentKind.Privacy })
                {
                    var link = Descendants(owner).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LoginLegal" + kind);
                    check(link.IsVisible && link.IsEnabled, "The login legal link is available without signing in: " + kind);
                    Exception? failure = null; var opened = false; var started = DateTime.UtcNow;
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(35) };
                    timer.Tick += async (_, _) =>
                    {
                        var dialog = Application.Current.Windows.OfType<LegalDocumentDialog>().FirstOrDefault(d => d.IsVisible);
                        if (dialog is null)
                        {
                            if ((DateTime.UtcNow - started).TotalSeconds > 8) { timer.Stop(); failure = new TimeoutException("Legal dialog did not open."); }
                            return;
                        }
                        timer.Stop(); opened = true;
                        try
                        {
                            check(dialog.Owner == owner && CommonWindowServices.UsesBackdrop(dialog), "Legal modal belongs to the visible login owner and uses the shared Figma backdrop");
                            var doc = dialog.Reader.Document;
                            var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
                            check(dialog.Kind == kind && text.Contains(kind == LegalDocumentKind.Terms ? "누리닷 서비스 이용약관" : "누리닷 개인정보 처리방침")
                                && text.Contains("2026년 11월 09일") && text.Contains("서울시 강남구 역삼로 180 2층") && text.Contains("고객센터 운영시간"),
                                "The complete latest Notion document is available offline, including its final operator information");
                            check(text.Contains(kind == LegalDocumentKind.Terms ? "[대표 결정: 앱 안 점역 무료 유지 여부]" : "[세법상 보존 기간 확인 중]"),
                                "Pending decisions remain unchanged as explicitly requested");
                            if (kind == LegalDocumentKind.Privacy) check(doc.Blocks.OfType<Table>().Count() >= 4, "Privacy policy tables remain real selectable tables");
                            dialog.Reader.Selection.Select(doc.ContentStart, doc.ContentEnd);
                            check(dialog.Reader.Selection.Text.Contains("부칙"), "The legal document supports text selection through its last section");
                            dialog.Reader.Selection.Select(doc.ContentStart, doc.ContentStart);
                            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            if (!embedded) Capture(dialog, Path.Combine(directory, "login-legal-" + kind + ".png"));
                            var scroll = Descendants(dialog.Reader).OfType<ScrollViewer>().First();
                            scroll.ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                            check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0, "Long legal text can scroll to its end");
                            dialog.Height = 360; dialog.UpdateLayout();
                            check(dialog.Reader.ActualHeight > 80 && dialog.Reader.ActualHeight < 300, "Legal modal keeps a usable scrolling body in a small window");
                            if (kind == LegalDocumentKind.Privacy)
                                dialog.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                            else Descendants(dialog).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LegalDocumentClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            check(!dialog.IsVisible, "The legal modal closes with its close button or Escape");
                        }
                        catch (Exception ex) { failure = ex; }
                        finally { if (dialog.IsVisible) dialog.Close(); }
                    };
                    timer.Start();
                    try { link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    finally { timer.Stop(); }
                    if (failure is not null) throw failure;
                    check(opened && owner.IsVisible && input.Text == "legal-fixture" && password.Password == "unchanged-fixture",
                        "Closing legal text preserves the login screen and entered credentials");
                }
                check(handler.Requests == 0, "Reading terms and privacy makes no API or authentication requests");
            }
            finally
            {
                if (main is not null)
                {
                    Descendants(main).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "EmbeddedLoginCancel")?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (pending is not null) await pending;
                    await main.ReleaseResourcesAsync(); main.PrepareSmokeShutdown(); main.Close();
                }
                else login.Close();
            }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is null) continue; yield return child; foreach (var item in Descendants(child)) yield return item; }
    }
    private static void Capture(FrameworkElement view, string path)
    {
        view.UpdateLayout(); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(path); png.Save(file);
    }
}