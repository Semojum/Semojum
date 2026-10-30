using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsGoToDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        NavigationTarget? selected = null; string? number = null;
        var dialog = new GoToDialog(SearchPanel.Text, (target, value) => { selected = target; number = value; return Task.FromResult(""); });
        dialog.Show();
        try
        {
            dialog.UpdateLayout();
            var buttons = Descendants(dialog).OfType<Button>().Where(b => b.IsVisible).ToArray();
            var icons = Descendants(dialog).OfType<SvgViewbox>().Where(i => i.IsVisible).ToArray();
            check(icons.Length == 3 && icons.All(i => i.Source is not null && i.Drawings is { Bounds.IsEmpty: false }),
                "Go-to shows all three Figma close and number-field icons");
            var choices = Descendants(dialog).OfType<ListBoxItem>().Where(i => i.IsVisible).ToArray();
            var original = choices.FirstOrDefault(b => Equals(b.Content, "원본 페이지"));
            var braille = choices.FirstOrDefault(b => Equals(b.Content, "점자 페이지"));
            check(original is not null && braille is not null, "Go-to offers both page targets as visible direct choices");
            if (braille is null) return;
            braille.IsSelected = true;
            dialog.Number.Text = "+3";
            var spinner = Descendants(dialog).OfType<RepeatButton>().First(b => AutomationProperties.GetName(b) == "번호 증가");
            spinner.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(dialog.Number.Text == "+4", "Go-to spinner preserves relative navigation when incrementing");
            dialog.Number.Text = "+3";
            buttons.First(b => Equals(b.Content, "이동")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(30);
            check(selected == NavigationTarget.BraillePage && number == "+3", "Go-to sends the chosen page target and relative number");
        }
        finally { dialog.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
