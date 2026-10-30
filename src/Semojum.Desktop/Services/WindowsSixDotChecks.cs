using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsSixDotChecks
{
    internal static void Run(Window owner, Action<bool, string> check, Action<string>? stage = null)
    {
        stage ??= message => Console.WriteLine(message);
        var dialog = new SixDotDialog("F D S J K L", true, true) { Owner = owner };
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var mode = Children(dialog).OfType<SegmentedChoice>().Single();
            var fields = Children(dialog).OfType<TextBox>()
                .Where(t => System.Windows.Automation.AutomationProperties.GetName(t).EndsWith("점 키"))
                .ToArray();
            check(fields.Length == 6 && mode.SelectedIndex == 1, "Six-dot dialog restores legacy two-row preset");
            check(Children(dialog).OfType<TextBlock>().Any(t => t.Text == "왼손  3 · 2 · 1") &&
                Children(dialog).OfType<TextBlock>().Any(t => t.Text == "오른손  4 · 5 · 6"),
                "Six-dot labels follow the displayed physical left-to-right dot order");
            check(fields.Select((field, i) => System.Windows.Automation.AutomationProperties.GetName(field) == $"{i + 1}점 키").All(matches => matches) &&
                Canvas.GetLeft(fields[2]) < Canvas.GetLeft(fields[1]) && Canvas.GetLeft(fields[1]) < Canvas.GetLeft(fields[0]) &&
                Canvas.GetLeft(fields[3]) < Canvas.GetLeft(fields[4]) && Canvas.GetLeft(fields[4]) < Canvas.GetLeft(fields[5]),
                "Six-dot accessible names match each field's dot number and physical position");
            foreach (var (index, stored, displayed) in new[] { (0, "WERUIO", "REWUIO"), (1, "FDSJKL", "SDFJKL"), (2, "XCVNM,", "VCXNM,") })
            {
                mode.SelectedIndex = index;
                var configured = string.Concat(fields.Select(field => field.Text));
                var visual = string.Concat(fields.OrderBy(Canvas.GetLeft).Select(field => field.Text));
                var input = new SixDotInput(); input.Configure(configured);
                var correctDots = configured.Length == 6;
                for (var dot = 0; dot < configured.Length && dot < 6; dot++)
                {
                    var key = configured[dot];
                    correctDots &= input.Press(key) && input.Release(key) == char.ConvertFromUtf32(0x2800 + (1 << dot));
                }
                check(configured == stored && visual == displayed && correctDots,
                    $"Six-dot preset {index + 1} keeps its displayed physical order and saved dot-to-key mapping");
            }
            mode.SelectedIndex = 0;
            check(string.Concat(fields.Select(x => x.Text)) == "WERUIO", "Six-dot one-row preset maps the six dots");
            mode.SelectedIndex = 2;
            check(string.Concat(fields.Select(x => x.Text)) == "XCVNM,", "Six-dot three-row preset includes comma");
            fields[0].Text = "7";
            check(mode.SelectedIndex == 3, "Editing a preset selects custom six-dot keys");
            fields[0].Focus();
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
            fields[0].RaiseEvent(click);
            check(click.Handled && fields[0].SelectionLength == 1, "Existing six-dot key can be selected for replacement");
        }
        finally { dialog.Close(); }

        foreach (var (index, expected) in new[] { (0, "W E R U I O"), (1, "F D S J K L"), (2, "X C V N M ,") })
        {
            var prefix = $"six-dot preset {index + 1}";
            stage($"{prefix}: creating dialog");
            var saveDialog = new SixDotDialog("F D S J K L", true, true) { Owner = owner };
            var timedOut = false;
            Exception? callbackError = null;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            timer.Tick += (_, _) =>
            {
                stage($"{prefix}: timer fired");
                timedOut = true;
                timer.Stop();
                saveDialog.Close();
            };
            saveDialog.Loaded += (_, _) =>
            {
                stage($"{prefix}: loaded");
                saveDialog.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        stage($"{prefix}: selecting preset");
                        Children(saveDialog).OfType<SegmentedChoice>().Single().SelectedIndex = index;
                        var confirm = Children(saveDialog).OfType<Button>().Single(button => button.Content as string == "확인");
                        stage($"{prefix}: clicking confirm");
                        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        stage($"{prefix}: confirm returned");
                    }
                    catch (Exception error)
                    {
                        callbackError = error;
                        stage($"{prefix}: callback failed: {error}");
                        timer.Stop();
                        if (saveDialog.IsVisible) saveDialog.Close();
                    }
                }));
            };
            bool? accepted = null;
            try
            {
                timer.Start();
                stage($"{prefix}: ShowDialog starting");
                accepted = saveDialog.ShowDialog();
                stage($"{prefix}: ShowDialog returned {accepted}");
            }
            catch (Exception error)
            {
                callbackError = error;
                stage($"{prefix}: ShowDialog failed: {error}");
            }
            finally
            {
                timer.Stop();
                if (saveDialog.IsVisible) saveDialog.Close();
            }
            check(!timedOut && callbackError is null && accepted == true && saveDialog.Keys == expected,
                $"Six-dot preset {index + 1} saves keys in dot order through confirmation" +
                (callbackError is null ? "" : $" (callback error: {callbackError.GetType().Name})"));
        }

        var reopened = new SixDotDialog("X C V N M ,", true, true) { Owner = owner };
        reopened.Show();
        try
        {
            reopened.UpdateLayout();
            check(Children(reopened).OfType<SegmentedChoice>().Single().SelectedIndex == 2,
                "Comma preset remains selected after reopening");
        }
        finally { reopened.Close(); }

        var block = new EditorBlock { Id = 1, Page = 1, Source = "test", Braille = "⠁" };
        string? inserted = null;
        var grid = new BrailleGridView { Blocks = [block], SixDotEnabled = true,
            ReplaceAsync = (_, value) => { inserted = value; return Task.CompletedTask; } };
        grid.Items.Add(new BrailleGridFace(1, [[new GridCell(block, 0, '⠁')]]));
        var host = new Window { Width = 300, Height = 200, Content = grid, Owner = owner };
        host.Show();
        try
        {
            host.UpdateLayout();
            check(grid.GoToFace(1), "Six-dot grid fixture selects a cell");
            var source = PresentationSource.FromVisual(grid) ?? throw new InvalidOperationException("Missing grid presentation source");
            void KeyPress(Key key)
            {
                grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            }
            grid.ConfigureKeys("X C V N M ,");
            KeyPress(Key.OemComma);
            check(inserted == "⠠", "Comma inserts dot six in the actual WPF grid");
            grid.ConfigureKeys("Q 1 , 4 - /");
            inserted = null;
            KeyPress(Key.D1);
            check(inserted == "⠂", "A configured digit inserts its assigned dot in the WPF grid");
        }
        finally { host.Close(); }
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
