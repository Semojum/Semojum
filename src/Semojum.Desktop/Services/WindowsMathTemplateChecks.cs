using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Dialogs;
using Semojum.Feature.Editor;
using WpfMath.Controls;

namespace Semojum.Desktop.Services;

internal static class WindowsMathTemplateChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var dialog = new MathDialog((_, _) => Task.FromResult(BrailleTranslation.Plain("⠁")), "x+1", "⠿");
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var preview = Descendants(dialog).OfType<FormulaControl>().Single();
            foreach (var button in dialog.TemplateButtons.Children.OfType<Button>())
            {
                var menu = button.ContextMenu;
                check(menu is { Items.Count: > 0 }, $"{AutomationProperties.GetName(button)} exposes templates");
                foreach (MenuItem item in menu!.Items)
                {
                    dialog.Input.Clear();
                    item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    check(preview.IsVisible && !preview.HasError && preview.Formula == dialog.Input.Text,
                        $"Template {AutomationProperties.GetName(item)} inserts and renders without an error");
                }
            }
            dialog.Input.Text = "q+x+1"; dialog.Input.Select(2, 3);
            var fraction = dialog.SymbolButtons.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "분수");
            fraction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(dialog.Input.Text == @"q+\frac{x+1}{b}" && dialog.Input.SelectedText == "b" && dialog.Input.IsKeyboardFocusWithin,
                "The fraction button wraps the selected expression and focuses its denominator");
            dialog.Input.Undo();
            check(dialog.Input.Text == "q+x+1", "Template insertion can be undone in the input editor");
            await dialog.RefreshAsync();
            check(dialog.BrailleInput.Text == "⠿" && dialog.Edited, "Template changes preserve manually edited braille");
            var category = dialog.TemplateButtons.Children.OfType<Button>().First();
            category.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(category.ContextMenu.IsOpen, "Category templates are available through a normal button click");
            category.ContextMenu.IsOpen = false;
        }
        finally { dialog.Close(); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
}
