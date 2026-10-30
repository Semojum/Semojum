using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsTableDraftChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "table-draft-" + Guid.NewGuid() + ".smj");
        var choices = new[] { new BrailleDraft("first", "첫 초안", "⠃", []), new BrailleDraft("second", "둘째 초안", "⠉", []) };
        var block = window.Session.ActiveBlock!;
        block.Metadata = block.Metadata with { Type = "table", RenderMode = "first", TableSource = "표 원본" };
        block.Translation = BrailleTranslation.Plain("⠃") with { Drafts = choices, RenderMode = "first" };
        block.Braille = "⠿"; block.HasManualBraille = true; block.State = "직접 수정";
        try
        {
            var confirmation = -1; var confirmations = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var draft = Application.Current.Windows.OfType<DraftDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
                if (draft is not null)
                {
                    Descendants(draft).OfType<ComboBox>().Single().SelectedIndex = 1;
                    Descendants(draft).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return;
                }
                var manual = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
                if (manual is not null)
                {
                    confirmations++; manual.ActionButtons[confirmation].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            };
            timer.Start();
            try
            {
                confirmation = 0; await window.ExecuteAsync("drafts");
                check(confirmations == 1 && block.Braille == "⠿" && block.HasManualBraille && block.Metadata.RenderMode == "first", "Keeping manually edited braille leaves the table draft unchanged");
                confirmation = 1; await window.ExecuteAsync("drafts");
                check(confirmations == 2 && block.Braille == "⠉" && !block.HasManualBraille && block.Metadata is { RenderMode: "second", TableSource: "표 원본" }
                    && block.Translation!.Drafts.SequenceEqual(choices), "Confirmed table draft keeps all alternatives and existing metadata");
                window.ReplayHistory();
                check(block.Braille == "⠿" && block.HasManualBraille && block.State == "직접 수정" && block.Metadata is { RenderMode: "first", TableSource: "표 원본" }
                    && block.Translation!.Drafts.SequenceEqual(choices), "Undo restores manual braille and original table metadata");
                window.ReplayHistory(true);
                check(block.Braille == "⠉" && !block.HasManualBraille && block.State == "표 초안 · 둘째 초안" && block.Metadata.RenderMode == "second" && block.Translation!.Drafts.SequenceEqual(choices),
                    "Redo restores the selected table draft without losing alternatives");
                await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
                var reopened = (await SmjFile.LoadAsync(path)).Blocks.Single(b => b.Id == block.Id);
                check(reopened.Braille == "⠉" && reopened.Metadata is { RenderMode: "second", TableSource: "표 원본" }
                    && reopened.Translation!.Drafts.Length == choices.Length && reopened.Translation.Drafts.Zip(choices).All(p =>
                        p.First.RenderMode == p.Second.RenderMode && p.First.Label == p.Second.Label && p.First.Cells == p.Second.Cells && p.First.Breaks.SequenceEqual(p.Second.Breaks)),
                    "Saving and reopening preserves the selected table draft and all alternatives");
                var beforeBraille = block.Braille; var beforeMetadata = block.Metadata;
                var stale = window.CaptureTableDraft(block);
                block.Translation = block.Translation! with { Drafts = [new("new-first", "새 첫 초안", "⠋", []), new("new-second", "새 둘째 초안", "⠙", [])] };
                window.ApplyTableDraft(stale, 1);
                check(block.Braille == beforeBraille && block.Metadata == beforeMetadata && block.Translation!.Drafts[1].Cells == "⠙"
                    && window.Session.Status.Contains("다시"), "Changed table drafts are not applied from a stale selection and the user is asked to select again");
                block.Braille = "⠿"; block.HasManualBraille = true; beforeMetadata = block.Metadata;
                var confirming = window.CaptureTableDraft(block);
                window.ApplyTableDraft(confirming, 1, b => { b.Translation = b.Translation! with { Drafts = [new("later", "새로 받은 초안", "⠚", [])] }; return ManualEditDecision.Overwrite; });
                check(block.Braille == "⠿" && block.HasManualBraille && block.Metadata == beforeMetadata && block.Translation!.Drafts is [{ Cells: "⠚" }]
                    && window.Session.Status.Contains("다시"), "Changes during manual overwrite confirmation do not erase manual braille");

                block.Source = "표 검토 원문"; block.Braille = "⠁⠃"; block.HasManualBraille = true;
                block.Metadata = new(Type: "table", RenderMode: "same");
                block.Translation = BrailleTranslation.Plain("⠁⠃") with { SourceText = block.Source, RenderMode = "same", Drafts = [new("same", "동일 초안", "⠁⠃", [])] };
                block.ReviewMarks.Clear(); block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
                var sameDraft = window.CaptureTableDraft(block);
                window.ApplyTableDraft(sameDraft, 0, _ => ManualEditDecision.Keep);
                window.ApplyTableDraft(sameDraft, 0, _ => ManualEditDecision.Cancel);
                check(block.ReviewMarks.Single() is { IsStale: false, OriginalText: "⠁" }, "Keeping or cancelling an identical table draft preserves the braille confirmation");
                var outdated = window.CaptureTableDraft(block); block.Metadata = block.Metadata with { RenderMode = "changed" };
                window.ApplyTableDraft(outdated, 0, _ => ManualEditDecision.Overwrite);
                check(!block.ReviewMarks.Single().IsStale, "A stale table draft is rejected without invalidating the braille confirmation");
                sameDraft = window.CaptureTableDraft(block); window.ApplyTableDraft(sameDraft, 0, _ => ManualEditDecision.Overwrite);
                check(block.Braille == "⠁⠃" && block.ReviewMarks.Single().IsStale, "Applying identical table braille invalidates the old confirmation");
                window.ReplayHistory(); check(!block.ReviewMarks.Single().IsStale, "Undo restores confirmation before an identical table draft");
                window.ReplayHistory(true); check(block.ReviewMarks.Single().IsStale, "Redo restores stale confirmation after the table draft");

                block.HasManualBraille = true; block.ReviewMarks.Clear(); block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
                typeof(MainWindow).GetMethod("RestoreOriginalBraille", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(window, null);
                check(block.Braille == "⠁⠃" && !block.HasManualBraille && block.ReviewMarks.Single().IsStale,
                    "Restoring identical original braille invalidates the old confirmation");
                window.ReplayHistory(); check(block.HasManualBraille && !block.ReviewMarks.Single().IsStale, "Undo of identical original restore recovers manual state and confirmation");
                window.ReplayHistory(true); check(!block.HasManualBraille && block.ReviewMarks.Single().IsStale, "Redo of original restore recovers stale confirmation");
            }
            finally { timer.Stop(); }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
