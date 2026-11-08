using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsRequiredUpdateChecks
{
    private sealed class Handler : HttpMessageHandler
    {
        internal readonly TaskCompletionSource<HttpResponseMessage> Version = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/login"))
                return Task.FromResult(Json("{\"accessToken\":\"a\",\"refreshToken\":\"r\",\"role\":\"ROLE_USER\"}"));
            Started.TrySetResult(); return Version.Task.WaitAsync(token);
        }
        internal static HttpResponseMessage Json(string result) => new(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") };
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetKeyboardState(byte[] state);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetKeyboardState(byte[] state);
    // The fixture stands for a plain F6 press. KeyGesture compares against Keyboard.Modifiers, which reads this thread's key
    // state, so an Alt, Ctrl, Shift or Windows key left down by real input (such as Alt+Tab away from a smoke window) would
    // make it another gesture. Release those keys for this thread only before injecting F6.
    private static void F6(MainWindow window)
    {
        var state = new byte[256];
        if (GetKeyboardState(state))
        {
            foreach (var modifier in new byte[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }) state[modifier] &= 0x7F;
            SetKeyboardState(state);
        }
        Keyboard.Focus(window);
        var source = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("Window has no presentation source");
        var key = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.F6) { RoutedEvent = Keyboard.KeyDownEvent };
        InputManager.Current.ProcessInput(key);
    }
    internal static async Task RunAsync(Action<bool, string> check)
    {
        foreach (var failSave in new[] { false, true })
        {
            var handler = new Handler();
            using var http = new HttpClient(handler);
            var api = new SemojumApiClient(http); await api.LoginAsync("editor", "fixture");
            var window = new MainWindow(api); window.Show();
            var model = window.Session;
            window.PageEditor.Editor.Document.Insert(0, "필수 업데이트 중 미저장 내용");
            var unsaved = model.ActiveBlock!.Source;
            window.UpdateDownloadForChecks = (_, _, _) => Task.CompletedTask;
            window.UpdateRequiredNoticeForChecks = _ => { };
            if (failSave)
            {
                var path = Path.Combine(Path.GetTempPath(), "semojum-missing-" + Guid.NewGuid().ToString("N"), "draft.smj");
                typeof(MainWindow).GetField("filePath", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, path);
            }
            var requested = false; var errorShown = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var notice = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (notice is null) return;
                if (!requested && notice.MessageText.Contains("저장"))
                {
                    requested = true;
                    var button = notice.ActionButtons[failSave ? 0 : 2];
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                else if (requested && !ReferenceEquals(notice, null) && notice.MessageText != "변경 내용을 저장하시겠습니까?")
                { errorShown = true; notice.Close(); }
            };
            try
            {
                bool initial = window.SixDotToggle.IsChecked == true;
                F6(window);
                check((window.SixDotToggle.IsChecked == true) != initial,
                    $"F6 fixture invokes the real editor key binding before startup (focus {Keyboard.FocusedElement?.GetType().Name}, active {window.IsActive}, modifiers {Keyboard.Modifiers})");
                F6(window);
                var startup = (Task)typeof(MainWindow).GetMethod("StartAccountAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                await handler.Started.Task;
                bool before = window.SixDotToggle.IsChecked == true;
                F6(window);
                check((window.SixDotToggle.IsChecked == true) == before, "F6 command cannot run while mandatory update metadata is loading");
                await window.ExecuteAsync("sixDotMode");
                check((window.SixDotToggle.IsChecked == true) == before, "Direct editor command is also blocked during account startup");
                timer.Start();
                handler.Version.TrySetResult(Handler.Json("{\"latestVersion\":\"99.0.0\",\"minSupportedVersion\":\"99.0.0\",\"downloadUrl\":\"https://fixture.invalid/update.zip\"}"));
                await startup.WaitAsync(TimeSpan.FromSeconds(8));
                check(requested && (!failSave || errorShown) && window.IsVisible && ReferenceEquals(window.Session, model)
                    && model.IsDirty && model.ActiveBlock!.Source == unsaved,
                    failSave ? "Failed save during required update keeps the unsaved document open" : "Cancelling required-update exit keeps the unsaved document open");
                if (window.IsVisible)
                {
                    await window.ExecuteAsync("sixDotMode");
                    check((window.SixDotToggle.IsChecked == true) != before, "Editor commands work again after startup is cancelled");
                }
            }
            finally
            {
                timer.Stop(); handler.Version.TrySetCanceled();
                await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close();
            }
        }
        // Cover the production response that blocked 0.9.2 before login: minimum 1.0.0, no download URL.
        foreach (var metadata in new[] { "null", "{\"latestVersion\":\"1.0.0\",\"minSupportedVersion\":\"1.0.0\",\"downloadUrl\":null}" })
        {
            var handler = new Handler();
            handler.Version.TrySetResult(Handler.Json(metadata));
            using var http = new HttpClient(handler);
            var window = new MainWindow(new SemojumApiClient(http)); window.Show();
            var loginUsable = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                if (window.Title != "누리닷 로그인") return;
                loginUsable = window.IsEnabled && window.WindowState == WindowState.Maximized
                    && !Application.Current.Windows.OfType<AccountDialog>().Any(d => d.IsVisible);
                window.PrepareSmokeShutdown(); window.Close();
            };
            try
            {
                timer.Start();
                var startup = (Task)typeof(MainWindow).GetMethod("StartAccountAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                await startup.WaitAsync(TimeSpan.FromSeconds(8));
                check(loginUsable, $"Startup displays a usable maximized login in the existing window (metadata: {metadata})");
            }
            finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); }
        }
    }
}
