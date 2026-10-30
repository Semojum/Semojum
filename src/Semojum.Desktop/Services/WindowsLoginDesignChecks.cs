using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.IO;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsLoginDesignChecks
{
    internal static void Run(Action<bool, string> check)
    {
        using var previewHttp = new HttpClient(new Handler());
        var preview = new AccountDialog(new SemojumApiClient(previewHttp), new MemoryStore(), null);
        var hasChoice = LogicalDescendants(preview).OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "로그인 상태 유지");
        check(hasChoice, "Login exposes a keep-signed-in choice");
        if (!hasChoice) { preview.Close(); return; }
        check(preview.WindowStyle == WindowStyle.None && System.Windows.Shell.WindowChrome.GetWindowChrome(preview)?.CaptionHeight == 32 && preview.Title == "세모점 로그인",
            "Login preserves a draggable 32px Windows title bar and a meaningful title");
        preview.Close();
        RunChoice(true, null, "editor", check);
        RunChoice(false, null, null, check);
        RunChoice(false, new("editor", "old-token"), null, check);
        RunChoice(false, new("other", "other-token"), "other", check);
        CheckCaptionClose(check);
        CheckCloseDuringLogin(check);
    }

    private static void CheckCaptionClose(Action<bool, string> check)
    {
        using var http = new HttpClient(new Handler());
        var dialog = new AccountDialog(new SemojumApiClient(http), new MemoryStore(), null);
        var clicked = false; var timedOut = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timedOut = true; timeout.Stop(); dialog.Close(); };
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var button = Descendants(dialog).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LoginCaptionclose");
            clicked = true; button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        timeout.Start();
        try { dialog.ShowDialog(); }
        finally { timeout.Stop(); }
        check(clicked && !timedOut && !dialog.IsVisible, "Login title-bar close command closes its own modal window");
    }

    private static void CheckCloseDuringLogin(Action<bool, string> check)
    {
        var delayed = new DelayedLogin(); var store = new MemoryStore();
        using var http = new HttpClient(delayed); var api = new SemojumApiClient(http);
        var dialog = new AccountDialog(api, store, null); var submitted = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timeout.Stop(); dialog.Close(); };
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var children = Descendants(dialog).ToArray();
            children.OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디").Text = "editor";
            children.OfType<PasswordBox>().Single(t => AutomationProperties.GetName(t) == "비밀번호").Password = "fixture-password";
            submitted = true;
            children.OfType<Button>().Single(b => Equals(b.Content, "로그인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LoginCaptionclose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        timeout.Start();
        try { dialog.ShowDialog(); }
        finally { timeout.Stop(); }
        delayed.Finish();
        var frame = new DispatcherFrame(); var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; }; settle.Start(); Dispatcher.PushFrame(frame);
        check(submitted && api.Account is null && store.Value is null, "Closing login while a response is pending cannot sign in or save credentials afterward");
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        yield return parent;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            foreach (var nested in LogicalDescendants(child)) yield return nested;
    }

    private static void RunChoice(bool remember, SessionCredential? initial, string? expectedAccount, Action<bool, string> check)
    {
        var store = new MemoryStore { Value = initial };
        using var http = new HttpClient(new Handler());
        var api = new SemojumApiClient(http);
        var dialog = new AccountDialog(api, store, null);
        var sawChoice = false; var loggedIn = false;
        Exception? callbackFailure = null;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => { timeout.Stop(); dialog.Close(); };
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            try
            {
                var children = Descendants(dialog).ToArray();
                var choice = children.OfType<CheckBox>().SingleOrDefault(c => AutomationProperties.GetName(c) == "로그인 상태 유지");
                sawChoice = choice is not null;
                if (choice is null) { dialog.Close(); return; }
                var login = children.OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "아이디");
                var password = children.OfType<PasswordBox>().Single(t => AutomationProperties.GetName(t) == "비밀번호");
                var submit = children.OfType<Button>().Single(b => Equals(b.Content, "로그인"));
                if (remember)
                {
                    var icons = children.OfType<SharpVectors.Converters.SvgViewbox>().ToArray();
                    foreach (var (asset, size) in new[] { ("login-logo.svg", 96d), ("login-check.svg", 16d), ("login-bell.svg", 16d), ("login-mail.svg", 16d),
                        ("login-caption-min.svg", 16d), ("login-caption-max.svg", 16d), ("login-caption-close.svg", 16d) })
                    {
                        var icon = icons.SingleOrDefault(i => i.Source?.ToString().EndsWith("/" + asset, StringComparison.Ordinal) == true);
                        check(icon is { Drawings.Bounds.IsEmpty: false } && icon.Width == size && icon.Height == size
                            && icon.ActualWidth > 0 && icon.ActualWidth <= size && icon.ActualHeight > 0 && icon.ActualHeight <= size,
                            $"Login renders final Figma asset in its intended size: {asset}");
                    }
                    foreach (var (id, label) in new[] { ("LoginCaptionmin", "최소화"), ("LoginCaptionmax", "최대화 / 복원"), ("LoginCaptionclose", "닫기") })
                    {
                        var caption = children.OfType<Button>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == id);
                        check(caption is not null && AutomationProperties.GetName(caption) == label
                            && System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(caption),
                            $"Login title bar exposes its Windows command and accessible name: {label}");
                    }
                    if (Math.Abs(dialog.ActualWidth - 1440) < 1 && Math.Abs(dialog.ActualHeight - 1000) < 1)
                    {
                        var card = children.OfType<Border>().Single(b => b.Width == 420);
                        var noticeLink = children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "공지사항");
                        var cardY = card.TransformToAncestor(dialog).Transform(new Point()).Y;
                        var linkY = noticeLink.TransformToAncestor(dialog).Transform(new Point()).Y;
                        check(Math.Abs(cardY - 260) < 3 && Math.Abs(linkY - 760) < 3,
                            $"The 1440x1000 login card and public links match the final Figma vertical placement (card={cardY:F1}, link={linkY:F1})");
                    }
                    var originalWidth = dialog.Width; var originalHeight = dialog.Height;
                    dialog.Width = 480; dialog.Height = 400; dialog.UpdateLayout();
                    var scroll = children.OfType<ScrollViewer>().First(s => s.Content is StackPanel);
                    scroll.ScrollToBottom();
                    var scrollFrame = new DispatcherFrame();
                    dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => scrollFrame.Continue = false));
                    Dispatcher.PushFrame(scrollFrame); dialog.UpdateLayout();
                    check(scroll.ScrollableHeight > 0 && scroll.VerticalOffset > 0 && scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                        "A small login window retains vertical scrolling to the public links");
                    scroll.ScrollToTop(); dialog.Width = originalWidth; dialog.Height = originalHeight; dialog.UpdateLayout();
                }
                if (remember && Environment.GetEnvironmentVariable("LOGIN_CAPTURE") is { Length: > 0 } capture && dialog.Content is FrameworkElement surface)
                {
                    surface.UpdateLayout();
                    var image = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                    image.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(capture); encoder.Save(file);
                }
                if (remember)
                {
                    void Pump()
                    {
                        var frame = new DispatcherFrame();
                        dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                    }
                    var maximize = children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LoginCaptionmax");
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    check(dialog.WindowState == WindowState.Maximized, "Login title-bar maximize command maximizes the window");
                    maximize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    check(dialog.WindowState == WindowState.Normal, "Login title-bar maximize command restores the window");
                    var minimize = children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "LoginCaptionmin");
                    minimize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                    check(dialog.WindowState == WindowState.Minimized, "Login title-bar minimize command minimizes the window");
                    System.Windows.SystemCommands.RestoreWindow(dialog); Pump();
                }
                login.Text = "editor"; password.Password = "fixture-password"; choice.IsChecked = remember;
                submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception ex) { callbackFailure = ex; dialog.Close(); }
        }));
        timeout.Start();
        try { loggedIn = dialog.ShowDialog() == true; }
        finally { timeout.Stop(); }
        if (callbackFailure is not null) throw new InvalidOperationException("Login UI test interaction failed", callbackFailure);
        check(sawChoice, "Login exposes a keep-signed-in choice");
        if (!sawChoice) return;
        check(loggedIn && api.Account?.LoginId == "editor" && store.Value?.LoginId == expectedAccount,
            $"Keep-signed-in {remember} respects the user's choice and preserves a different account");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }

    private sealed class MemoryStore : IAccountCredentialStore
    {
        internal SessionCredential? Value;
        public SessionCredential? Read() => Value;
        public void Write(SessionCredential value) => Value = value;
        public void Delete() => Value = null;
        public void DeleteIfMatches(SessionCredential value) { if (Value == value) Value = null; }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"new-token\",\"role\":\"ROLE_USER\"}}", Encoding.UTF8, "application/json") });
    }

    private sealed class DelayedLogin : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Finish() => response.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
            "{\"isSuccess\":true,\"result\":{\"accessToken\":\"fixture\",\"refreshToken\":\"new-token\",\"role\":\"ROLE_USER\"}}", Encoding.UTF8, "application/json") });
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)
            ? response.Task : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":null}", Encoding.UTF8, "application/json") });
    }
}
