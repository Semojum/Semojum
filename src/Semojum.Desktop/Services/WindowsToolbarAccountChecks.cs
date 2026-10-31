using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsToolbarAccountChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var http = new HttpClient(new Fixture());
        var api = new SemojumApiClient(http);
        var window = new MainWindow(api) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var button = window.ToolbarAccount;
            var label = Descendants(button).OfType<TextBlock>().Single();
            var icons = Descendants(button).OfType<SvgViewbox>().ToArray();
            check(button.IsVisible && button.ActualWidth == 128 && button.ActualHeight == 24
                && AutomationProperties.GetName(button) == "마이페이지 · 사용량" && button.Focusable,
                "Account control uses Figma dimensions and an accessible focusable action");
            check(icons.Length == 2 && icons.All(i => i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false })
                && icons[0].Source.ToString().EndsWith("editor-account-user.svg", StringComparison.Ordinal)
                && icons[1].Source.ToString().EndsWith("editor-account-chevron.svg", StringComparison.Ordinal),
                "Account control renders the original Figma user and chevron SVGs");
            check(label.Text == "로그인 안 됨", "Signed-out account control displays the actual signed-out state");
            var loginSeen = await ClickAndCancelLoginAsync(window, button);
            check(loginSeen && api.Account is null && label.Text == "로그인 안 됨", "Signed-out account action opens login and preserves cancellation");
            await api.LoginAsync("testorg01", "fixture");
            typeof(MainWindow).GetMethod("RefreshAccount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(label.Text == "testorg01", "Account control follows the real authenticated login ID");
            var usageSeen = await ClickAndCloseAsync<UsageDialog>(window, button);
            check(usageSeen, "Authenticated account action opens the existing my-page window");
            foreach (var width in new[] { 960d, 480d })
            {
                window.Width = width; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var p = button.TranslatePoint(new Point(), window);
                check(button.IsVisible && button.ActualWidth == 128 && p.X >= 0 && p.X + button.ActualWidth <= window.ActualWidth,
                    $"Account action remains visible and reachable at {width:0}px width");
            }
            if (Environment.GetEnvironmentVariable("TOOLBAR_ACCOUNT_CAPTURE") is { Length: > 0 } capture)
                await WindowsSmoke.RenderElementAsync(button, capture);
            await api.LogoutAsync();
            typeof(MainWindow).GetMethod("RefreshAccount", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(label.Text == "로그인 안 됨", "Account control follows local logout");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static async Task<bool> ClickAndCancelLoginAsync(Window owner, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = DateTime.UtcNow.AddSeconds(5);
        Button? cancel = null;
        while (DateTime.UtcNow < deadline && cancel is null)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            cancel = Descendants(owner).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "EmbeddedLoginCancel" && b.IsVisible);
            if (cancel is null) await Task.Delay(20);
        }
        if (cancel is null) return false;
        var embedded = owner.WindowState == WindowState.Maximized && !Application.Current.Windows.OfType<AccountDialog>().Any(w => w.IsVisible);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        return embedded;
    }

    private static async Task<bool> ClickAndCloseAsync<T>(Window owner, Button button) where T : Window
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<T>().FirstOrDefault(d => d.Owner == owner && d.IsVisible) is { } dialog)
            { timer.Stop(); dialog.Close(); completion.TrySetResult(true); }
            else if (++ticks >= 250)
            {
                timer.Stop();
                foreach (var owned in owner.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) owned.Close();
                completion.TrySetResult(false);
            }
        };
        timer.Start();
        try { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return await completion.Task; }
        finally { timer.Stop(); }
    }

    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var result = request.RequestUri!.AbsolutePath.EndsWith("/login", StringComparison.Ordinal)
                ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}" : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
