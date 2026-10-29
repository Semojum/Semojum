using System.Net.Mail;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media;
using Semojum.Core.Integration;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class PublicNoticesDialog : CompactDialog
{
    public PublicNoticesDialog(SemojumApiClient api) : base("공지사항", 620, 440)
    {
        var panel = new StackPanel { Margin = new(16) };
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var loading = false;
        Closing += (_, e) => { if (loading) { e.Cancel = false; Lifetime.Cancel(); } };
        async Task LoadAsync()
        {
            loading = true;
            try
            {
                var result = await api.GetPublicAsync("/api/public/notices", Lifetime.Token);
                if (Lifetime.IsCancellationRequested) return;
                var notices = result.EnumerateArray().Select(n => (Title: n.GetProperty("title").GetString() ?? "", Body: n.GetProperty("body").GetString() ?? "", Date: NoticeDate(n))).ToArray();
                var cards = notices.Select((n, i) => CreateNotice(n.Title, n.Body, n.Date, i == 0)).ToArray();
                panel.Children.Clear();
                if (notices.Length == 0) panel.Children.Add(new TextBlock { Text = "현재 표시할 공지가 없습니다." });
                foreach (var card in cards) panel.Children.Add(card);
            }
            finally { loading = false; }
        }
        Action("새로고침", LoadAsync); Action("닫기", Close); OnLoad(LoadAsync);
    }

    private static string NoticeDate(JsonElement notice)
    {
        if (!notice.TryGetProperty("createdAt", out var value) || value.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return "";
        return date.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy. MM. dd.", CultureInfo.InvariantCulture);
    }

    private static Expander CreateNotice(string title, string body, string date, bool expanded)
    {
        var header = new Grid { MinHeight = 40, Margin = new(12, 0, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var dateText = new TextBlock { Text = date, FontSize = 12, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
        Grid.SetColumn(dateText, 1); header.Children.Add(dateText);
        var icon = new SvgViewbox { Width = 16, Height = 16, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 2); header.Children.Add(icon);
        var content = new Border { Background = Brushes.White, Margin = new(12, 0, 12, 8), Padding = new(12, 8, 12, 8),
            Child = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 13 } };
        var card = new Expander { Header = header, Content = content, IsExpanded = expanded, Margin = new(0, 0, 0, 8), Template = NoticeTemplate() };
        AutomationProperties.SetName(card, title);
        void UpdateIcon() => icon.Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/notice-chevron-{(card.IsExpanded ? "down" : "right")}.svg");
        card.Expanded += (_, _) => UpdateIcon(); card.Collapsed += (_, _) => UpdateIcon(); UpdateIcon();
        return card;
    }

    private static ControlTemplate NoticeTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Expander">
          <Border x:Name="Card" Background="White" BorderBrush="#D6D6D6" BorderThickness="1" CornerRadius="4">
            <StackPanel>
              <ToggleButton IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Content="{TemplateBinding Header}" HorizontalContentAlignment="Stretch" BorderThickness="0" Background="Transparent" Padding="0">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}"><ContentPresenter HorizontalAlignment="Stretch" /></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="Details" Content="{TemplateBinding Content}" Visibility="Collapsed" />
            </StackPanel>
          </Border>
          <ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Card" Property="Background" Value="#CCE4F7"/><Setter TargetName="Card" Property="BorderBrush" Value="#005FB8"/><Setter TargetName="Details" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);
}
public sealed class PublicInquiryDialog : CompactDialog
{
    public PublicInquiryDialog(SemojumApiClient api) : base("문의하기", 560, 480)
    {
        var panel = new StackPanel { Margin = new(16) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var type = Choices(["도입 문의", "오류 신고", "기타"], "기타"); var name = new TextBox { MaxLength = 50 }; var email = new TextBox { MaxLength = 100 };
        var initialType = type.SelectedItem;
        var organization = new TextBox { MaxLength = 2000 }; var title = new TextBox { MaxLength = 2000 };
        AutomationProperties.SetName(organization, "기관명"); AutomationProperties.SetName(title, "제목");
        var message = new TextBox { MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Row(panel, "문의 유형", type); Row(panel, "이름 (선택)", name); Row(panel, "답변받을 이메일", email);
        Row(panel, "기관명 (선택)", organization); Row(panel, "제목 (선택)", title); panel.Children.Add(message);
        panel.Children.Add(new TextBlock { Text = "기관명과 제목은 문의 내용에 함께 전달됩니다. 현재 첨부파일은 보낼 수 없습니다.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        panel.Children.Add(new TextBlock { Text = "이름·이메일·문의 내용이 운영자에게 전달됩니다. 문의 이메일: contact@semo-jum.com", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var submitted = false; var discardConfirmed = false;
        Closing += (_, e) =>
        {
            if (e.Cancel || submitted || discardConfirmed || Equals(type.SelectedItem, initialType) && string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(email.Text)
                && string.IsNullOrWhiteSpace(organization.Text) && string.IsNullOrWhiteSpace(title.Text) && string.IsNullOrWhiteSpace(message.Text)) return;
            var choice = NoticeDialog.Choose(this, "작성 중인 문의를 닫으시겠습니까?", "계속 작성하면 입력한 내용을 유지합니다.", ["계속 작성", "닫기"], width: 440);
            if (choice != 1) e.Cancel = true; else discardConfirmed = true;
        };
        Action("문의 보내기", async () =>
        {
            if (!MailAddress.TryCreate(email.Text.Trim(), out var address) || address.Address != email.Text.Trim() || string.IsNullOrWhiteSpace(message.Text)) throw new ArgumentException("이메일 주소와 문의 내용을 확인해 주세요.");
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(organization.Text)) lines.Add("기관명: " + organization.Text.Trim());
            if (!string.IsNullOrWhiteSpace(title.Text)) lines.Add("제목: " + title.Text.Trim());
            var body = lines.Count == 0 ? message.Text.Trim() : string.Join("\n", lines) + "\n\n" + message.Text.Trim();
            if (body.Length > 2000) throw new ArgumentException("기관명·제목을 포함한 문의 내용은 2,000자까지 입력할 수 있습니다.");
            await api.SubmitInquiryAsync(new { type = type.SelectedIndex switch { 0 => "ONBOARDING", 1 => "ERROR_REPORT", _ => "ETC" }, name = name.Text.Trim(), email = address.Address, message = body, website = "" }, Lifetime.Token);
            submitted = true;
            MessageBox.Show(this, "문의를 접수했습니다.", "문의하기"); DialogResult = true;
        }, true);
        var cancel = Action("취소", Close); cancel.IsCancel = false;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
    }
}
