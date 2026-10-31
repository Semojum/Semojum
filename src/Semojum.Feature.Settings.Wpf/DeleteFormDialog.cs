using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

public sealed class DeleteFormDialog : CompactDialog
{
    public DeleteFormDialog(string name) : base("사용자 양식 지우기", 380, 218, popover: true)
    {
        var frame = PublicDialogDesign.Frame(this, "사용자 양식 지우기", "form-delete-close.svg", "FormDeleteClose", out var actions, "Semojum.Feature.Settings.Wpf"); Body(frame);
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
