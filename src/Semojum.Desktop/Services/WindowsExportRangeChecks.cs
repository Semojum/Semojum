using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsExportRangeChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var first = new ExportRangeDialog([3, 4, 6], () => Task.FromResult(4), allowFormatChange: true) { Owner = owner };
        first.Loaded += (_, _) =>
        {
            try
            {
                var scope = Descendants(first).OfType<SegmentedChoice>().Single(x => AutomationProperties.GetName(x) == "범위 기준");
                check(scope.Items.Count == 2 && scope.SelectedIndex == 0 && scope.Items.Cast<ListBoxItem>().All(x => !Equals(x.Content, "전체 문서")),
                    "Range save offers only source-page and braille-face scopes");
                var format = Descendants(first).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "형식");
                scope.SelectedIndex = 1; format.SelectedItem = ".txt — 원문";
                check(scope.SelectedIndex == 0 && !((ListBoxItem)scope.Items[1]).IsEnabled,
                    "Switching to source text selects a source-page range and disables braille faces");
                format.SelectedItem = ".brf";
                check(((ListBoxItem)scope.Items[1]).IsEnabled && first.SelectedFormat == ".brf",
                    "Switching back to BRF restores face selection and records the format");
                var start = Descendants(first).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "시작 페이지 번호");
                var end = Descendants(first).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "끝 페이지 번호");
                check(start.Text == "3" && end.Text == "6", "Source-page bounds initialize from the document");
                start.Text = "4"; end.Text = "6";
                Descendants(first).OfType<Button>().Single(x => Equals(x.Content, "저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch { first.Close(); throw; }
        };
        check(first.ShowDialog() == true && first.Result == new ExportRange(ExportScope.OriginalPages, 4, 6),
            "Source-page range selection yields an explicit source range");

        var second = new ExportRangeDialog([], () => Task.FromResult(4), brailleOnly: true) { Owner = owner };
        second.Loaded += (_, _) =>
        {
            try
            {
                var scope = Descendants(second).OfType<SegmentedChoice>().Single(x => AutomationProperties.GetName(x) == "범위 기준");
                check(scope.SelectedIndex == 1 && !((ListBoxItem)scope.Items[0]).IsEnabled, "Preview-only export starts in braille-face scope");
                var start = Descendants(second).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "시작 페이지 번호");
                var end = Descendants(second).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "끝 페이지 번호");
                check(start.Text == "1" && end.Text == "4", "Braille-face bounds use composed physical faces");
                start.Text = "2"; end.Text = "3";
                Descendants(second).OfType<Button>().Single(x => Equals(x.Content, "저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch { second.Close(); throw; }
        };
        check(second.ShowDialog() == true && second.Result == new ExportRange(ExportScope.BraillePages, 2, 3),
            "Braille-face range selection yields an explicit face range");

        var third = new ExportRangeDialog([1, 2], () => Task.FromResult(3), format: ".txt — 원문", allowFormatChange: true) { Owner = owner };
        third.Loaded += (_, _) =>
        {
            try
            {
                var scope = Descendants(third).OfType<SegmentedChoice>().Single(x => AutomationProperties.GetName(x) == "범위 기준");
                var format = Descendants(third).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == "형식");
                check(scope.SelectedIndex == 0 && !((ListBoxItem)scope.Items[1]).IsEnabled, "Source TXT initially uses source-page scope");
                format.SelectedItem = ".brf";
                check(scope.SelectedIndex == 0 && ((ListBoxItem)scope.Items[1]).IsEnabled, "Changing source TXT to BRF retains source-page selection");
                Descendants(third).OfType<TextBox>().Single(x => AutomationProperties.GetName(x) == "끝 페이지 번호").Text = "1";
                Descendants(third).OfType<Button>().Single(x => Equals(x.Content, "저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch { third.Close(); throw; }
        };
        check(third.ShowDialog() == true && third.Result == new ExportRange(ExportScope.OriginalPages, 1, 1),
            "Source TXT changed to BRF confirms a valid source-page subset");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
