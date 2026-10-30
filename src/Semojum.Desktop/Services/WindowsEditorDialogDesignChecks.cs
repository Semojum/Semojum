using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsEditorDialogDesignChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var keys = new SixDotDialog("W E R U I O", true, true) { Owner = owner };
        keys.Show();
        try
        {
            keys.UpdateLayout();
            var mode = Children(keys).OfType<SegmentedChoice>().Single();
            check(keys.Width == 520 && keys.Height == 356 && keys.KeyFields.All(field => field.IsReadOnly), "Six-dot Figma size and preset fields");
            CheckIcons(keys, check);
            check(Canvas.GetLeft(keys.KeyFields[2]) < Canvas.GetLeft(keys.KeyFields[1]) && Canvas.GetLeft(keys.KeyFields[1]) < Canvas.GetLeft(keys.KeyFields[0]), "Six-dot left hand displayed in physical key order");
            mode.SelectedIndex = 2;
            check(string.Concat(keys.KeyFields.Select(field => field.Text)) == "XCVNM,", "Six-dot three-row dot mapping remains unchanged");
            mode.SelectedIndex = 3;
            check(keys.KeyFields.All(field => !field.IsReadOnly), "Custom six-dot keys are editable");
        }
        finally { keys.Close(); }

        var layout = new LayoutDialog(new PageLayout()) { Owner = owner };
        layout.Show();
        try
        {
            layout.UpdateLayout();
            check(layout.Width == 520 && layout.Height == 412, "Page layout Figma size");
            CheckIcons(layout, check);
            check(Children(layout).OfType<TextBlock>().Any(t => t.Text == "면 크기") && Children(layout).OfType<TextBlock>().Any(t => t.Text == "쪽 번호와 꼬리말"), "Page layout groups are visible");
            var combo = Children(layout).OfType<ComboBox>().First(); combo.IsDropDownOpen = true; combo.SelectedIndex = 1;
            check(combo.IsDropDownOpen && combo.SelectedIndex == 1, "Page layout combo opens and selects after rounded Figma styling"); combo.IsDropDownOpen = false;
        }
        finally { layout.Close(); }

        string? searched = null; SearchPanel? searchedPanel = null;
        var find = new FindDialog(SearchPanel.Text, "", false,
            (text, panel) => { searched = text; searchedPanel = panel; return Task.FromResult("ok"); },
            (_, _, _, _) => Task.FromResult("ok"), "Q 1 , 4 - /") { Owner = owner };
        find.Show();
        try
        {
            find.UpdateLayout();
            var toggle = Children(find).OfType<ToggleButton>().Single(t => AutomationProperties.GetName(t) == "점자 입력");
            var query = Children(find).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "찾을 내용");
            toggle.IsChecked = true; query.Focus();
            find.UpdateLayout(); CheckIcons(find, check);
            var source = PresentationSource.FromVisual(query) ?? throw new InvalidOperationException("Missing find input presentation source");
            query.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Q) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            query.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Q) { RoutedEvent = Keyboard.PreviewKeyUpEvent });
            check(query.Text == "⠁", "Find dialog uses configured six-dot keys");
            var button = Children(find).OfType<Button>().Single(b => b.Content as string == "다음 찾기");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(searched == "⠁" && searchedPanel == SearchPanel.Braille, "Find uses braille input and braille search target");
        }
        finally { find.Close(); }
    }

    private static void CheckIcons(Window window, Action<bool, string> check)
    {
        var icons = Children(window).OfType<SvgViewbox>().ToArray();
        check(icons.Length > 0 && icons.All(icon => icon.Drawings is { Bounds.IsEmpty: false } && icon.ActualWidth > 0 && icon.ActualHeight > 0 && icon.ActualWidth <= icon.Width + 1 && icon.ActualHeight <= icon.Height + 1), $"{window.Title} Figma SVGs render in their intended slots");
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Children(child)) yield return nested;
        }
    }
}
