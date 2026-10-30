using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using WpfMath.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsMathDockChecks
{
    internal static async Task RunAsync(Action<bool, string> check, string directory)
    {
        var path = Path.Combine(directory, "math-dock-" + Guid.NewGuid().ToString("N") + ".smj");
        var model = new DocumentSession();
        model.Add(new EditorBlock { Id = 1, Page = 1, Source = "일반 문단" });
        model.Add(new EditorBlock { Id = 2, Page = 1, Source = "<!수식>\\frac{a+b}{2}<!/수식>", Braille = "⠿⠿", HasManualBraille = true, Metadata = new(Type: "formula") });
        model.Add(new EditorBlock { Id = 3, Page = 1, Source = "<!수식>\\frac{<!/수식>", Braille = "⠿", HasManualBraille = true, Metadata = new(Type: "formula") });
        model.ActiveBlock = model.Blocks[0]; model.IsDirty = false;
        await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
        await using var engine = EngineRuntime.Create();
        var window = new MainWindow(smoke: true, testWithoutAccount: true); window.Show();
        typeof(MainWindow).GetField("engine", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(window, new LocalBrailleEngine(engine));
        try
        {
            await window.OpenAsync(path);
            var dock = window.FindName("MathDock") as FrameworkElement;
            check(dock is not null, "The source panel contains a formula selection dock");
            if (dock is null) return;
            check(dock.Visibility == Visibility.Collapsed, "A non-formula selection does not show a formula dock");
            var formula = window.Session.Blocks.Single(b => b.Id == 2);
            window.Session.ActiveBlock = formula;
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            var rendered = Descendants(dock).OfType<FormulaControl>().Single();
            var latex = Descendants(dock).OfType<TextBox>().Single();
            check(dock.IsVisible && rendered.Formula == @"\frac{a+b}{2}" && latex.Text == @"\frac{a+b}{2}",
                "Selecting a formula shows its real rendered expression and LaTeX");
            await WindowsSmoke.RenderElementAsync(dock, Path.Combine(directory, "math-dock.png"));
            check(formula.Braille == "⠿⠿" && formula.HasManualBraille && !window.Session.IsDirty,
                "Opening the preview does not change the source or manual braille");
            var opened = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<MathDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (dialog is null) return;
                opened = dialog.Latex == @"\frac{a+b}{2}" && dialog.BrailleInput.Text == "⠿⠿"; dialog.Close();
            };
            try
            {
                timer.Start();
                Descendants(dock).OfType<Button>().Single(b => Equals(b.Content, "편집")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            finally { timer.Stop(); }
            check(opened && formula.Braille == "⠿⠿", "The preview opens the selected formula in the editor without changing manual braille");
            var invalid = window.Session.Blocks.Single(b => b.Id == 3); window.Session.ActiveBlock = invalid;
            await Dispatcher.Yield(DispatcherPriority.Background);
            check(dock.IsVisible && Descendants(dock).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("표시할 수 없습니다")),
                "Invalid LaTeX shows a rendering error instead of stale formula output");
            invalid.IsBrailleLocked = true;
            await window.ExecuteAsync("math");
            check(invalid.Source == "<!수식>\\frac{<!/수식>" && window.Session.Status.Contains("잠긴 블록"), "A locked formula cannot be changed through the menu command");
            window.Session.ActiveBlock = formula;
            window.Width = 480; window.Height = 360; window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(!dock.IsVisible && window.PageEditor.ActualHeight >= 28,
                $"A very short window hides the formula preview to keep editing reachable (editor={window.PageEditor.ActualHeight:F0}, panel={window.SourcePanel.ActualHeight:F0})");
            check(window.LinePreviewToggle.IsChecked && window.LineStrip.ActualHeight == 0 && window.SourcePageBar.IsVisible,
                "A short window temporarily folds line preview while retaining its preference and page controls");
            window.DraftDock.Visibility = Visibility.Visible; window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(window.DraftDock.Visibility == Visibility.Visible && window.DraftDock.Opacity == 0 && !window.DraftDock.IsEnabled && window.PageEditor.ActualHeight >= 28,
                $"An open alternative draft remains selected without covering the source editor in a short window (visibility={window.DraftDock.Visibility}, height={window.DraftDock.ActualHeight:F0}, enabled={window.DraftDock.IsEnabled}, editor={window.PageEditor.ActualHeight:F0})");
            var firstBlock = window.Session.Blocks[0]; var source = firstBlock.Source;
            window.PageEditor.Editor.Document.Insert(0, "가");
            check(firstBlock.Source == "가" + source, "Source input still updates the document in the short window");
            window.Width = 960; window.Height = 700; window.UpdateLayout();
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(window.LinePreviewToggle.IsChecked && window.LineStrip.ActualHeight > 0 && window.DraftDock.Visibility == Visibility.Visible && window.DraftDock.ActualHeight > 0 && window.DraftDock.IsEnabled && window.DraftDock.Opacity == 1,
                "Line preview and the open draft return when the window grows");
            window.DraftDock.Visibility = Visibility.Collapsed;
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(dock.IsVisible && window.PageEditor.ActualHeight >= 70, "The formula preview returns when enough source-panel height is available");
            window.ToggleSourcePanel();
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(dock.IsVisible && Window.GetWindow(dock) != window, "The formula dock follows the detached source panel");
            window.ToggleSourcePanel();
            await Dispatcher.Yield(DispatcherPriority.Background); window.UpdateLayout();
            check(dock.IsVisible && Window.GetWindow(dock) == window, "The formula dock returns with the source panel");
            window.DraftDock.Visibility = Visibility.Visible;
            await Dispatcher.Yield(DispatcherPriority.Background);
            check(!dock.IsVisible, "The formula dock does not overlap the open alternative-draft dock");
            window.DraftDock.Visibility = Visibility.Collapsed;
            await Dispatcher.Yield(DispatcherPriority.Background);
            check(dock.IsVisible, "The formula dock reappears after the alternative-draft dock closes");
            Descendants(dock).OfType<Button>().Single(b => b.ToolTip?.ToString() == "수식 미리보기 닫기").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!dock.IsVisible, "Closing the formula dock leaves the document unchanged");
            window.Session.ActiveBlock = window.Session.Blocks.Single(b => b.Id == 1);
            check(dock.Visibility == Visibility.Collapsed, "Changing to a non-formula block closes the formula dock");
            window.Session.ActiveBlock = formula;
            await Dispatcher.Yield(DispatcherPriority.Background);
            check(dock.IsVisible, "Selecting the formula again restores a dismissed dock");
            window.Session.ActiveBlock = window.Session.Blocks.Single(b => b.Id == 1);
            var before = window.Session.ActiveBlock.Source;
            window.PageEditor.FollowActive(0, 2); window.InsertTag("강조");
            ApplicationCommands.Undo.Execute(null, window.PageEditor.Editor.TextArea);
            check(window.Session.ActiveBlock.Source == before, "Undo after source tag insertion tolerates transient selection offsets");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); if (File.Exists(path)) File.Delete(path); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
