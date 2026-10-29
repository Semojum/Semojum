using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;
internal static class WindowsInlineMathChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        Run(check);
        await using var connection = EngineRuntime.Create();
        var engine = new LocalBrailleEngine(connection);
        foreach (var mode in new[] { "keep", "overwrite", "cancel", "failure", "engine-cancel", "changed" })
            await CheckEditorAsync(engine, check, directory, mode);
    }
    internal static void Run(Action<bool, string> check)
    {
        const string source = "앞 <!수식>x+1<!/수식> 가운데 <!수식>y+2<!/수식> 뒤";
        var first = FormulaRanges.Find(source, source.IndexOf("x+1", StringComparison.Ordinal), 0);
        var second = FormulaRanges.Find(source, source.IndexOf("y+2", StringComparison.Ordinal), 3);
        check(first.Status == FormulaRangeStatus.Selected && first.Latex == "x+1" && source.Substring(first.Start, first.Length) == "<!수식>x+1<!/수식>", "A caret selects the exact first inline formula");
        check(second.Status == FormulaRangeStatus.Selected && second.Latex == "y+2", "A text selection chooses only the second inline formula");
        check(FormulaRanges.Find(source, 0, 0).Status == FormulaRangeStatus.None && FormulaRanges.Find(source, 0, source.Length).Status == FormulaRangeStatus.None, "Text outside or crossing formula boundaries is not guessed");
        check(FormulaRanges.Find("a <!수식>x", 5, 0).Status == FormulaRangeStatus.Malformed && FormulaRanges.Find("<!수식>x<!수식>y<!/수식><!/수식>", 6, 0).Status == FormulaRangeStatus.Malformed && FormulaRanges.Find("a <!/수식>", 2, 0).Status == FormulaRangeStatus.Malformed && FormulaRanges.Find("a <!수식", 2, 0).Status == FormulaRangeStatus.Malformed, "Unclosed, nested and unmatched formula tags are rejected");
    }
    private static async Task CheckEditorAsync(LocalBrailleEngine engine, Action<bool, string> check, string directory, string mode)
    {
        const string original = "앞 <!수식>x+1<!/수식> 가운데 <!수식>y+2<!/수식> 뒤";
        const string changed = "앞 <!수식>x+1<!/수식> 가운데 <!수식>\\sqrt{x}<!/수식> 뒤";
        var path = Path.Combine(directory, "inline-math-" + Guid.NewGuid().ToString("N") + ".smj");
        var model = new DocumentSession();
        model.Add(new EditorBlock { Id = 1, Page = 1, Source = original, Braille = "⠿⠿", HasManualBraille = true, Metadata = new(Type: "text"), Translation = BrailleTranslation.Plain("⠿⠿") with { SourceText = original } });
        await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
        var window = new MainWindow(smoke: true, testWithoutAccount: true); window.Show();
        typeof(MainWindow).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, engine);
        var stage = 0; var previewWasReadOnly = false; var initialLatexCorrect = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            var dialog = Application.Current.Windows.OfType<MathDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (dialog is not null && stage == 0 && dialog.Translation is not null && dialog.BrailleInput.IsReadOnly)
            { initialLatexCorrect = dialog.Latex == "y+2"; previewWasReadOnly = dialog.BrailleInput.IsReadOnly; stage = 1; dialog.Input.Text = @"\sqrt{x}"; }
            else if (dialog is not null && stage == 1 && dialog.Translation is not null && dialog.Latex == @"\sqrt{x}")
            { stage = 2; Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "넣기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            var notice = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (notice is not null && stage == 2)
            { stage = 3; notice.ActionButtons[mode switch { "keep" => 0, "cancel" => 2, _ => 1 }].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        var pending = new TaskCompletionSource<BrailleTranslation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (mode == "failure") window.InlineMathTranslationForChecks = (_, _, _, _) => throw new IOException("fixture engine failure");
        if (mode == "engine-cancel") window.InlineMathTranslationForChecks = (_, _, _, _) => throw new OperationCanceledException();
        if (mode == "changed") window.InlineMathTranslationForChecks = (_, _, _, _) => { started.TrySetResult(); return pending.Task; };
        try
        {
            await window.OpenAsync(path);
            var block = window.Session.ActiveBlock!;
            window.PageEditor.Editor.Select(original.IndexOf("y+2", StringComparison.Ordinal), 3);
            await Dispatcher.Yield(DispatcherPriority.Background);
            var dock = window.FindName("MathDock") as FrameworkElement;
            check(dock?.IsVisible == true && Descendants(dock).OfType<TextBox>().Single().Text == "y+2", "Selecting an inline formula shows the exact expression in the source dock");
            timer.Start();
            var action = window.ExecuteAsync("math");
            if (mode == "changed")
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(12));
                window.PageEditor.Editor.Document.Insert(0, "동시 ");
                pending.TrySetResult(BrailleTranslation.Plain("⠁"));
            }
            await action.WaitAsync(TimeSpan.FromSeconds(12)); timer.Stop();
            block = window.Session.ActiveBlock!;
            var expectedSource = mode is "keep" or "overwrite" ? changed : mode == "changed" ? "동시 " + original : original;
            var expectedBraille = mode == "overwrite" ? (await engine.TranslateElementAsync(changed, new(Type: "text"), new(), CancellationToken.None)).Cells : "⠿⠿";
            check(stage == 3 && previewWasReadOnly && initialLatexCorrect && block.Source == expectedSource && block.Braille == expectedBraille,
                mode switch { "keep" => "Inline change keeps manually corrected full-block braille and surrounding text", "overwrite" => "Inline overwrite translates the whole block and preserves surrounding text", "cancel" => "Cancelling inline formula replacement preserves source and braille", "failure" => "A full-block engine failure preserves source and braille", "engine-cancel" => "Cancelling full-block translation preserves source and braille", _ => "A concurrent edit prevents an old full-block translation from overwriting it" });
            if (mode is "keep" or "overwrite")
            {
                window.ReplayHistory();
                check(window.Session.ActiveBlock is { Source: original, Braille: "⠿⠿", HasManualBraille: true }, "Undo restores inline formula and original manual braille");
                window.ReplayHistory(true);
                check(window.Session.ActiveBlock!.Source == changed && window.Session.ActiveBlock.Braille == expectedBraille, "Redo restores the inline formula change and chosen full-block braille");
            }
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
            var restored = (await SmjFile.LoadAsync(path)).Blocks.Single();
            check(restored.Source == expectedSource && restored.Braille == expectedBraille, "Inline formula outcome survives SMJ save and reopen");
        }
        finally { pending.TrySetCanceled(); timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
