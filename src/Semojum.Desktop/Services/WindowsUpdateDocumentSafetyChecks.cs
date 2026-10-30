using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsUpdateDocumentSafetyChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); window.Show();
        var restart = typeof(MainWindow).GetMethod("RestartIntoUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task<bool> Restart() => (Task<bool>)restart.Invoke(window, null)!;
        var secondary = new Window { Owner = window, IsEnabled = false }; secondary.Show();
        try
        {
            window.ToggleDetached();
            var detached = window.DetachedWindow!;
            var model = window.Session; model.IsDirty = false;
            var launchCount = 0;
            var pending = new TaskCompletionSource<Func<bool>?>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.UpdatePrepareForChecks = (_, _) => pending.Task;
            var operation = Restart();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!window.IsEnabled && !window.PageEditor.Editor.IsEnabled && !detached.IsEnabled && !secondary.IsEnabled,
                "The text editor and detached braille window cannot accept user input during update validation");
            model.ActiveBlock!.Source = "비동기 변경 보존";
            pending.SetResult(() => { launchCount++; return true; });
            check(!await operation && launchCount == 0 && window.IsVisible && model.IsDirty && model.ActiveBlock.Source == "비동기 변경 보존",
                "A document change during update validation prevents the new process from starting");
            check(window.IsEnabled && detached.IsEnabled && !secondary.IsEnabled,
                "Validation cancellation restores every window's previous enabled state");

            model.IsDirty = false;
            pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = Restart(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var newDocument = typeof(MainWindow).GetMethod("NewTextDocumentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)newDocument.Invoke(window, new object[] { "추가된 탭", "새 탭.smj" })!;
            pending.SetResult(() => { launchCount++; return true; });
            check(!await operation && launchCount == 0 && window.IsVisible && window.DocumentTabs.Items.Count == 2,
                "A tab created during update validation prevents the new process from starting");
            check(window.IsEnabled && detached.IsEnabled && !secondary.IsEnabled,
                "A changed tab list also restores the prior enabled states");

            model.IsDirty = false;
            window.Session.ActiveBlock!.Source = "두 번째 문서";
            window.UpdatePrepareForChecks = (_, _) => Task.FromResult<Func<bool>?>(() => { launchCount++; return true; });
            var otherPrompt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            otherPrompt.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText.Contains("저장"));
                if (prompt is null) return;
                otherPrompt.Stop(); model.ActiveBlock!.Source = "첫 문서의 비동기 변경";
                prompt.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            otherPrompt.Start();
            try
            {
                check(!await Restart() && launchCount == 0 && model.IsDirty && model.ActiveBlock!.Source == "첫 문서의 비동기 변경",
                    "A previously confirmed document changed during the next save prompt prevents restart");
            }
            finally { otherPrompt.Stop(); }
            check(window.IsEnabled && detached.IsEnabled && !secondary.IsEnabled, "A change during another document confirmation restores window input");
            model.IsDirty = false;
            window.UpdatePrepareForChecks = (_, _) => Task.FromException<Func<bool>?>(new IOException("fixture"));
            // The second document was created with initial content; mark it clean for this failure path.
            window.Session.IsDirty = false;
            try { await Restart(); } catch (IOException) { }
            check(window.IsVisible && window.IsEnabled && detached.IsEnabled && !secondary.IsEnabled && launchCount == 0,
                "A validation exception restores all windows without launching an update");

            window.Session.ActiveBlock!.Source = "저장 취소 보존";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText.Contains("저장"));
                if (prompt is not null) prompt.ActionButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { check(!await Restart() && window.IsVisible && window.Session.IsDirty, "Cancelling save confirmation preserves the unsaved document"); }
            finally { timer.Stop(); }
            check(window.IsEnabled && detached.IsEnabled && !secondary.IsEnabled && launchCount == 0,
                "Cancelling save confirmation restores all windows without launching");
        }
        finally { secondary.Close(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); }
    }
}
