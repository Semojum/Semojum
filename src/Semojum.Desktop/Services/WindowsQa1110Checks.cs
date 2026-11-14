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
                scroll.ScrollToTop(); host.UpdateLayout();
                grid.SetLinkedRanges([new BlockSpan(block, 50, 1)]);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
                Verify(scroll.VerticalOffset > 100, "QA1110: linking the final row scrolls inside a tall braille face");
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
        await using var navigationEngine = EngineRuntime.Create();
        var navigation = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { ShowInTaskbar = false };
        navigation.Show();
        try
        {
            var model = navigation.Session;
            model.Blocks.Clear(); model.ActiveBlock = null;
            var first = new EditorBlock { Id = 1, Page = 1, Source = "첫 줄\n끝 줄", Braille = string.Join("\n", Enumerable.Repeat("⠁", 80)) };
            var last = new EditorBlock { Id = 2, Page = 1, Source = "마지막 블록", Braille = "⠃", HasManualBraille = true };
            first.Translation = BrailleTranslation.Plain(first.Braille) with
            {
                SourceText = first.Source,
                SourceLineMap = [new(0, 3, 0, 2), new(4, 7, 158, 159)],
                SourceMapStamp = new(first.SourceRevision, first.BrailleRevision, first.MetadataRevision)
            };
            model.Add(first); model.Add(last); model.ActiveBlock = first;
            navigation.PageEditor.Editor.Focus();
            var grid = navigation.Braille.GridView;
            async Task Settle()
            {
                await Task.Delay(150);
                for (var i = 0; !grid.CoordinatesCurrent && i < 500; i++) await Task.Delay(20);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                navigation.UpdateLayout();
            }
            await Settle();
            var scroll = Descendants(grid).OfType<ScrollViewer>().First();
            scroll.ScrollToTop(); navigation.UpdateLayout();
            navigation.PageEditor.Editor.CaretOffset = 4;
            await Settle();
            Verify(RowVisible(first, 158), "QA1110: moving source caret within one block reveals its mapped braille row");
            scroll.ScrollToTop(); navigation.UpdateLayout();
            navigation.PageEditor.Editor.CaretOffset = navigation.PageEditor.Editor.Text.IndexOf(last.Source, StringComparison.Ordinal);
            await Settle();
            Verify(model.ActiveBlock == last && RowVisible(last, 0), "QA1110: source caret selects and reveals a later block");
            scroll.ScrollToTop(); navigation.UpdateLayout();
            var localEngine = new Semojum.Core.Integration.LocalBrailleEngine(navigationEngine);
            navigation.Braille.ComposeGridAsync = owner => localEngine.ComposeAsync(SmjFile.Snapshot(owner, false).Manifest);
            await navigation.AddBlockAsync();
            await Settle();
            Verify(model.ActiveBlock is { } added && added != last && model.Blocks.Count == 3 && grid.CoordinatesCurrent && RowVisible(last, last.Braille.Length),
                $"QA1110: new block is revealed after braille grid refresh (current={grid.CoordinatesCurrent}, active={model.ActiveBlock?.Id}, faces={grid.Items.Count}, ids={string.Join(",", grid.Items.OfType<BrailleGridFace>().SelectMany(f => f.Rows).SelectMany(r => r).Where(c => c is not null).Select(c => c!.Block.Id).Distinct())}, status={model.Status})");
            Verify(navigation.PageEditor.Editor.IsKeyboardFocusWithin, "QA1110: adding a block retains source editor keyboard focus");
            bool RowVisible(EditorBlock block, int offset)
            {
                foreach (var face in grid.Items.OfType<BrailleGridFace>())
                {
                    var row = Array.FindIndex(face.Rows, cells => cells.Any(cell => cell?.Block == block && cell.Offset == offset));
                    if (row < 0 || grid.ItemContainerGenerator.ContainerFromItem(face) is not DependencyObject container) continue;
                    var visual = Descendants(container).OfType<FrameworkElement>().FirstOrDefault(e => e.GetType().Name == "Face");
                    if (visual is null) continue;
                    var height = grid.FontSize * 28 / 22;
                    var top = visual.TransformToAncestor(scroll).Transform(new Point(0, row * height)).Y;
                    return top >= -.5 && top + height <= scroll.ViewportHeight + .5;
                }
                return false;
            }
        }
        finally { navigation.PrepareSmokeShutdown(); navigation.Close(); }
        await WindowsPanelWheelChecks.RunAsync(Verify);
        await WindowsQa1110BoardChecks.RunAsync(Verify);
        await WindowsQa1110DraftChecks.RunAsync(Verify);
        await WindowsVisualPolicyChecks.RunAsync(Verify);
        await WindowsQa1110TableChecks.RunAsync(Verify);
        await WindowsQa1110SpaceChecks.RunAsync(Verify);
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
