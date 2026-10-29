using System.Windows;
using System.Windows.Controls;
using System.Reflection;
using Semojum.Core;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsDraftPendingChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var pending = new MainWindow(smoke: true); pending.Show();
        var block = pending.Session.ActiveBlock!;
        block.Source = "기존 원문"; block.Braille = "⠿"; block.HasManualBraille = true;
        block.Metadata = new(Type: "image", SelectedIndex: 0, Alternatives: [
            new("기존", "기존 원문", "기존 원문", null, null), new("새 초안", "새 원문", "새 원문", null, null)]);
        try
        {
            await pending.ExecuteAsync("drafts");
            ((RadioButton)pending.DraftChoiceGrid.Children[1]).IsChecked = true;
            pending.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(pending.DraftDock.IsVisible && block.Source == "기존 원문" && block.Braille == "⠿"
                && pending.Session.Status.Contains("점역", StringComparison.Ordinal),
                "Applying while the selected preview is pending keeps the dock and manual work with an explanation");
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (pending.DraftBraillePreview.Text == "점역 중…" && DateTime.UtcNow < deadline) await Task.Delay(20);
            // The separate WindowsDraftApplyChecks exercises the manual-confirmation choice.
            block.HasManualBraille = false;
            pending.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!pending.DraftDock.IsVisible && block.Source == "새 원문" && block.Translation?.SourceText == "새 원문",
                "After preview completes, the chosen draft can be applied");
        }
        finally { await pending.ReleaseResourcesAsync(); pending.PrepareSmokeShutdown(); pending.Close(); }

        var failure = new MainWindow(testWithoutAccount: true); failure.Show();
        typeof(MainWindow).GetField("autoTranslate", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(failure, false);
        var original = failure.Session.ActiveBlock!;
        original.Source = "기존 설명"; original.Braille = "⠿"; original.HasManualBraille = true;
        original.Metadata = new(Type: "image", SelectedIndex: 0, Alternatives: [
            new("오류 초안", "너무 긴 초안", new string('가', 1001), null, null)]);
        try
        {
            await failure.ExecuteAsync("drafts");
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (failure.DraftBraillePreview.Text == "점역 중…" && DateTime.UtcNow < deadline) await Task.Delay(50);
            check(failure.DraftBraillePreview.Text.StartsWith("점역 미리보기 실패:", StringComparison.Ordinal),
                "A failed engine preview displays its reason");
            var reason = failure.DraftBraillePreview.Text;
            failure.DraftDockClose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(failure.DraftDock.IsVisible && failure.DraftBraillePreview.Text == reason && original.Source == "기존 설명"
                && original.Braille == "⠿" && original.HasManualBraille && original.Metadata.SelectedIndex == 0,
                "Applying a failed preview retains its reason and existing manual work");
        }
        finally { await failure.ReleaseResourcesAsync(); failure.PrepareSmokeShutdown(); failure.Close(); }
    }
}
