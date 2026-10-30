using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsMemoDeleteChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "memo-delete-" + Guid.NewGuid() + ".smj");
        try
        {
            var block = window.Session.ActiveBlock!; block.ServerId = "memo-delete-fixture";
            var at = new DateTimeOffset(2026, 10, 1, 12, 30, 0, TimeSpan.FromHours(9));
            var original = new LineMemo("delete-fixture", 42, "TEXT", 0, "원문", "원 메모", at,
                Author: "원 작성자", Replies: [new("다른 작성자", "첫 답글", at.AddMinutes(1)), new(null, "이전 답글", at.AddMinutes(2))]);
            block.Memos.Add(original);
            var dialog = new MemoThreadDialog(block, "TEXT", 0, "원문", window.EditStructure, window.Session, author: "현재 사용자") { Owner = window };
            dialog.Show(); dialog.UpdateLayout();
            var input = dialog.ReplyInput;
            input.Focus(); dialog.UpdateLayout();
            var replySurface = input.Template.FindName("Surface", input) as Border;
            check(input.IsKeyboardFocusWithin && replySurface?.BorderBrush is SolidColorBrush replyFocus && replyFocus.Color == Color.FromRgb(0, 95, 184) && replySurface.BorderThickness == new Thickness(1),
                "Focused memo reply shows the full Figma blue outline");
            check(AutomationProperties.GetName(input) == "메모 회신", "Memo reply retains its accessible name");
            input.SelectionBrush = Brushes.Purple; dialog.ApplyReplyAppearance(true); dialog.ApplyReplyAppearance(false);
            check(input.SelectionBrush == Brushes.Purple, "Memo reply preserves explicitly chosen selection colors when contrast changes");
            input.ClearValue(TextBox.SelectionBrushProperty); dialog.ApplyReplyAppearance(true); dialog.UpdateLayout();
            var hcSurface = input.Template.FindName("Surface", input) as Border;
            check(input.Background is SolidColorBrush bg && bg.Color == SystemColors.WindowColor &&
                input.Foreground is SolidColorBrush fg && fg.Color == SystemColors.WindowTextColor &&
                input.SelectionBrush is SolidColorBrush selection && selection.Color == SystemColors.HighlightColor &&
                hcSurface?.BorderBrush is SolidColorBrush border && border.Color == SystemColors.HighlightColor,
                "Focused memo reply uses system contrast colors");
            dialog.ApplyReplyAppearance(false); input.Text = string.Join("\n", Enumerable.Repeat("긴 회신 내용", 20)); input.Focus(); dialog.UpdateLayout(); input.ScrollToEnd(); dialog.UpdateLayout();
            var replyScroll = input.Template.FindName("PART_ContentHost", input) as ScrollViewer;
            check(replyScroll is { ScrollableHeight: > 0, VerticalOffset: > 0 } && input.Text.Contains("긴 회신 내용"),
                "Long memo replies stay editable and scroll within the focus outline");
            input.Clear();
            var replyButtons = Descendants(dialog).OfType<Button>().Where(b => AutomationProperties.GetName(b) == "답글 삭제").ToArray();
            check(replyButtons.Length == 2 && replyButtons.All(b => b.IsEnabled), "Each reply has an enabled delete action for a document editor");
            replyButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var after = block.Memos.Single(m => m.LocalId == original.LocalId);
            check(after.Body == original.Body && after.Author == original.Author && after.Replies is [{ Author: "다른 작성자", Body: "첫 답글" }],
                "Deleting one reply keeps the root and other author's reply unchanged");
            replyButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Memos.Single(m => m.LocalId == original.LocalId).Replies is [{ Body: "첫 답글" }],
                "A stale reply action cannot delete a different reply after the conversation changes");
            dialog.Close(); window.ReplayHistory();
            check(block.Memos.Single(m => m.LocalId == original.LocalId).Replies!.SequenceEqual(original.Replies!), "Undo restores the deleted reply");
            window.ReplayHistory(true);
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
            var loaded = await SmjFile.LoadAsync(path);
            var saved = loaded.Blocks.SelectMany(b => b.Memos).Single(m => m.LocalId == original.LocalId);
            var sent = JobClient.PlanSave(SmjFile.Snapshot(loaded).Manifest).Pages.SelectMany(p => p.Memos ?? []).Single(m => m.MemoId == 42);
            check(saved.Replies is [{ Author: "다른 작성자", Body: "첫 답글" }] && sent.Replies is [{ Author: "다른 작성자", Body: "첫 답글" }],
                "Redo, SMJ reopen and server save snapshot retain the remaining reply");
            dialog = new MemoThreadDialog(block, "TEXT", 0, "원문", window.EditStructure, window.Session) { Owner = window };
            dialog.Show(); dialog.UpdateLayout();
            Descendants(dialog).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "메모 삭제")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!block.Memos.Any(m => m.LocalId == original.LocalId), "Deleting the root removes its conversation");
            dialog.Close(); window.ReplayHistory();
            check(block.Memos.Single(m => m.LocalId == original.LocalId).Replies is [{ Body: "첫 답글" }],
                "Undo of root deletion restores its remaining reply");

            dialog = new MemoThreadDialog(block, "TEXT", 0, "원문", window.EditStructure, window.Session) { Owner = window };
            dialog.Show(); dialog.UpdateLayout();
            window.Session.IsReadOnly = true; dialog.UpdateLayout();
            var deletes = Descendants(dialog).OfType<Button>().Where(b => AutomationProperties.GetName(b) is "메모 삭제" or "답글 삭제").ToArray();
            check(deletes.Length == 2 && deletes.All(b => !b.IsEnabled), "Read-only documents disable root and reply deletion");
            foreach (var b in deletes) b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Memos.Single(m => m.LocalId == original.LocalId).Replies is [{ Body: "첫 답글" }],
                "A forced click cannot delete a read-only memo or reply");
            dialog.Close();
        }
        finally { window.Session.IsReadOnly = false; await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
