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
            var choiceRow = (FrameworkElement)window.DraftChoiceScroll.Parent;
            check(window.DraftDockTitle.Text == "대체 초안 · 그림 설명 4안" && Label(window, 3) == "④ 줄글" && Math.Abs(choiceRow.ActualHeight - 40) < 0.5,
                "Four server drafts fill one 40px row of the Figma dock with the server labels");
            await WindowsSmoke.RenderElementAsync(window.DraftDock, Path.Combine(Path.GetTempPath(), "draft-dock-verification.png"));
            ((RadioButton)window.DraftChoiceGrid.Children[3]).IsChecked = true;
            check(window.DraftTextPreview.Text == "네 지역의 기후 특성을 비교한 그림입니다." && window.DraftBraillePreview.Text == "⠃"
                && block.Metadata.SelectedIndex == 2, "Changing a draft updates both previews without applying it early");
            window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            check(!window.DraftDock.IsVisible && block.Metadata.SelectedIndex == 2 && block.Metadata.Alternatives?.Length == 4,
                "Escape cancels the preview and preserves the selected draft and all alternatives");

            // The server decides the number of drafts; the Figma four is one example.
            static AlternativeDraft[] Drafts(params string[] labels) =>
                labels.Select((label, i) => new AlternativeDraft(label, label + " 설명", null, "⠁" + new string('⠃', i), null)).ToArray();
            async Task OpenAsync(int selected, params string[] labels)
            {
                block.Metadata = new(Type: "image", SelectedIndex: selected, Alternatives: Drafts(labels));
                await window.ExecuteAsync("drafts"); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            }
            await OpenAsync(1, "짧은 제목", "줄글");
            check(window.DraftDockTitle.Text == "대체 초안 · 그림 설명 2안" && window.DraftChoiceGrid.Children.Count == 2 && Label(window, 0) == "① 짧은 제목" && Label(window, 1) == "② 줄글"
                && ((RadioButton)window.DraftChoiceGrid.Children[1]).IsChecked == true && window.DraftTextPreview.Text == "줄글 설명" && window.DraftBraillePreview.Text == "⠁⠃"
                && Math.Abs(choiceRow.ActualHeight - 40) < 0.5 && Math.Abs(window.DraftDock.ActualHeight - 212) < 0.5,
                "Two server drafts show both labels in the same row and open on the server-selected draft");
            await OpenAsync(5, "생략", "짧은 제목", "개조식", "상세", "줄글", "별책 참조");
            check(window.DraftDockTitle.Text == "대체 초안 · 그림 설명 6안" && window.DraftChoiceGrid.Children.Count == 6 && Label(window, 5) == "⑥ 별책 참조"
                && window.DraftChoiceScroll.ScrollableHeight < 0.5 && Math.Abs(window.DraftChoiceScroll.ActualHeight - 76) < 0.5
                && Math.Abs(window.DraftDock.ActualHeight - 212) < 0.5 && window.DraftTextPreview.Text == "별책 참조 설명",
                "Six server drafts show in two rows without scrolling while the dock keeps its Figma height");
            await OpenAsync(9, Enumerable.Range(1, 10).Select(i => "안 " + i).ToArray());
            var last = (RadioButton)window.DraftChoiceGrid.Children[9];
            var bounds = last.TransformToAncestor(window.DraftChoiceScroll).TransformBounds(new Rect(0, 0, last.ActualWidth, last.ActualHeight));
            check(window.DraftDockTitle.Text == "대체 초안 · 그림 설명 10안" && Label(window, 9) == "⑩ 안 10" && last.IsChecked == true
                && window.DraftChoiceScroll.ScrollableHeight > 0 && bounds.Top >= -0.5 && bounds.Bottom <= window.DraftChoiceScroll.ViewportHeight + 0.5
                && Math.Abs(window.DraftDock.ActualHeight - 212) < 0.5 && window.DraftTextPreview.Text == "안 10 설명",
                "More drafts scroll inside the dock and a saved later choice opens scrolled into view");
            var radios = window.DraftChoiceGrid.Children.OfType<RadioButton>().ToArray();
            check(radios.Length == 10 && radios.Select(r => r.GroupName).Distinct().Count() == 1
                && radios.All(r => r.Focusable && r.IsEnabled && KeyboardNavigation.GetIsTabStop(r)),
                "Every draft, whatever the count, is a keyboard-reachable choice in one radio group");
        }
        finally
        {
            await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
        }
    }

    private static string Label(MainWindow window, int index) =>
        ((StackPanel)((RadioButton)window.DraftChoiceGrid.Children[index]).Content).Children.OfType<TextBlock>().Single().Text;
}
