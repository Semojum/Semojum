using System.IO;
using System.Windows;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsDraftApplyChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: false, testWithoutAccount: true);
        window.Show();
        var block = window.Session.ActiveBlock!;
        window.SourceEditor.Document.Insert(0, "기존 설명");
        block.Braille = "⠿"; block.HasManualBraille = true;
        block.Translation = BrailleTranslation.Plain("⠿") with { SourceText = "기존 설명" };
        block.Metadata = new(Type: "image", SelectedIndex: 0, Alternatives: [
            new("기본", "기존 설명", null, "⠁", null),
            new("다른 설명", "새 설명", null, "⠃", null)]);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            var manual = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (manual is not null) manual.ActionButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        };
        timer.Start();
        try
        {
            await window.ExecuteAsync("drafts");
            ((System.Windows.Controls.RadioButton)window.DraftChoiceGrid.Children[1]).IsChecked = true;
            window.DraftDockClose.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            check(block.Source == "새 설명" && block.Braille == "⠿" && block.HasManualBraille
                && block.Metadata.SelectedIndex == 1 && block.Metadata.Alternatives?.Length == 2,
                "Applying an alternative while keeping manual braille preserves all drafts and the edited braille");
            check(block.Translation is null, "Keeping manual braille does not retain translation evidence for the previous source");
            var path = Path.Combine(directory, "manual-draft.smj");
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
            var restored = await SmjFile.LoadAsync(path);
            var saved = restored.Blocks.Single();
            check(saved.Source == "새 설명" && saved.Braille == "⠿" && saved.HasManualBraille
                && saved.Metadata.SelectedIndex == 1 && saved.Metadata.Alternatives?.Length == 2 && saved.Translation is null,
                "Saved and reopened work retains the selected draft and manually edited braille");
        }
        finally
        {
            timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close();
        }
    }
}
