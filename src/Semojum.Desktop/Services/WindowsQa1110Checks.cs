using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Automation;
using Semojum.Desktop.Dialogs;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsQa1110Checks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var failures = new List<string>();
        void Verify(bool success, string name)
        {
            if (success) check(true, name);
            else { failures.Add(name); Console.WriteLine("FAIL " + name); }
        }
        foreach (var size in new[] { 22d, 28d })
        {
            var block = new EditorBlock { Id = 1, Page = 1, Source = "QA", Braille = string.Join("\n", Enumerable.Repeat("⠁", 26)) };
            var rows = Enumerable.Range(0, 26).Select(i => new GridCell?[] { new(block, i * 2, '⠁') }).ToArray();
            var grid = new BrailleGridView { FontSize = size, Blocks = [block] };
            grid.Items.Add(new BrailleGridFace(1, rows));
            var host = new Window { Width = 400, Height = 500, Content = grid, ShowInTaskbar = false };
            host.Show();
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                host.UpdateLayout();
                var scroll = Descendants(grid).OfType<ScrollViewer>().First();
                Verify(scroll.ScrollableHeight > 0, $"QA1110: a single 26-row face can scroll at font {size} (extent={scroll.ExtentHeight}, viewport={scroll.ViewportHeight})");
                scroll.ScrollToEnd();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                host.UpdateLayout();
                var face = Descendants(grid).OfType<FrameworkElement>().First(e => e.GetType().Name == "Face");
                var bottom = face.TransformToAncestor(scroll).Transform(new Point(0, face.ActualHeight));
                Verify(bottom.Y <= scroll.ActualHeight + 1, $"QA1110: last braille row is reachable at font {size} (bottom={bottom.Y}, viewport={scroll.ActualHeight})");
            }
            finally { host.Close(); }
        }
        var field = new TextBox { Text = "입력 위치", Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
        var fieldHost = new Window { Width = 400, Height = 120, Content = field, ShowInTaskbar = false };
        fieldHost.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            fieldHost.UpdateLayout();
            var x = field.GetRectFromCharacterIndex(0).X;
            Verify(x <= field.Padding.Left + field.BorderThickness.Left + 2,
                $"QA1110: common text input applies left padding once (actual={x}, padding={field.Padding.Left})");
        }
        finally { fieldHost.Close(); }
        var find = new FindDialog(SearchPanel.Braille, "", false, (_, _) => Task.FromResult(""), (_, _, _, _) => Task.FromResult("")) { ShowInTaskbar = false };
        find.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var query = Descendants(find).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "찾을 내용");
            query.Focus();
            var source = PresentationSource.FromVisual(query)!;
            void Chord(params Key[] keys)
            {
                foreach (var key in keys) query.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                foreach (var key in keys) query.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            }
            Chord(Key.F, Key.D);
            Chord(Key.J);
            Verify(query.Text == "⠃⠈" && query.SelectionLength == 0 && query.CaretIndex == 2,
                $"QA1110: consecutive find chords append without refocus (actual={query.Text}, selected={query.SelectionLength})");
            query.SelectAll();
            Chord(Key.F);
            Chord(Key.D);
            Verify(query.Text == "⠁⠂", "QA1110: find chord replaces selection then appends next cell");
            var composition = new TextComposition(InputManager.Current, query, "한");
            var textInput = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
            query.RaiseEvent(textInput);
            Verify(textInput.Handled, "QA1110: braille find rejects non-braille composed text");
            Verify(!InputMethod.GetIsInputMethodEnabled(query), "QA1110: braille find bypasses Korean IME composition");
            Descendants(find).OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(t => AutomationProperties.GetName(t) == "점자 입력").IsChecked = false;
            Verify(InputMethod.GetIsInputMethodEnabled(query), "QA1110: text find restores Korean IME composition");
        }
        finally { find.Close(); }
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { ShowInTaskbar = false };
        window.Show();
        try
        {
            Verify(window.Braille.SixDotEnabled, "QA1110: braille panel accepts chords without discovering a hidden F6 command");
            var grid = window.Braille.GridView;
            grid.GoToFace(1);
            var source = PresentationSource.FromVisual(grid)!;
            foreach (var key in new[] { Key.F, Key.J })
            {
                for (var wait = 0; grid.IsReadOnly && wait < 100; wait++) await Task.Delay(10);
                grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            Verify(window.Session.ActiveBlock?.Braille == "⠁⠈", "QA1110: actual main panel persists two consecutive braille cells");
            Verify(!InputMethod.GetIsInputMethodEnabled(grid), "QA1110: main braille panel bypasses Korean IME");
            await window.ExecuteAsync("new");
            Verify(window.OriginalPanel.IsVisible && window.Braille.IsVisible && window.PageEditor.IsVisible,
                "QA1110: new text document shows original, text and braille panels");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); }
        await WindowsQa1110BoardChecks.RunAsync(Verify);
        await WindowsQa1110DraftChecks.RunAsync(Verify);
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures));
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
