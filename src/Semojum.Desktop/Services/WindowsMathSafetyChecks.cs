using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using WpfMath.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsMathSafetyChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        await using var engine = EngineRuntime.Create();
        var local = new LocalBrailleEngine(engine);
        var dialog = new MathDialog((value, ct) => local.TranslateElementAsync(value, new(Type: "formula"), new(), ct),
            @"\frac{a+b}{2}", "⠿⠿");
        dialog.Show();
        try
        {
            await dialog.RefreshAsync();
            var rendered = Descendants(dialog).OfType<FormulaControl>().Single();
            check(rendered.Formula == @"\frac{a+b}{2}" && dialog.Translation is not null,
                "An existing formula displays its LaTeX and renders it with real engine braille");
            dialog.Input.Text = @"\frac{";
            await Dispatcher.Yield(DispatcherPriority.Background); dialog.UpdateLayout();
            var previewError = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "MathPreviewError");
            check(rendered.Visibility == Visibility.Collapsed && previewError.IsVisible && dialog.BrailleInput.Text == "⠿⠿", "Invalid LaTeX hides the previous formula and preserves manual braille");
            dialog.Input.Text = @"\frac{a+b}{2}"; await dialog.RefreshAsync(); dialog.UpdateLayout();
            check(rendered.Visibility == Visibility.Visible && rendered.Formula == @"\frac{a+b}{2}" && !previewError.IsVisible, "A corrected formula restores the real preview");
            check(dialog.BrailleInput.Text == "⠿⠿" && dialog.Edited,
                "Opening an existing formula displays and marks its manually corrected braille");
            dialog.Input.Text = @"\sqrt{x}";
            await dialog.RefreshAsync();
            check(rendered.Formula == @"\sqrt{x}" && dialog.Translation is not null && dialog.BrailleInput.Text == "⠿⠿" && dialog.Edited,
                "Changing LaTeX keeps the existing manual braille available for an explicit choice");
            dialog.BrailleInput.Text = "⠿⠁";
            dialog.Input.Text = @"x+1";
            await dialog.RefreshAsync();
            check(dialog.BrailleInput.Text == "⠿⠁" && dialog.Edited,
                "Changing LaTeX again does not silently erase braille edited in the dialog");
            var automatic = (MenuItem)dialog.BrailleInput.ContextMenu.Items[0];
            automatic.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(dialog.Translation is not null && dialog.BrailleInput.Text == dialog.Translation.Cells && !dialog.Edited,
                "The user can explicitly restore the current automatic braille result");
        }
        finally { dialog.Close(); }
        foreach (var choice in new[] { 0, 1, 2 }) await CheckApplyAsync(local, directory, check, choice);
        await CheckSameResultAsync(local, check, table: false);
        await CheckSameResultAsync(local, check, table: true);
        await CheckSameResultAsync(local, check, table: false, stale: true);
        await CheckSameResultAsync(local, check, table: true, stale: true);
    }

    private static async Task CheckSameResultAsync(LocalBrailleEngine engine, Action<bool, string> check, bool table, bool stale = false)
    {
        var source = table ? TaggedTable.Write([["가", "나"]]) : "<!수식>x+1<!/수식>";
        var metadata = table ? new BlockMetadata(Type: "table", TableSource: source) : new BlockMetadata(Type: "formula");
        var initial = await engine.TranslateElementAsync(table ? source : "x+1", metadata, new(), CancellationToken.None);
        var window = new MainWindow(smoke: true, testWithoutAccount: true); window.Show();
        typeof(MainWindow).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, engine);
        var block = window.Session.ActiveBlock!;
        block.Source = source; block.Metadata = metadata; block.Braille = initial.Cells;
        block.Translation = initial with { SourceText = source }; block.HasManualBraille = true;
        block.AddReviewMark(ReviewKind.HumanConfirmed, ReviewPanel.Braille, 0, 1);
        var stage = 0; var timedOut = false; var deadline = DateTime.UtcNow.AddSeconds(30);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            if (DateTime.UtcNow >= deadline)
            {
                timedOut = true; timer.Stop();
                foreach (Window owned in window.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) owned.Close();
                return;
            }
            if (stage == 0 && table && Application.Current.Windows.OfType<TableDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible) is { } tableDialog)
            { stage = 1; Descendants(tableDialog).OfType<Button>().Single(b => Equals(b.Content, "적용")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            if (stage == 0 && !table && Application.Current.Windows.OfType<MathDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible) is { Translation: not null } math && !math.BrailleInput.IsReadOnly)
            {
                stage = 1; ((MenuItem)math.BrailleInput.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Descendants(math).OfType<Button>().Single(b => Equals(b.Content, "넣기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            if (stage == 1 && Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible) is { } manual)
            {
                stage = 2;
                if (stale) window.Session.Options = window.Session.Options with { KoreanGrade1 = !window.Session.Options.KoreanGrade1 };
                manual.ActionButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
        };
        try
        {
            timer.Start(); await window.ExecuteAsync(table ? "table" : "math"); timer.Stop();
            if (stale)
            {
                check(!timedOut && stage == 2 && block.Source == source && block.Braille == initial.Cells && !block.ReviewMarks.Single().IsStale
                    && (window.Session.Status.Contains("변경") || window.Session.Status.Contains("바뀌")),
                    $"Stale {(table ? "table" : "formula")} output is rejected without invalidating the old confirmation");
                return;
            }
            check(!timedOut && stage == 2 && block.Source == source && block.Braille == initial.Cells && block.ReviewMarks.Single().IsStale,
                $"Applying identical {(table ? "table" : "formula")} braille invalidates the previous confirmation");
            window.ReplayHistory(); check(block.ReviewMarks.Single() is { IsStale: false }, $"Undo restores the confirmed {(table ? "table" : "formula")} braille");
            window.ReplayHistory(true); check(block.ReviewMarks.Single().IsStale, $"Redo restores stale {(table ? "table" : "formula")} confirmation");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    private static async Task CheckApplyAsync(LocalBrailleEngine engine, string directory, Action<bool, string> check, int choice)
    {
        var path = Path.Combine(directory, "math-" + Guid.NewGuid().ToString("N") + ".smj");
        var model = new DocumentSession();
        model.Add(new EditorBlock { Id = 1, Page = 1, Heading = "수식", Source = "<!수식>x+1<!/수식>", Braille = "⠿⠿", HasManualBraille = true,
            Metadata = new(Type: "formula", Rules: [new("fixture", "engine", "1", "old", "old", 0, 0, 1)]),
            Translation = BrailleTranslation.Plain("⠿⠿") with { SourceText = "<!수식>x+1<!/수식>" } });
        await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
        var window = new MainWindow(smoke: true, testWithoutAccount: true); window.Show();
        typeof(MainWindow).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, engine);
        var stage = 0; var initialManual = false; var generated = "";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        timer.Tick += (_, _) =>
        {
            var math = Application.Current.Windows.OfType<MathDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (math is not null && stage == 0 && math.Translation is not null && !math.BrailleInput.IsReadOnly)
            {
                initialManual = math.BrailleInput.Text == "⠿⠿";
                stage = 1; math.Input.Text = @"\sqrt{x}";
            }
            else if (math is not null && stage == 1 && math.Translation is not null && !math.BrailleInput.IsReadOnly)
            {
                generated = math.Translation.Cells;
                if (choice == 1) ((MenuItem)math.BrailleInput.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                stage = 2;
                Descendants(math).OfType<Button>().Single(b => Equals(b.Content, "넣기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            var notice = Application.Current.Windows.OfType<ManualEditDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
            if (notice is not null && stage == 2)
            { stage = 3; notice.ActionButtons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        };
        try
        {
            await window.OpenAsync(path); timer.Start();
            await window.ExecuteAsync("math").WaitAsync(TimeSpan.FromSeconds(12));
            timer.Stop();
            var block = window.Session.ActiveBlock!;
            var expectedSource = choice == 2 ? "<!수식>x+1<!/수식>" : "<!수식>\\sqrt{x}<!/수식>";
            var expectedBraille = choice == 1 ? generated : "⠿⠿";
            check(stage == 3 && initialManual && block.Source == expectedSource && block.Braille == expectedBraille
                && block.HasManualBraille == (choice != 1) && (choice != 0 || block.Translation is null)
                && (choice == 2 ? block.Metadata.Rules?.Length == 1 : block.Metadata.Rules is null),
                choice switch { 0 => "Keeping manual braille applies the new formula and clears stale translation evidence", 1 => "Overwriting manual braille applies the explicitly selected real engine result", _ => "Cancelling formula replacement preserves the original formula and manual braille" });
            if (choice != 2)
            {
                window.ReplayHistory();
                check(window.Session.ActiveBlock is { Source: "<!수식>x+1<!/수식>", Braille: "⠿⠿", HasManualBraille: true, Metadata.Rules.Length: 1 }, "Undo restores the original formula, braille and rule evidence");
                window.ReplayHistory(true);
                block = window.Session.ActiveBlock!;
                check(block.Source == expectedSource && block.Braille == expectedBraille, "Redo restores the selected formula and braille");
            }
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(window.Session));
            var restored = (await SmjFile.LoadAsync(path)).Blocks.Single();
            check(restored.Source == expectedSource && restored.Braille == expectedBraille && restored.HasManualBraille == (choice != 1),
                "The chosen formula and braille survive SMJ save and reopen");
        }
        finally { timer.Stop(); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
