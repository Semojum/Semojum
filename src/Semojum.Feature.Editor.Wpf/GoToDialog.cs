using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class GoToDialog : CompactDialog
{
    internal TextBox Number { get; } = new() { Text = "1", FontSize = 12 };
    internal TextBlock Result { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    public GoToDialog(SearchPanel panel, Func<NavigationTarget, string, Task<string>> navigate) : base("이동", 440, 252, popover: true)
    {
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#EAEAEA"); frame.Background = Brush("#F3F3F3"); }
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = new(55) });
        layout.RowDefinitions.Add(new() { Height = new(132) });
        layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);

        var title = new Grid { Margin = new(24, 0, 24, 0) };
        title.Children.Add(new TextBlock { Text = "이동", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new System.Windows.Controls.Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = DesignIcon("goto-close.svg", 16, 16, "Semojum.Feature.Editor.Wpf"), Template = SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); title.Children.Add(close);
        title.MouseLeftButtonDown += (_, e) => { if (!close.IsMouseOver && e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        layout.Children.Add(title);

        var body = new StackPanel { Margin = new(24, 16, 24, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "이동할 자리", Foreground = Brush("#5F5F5F"), FontSize = 12, Margin = new(0, 0, 0, 4) });
        var form = new Grid { Margin = new(16, 12, 16, 4) };
        form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        form.RowDefinitions.Add(new() { Height = new(36) }); form.RowDefinitions.Add(new() { Height = new(36) });
        body.Children.Add(new Border { Height = 88, CornerRadius = new(6), Background = Brush("#F9F9F9"), Child = form });
        void Field(int row, string label, FrameworkElement control)
        {
            var caption = new TextBlock { Text = label, FontSize = 14, Foreground = Brush("#1A1A1A"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(caption, row); form.Children.Add(caption); Grid.SetRow(control, row); Grid.SetColumn(control, 1); form.Children.Add(control);
        }
        var target = new SegmentedChoice("원본 페이지", "점자 페이지") { Width = 218, Height = 32, ItemContainerStyle = ScopeStyle() };
        AutomationProperties.SetName(target, "이동할 대상");
        target.ToolTip = "점자 페이지는 조판 결과를 첫 면부터 센 번호입니다.";
        target.SelectionChanged += (_, _) => Result.Text = "";
        Field(0, "이동할 대상", target);
        var numberField = new Grid { Width = 104, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        numberField.ColumnDefinitions.Add(new() { Width = new(80) }); numberField.ColumnDefinitions.Add(new() { Width = new(24) });
        Number.Padding = new(11, 0, 2, 0); Number.VerticalContentAlignment = VerticalAlignment.Center;
        Number.Background = Brush("#FBFBFB"); Number.BorderBrush = Brush("#EAEAEA");
        Number.ToolTip = "+3 · -3처럼 상대 이동도 가능합니다."; AutomationProperties.SetName(Number, "번호"); numberField.Children.Add(Number);
        var arrows = new Grid { Background = Brush("#F0F0F0") }; arrows.RowDefinitions.Add(new()); arrows.RowDefinitions.Add(new());
        Grid.SetColumn(arrows, 1); numberField.Children.Add(arrows);
        var upIcon = DesignIcon("goto-spinner-up.svg", 9.60063, 5.19934, "Semojum.Feature.Editor.Wpf"); upIcon.RenderTransformOrigin = new(.5, .5); upIcon.RenderTransform = new ScaleTransform(1, -1);
        foreach (var (delta, icon, row) in new[] { (1, upIcon, 0), (-1, DesignIcon("goto-spinner-down.svg", 16, 16, "Semojum.Feature.Editor.Wpf"), 1) })
        {
            var button = new RepeatButton { Content = icon, BorderThickness = new(0), Background = Brush("#F0F0F0"), Padding = new(0), MinHeight = 0 };
            AutomationProperties.SetName(button, delta > 0 ? "번호 증가" : "번호 감소");
            button.Click += (_, _) =>
            {
                var text = Number.Text.Trim();
                if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var current)) return;
                var next = (long)current + delta;
                if (next < int.MinValue || next > int.MaxValue) return;
                var relative = text.StartsWith('+') || text.StartsWith('-');
                if (!relative && next < 1) return;
                Number.Text = relative && next >= 0 ? "+" + next.ToString(CultureInfo.InvariantCulture) : next.ToString(CultureInfo.InvariantCulture);
            };
            Grid.SetRow(button, row); arrows.Children.Add(button);
        }
        Field(1, "번호", numberField);

        var footer = new Grid { Margin = new(24, 16, 24, 16) }; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Result.MaxWidth = 200; Result.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(Result);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        var cancel = Button("닫기", () => { Close(); return Task.CompletedTask; }); cancel.IsCancel = true;
        var busy = false;
        var go = Button("이동", async () =>
        {
            if (busy) return;
            busy = true; body.IsEnabled = false;
            try { Result.Text = await navigate(target.SelectedIndex == 0 ? NavigationTarget.OriginalPage : NavigationTarget.BraillePage, Number.Text); }
            catch (Exception ex) { Result.Text = ex.Message; }
            finally { busy = false; body.IsEnabled = true; }
        }, true);
        foreach (var button in new[] { cancel, go }) { button.Width = 88; button.MinWidth = 0; button.Height = 32; button.MinHeight = 0; button.Template = SurfaceTemplate(4); actions.Children.Add(button); }
        cancel.BorderBrush = Brush("#EAEAEA");
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Loaded += (_, _) => { Services.WindowBounds.Fit(this); Number.Focus(); Number.SelectAll(); };
    }
}
