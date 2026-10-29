using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsDraftSafetyChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var window = new MainWindow(smoke: true); window.Show();
        typeof(MainWindow).GetField("autoTranslate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
        var block = window.Session.ActiveBlock!;
        var evidence = new RuleEvidence("original", "server", "1", "기존 규정", "기존 원문", -1, 0, 0, Origin: RuleEvidenceOrigin.TextPanel);
        var drafts = new[] { new AlternativeDraft("기존", "기존 원문", null, "⠁", null), new AlternativeDraft("다른 안", "다른 원문", null, "⠃", null) };
        var path = Path.Combine(directory, "draft-evidence-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            block.Source = "기존 원문"; block.Braille = "⠁"; block.Metadata = new(Type: "image", SelectedIndex: 0, Alternatives: drafts, Rules: [evidence]);
            await window.ExecuteAsync("drafts");
            window.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Source == "기존 원문" && block.Metadata.Rules is [{ RuleId: "original" }], "Reapplying unchanged source preserves its original evidence");
            await window.ExecuteAsync("drafts"); ((RadioButton)window.DraftChoiceGrid.Children[1]).IsChecked = true;
            window.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(block.Source == "다른 원문" && block.Metadata.Rules is null && block.Metadata.Alternatives?.Length == 2 && AppliedRules.Select(block, 0, 0).Length == 0,
                "Changing visual draft source invalidates old source evidence without losing other drafts");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session)); var saved = (await SmjFile.LoadAsync(path)).Blocks.Single(b => b.Id == block.Id);
            check(saved.Metadata.Rules is null && saved.Metadata.Alternatives?.Length == 2, "Reopened draft does not revive old source evidence");
            window.ReplayHistory();
            check(block.Source == "기존 원문" && block.Metadata.Rules is [{ RuleId: "original" }], "Undo restores original source evidence");
            window.ReplayHistory(true);
            check(block.Source == "다른 원문" && block.Metadata.Rules is null, "Redo restores the new draft without old evidence");
            await window.ExecuteAsync("drafts");
            window.Session.Options = window.Session.Options with { KoreanGrade1 = !window.Session.Options.KoreanGrade1 };
            check(!window.DraftDock.IsVisible && block.Source == "다른 원문" && block.Braille == "⠃", "Changing document translation options invalidates an open draft preview");
            block.HasManualBraille = true; block.Braille = "⠿";
            await window.ExecuteAsync("drafts"); ((RadioButton)window.DraftChoiceGrid.Children[0]).IsChecked = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (dialog is null) return;
                timer.Stop(); block.Source = "비동기 갱신 원문"; dialog.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { window.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); }
            check(block.Source == "비동기 갱신 원문" && block.Braille == "⠿" && block.HasManualBraille,
                "A draft confirmation cannot overwrite a block changed during the confirmation dialog");
            block.HasManualBraille = false; block.Source = "표 원문"; block.Braille = "⠁";
            block.Metadata = new(Type: "table", RenderMode: "first");
            block.Translation = BrailleTranslation.Plain("⠁") with { Drafts = [new("first", "첫 안", "⠁", []), new("second", "둘째 안", "⠃", [])] };
            var table = window.CaptureTableDraft(block);
            window.ApplyTableDraft(table, 1, _ => { window.Session.Options = window.Session.Options with { KoreanGrade1 = !window.Session.Options.KoreanGrade1 }; return ManualEditDecision.Overwrite; });
            check(block.Braille == "⠁" && block.Metadata.RenderMode == "first", "Table confirmation rejects a draft when translation options changed");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
}
