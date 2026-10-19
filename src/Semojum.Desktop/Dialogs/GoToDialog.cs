using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class GoToDialog : Window
{
    internal ComboBox Target { get; } = new() { MinHeight = 32, FontSize = 12 };
    internal TextBox Number { get; } = new() { Text = "1", MinHeight = 32, FontSize = 12 };
    internal TextBlock Result { get; } = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick };
    public GoToDialog(SearchPanel panel, Func<NavigationTarget, string, Task<string>> navigate)
    {
        Title = "이동"; Width = 440; MinHeight = 250; SizeToContent = SizeToContent.Height;
        FontSize = 12; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel(); Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        var footer = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new StackPanel { Margin = new Thickness(16, 12, 16, 16) }; root.Children.Add(body);
        var hint = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) };
        foreach (var (name, value) in new[] { ("원본 쪽 (묵자)", NavigationTarget.OriginalPage),
            ($"현재 {(panel == SearchPanel.Text ? "텍스트" : "점자")} 블록의 줄", NavigationTarget.Line),
            ("검토가 필요한 블록", NavigationTarget.Review), ("메모 있는 줄", NavigationTarget.Memo) })
            Target.Items.Add(new ComboBoxItem { Content = name, Tag = value });
        Target.Items.Add(new ComboBoxItem { Content = "점자 면 — 편집 위치 연결 준비 중", IsEnabled = false });
        Target.SelectionChanged += (_, _) =>
        {
            hint.Text = Target.SelectedIndex switch
            {
                0 => "변환에 성공해 편집할 수 있는 원본 쪽으로 이동합니다.",
                1 => "현재 블록 안의 줄 번호입니다.",
                2 => "점역에서 차단되거나 누락 문자가 발생한 블록 순서입니다.",
                _ => $"문서의 {(panel == SearchPanel.Text ? "텍스트" : "점자")} 메모가 있는 줄 순서입니다."
            };
            Result.Text = "";
        };
        Target.SelectedIndex = 0;
        void Row(string label, FrameworkElement control)
        {
            var row = new DockPanel(); row.Children.Add(new TextBlock { Text = label, Width = 120 }); row.Children.Add(control); body.Children.Add(row);
        }
        Row("이동할 대상", Target); body.Children.Add(hint); Row("번호", Number);
        body.Children.Add(new TextBlock { Text = "+3 · −3처럼 상대 이동도 가능합니다.", Margin = new Thickness(0, 12, 0, 8), Foreground = (Brush)FindResource("Muted") });
        body.Children.Add(Result);
        var busy = false;
        foreach (var (label, delta) in new[] { ("이동", ""), ("이전", "-1"), ("닫기", "close") })
        {
            var button = new Button { Content = label, Width = 76, MinHeight = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(4, 0, 4, 0), IsDefault = delta == "", IsCancel = delta == "close" };
            if (delta == "") button.Style = (Style)FindResource("PrimaryButton");
            button.Click += async (_, _) =>
            {
                if (delta == "close") { Close(); return; }
                if (busy || Target.SelectedItem is not ComboBoxItem { Tag: NavigationTarget target }) return;
                busy = true; body.IsEnabled = buttons.IsEnabled = false;
                try { Result.Text = await navigate(target, delta.Length == 0 ? Number.Text : delta); }
                catch (ArgumentException ex) { Result.Text = ex.Message; }
                finally { busy = false; body.IsEnabled = buttons.IsEnabled = true; }
            };
            buttons.Children.Add(button);
        }
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Loaded += (_, _) => { Number.Focus(); Number.SelectAll(); };
    }
}
