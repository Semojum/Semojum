using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
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
        try
        {
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
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); if (window.IsVisible) window.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var item in Descendants(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
}
