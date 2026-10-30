using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsVisualDescriptionDesignChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var values = VisualDescriptions.Defaults(); values["figure"] = new(["생략", "상세"], "상세");
        var dialog = new VisualDescriptionDialog(values) { Owner = owner }; dialog.Show();
        try
        {
            dialog.UpdateLayout(); var checks = Children(dialog).OfType<CheckBox>().ToArray(); var selectors = Children(dialog).OfType<ComboBox>().ToArray();
            check(dialog.Width == 700 && dialog.Height == 518 && checks.Length == VisualDescriptions.Choices.Values.Sum(v => v.Length) && selectors.Length == 5, "Visual description dialog uses the final Figma size and only supported choices");
            var detailed = checks.Single(c => AutomationProperties.GetName(c) == "그림 상세");
            var omitted = checks.Single(c => AutomationProperties.GetName(c) == "그림 생략");
            var figureDefault = selectors.Single(c => AutomationProperties.GetName(c) == "그림 기본 설명");
            check(detailed.IsChecked == true && omitted.IsChecked == true && (string?)figureDefault.SelectedItem == "상세", "Visual description dialog restores the caller's selected options and default");
            detailed.IsChecked = false; detailed.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check((string?)figureDefault.SelectedItem == "생략" && values["figure"].DefaultOption == "상세", "Removing the current default chooses an enabled alternative without mutating saved input");
            var restore = Children(dialog).OfType<Button>().Single(b => Equals(b.Content, "기본값 복원")); restore.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(omitted.IsChecked == false && detailed.IsChecked == true && (string?)figureDefault.SelectedItem == "개조식", "Visual description restore returns the original defaults");
            dialog.UpdateLayout();
            var icons = Children(dialog).OfType<SvgViewbox>().Where(i => i.IsVisible).ToArray();
            check(icons.Length > 0 && icons.All(i => i.Drawings is { Bounds.IsEmpty: false } && i.ActualWidth > 0 && i.ActualHeight > 0 && i.ActualWidth <= i.Width + 1 && i.ActualHeight <= i.Height + 1), "Visual description Figma SVGs render in their intended slots");
        }
        finally { dialog.Close(); }

        var save = new VisualDescriptionDialog(values) { Owner = owner }; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) }; var deadline = DateTime.UtcNow.AddSeconds(5);
        timer.Tick += (_, _) =>
        {
            if (!save.IsVisible) { if (DateTime.UtcNow >= deadline) timer.Stop(); return; }
            timer.Stop(); var omit = Children(save).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "그림 생략");
            omit.IsChecked = false; omit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Children(save).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try { timer.Start(); check(save.ShowDialog() == true && save.Result["figure"].Options.SequenceEqual(["상세"]) && save.Result["figure"].DefaultOption == "상세" && values["figure"].Options.SequenceEqual(["생략", "상세"]), "Confirm returns selected descriptions without changing caller data"); }
        finally { timer.Stop(); }

        var invalid = new VisualDescriptionDialog(values) { Owner = owner }; var blocked = false;
        var invalidTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) }; var invalidDeadline = DateTime.UtcNow.AddSeconds(5);
        invalidTimer.Tick += (_, _) =>
        {
            if (!invalid.IsVisible) { if (DateTime.UtcNow >= invalidDeadline) invalidTimer.Stop(); return; }
            invalidTimer.Stop(); foreach (var box in Children(invalid).OfType<CheckBox>().Where(c => AutomationProperties.GetName(c).StartsWith("그림 ", StringComparison.Ordinal))) { box.IsChecked = false; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
            Children(invalid).OfType<Button>().Single(b => Equals(b.Content, "확인")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            blocked = invalid.IsVisible && Children(invalid).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("하나 이상", StringComparison.Ordinal));
            Children(invalid).OfType<Button>().Single(b => Equals(b.Content, "취소")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        try { invalidTimer.Start(); check(invalid.ShowDialog() == false && blocked, "Empty visual description choices are rejected before saving"); }
        finally { invalidTimer.Stop(); }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
}
