using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

/// <summary>The server receives a request for operator review, not an immediate account or credit change.</summary>
internal sealed class InstitutionRequestDialog : CompactDialog
{
    internal TextBox First { get; } = new();
    internal TextBox Second { get; } = new();
    internal ComboBox Option { get; } = new();
    internal Button Submit { get; }
    internal string? SubmissionMessage { get; private set; }

    internal InstitutionRequestDialog(bool accountRequest, Func<string, Task> submit)
        : base(accountRequest ? "계정 발급 요청" : "크레딧 추가 요청", 520, accountRequest ? 344 : 288, popover: true)
    {
        var title = accountRequest ? "계정 발급 요청" : "크레딧 추가 요청";
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = ExportRangeDialog.Brush("#EAEAEA"); frame.Background = ExportRangeDialog.Brush("#F3F3F3"); }
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(55) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right, Content = ExportRangeDialog.DesignIcon(accountRequest ? "institution-account-request-close.svg" : "institution-credit-request-close.svg", 16, 16), Template = ExportRangeDialog.SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close); layout.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 15, 24, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        body.Children.Add(new TextBlock { Text = accountRequest ? "새 계정" : "요청할 양", Foreground = ExportRangeDialog.Brush("#5F5F5F"), FontSize = 12, Margin = new(0, 0, 0, 4) });
        var form = new Grid { Margin = new(16, 12, 16, 4) }; form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        for (var i = 0; i < 3; i++) form.RowDefinitions.Add(new() { Height = new(36) });
        body.Children.Add(new Border { Height = 124, Background = ExportRangeDialog.Brush("#F9F9F9"), CornerRadius = new(6), Child = form });
        void Field(int row, string label, FrameworkElement field)
        {
            var caption = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(caption, row); form.Children.Add(caption);
            field.Height = 32; field.HorizontalAlignment = HorizontalAlignment.Stretch; AutomationProperties.SetName(field, label); Grid.SetRow(field, row); Grid.SetColumn(field, 1); form.Children.Add(field);
        }
        First.MaxLength = 150; Second.MaxLength = 254;
        if (accountRequest)
        {
            Field(0, "별칭", First); Field(1, "받을 사람 메일", Second);
            Option.ItemsSource = new[] { "기관 할당 공유", "계정별 한도 지정", "크레딧 없음 (읽기만)" }; Option.SelectedIndex = 0; Field(2, "쓸 수 있는 크레딧", Option);
            body.Children.Add(new TextBlock { Text = "입력한 내용은 운영자에게 요청으로 전달됩니다.", Foreground = ExportRangeDialog.Brush("#5F5F5F"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 0) });
        }
        else
        {
            Field(0, "크레딧", First);
            Field(1, "예상 금액", new TextBlock { Text = "금액 정보가 제공되지 않았습니다.", Foreground = ExportRangeDialog.Brush("#5F5F5F"), VerticalAlignment = VerticalAlignment.Center });
            Option.ItemsSource = new[] { "기관 명의 발행", "개인 명의 발행", "발행 안 함" }; Option.SelectedIndex = 0; Field(2, "세금계산서", Option);
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 0) }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var cancel = Button("취소", () => { Close(); return Task.CompletedTask; }); cancel.Width = 88; cancel.Height = 32; actions.Children.Add(cancel);
        Submit = Button("요청 보내기", async () => await RunAsync(async () =>
        {
            string message;
            if (accountRequest)
            {
                if (string.IsNullOrWhiteSpace(First.Text) || !System.Net.Mail.MailAddress.TryCreate(Second.Text.Trim(), out _)) throw new ArgumentException("별칭과 올바른 이메일을 입력해 주세요.");
                message = $"별칭: {First.Text.Trim()}\n받을 사람 메일: {Second.Text.Trim()}\n쓸 수 있는 크레딧: {Option.SelectedItem}";
            }
            else
            {
                if (!int.TryParse(First.Text.Replace(",", "").Trim(), out var amount) || amount <= 0) throw new ArgumentException("요청할 크레딧을 양의 정수로 입력해 주세요.");
                message = $"요청 크레딧: {amount}\n세금계산서: {Option.SelectedItem}";
            }
            await submit(message); SubmissionMessage = message; DialogResult = true;
        }), true); Submit.Width = 110; Submit.Height = 32; actions.Children.Add(Submit);
    }
}
