using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsAppearanceDefaultChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true); window.Show();
        var field = typeof(MainWindow).GetField("accountPreferences", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var edit = typeof(MainWindow).GetMethod("EditAppearanceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        AccountPreferences Preferences() => (AccountPreferences)field.GetValue(window)!;
        string shownNumber = "";
        async Task<string> Edit(string button, bool changeFontSize = false)
        {
            string shown = "";
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<AppearanceDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (dialog is null) return;
                timer.Stop();
                var inputs = (Dictionary<string, TextBox>)typeof(AppearanceDialog).GetField("inputs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dialog)!;
                shown = inputs["CurrentLineColor"].Text; shownNumber = inputs["LineNumberColor"].Text;
                if (changeFontSize) inputs["TextSize"].Text = "15";
                Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, button)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { await (Task)edit.Invoke(window, new object[] { true })!; }
            finally { timer.Stop(); }
            return shown;
        }
        bool Blue() => window.PageEditor.Editor.TextArea.TextView.CurrentLineBackground is SolidColorBrush { Color: var c } && c == Color.FromRgb(242, 246, 251);
        bool Yellow() => window.PageEditor.Editor.TextArea.TextView.CurrentLineBackground is SolidColorBrush { Color: var c } && c == Color.FromRgb(255, 245, 163);
        bool MarksShown() => new[] { window.PageEditor.Editor, window.SourceEditor }.All(e => e.Options.ShowSpaces && e.Options.ShowTabs && e.TextArea.TextView.ElementGenerators.OfType<Semojum.Desktop.Controls.EnterMarkGenerator>().Any(g => g.Enabled));
        try
        {
            check(new EditorAppearance().ShowFormattingMarks && window.FormattingToggle.IsChecked && MarksShown(),
                "View › 조판부호 is on by default as in the final Figma view menu");
            window.ApplyAppearance(new());
            var shown = await Edit("확인", changeFontSize: true);
            check(shown == "#F2F6FB" && Preferences().Appearance is { CurrentLineColor: "#F2F6FB", TextSize: 15 } && Blue(),
                "A new account displays and retains the Figma default when changing another appearance setting");
            check(shownNumber == "#8A8A8A" && Preferences().Appearance?.LineNumberColor == "#8A8A8A",
                "The displayed Figma line-number color is saved unchanged when editing another setting");
            var root = Path.Combine(Directory.GetCurrentDirectory(), "semojum-appearance-fixture-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new AccountPreferenceStore(root); await store.SaveAsync("fixture", Preferences());
                check(store.Load("fixture").Appearance?.CurrentLineColor == "#F2F6FB", "The chosen default persists and reloads from an isolated account profile");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            var saved = new EditorAppearance(CurrentLineColor: "#FFF5A3", LineNumberColor: "#123456");
            field.SetValue(window, new AccountPreferences(Appearance: saved)); window.ApplyAppearance(saved);
            shown = await Edit("확인", changeFontSize: true);
            check(shown == "#FFF5A3" && Preferences().Appearance?.CurrentLineColor == "#FFF5A3" && Yellow(),
                "A previously saved yellow current-line color survives editing another setting");
            check(shownNumber == "#123456" && Preferences().Appearance?.LineNumberColor == "#123456",
                "Editing other appearance settings preserves a saved line-number color");
            shown = await Edit("기본값");
            check(Preferences().Appearance?.LineNumberColor == "#8A8A8A", "Restoring appearance defaults restores the Figma line-number color");
            check(shown == "#FFF5A3" && Preferences().Appearance?.CurrentLineColor == "#F2F6FB" && Blue(),
                "Explicitly restoring defaults replaces a saved color with the Figma default");
            check(Preferences().Appearance?.ShowFormattingMarks == true && window.FormattingToggle.IsChecked && MarksShown(), "Restoring appearance defaults shows formatting marks");
            var hidden = new EditorAppearance(ShowFormattingMarks: false);
            field.SetValue(window, new AccountPreferences(Appearance: hidden)); window.ApplyAppearance(hidden);
            check(!window.FormattingToggle.IsChecked && !MarksShown(), "A saved choice to hide formatting marks is kept");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); }
        await CheckOutlineAsync(check);
    }

    // Figma draws the 32-cell boundary in #C42B1C on both the three-panel (263:7003) and two-panel (263:7321) screens.
    private static async Task CheckOutlineAsync(Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true) { Width = 1440, Height = 1000 }; window.Show();
        try
        {
            var red = Color.FromRgb(196, 43, 28);
            bool Is(Brush? brush, Color color) => brush is SolidColorBrush { Color: var c } && c == color;
            foreach (var panels in new[] { 3, 2 })
            {
                if (panels == 2) window.HidePanel("hideOriginal");
                await WaitUntilAsync(() => { window.UpdateLayout(); return Boundaries(window.Braille.GridView).Length > 0; });
                var pens = Boundaries(window.Braille.GridView);
                check(window.OriginalPanel.IsVisible == (panels == 3) && pens.Length > 0 && pens.All(p => Is(p.Brush, red) && p.Thickness == 1)
                    && Is(window.Braille.Editor.TextArea.TextView.ColumnRulerPen?.Brush, red),
                    $"The {panels}-panel braille grid draws its 32-cell boundary in Figma red #C42B1C ({string.Join(",", pens.Select(p => p.Brush))})");
            }
            window.ApplyAppearance(new EditorAppearance(OutlineColor: "#123456", OutlineWidth: 2), explicitAppearance: true);
            var custom = Color.FromRgb(18, 52, 86);
            await WaitUntilAsync(() => { window.UpdateLayout(); return Boundaries(window.Braille.GridView).Any(p => Is(p.Brush, custom)); });
            check(Boundaries(window.Braille.GridView) is { Length: > 0 } customPens && customPens.All(p => Is(p.Brush, custom) && p.Thickness == 2),
                "The 판면 윤곽선 appearance setting also colors the grid boundary");
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    // The rightmost vertical line of each rendered face is its column boundary; the other is the line-number gutter edge.
    private static Pen[] Boundaries(BrailleGridView grid) => Descendants(grid).OfType<FrameworkElement>().Where(e => e.GetType().Name == "Face")
        .Select(face => Lines(VisualTreeHelper.GetDrawing(face)).Where(l => l.Line.StartPoint.X == l.Line.EndPoint.X).OrderBy(l => l.Line.StartPoint.X).Select(l => l.Pen).LastOrDefault())
        .OfType<Pen>().ToArray();
    private static IEnumerable<(LineGeometry Line, Pen Pen)> Lines(Drawing? drawing)
    {
        if (drawing is GeometryDrawing { Geometry: LineGeometry line, Pen: { } pen }) yield return (line, pen);
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var item in Lines(child)) yield return item;
    }
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
