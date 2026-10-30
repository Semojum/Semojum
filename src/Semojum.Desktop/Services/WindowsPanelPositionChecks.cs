using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Services;

internal static class WindowsPanelPositionChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var model = window.Session;
            var refresh = typeof(MainWindow).GetMethod("RefreshEditorStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;
            void Refresh() { refresh.Invoke(window, null); window.UpdateLayout(); }
            check(window.OriginalPosition.Text == "", "Blank original does not invent a page position");
            check(window.OriginalPosition.FontSize == 12 && window.SourcePosition.FontSize == 12 && window.Braille.LayoutLabel.FontSize == 12,
                "All three panel position labels use the Figma 12px text size");

            model.Blocks.Clear(); model.ActiveBlock = null; model.OriginalPages.Clear(); model.PageStatuses.Clear();
            var first = new EditorBlock { Id = 1, Page = 1, Source = "첫 쪽", Braille = "⠁" };
            var third = new EditorBlock { Id = 3, Page = 3, Source = "a\nbc", Braille = string.Join("\n", Enumerable.Repeat("⠁", 16)), HasManualBraille = true };
            model.Add(first); model.Add(third); model.ActiveBlock = third;
            model.OriginalPages[1] = new OriginalPage("text", null, ["첫 쪽"]);
            model.OriginalPages[3] = new OriginalPage("text", null, ["셋째 쪽"]);
            model.PageStatuses[1] = "COMPLETED"; model.PageStatuses[2] = "FAILED"; model.PageStatuses[3] = "COMPLETED";
            model.JobId = "position-job"; model.JobStatus = "COMPLETED";
            window.PageEditor.FollowActive(3);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Refresh();
            check(window.OriginalPosition.Text == "3쪽", "A contiguous partial page list does not invent an original total");
            check(window.SourcePosition.Text == "3쪽 2줄 2칸", $"Source position uses current page caret (actual {window.SourcePosition.Text})");
            check(window.StatusPage.Text == "쪽 3" && Equals(window.StatusPage.ToolTip, "원본 3쪽 (전체 쪽수 미확인)"),
                "A partial original list does not invent a status-bar total or tooltip");

            model.PageStatuses.Remove(2); Refresh();
            check(window.OriginalPosition.Text == "3쪽", "Sparse page numbers do not invent an original total");
            check(window.StatusPage.Text == "쪽 3" && Equals(window.StatusPage.ToolTip, "원본 3쪽 (전체 쪽수 미확인)"),
                "Sparse original pages do not claim the count is a known total");
            model.JobStatus = "IN_PROGRESS";
            check(model.TryApplyServerOriginalPageTotal("position-job", 128, [1, 3]), "A matching partial server response preserves its explicit original total");
            Refresh();
            check(window.OriginalPosition.Text == "3 / 128쪽", "A sparse in-progress response displays only the confirmed server total");
            check(window.StatusPage.Text == "쪽 3 / 128" && Equals(window.StatusPage.ToolTip, "원본 3쪽 (총 128쪽)"),
                "A partially received job uses the confirmed total in the status bar");
            model.OriginalPages.Remove(3); Refresh();
            check(window.StatusPage.Text == "쪽 3 / 128" && Equals(window.StatusPage.ToolTip, "작업 3쪽 (총 128쪽 · 원본 미수신)"),
                "A known total remains accurate before the current original page is received");
            model.OriginalPages[3] = new OriginalPage("text", null, ["셋째 쪽"]); Refresh();
            check(!model.TryApplyServerOriginalPageTotal("old-job", 4, [1, 3]) && model.TotalOriginalPages == 128,
                "An older job response cannot change the current document total");
            model.JobId = "next-job"; Refresh();
            check(window.OriginalPosition.Text == "3쪽", "Changing jobs discards the previous job's total");
            model.TotalOriginalPages = 128;
            model.IsTextJob = true; Refresh();
            check(window.OriginalPosition.Text == "", "TXT documents do not show server job pages as original-page positions");
            check(window.StatusPage.Text == "쪽 3" && Equals(window.StatusPage.ToolTip, "텍스트 3쪽"),
                "TXT status is labelled as text without using an original-page total");
            model.TotalOriginalPages = null; model.IsTextJob = false; Refresh();

            third.Source = "새 원문"; Refresh();
            check(window.SourcePosition.Text == "", "An outdated source projection does not show a caret for newer text");
            await WaitUntilAsync(() => window.PageEditor.Editor.Text == "새 원문" && window.SourcePosition.Text.StartsWith("3쪽 "));
            check(window.SourcePosition.Text.StartsWith("3쪽 "), "Source position returns after the page projection catches up");

            model.Layout = new(Columns: 8, Lines: 4, StartPage: 7, PageNumbers: "출력 안 함", Footer: "출력 안 함");
            check(!window.Braille.GridView.CoordinatesCurrent && window.Braille.LayoutLabel.Text == "",
                "A braille recomposition clears its old position immediately");
            await WaitUntilAsync(() => window.Braille.GridView.CoordinatesCurrent && window.Braille.GridView.Items.Count >= 2);
            var grid = window.Braille.GridView;
            check(grid.GoToFace(2), "The real second braille face can be selected");
            await WaitUntilAsync(() => grid.CoordinatesCurrent && grid.CaretCoordinates is { Face: 2 }
                && window.Braille.LayoutLabel.Text.StartsWith("2면 "));
            var p = grid.CaretCoordinates;
            check(p is { Face: 2 } && window.Braille.LayoutLabel.Text == $"{p.Value.Face}면 {p.Value.Line}줄 {p.Value.Cell}칸",
                $"Braille header uses the current composed face, line and cell (actual {window.Braille.LayoutLabel.Text})");
            window.Braille.RefreshAppearance();
            check(window.Braille.LayoutLabel.Text == $"{p?.Face}면 {p?.Line}줄 {p?.Cell}칸",
                "Refreshing appearance does not replace grid position with the hidden editor's dimensions");

            window.ToggleDetached(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var detached = window.DetachedPane;
            await WaitUntilAsync(() => detached is not null && detached.GridView.CoordinatesCurrent && detached.GridView.CaretCoordinates is not null);
            var dp = detached!.GridView.CaretCoordinates;
            check(dp is not null && detached.LayoutLabel.Text == $"{dp.Value.Face}면 {dp.Value.Line}줄 {dp.Value.Cell}칸",
                "Detached braille pane tracks its own current grid position");
            window.ToggleDetached(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(window.DetachedPane is null && grid.CoordinatesCurrent, "Returning the detached panel keeps the current main grid");

            foreach (var width in new[] { 960d, 480d })
            {
                window.Width = width; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                check(LabelFits(window.OriginalPosition, window.OriginalHeader) && LabelFits(window.SourcePosition, window.SourceHeader)
                    && LabelFits(window.Braille.LayoutLabel, window.Braille.Header),
                    $"The panel location labels remain inside their headers at {width:0}px");
            }
            model.OriginalPages.Clear(); Refresh();
            check(window.OriginalPosition.Text == "", "An original-free document does not retain an old original position");
            typeof(MainWindow).GetMethod("SetActiveEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [window.PageEditor.Editor]);
            Refresh();
            check(window.StatusPage.Text == "쪽 3" && Equals(window.StatusPage.ToolTip, "작업 3쪽 (원본 없음)"),
                "An original-free document does not describe its working page as an original");

            var lines = window.AllTagsMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "점자 줄"));
            var lineItems = lines.Items.OfType<MenuItem>().ToArray();
            check(lineItems.Select(item => item.Tag as string).SequenceEqual(new[] {
                "tag:상단줄", "tag:중단줄", "tag:하단줄", "tag:상단굵은줄", "tag:하단굵은줄",
                "tag:표시작", "tag:표중앙", "tag:표끝", "tag:표구분선", "tag:표지시작", "tag:표지세로", "tag:표지끝" }),
                "The real braille-line menu preserves the Figma table-line order and supported extra tags");
            foreach (var name in new[] { "표시작", "표중앙", "표끝", "표구분선", "표지시작" })
            {
                var editor = window.PageEditor.Editor;
                editor.Select(editor.Document.TextLength, 0);
                var previous = editor.Text;
                lineItems.Single(item => Equals(item.Tag, "tag:" + name)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                check(editor.Text == previous + BrailleTags.Get(name).Insert(""),
                    $"The {name} command inserts its real tag through the submenu click");
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static bool LabelFits(TextBlock label, FrameworkElement header)
    {
        var bounds = label.TransformToAncestor(header).TransformBounds(new Rect(0, 0, label.ActualWidth, label.ActualHeight));
        return bounds.Left >= -1 && bounds.Right <= header.ActualWidth + 1;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
    }
}
