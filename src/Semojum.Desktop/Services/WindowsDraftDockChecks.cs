using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.IO;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsDraftDockChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: false, testWithoutAccount: true);
        window.Show();
        window.WindowState = WindowState.Normal; window.Width = 1440; window.Height = 1000;
        var block = window.Session.ActiveBlock!;
        block.Metadata = new(Type: "image", SelectedIndex: 2, Alternatives: [
            new("생략", "", null, "", null),
            new("짧은 제목", "네 지역의 기후 특성", null, "⠁", null),
            new("개조식", "그림: 네 지역의 기후 특성\n1. (가) 최한월 −6℃, 연강수량 1,300㎜\n2. (나) 최한월 −2℃, 연강수량 1,100㎜", null, "⠲⠗⠡⠌⠤⠀⠙⠘⠤⠀⠠⠥⠧⠎⠅⠓⠀⠠⠍⠧⠑⠕\n⠼⠁⠲⠂⠅⠲⠀⠠⠺⠓⠱⠥⠀⠤⠼⠋⠲⠀⠘⠑⠲⠊⠊⠤", null),
            new("줄글", "네 지역의 기후 특성을 비교한 그림입니다.", null, "⠃", null)]);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var oldDialog = Application.Current.Windows.OfType<AlternativeDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (oldDialog is not null) oldDialog.DialogResult = false;
        };
        timer.Start();
        try
        {
            await window.ExecuteAsync("drafts");
            check(window.FindName("DraftDock") is FrameworkElement { IsVisible: true },
                "Alternative drafts open in the dock below the text and braille panels");
            check(window.DraftTextPreview.Text.StartsWith("그림: 네 지역의 기후 특성") && window.DraftBraillePreview.Text.StartsWith("⠲⠗⠡"),
                "The selected draft shows text and braille together");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            check(Math.Abs(window.DraftDock.ActualWidth - window.SourcePanel.ActualWidth - window.BrailleContainer.ActualWidth) < 3
                && Math.Abs(window.DraftTextColumn.ActualWidth - window.SourcePanel.ActualWidth) < 3,
                "The dock spans both editor panels and its divider follows their actual widths");
            await WindowsSmoke.RenderElementAsync(window.DraftDock, Path.Combine(Path.GetTempPath(), "draft-dock-verification.png"));
            ((RadioButton)window.DraftChoiceGrid.Children[3]).IsChecked = true;
            check(window.DraftTextPreview.Text == "네 지역의 기후 특성을 비교한 그림입니다." && window.DraftBraillePreview.Text == "⠃"
                && block.Metadata.SelectedIndex == 2, "Changing a draft updates both previews without applying it early");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(!window.DraftDock.IsVisible && block.Metadata.SelectedIndex == 2 && block.Metadata.Alternatives?.Length == 4,
                "Escape cancels the preview and preserves the selected draft and all alternatives");
        }
        finally
        {
            timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
        }
    }
}
