using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsTableDockChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true); window.Show();
        var path = Path.Combine(directory, "table-dock-" + Guid.NewGuid().ToString("N") + ".smj");
        var source = TaggedTable.Write([["가", "나"], ["다", "라"]]);
        var transposed = TaggedTable.Write([["가", "다"], ["나", "라"]]);
        var choices = new[] { new BrailleDraft("table_grid", "테두리", "⠃", [], source), new BrailleDraft("transposed", "행열 바꿈", "⠉", [], transposed) };
        var localRule = new RuleEvidence("local", "규정", "절", "표", "이전 안", 0, 0, 1);
        var serverRule = new RuleEvidence("server", "규정", "절", "원문", "원문 근거", -1, 0, 0, Origin: RuleEvidenceOrigin.TextPanel);
        var spacing = new BrailleSpacingMetadata(false, source, false);
        var block = window.Session.ActiveBlock!; block.Source = source;
        block.Metadata = block.Metadata with { Type = "table", TableSource = source, TableDisplayText = source, RenderMode = "table_grid", Rules = [serverRule] };
        block.Translation = BrailleTranslation.Plain("⠃") with { SourceText = source, Drafts = choices, RenderMode = "table_grid", RuleTrail = [localRule], Spacing = spacing };
        block.Braille = "⠿"; block.HasManualBraille = true; block.State = "직접 수정";
        var confirmation = 0; var oldDialogs = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var old = Application.Current.Windows.OfType<DraftDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
            if (old is not null) { oldDialogs++; old.DialogResult = false; return; }
            var manual = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.IsVisible && d.Owner == window);
            if (manual is not null) manual.ActionButtons[confirmation].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try
        {
            var complete = block.Translation!;
            block.Translation = complete with { Drafts = [choices[0], choices[1] with { SourceText = null }] };
            await window.ExecuteAsync("drafts");
            check(oldDialogs == 1 && !window.DraftDock.IsVisible, "Incomplete or legacy draft text falls back to the original table dialog");
            block.Translation = complete;
            await window.ExecuteAsync("drafts");
            check(window.DraftDock.IsVisible && oldDialogs == 1 && window.DraftChoiceGrid.Children.Count == 2,
                "Table drafts with verified source text open in the shared dock");
            ((RadioButton)window.DraftChoiceGrid.Children[1]).IsChecked = true;
            check(window.DraftTextPreview.Text == transposed && window.DraftBraillePreview.Text == "⠉" && block.Source == source && block.Braille == "⠿",
                "Transposed source and matching braille preview without changing manual work");
            confirmation = 0; window.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Source == source && block.Braille == "⠿" && block.HasManualBraille && block.Metadata.RenderMode == "table_grid",
                "Keeping manually corrected braille leaves the original table and selection intact");
            await window.ExecuteAsync("drafts"); ((RadioButton)window.DraftChoiceGrid.Children[1]).IsChecked = true;
            confirmation = 1; window.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Source == source && block.Metadata.TableSource == source && block.Metadata.RenderMode == "transposed"
                && block.Braille == "⠉" && !block.HasManualBraille && block.Translation!.Drafts[1].SourceText == transposed
                && block.Translation.RuleTrail is null && block.Translation.Spacing is null && block.Metadata.Rules is [{ RuleId: "server" }],
                "Applying the transposed draft keeps the input table and selected cells without double transposition");
            window.ReplayHistory();
            check(block.Source == source && block.Braille == "⠿" && block.HasManualBraille && block.Metadata.RenderMode == "table_grid"
                && block.Translation is { RuleTrail: [{ RuleId: "local" }], Spacing: not null }, "Undo restores manual braille, previous table mode and previous local metadata");
            window.ReplayHistory(true);
            check(block.Source == source && block.Braille == "⠉" && block.Metadata.RenderMode == "transposed"
                && block.Translation is { RuleTrail: null, Spacing: null }, "Redo restores the selected table mode without stale local metadata");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session)); var reopened = (await SmjFile.LoadAsync(path)).Blocks.Single(b => b.Id == block.Id);
            check(reopened.Source == source && reopened.Metadata.TableSource == source && reopened.Metadata.RenderMode == "transposed" && reopened.Braille == "⠉"
                && reopened.Translation!.Drafts[1].SourceText == transposed && reopened.Translation is { RuleTrail: null, Spacing: null }
                && reopened.Metadata.Rules is [{ RuleId: "server" }], "SMJ reopen preserves the table, source evidence and unverified local metadata state");
            await window.OpenAsync(path); await window.ExecuteAsync("drafts");
            check(window.DraftDock.IsVisible && window.DraftTextPreview.Text == transposed && window.DraftBraillePreview.Text == "⠉"
                && window.Session.ActiveBlock!.Source == source, "Reopened SMJ restores the selected table dock preview without changing the original table");
            var stable = window.Session.ActiveBlock!; stable.Translation = stable.Translation! with { RuleTrail = [localRule], Spacing = spacing };
            var unchanged = window.CaptureTableDraft(stable); window.ApplyTableDraft(unchanged, 1, _ => ManualEditDecision.Overwrite);
            check(stable.Translation is { RuleTrail: [{ RuleId: "local" }], Spacing: not null }, "Applying the unchanged draft preserves its valid local evidence and spacing");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
}
