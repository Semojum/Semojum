using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;
internal static class WindowsNoticeDesignChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 1000 }; window.Show();
        try
        {
            var model = window.Session; var block = model.ActiveBlock!; block.Source = "원문 보존";
            model.JobId = "fixture-job"; model.Progress = new ConversionProgress(128, 46, 1, 1, "잔여 약 12분");
            model.CreditNotice = "크레딧이 소진되었습니다. (46/128쪽 변환 완료)";
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(window.CreditBanner.IsVisible && window.CreditBanner.ActualHeight == 32 && !window.ConversionBanner.IsVisible,
                "Credit notice keeps the 32-pixel Figma banner without a duplicate fixed conversion panel");
            var bannerIcons = Descendants(window.CreditBanner).OfType<SvgViewbox>().ToArray();
            check(bannerIcons.Length == 2 && bannerIcons.All(i => i.Drawings is { Bounds.IsEmpty: false }), "Credit banner renders the original warning and close SVGs");
            check(Descendants(window.CreditBanner).OfType<Button>().Any(b => Equals(b.Content, "이어서 처리") && b.Width == 110), "Credit recovery action remains available at the Figma width");
            if (Environment.GetEnvironmentVariable("NOTICE_CREDIT_CAPTURE") is { Length: > 0 } creditCapture) await WindowsSmoke.RenderElementAsync(window.CreditBanner, creditCapture);
            var tab = (TabItem)window.DocumentTabs.Items[0];
            var trigger = Descendants((DependencyObject)tab.Header).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "TabConversionProgress");
            check(Equals(trigger.Content, "변환 중") && AutomationProperties.GetName(trigger).Contains("47", StringComparison.Ordinal), "An accessible text control exposes real progress when the Figma loading icon is empty");
            var popup = ((Grid)VisualTreeHelper.GetParent(trigger)).Children.OfType<Popup>().Single();
            trigger.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(popup.IsOpen && popup.Child is Border { Width: 320, Height: 108 } && Descendants(popup.Child).OfType<TextBlock>().Any(t => t.Text.Contains("47쪽 / 128쪽 처리 (37%)")),
                "Progress popup uses real finished and total counts with the Figma dimensions");
            var countLabel = Descendants(popup.Child).OfType<TextBlock>().First(t => t.Text.Contains("47쪽 / 128쪽"));
            check(countLabel.FontWeight == FontWeights.Normal && Descendants(popup.Child).OfType<Button>().First(b => Equals(b.Content, "취소")).FontWeight == FontWeights.Normal,
                "Progress text and cancel do not inherit the selected tab's bold font");
            if (Environment.GetEnvironmentVariable("NOTICE_PROGRESS_CAPTURE") is { Length: > 0 } progressCapture) await WindowsSmoke.RenderElementAsync((FrameworkElement)popup.Child, progressCapture);
            popup.IsOpen = false; model.Progress = null; model.CreditNotice = "";
            model.ErrorNotice = "연결이 끊겼습니다. 수신된 원문은 유지됩니다."; window.UpdateLayout();
            check(window.OperationErrorBanner.IsVisible && block.Source == "원문 보존", "Connection failures remain visible without changing document content");
            model.ErrorNotice = "";

            var root = Path.Combine(directory, "update-fixture-" + Guid.NewGuid().ToString("N"));
            var version = new Version(MainWindow.CurrentVersion.ToString(3)); var stage = Path.Combine(root, version.ToString(3));
            Directory.CreateDirectory(Path.Combine(stage, "engine"));
            var files = new[] { "Semojum.Mockup.exe", "Semojum.Mockup.dll", "engine/semojum-engine.exe" };
            await File.WriteAllTextAsync(Path.Combine(stage, files[0]), "fixture executable");
            File.Copy(typeof(MainWindow).Assembly.Location, Path.Combine(stage, files[1]));
            await File.WriteAllTextAsync(Path.Combine(stage, files[2]), "fixture engine");
            var hashes = new Dictionary<string, string>();
            foreach (var file in files) hashes[file] = await PortableUpdate.HashAsync(Path.Combine(stage, file));
            await File.WriteAllTextAsync(Path.Combine(root, "pending.json"), JsonSerializer.Serialize(new PendingUpdate(version.ToString(3), hashes)));
            check(await AppUpdates.IsReadyAtAsync(root, new Version(0, 0, 0), version), "A complete staged update with matching version and hashes validates");
            check(!await AppUpdates.IsReadyAtAsync(root, new Version(0, 0, 0), new Version(version.Major + 1, 0, 0)), "A staged update for a different announced version is not treated as ready");
            await File.WriteAllTextAsync(Path.Combine(stage, files[0]), "tampered executable");
            check(!await AppUpdates.IsReadyAtAsync(root, new Version(0, 0, 0), version), "A modified staged file cannot produce a ready notice");

            var info = new AppVersionInfo(new Version(99, 0, 0), new Version(0, 0, 1), new Uri("https://fixture.invalid/update.zip"), "");
            typeof(MainWindow).GetField("availableVersion", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, info);
            var download = typeof(MainWindow).GetMethod("DownloadUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            window.UpdateDownloadForChecks = (_, _, _) => Task.CompletedTask;
            window.UpdateReadyForChecks = (_, _) => Task.FromResult(false);
            await (Task)download.Invoke(window, null)!;
            check(!Application.Current.Windows.OfType<UpdateReadyDialog>().Any(d => d.Owner == window && d.IsVisible), "Download completion without verified staging does not display a ready notice");
            window.UpdateReadyForChecks = (_, _) => Task.FromResult(true);
            await (Task)download.Invoke(window, null)!; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var notice = Application.Current.Windows.OfType<UpdateReadyDialog>().Single(d => d.Owner == window && d.IsVisible);
            notice.UpdateLayout();
            var updateIcon = Descendants(notice).OfType<SvgViewbox>().SingleOrDefault();
            check(notice.ActualWidth == 580 && notice.ActualHeight == 80 && updateIcon is { Width: 28, Height: 28, Drawings.Bounds.IsEmpty: false }, "Verified update notice uses the Figma dimensions and original icon");
            if (Environment.GetEnvironmentVariable("NOTICE_UPDATE_CAPTURE") is { Length: > 0 } updateCapture) await WindowsSmoke.RenderElementAsync(notice, updateCapture);
            var activates = 0; window.UpdateActivateForChecks = (_, _) => { activates++; return Task.FromResult(false); };
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var prompt = Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible && d.MessageText.Contains("저장"));
                if (prompt is not null) prompt.ActionButtons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { notice.RestartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150); }
            finally { timer.Stop(); }
            check(activates == 0 && window.IsVisible && block.Source == "원문 보존" && model.IsDirty, "Cancelling the save confirmation keeps all edits and does not launch the update");
            model.IsDirty = false;
            notice.RestartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(150);
            check(activates == 1 && notice.IsVisible && window.IsVisible && model.Status.Contains("확인할 수 없습니다"), "Failed revalidation keeps the editor open and reports the failure");
            notice.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!notice.IsVisible && window.IsVisible && block.Source == "원문 보존", "Dismissing the update notice preserves the open document");
            await (Task)download.Invoke(window, null)!; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            notice = Application.Current.Windows.OfType<UpdateReadyDialog>().Single(d => d.Owner == window && d.IsVisible);
            window.UpdateActivateForChecks = (_, _) => { activates++; return Task.FromResult(true); };
            notice.RestartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
            check(activates == 2 && !window.IsVisible, "Explicit restart closes the editor only after the verified activation succeeds");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
