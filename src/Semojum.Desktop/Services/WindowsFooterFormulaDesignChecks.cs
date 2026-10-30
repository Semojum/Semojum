using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsFooterFormulaDesignChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var header = new HeaderInputDialog("선택한 내용") { Owner = owner }; header.Show();
        try
        {
            header.UpdateLayout(); var align = Children(header).OfType<SegmentedChoice>().Single();
            check(header.Width == 520 && header.Height == 252 && header.Result == "선택한 내용", "Header dialog preserves selected text and Figma geometry");
            check(align.SelectedIndex == 1 && !((ListBoxItem)align.Items[0]).IsEnabled && !((ListBoxItem)align.Items[2]).IsEnabled, "Unsupported header alignment choices are not offered as working features");
            CheckIcons(header, check);
        }
        finally { header.Close(); }

        var footer = new FooterInputDialog(3) { Owner = owner }; footer.Show();
        try
        {
            footer.UpdateLayout(); var start = Children(footer).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "시작 면 번호");
            var increase = Children(footer).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "시작 면 번호 늘리기");
            increase.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(footer.Width == 520 && footer.Height == 288 && start.Text == "4", "Footer starts on selected page and spinner increments it");
            CheckIcons(footer, check);
        }
        finally { footer.Close(); }

        var model = new DocumentSession { FooterText = "기본", FooterRanges = [new(1, 12, "첫 머리", "center"), new(13, 28, "다음 머리", "right")] };
        var manager = new FooterDialog(model) { Owner = owner }; manager.Show();
        try
        {
            manager.UpdateLayout(); var grid = Children(manager).OfType<DataGrid>().Single(); var entries = (IList)grid.ItemsSource;
            var first = (FooterDialog.Entry)entries[0]!; var second = (FooterDialog.Entry)entries[1]!;
            check(manager.Width == 680 && manager.Height == 326 && first.End == "12" && second.End == "28" &&
                entries.Count == 3 && ((FooterDialog.Entry)entries[2]!).Start == "29" && ((FooterDialog.Entry)entries[2]!).Text == "기본",
                "Footer manager preserves the automatic restoration row after the final finite range");
            var add = Children(manager).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "꼬리말 줄 더하기");
            var selectedDelete = Children(manager).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "선택한 꼬리말 지우기");
            var outline = Children(add).OfType<Rectangle>().Single();
            check(add.ActualHeight == 32 && Math.Abs(add.ActualWidth - grid.ActualWidth) < 2 && add.Focusable && add.IsTabStop &&
                outline.StrokeDashArray is { Count: > 0 } && outline.RadiusX == 4,
                "Footer row addition is a full-width, keyboard-focusable dashed action");
            var addBounds = add.TransformToAncestor(manager).TransformBounds(new Rect(0, 0, add.ActualWidth, add.ActualHeight));
            var confirmButton = Children(manager).OfType<Button>().Single(b => b.IsDefault);
            var confirmBounds = confirmButton.TransformToAncestor(manager).TransformBounds(new Rect(0, 0, confirmButton.ActualWidth, confirmButton.ActualHeight));
            check(addBounds.Bottom <= confirmBounds.Top - 15,
                "The full dashed add action remains visible above the footer button row");
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(entries.Count == 4 && grid.SelectedItem is FooterDialog.Entry { Start: "30" },
                "Footer add action appends and selects a new row after the restoration row");
            selectedDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(entries.Count == 3 && ((FooterDialog.Entry)entries[2]!).Start == "29", "Deleting the selected new row keeps the restoration row");
            second.Start = "20"; check(first.End == "19", "Footer end follows the next start page");
            var remove = Children(manager).OfType<Button>().First(b => AutomationProperties.GetName(b) == "이 꼬리말 삭제");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check(entries.Count == 2, "Footer row delete acts on that row"); CheckIcons(manager, check);
            for (var i = 0; i < 12; i++) add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            manager.Width = 480; manager.UpdateLayout();
            var scroll = Children(grid).OfType<ScrollViewer>().First();
            check(scroll.ScrollableHeight > 0 && scroll.ScrollableWidth > 0 && add.IsVisible && add.ActualWidth <= manager.ActualWidth,
                "Long footer lists scroll vertically and the narrow window exposes horizontal scrolling and add action");
        }
        finally { manager.Close(); }
        check(model.FooterRanges.SequenceEqual([new FooterRange(1, 12, "첫 머리", "center"), new FooterRange(13, 28, "다음 머리", "right")]),
            "Closing footer management without confirmation leaves the document unchanged");

        var saved = new FooterDialog(model) { Owner = owner }; var timedOut = false; Exception? callbackError = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timedOut = true; timer.Stop(); if (saved.IsVisible) saved.Close(); };
        saved.Loaded += (_, _) => saved.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                var data = Children(saved).OfType<DataGrid>().Single();
                var plus = Children(saved).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "꼬리말 줄 더하기");
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((FooterDialog.Entry)data.SelectedItem).Text = "추가 꼬리말";
                Children(saved).OfType<Button>().Single(b => b.IsDefault).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            catch (Exception e) { callbackError = e; if (saved.IsVisible) saved.Close(); }
        }));
        bool? accepted = null;
        try { timer.Start(); accepted = saved.ShowDialog(); }
        finally { timer.Stop(); if (saved.IsVisible) saved.Close(); }
        check(!timedOut && callbackError is null && accepted == true && saved.Ranges.Any(r => r.Start == 29 && r.Text == "기본") &&
            saved.Ranges.Any(r => r.Start == 30 && r.Text == "추가 꼬리말"),
            "Confirming an added footer preserves the restoration range and saves the new range");

        var math = new MathDialog((_, _) => Task.FromResult(BrailleTranslation.Plain("⠁")), "x+1", "⠿") { Owner = owner }; math.Show();
        try
        {
            math.UpdateLayout(); math.RefreshAsync().GetAwaiter().GetResult();
            check(math.Width == 980 && math.Height == 510 && math.BrailleInput.Text == "⠿" && math.Edited, "Math editor Figma size preserves manual braille");
            math.Input.Text = "x+2"; math.RefreshAsync().GetAwaiter().GetResult();
            check(math.BrailleInput.Text == "⠿" && math.Edited, "Changing LaTeX does not replace manual braille"); CheckIcons(math, check);
            var fraction = math.SymbolButtons.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "분수");
            var root = math.SymbolButtons.Children.OfType<Button>().Single(b => AutomationProperties.GetName(b) == "제곱근");
            static bool ColorIs(Brush? brush, byte r, byte g, byte b) => brush is SolidColorBrush solid && solid.Color == Color.FromRgb(r, g, b);
            check(ColorIs(fraction.Background, 255, 255, 255) && ColorIs(fraction.BorderBrush, 209, 209, 209), "An unselected math symbol uses the Figma white fill and gray border");
            fraction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(ColorIs(fraction.Background, 239, 246, 252) && ColorIs(fraction.BorderBrush, 0, 95, 184) && ColorIs(root.Background, 255, 255, 255), "Selecting a math symbol uses the Figma blue fill and border");
            root.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(ColorIs(fraction.Background, 255, 255, 255) && ColorIs(fraction.BorderBrush, 209, 209, 209) && ColorIs(root.Background, 239, 246, 252), "Choosing another math symbol restores the previous symbol's unselected state");
        }
        finally { math.Close(); }
    }
    private static void CheckIcons(Window window, Action<bool, string> check)
    {
        var icons = Children(window).OfType<SvgViewbox>().ToArray();
        check(icons.Length > 0 && icons.All(icon => icon.Drawings is { Bounds.IsEmpty: false } && icon.ActualWidth > 0 && icon.ActualHeight > 0 && icon.ActualWidth <= icon.Width + 1 && icon.ActualHeight <= icon.Height + 1), $"{window.Title} Figma SVGs render in their intended slots");
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
}
