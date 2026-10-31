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
    public new bool? ShowDialog() => Services.OwnedModal.Show(this, base.ShowDialog);
    public int Choice { get; private set; } = -1;
    internal List<Button> ActionButtons { get; } = [];
    protected StackPanel MessageBody { get; } = new();
    internal string MessageText { get; }
    internal string DetailText { get; }
    public NoticeDialog(string message, string detail, string[] labels, bool error = false, double width = 520, double height = 180)
    {
        MessageText = message; DetailText = detail;
        Title = "세모점"; Width = width; Height = Math.Max(height, 196); MinHeight = Height; MinWidth = width;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window)); Background = FigmaDialogElements.Brush("#F3F3F3"); FontSize = 12; UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(this, new() { CaptionHeight = 0, ResizeBorderThickness = new(0), GlassFrameThickness = new(0), UseAeroCaptionButtons = false });
        var root = new DockPanel(); Content = new Border { BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = root };
        var buttons = new Grid { Margin = new(24, 24, 24, 8) };
        var footer = new Border { Height = 64, Background = FigmaDialogElements.Brush("#EEEEEE"), BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0), Child = buttons };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        for (var i = 0; i < labels.Length; i++)
        {
            var choice = i; var label = labels[i];
            if (i > 0) buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            buttons.ColumnDefinitions.Add(new ColumnDefinition());
            var destructive = label is "덮어쓰기" or "합치기" or "저장 안 함";
            var color = destructive ? "#C42B1C" : i == 0 ? "#005FB8" : "#FBFBFB";
            var button = new Button { Content = label, MinWidth = 0, Height = 32, MinHeight = 0, Padding = new(4, 0, 4, 0), Margin = new(0),
                Background = FigmaDialogElements.Brush(color), BorderBrush = FigmaDialogElements.Brush(i == 0 || destructive ? color : "#EAEAEA"),
                Foreground = i == 0 || destructive ? Brushes.White : FigmaDialogElements.Brush("#1A1A1A"), IsDefault = i == 0, IsCancel = label == "취소", Template = FigmaDialogElements.SurfaceTemplate(4) };
            Grid.SetColumn(button, i * 2); button.Click += (_, _) => { Choice = choice; DialogResult = label != "취소"; }; buttons.Children.Add(button); ActionButtons.Add(button);
        }
        var body = new ScrollViewer { Margin = new(23, 19, 23, 0), Content = MessageBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(body);
        var status = new DockPanel { LastChildFill = true, Margin = new(12, 0, 12, 0) };
        var icon = new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/case-{(error ? "error" : "warning")}.svg"), Width = 16, Height = 16, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var iconSlot = new Border { Width = 24, Child = icon }; DockPanel.SetDock(iconSlot, Dock.Left); status.Children.Add(iconSlot);
        status.Children.Add(new TextBlock { Text = message, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = FigmaDialogElements.Brush(error ? "#C42B1C" : "#9D5D00"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        MessageBody.Children.Add(new Border { MinHeight = 40, CornerRadius = new(4), Background = FigmaDialogElements.Brush(error ? "#FDE7E9" : "#FFF4CE"), Child = status });
        MessageBody.Children.Add(new TextBlock { Text = detail, FontSize = 14, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 8) });
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
    public DeleteFormDialog(string name) : base("사용자 양식 지우기", 380, 218, popover: true)
    {
        var frame = PublicDialogDesign.Frame(this, "사용자 양식 지우기", "form-delete-close.svg", "FormDeleteClose", out var actions); Body(frame);
        // This Figma confirmation's action row has no divider, unlike public dialogs.
        if (actions.Parent is Border footer) footer.BorderThickness = new Thickness(0);
        var body = new StackPanel { Margin = new(16, 16, 16, 0) }; Grid.SetRow(body, 1); frame.Children.Add(body);
        body.Children.Add(new TextBlock { Text = $"「{name}」을 목록에서 지웁니다.", FontSize = 14, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = "이 양식으로 이미 변환한 문서는 그대로 남습니다.", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; }); PublicDialogDesign.StyleAction(cancel); actions.Children.Add(cancel);
        var delete = Button("지우기", () => { DialogResult = true; return Task.CompletedTask; }, true); PublicDialogDesign.StyleAction(delete);
        delete.Background = delete.BorderBrush = FigmaDialogElements.Brush("#C42B1C"); delete.Foreground = Brushes.White; actions.Children.Add(delete);
    }
}
