using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class GoToDialog : CompactDialog
{
    internal ComboBox Target { get; } = new() { MinHeight = 32, FontSize = 12 };
    internal TextBox Number { get; } = new() { Text = "1", MinHeight = 32, FontSize = 12 };
    internal TextBlock Result { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    public GoToDialog(SearchPanel panel, Func<NavigationTarget, string, Task<string>> navigate) : base("이동", 440, 250)
    {
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 0) }; Body(body);
        var hint = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
        foreach (var (name, value) in new[] { ("원본 페이지", NavigationTarget.OriginalPage), ("점자 페이지", NavigationTarget.BraillePage) })
            Target.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        Target.SelectionChanged += (_, _) =>
        {
            hint.Text = Target.SelectedIndex switch
            {
                0 => "변환에 성공해 편집할 수 있는 원본 쪽으로 이동합니다.",
                _ => "조판 결과를 첫 면부터 센 번호입니다. 점자 패널의 해당 면으로 이동합니다."
            };
            Result.Text = "";
        };
        Target.SelectedIndex = 0;
        void Row(string label, FrameworkElement control)
        {
            var row = new DockPanel { Height = 40 }; row.Children.Add(new TextBlock { Text = label, Width = 120, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
            control.Height = 32; control.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(control); body.Children.Add(row);
        }
        Row("이동할 대상", Target); Row("번호", Number);
        Target.ToolTip = hint; Number.ToolTip = "+3 · −3처럼 상대 이동도 가능합니다.";
        body.Children.Add(Result);
        var busy = false;
        foreach (var (label, delta) in new[] { ("이동", ""), ("닫기", "close") })
        {
            if (delta == "close") { Action(label, Close); continue; }
            Action(label, async () =>
            {
                if (delta == "close") { Close(); return; }
                if (busy || Target.SelectedItem is not ComboBoxItem { Tag: NavigationTarget target }) return;
                busy = true; body.IsEnabled = false;
                try { Result.Text = await navigate(target, delta.Length == 0 ? Number.Text : delta); }
                catch (ArgumentException ex) { Result.Text = ex.Message; }
                finally { busy = false; body.IsEnabled = true; }
            }, delta == "");
        }
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Loaded += (_, _) => { Services.WindowBounds.Fit(this); Number.Focus(); Number.SelectAll(); };
    }
}
