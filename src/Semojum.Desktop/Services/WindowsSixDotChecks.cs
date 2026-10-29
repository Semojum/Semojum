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
    internal static void Run(Window owner, Action<bool, string> check)
    {
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
