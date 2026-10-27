using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

/// <summary>Figma message-window geometry; action labels describe the real operation.</summary>
public class NoticeDialog : Window
{
    public int Choice { get; private set; } = -1;
    internal List<Button> ActionButtons { get; } = [];
    // Figma sizes long labels individually (1:2385, 1:2355/1:2432, 1:2246, 1:2415, 1:2400, 1:2464); other buttons are 88px or text + 28px.
    private static readonly Dictionary<string, double> DesignWidths = new()
    {
        ["비밀번호 찾기"] = 103, ["읽기 전용으로 열기"] = 134, ["다른 이름으로 저장"] = 134, ["실패한 쪽 다시 변환"] = 140,
        ["그대로 열기"] = 91, ["크레딧 구매"] = 91, ["오프라인으로 계속"] = 127, ["범위 다시 지정"] = 110, ["해당 위치로 이동"] = 122,
    };
    protected StackPanel MessageBody { get; } = new();
    internal string MessageText { get; }
    internal string DetailText { get; }
    public NoticeDialog(string message, string detail, string[] labels, bool error = false, double width = 520, double height = 180)
    {
        MessageText = message; DetailText = detail;
        Title = "세모점"; Width = width; Height = height; MinHeight = height; MinWidth = width;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window)); Background = Brushes.White; FontSize = 12; UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new() { CaptionHeight = 30, ResizeBorderThickness = new(0), GlassFrameThickness = new(0), UseAeroCaptionButtons = false });
        var root = new DockPanel(); Content = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(134, 134, 134)), BorderThickness = new(1), Child = root };
        var heading = new DockPanel { Height = 29 };
        var close = new Button { Content = "✕", Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), Margin = new(0), Style = (Style)FindResource("ToolButton"), ToolTip = "닫기" };
        WindowChrome.SetIsHitTestVisibleInChrome(close, true); close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = Title, Margin = new(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(12, 9, 11, 9) };
        var footer = new Border { Height = 41, Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)), Child = buttons };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        for (var i = 0; i < labels.Length; i++)
        {
            var choice = i; var label = labels[i];
            var button = new Button { Content = label, MinWidth = DesignWidths.GetValueOrDefault(label, 88), Height = 23, MinHeight = 23, Padding = new(14, 0, 14, 0), Margin = new(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(225, 225, 225)), BorderBrush = new SolidColorBrush(i == 0 ? Color.FromRgb(0, 120, 212) : Color.FromRgb(173, 173, 173)), IsDefault = i == 0, IsCancel = label == "취소" };
            button.Click += (_, _) => { Choice = choice; DialogResult = label != "취소"; }; buttons.Children.Add(button); ActionButtons.Add(button);
        }
        var body = new DockPanel { Margin = new(23, 19, 23, 12) }; root.Children.Add(body);
        var icon = new Grid { Width = 32, Height = 32, Margin = new(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.Children.Add(new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/notice-{(error ? "error" : "warning")}.svg"),
            Width = error ? 32 : 27.7128, Height = error ? 32 : 21, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, error ? 0 : 2, 0, 0) });
        icon.Children.Add(new TextBlock { Text = error ? "✕" : "!", Foreground = error ? Brushes.White : (Brush)FindResource("Ink"), FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, error ? 0 : 4, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        body.Children.Add(icon); body.Children.Add(new ScrollViewer { Content = MessageBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        MessageBody.Children.Add(new TextBlock { Text = message, FontSize = 14, LineHeight = 20, TextWrapping = TextWrapping.Wrap });
        MessageBody.Children.Add(new TextBlock { Text = detail, FontSize = 14, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) });
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
    }
    public static int Choose(Window owner, string message, string detail, string[] labels, bool error = false, double width = 520, double height = 180)
    { var dialog = new NoticeDialog(message, detail, labels, error, width, height) { Owner = owner }; dialog.ShowDialog(); return dialog.Choice; }
}

public sealed class MergeBlocksDialog : NoticeDialog
{
    public Semojum.Core.ManualEditDecision Decision => Choice switch
    {
        0 => Semojum.Core.ManualEditDecision.Overwrite,
        1 => Semojum.Core.ManualEditDecision.Keep,
        _ => Semojum.Core.ManualEditDecision.Cancel
    };
    public MergeBlocksDialog() : base("두 블록을 합치면 전체를 다시 점역합니다.",
        "직접 고친 점자가 사라집니다.", ["합치기", "고친 점자 유지", "취소"], width: 440) { }
}

public sealed class DeleteFormDialog : CompactDialog
{
    public DeleteFormDialog(string name) : base("사용자 양식 지우기", 380, 170)
    {
        var body = new StackPanel { Margin = new(16, 16, 16, 0) }; Body(body);
        body.Children.Add(new TextBlock { Text = $"「{name}」을 목록에서 지웁니다.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "이 양식으로 이미 변환한 문서는 그대로 남습니다.", Foreground = (Brush)FindResource("Muted"), Margin = new(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
        Action("지우기", () => DialogResult = true, true); Action("취소", () => DialogResult = false);
    }
}
