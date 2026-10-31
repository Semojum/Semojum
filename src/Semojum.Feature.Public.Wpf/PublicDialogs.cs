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
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class PublicNoticesDialog : CompactDialog
{
    public PublicNoticesDialog(SemojumApiClient api) : base("공지사항", 600, 488, popover: true)
    {
        var layout = PublicDialogDesign.Frame(this, "공지사항", "public-notice-close.svg", "PublicNoticesClose", out var actions, "Semojum.Feature.Public.Wpf"); Body(layout);
        var body = new DockPanel { Margin = new(16, 12, 16, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        var categories = new StackPanel { Orientation = Orientation.Horizontal, Height = 24, Margin = new(0, 0, 0, 16) };
        DockPanel.SetDock(categories, Dock.Top); body.Children.Add(categories);
        foreach (var (label, width, available) in new[] { ("전체", 44d, true), ("점검", 44d, false), ("새 판", 51d, false), ("자료", 44d, false) })
        {
            var chip = new Button { Content = label, Width = width, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), Margin = new(0, 0, 8, 0),
                Background = Brush(available ? "#EFF6FC" : "#FFFFFF"), BorderBrush = Brush(available ? "#005FB8" : "#D1D1D1"),
                Foreground = Brush(available ? "#1A1A1A" : "#5F5F5F"), Template = SurfaceTemplate(10), IsEnabled = available, Opacity = available ? 1 : .5 };
            if (!available) { chip.ToolTip = "현재 서버는 공지 분류를 지원하지 않습니다."; ToolTipService.SetShowOnDisabled(chip, true); AutomationProperties.SetHelpText(chip, "현재 서버는 공지 분류를 지원하지 않습니다."); }
            categories.Children.Add(chip);
        }
        var panel = new StackPanel(); body.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
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
        // Keep the custom title-bar close action interactive while the server is slow.
        // Disable duplicate requests without disabling the entire dialog.
        async Task LoadSafelyAsync()
        {
            if (loading) return;
            Status.Text = "";
            try { await LoadAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
        var refresh = Button("새로고침", LoadSafelyAsync); PublicDialogDesign.StyleAction(refresh); actions.Children.Add(refresh);
        var close = Button("닫기", () => { Close(); return Task.CompletedTask; }, true); PublicDialogDesign.StyleAction(close, true); actions.Children.Add(close);
        Loaded += async (_, _) => await LoadSafelyAsync();
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
        var dateText = new TextBlock { Text = date, FontSize = 12, Foreground = Brush("#5F5F5F"), VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 8, 0) };
        Grid.SetColumn(dateText, 1); header.Children.Add(dateText);
        var icon = new SvgViewbox { Width = 16, Height = 16, Stretch = Stretch.None, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 2); header.Children.Add(icon);
        var content = new Border { Background = Brushes.White, BorderBrush = Brush("#EAEAEA"), BorderThickness = new(1), Margin = new(12, 0, 12, 8), Padding = new(12, 8, 12, 8),
            Child = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap, FontSize = 12 } };
        var card = new Expander { Header = header, Content = content, IsExpanded = expanded, Margin = new(0, 0, 0, 8), Template = NoticeTemplate() };
        AutomationProperties.SetName(card, title);
        void UpdateIcon() => icon.Source = new Uri($"pack://application:,,,/Semojum.Feature.Public.Wpf;component/Assets/Figma/public-notice-chevron-{(card.IsExpanded ? "down" : "right")}.svg");
        card.Expanded += (_, _) => UpdateIcon(); card.Collapsed += (_, _) => UpdateIcon(); UpdateIcon();
        return card;
    }

    private static ControlTemplate NoticeTemplate() => (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Expander">
          <Border x:Name="Card" Background="White" BorderBrush="#EAEAEA" BorderThickness="1">
            <StackPanel>
              <ToggleButton IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" Content="{TemplateBinding Header}" HorizontalContentAlignment="Stretch" BorderThickness="0" Background="Transparent" Padding="0">
                <ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}"><ContentPresenter HorizontalAlignment="Stretch" /></Border></ControlTemplate></ToggleButton.Template>
              </ToggleButton>
              <ContentPresenter x:Name="Details" Content="{TemplateBinding Content}" Visibility="Collapsed" />
            </StackPanel>
          </Border>
          <ControlTemplate.Triggers><Trigger Property="IsExpanded" Value="True"><Setter TargetName="Card" Property="Background" Value="#EFF6FC"/><Setter TargetName="Card" Property="BorderBrush" Value="#005FB8"/><Setter TargetName="Details" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate>
        """);
}
public sealed class PublicInquiryDialog : CompactDialog
{
    public PublicInquiryDialog(SemojumApiClient api, string? initialTitle = null) : base("문의하기", 540, 528, popover: true)
    {
        var layout = PublicDialogDesign.Frame(this, "문의하기", "public-inquiry-close.svg", "PublicInquiryClose", out var actions, "Semojum.Feature.Public.Wpf"); Body(layout);
        var panel = new StackPanel { Margin = new(16, 12, 16, 0) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        panel.Children.Add(new TextBlock { Text = "로그인 없이도 보낼 수 있습니다. 적어 주신 메일 주소로 회신해드립니다.", Foreground = Brush("#5F5F5F"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        var type = Choices(["도입 문의", "오류 신고", "기타"], "기타"); var name = new TextBox { MaxLength = 50 }; var email = new TextBox { MaxLength = 100 };
        var initialType = type.SelectedItem;
        var organization = new TextBox { MaxLength = 2000 }; var title = new TextBox { MaxLength = 2000, Text = initialTitle ?? "" };
        var message = new TextBox { MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 92, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        void AddLabel(string label) => panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#5F5F5F"), FontSize = 12 });
        void AddField(string label, TextBox field, string hint)
        {
            AddLabel(label); var element = PublicDialogDesign.TextField(field, label == "기관명 (선택)" ? "기관명" : label == "제목 (선택)" ? "제목" : label, hint);
            element.Height = 32; panel.Children.Add(element);
        }
        AddLabel("분류"); type.Height = type.MinHeight = 32; type.Padding = new(11, 0, 25, 0); AutomationProperties.SetName(type, "분류");
        var typeField = new Grid { Height = 32 }; typeField.Children.Add(type);
        typeField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var chevron = DesignIcon("public-inquiry-chevron.svg", 16, 16, "Semojum.Feature.Public.Wpf"); chevron.HorizontalAlignment = HorizontalAlignment.Right; chevron.Margin = new(0, 0, 5, 0); chevron.IsHitTestVisible = false; typeField.Children.Add(chevron); panel.Children.Add(typeField);
        AddField("답 받을 주소", email, "name@example.com");
        // The legacy anonymous API still accepts an optional sender name. Keep it
        // alongside the organization field without displacing the remaining form.
        var identity = new Grid();
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new(2, GridUnitType.Star) });
        identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new(1, GridUnitType.Star) });
        void IdentityField(int column, string label, string accessibleName, TextBox field, string hint)
        {
            var group = new StackPanel { Margin = column == 0 ? new(0, 0, 4, 0) : new(4, 0, 0, 0) };
            group.Children.Add(new TextBlock { Text = label, Foreground = Brush("#5F5F5F"), FontSize = 12 });
            var entry = PublicDialogDesign.TextField(field, accessibleName, hint); entry.Height = 32; group.Children.Add(entry);
            Grid.SetColumn(group, column); identity.Children.Add(group);
        }
        IdentityField(0, "기관명 (선택)", "기관명", organization, "(선택) 기관명");
        IdentityField(1, "이름 (선택)", "이름 (선택)", name, "(선택) 이름");
        panel.Children.Add(identity); AddField("제목 (선택)", title, "문의 제목을 입력해 주세요");
        AddLabel("내용"); AutomationProperties.SetName(message, "내용"); message.Background = Brushes.White; message.BorderBrush = Brush("#EAEAEA"); message.Padding = new(11, 7, 11, 7); panel.Children.Add(message);
        var attachmentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        var attachmentContent = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        attachmentContent.Children.Add(DesignIcon("public-inquiry-upload.svg", 16, 16, "Semojum.Feature.Public.Wpf")); attachmentContent.Children.Add(new TextBlock { Text = "파일", Margin = new(8, 0, 0, 0) });
        var attachment = new Button { Content = attachmentContent, Width = 104, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new(0),
            Background = Brushes.White, BorderBrush = Brush("#D1D1D1"), Template = SurfaceTemplate(0), IsEnabled = false, Opacity = .5,
            ToolTip = "현재 공개 문의는 첨부 파일을 지원하지 않습니다." };
        ToolTipService.SetShowOnDisabled(attachment, true);
        AutomationProperties.SetAutomationId(attachment, "PublicInquiryAttachment"); AutomationProperties.SetName(attachment, "파일 첨부"); AutomationProperties.SetHelpText(attachment, "현재 공개 문의는 첨부 파일을 지원하지 않습니다.");
        attachmentRow.Children.Add(attachment); attachmentRow.Children.Add(new TextBlock { Text = "첨부 기능은 아직 지원되지 않습니다.", Foreground = Brush("#5F5F5F"), Margin = new(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap }); panel.Children.Add(attachmentRow);
        var consent = new CheckBox { Content = "문의 답변을 위한 개인정보 수집·이용에 동의합니다", Style = PublicDialogDesign.ConsentStyle("Semojum.Feature.Public.Wpf"), Margin = new(0, 4, 0, 0) };
        AutomationProperties.SetAutomationId(consent, "PublicInquiryConsent"); AutomationProperties.SetName(consent, "문의 답변을 위한 개인정보 수집·이용에 동의합니다"); panel.Children.Add(consent);
        panel.Children.Add(new TextBlock { Text = "기관명과 제목은 문의 내용에 함께 전달됩니다. 이름·이메일·문의 내용은 운영자에게 전달됩니다.", Foreground = Brush("#5F5F5F"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) });
        panel.Children.Add(new TextBlock { Text = "문의 이메일: contact@semo-jum.com", Foreground = Brush("#5F5F5F"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        var submitted = false; var discardConfirmed = false;
        Closing += (_, e) =>
        {
            if (e.Cancel || submitted || discardConfirmed || Equals(type.SelectedItem, initialType) && string.IsNullOrWhiteSpace(name.Text) && string.IsNullOrWhiteSpace(email.Text)
                && string.IsNullOrWhiteSpace(organization.Text) && string.Equals(title.Text.Trim(), (initialTitle ?? "").Trim(), StringComparison.Ordinal) && string.IsNullOrWhiteSpace(message.Text)) return;
            var choice = NoticeDialog.Choose(this, "작성 중인 문의를 닫으시겠습니까?", "계속 작성하면 입력한 내용을 유지합니다.", ["계속 작성", "닫기"], width: 440);
            if (choice != 1) e.Cancel = true; else discardConfirmed = true;
        };
        async Task SubmitAsync()
        {
            if (consent.IsChecked != true) throw new ArgumentException("문의 답변을 위한 개인정보 수집·이용에 동의해 주세요.");
            if (!MailAddress.TryCreate(email.Text.Trim(), out var address) || address.Address != email.Text.Trim() || string.IsNullOrWhiteSpace(message.Text)) throw new ArgumentException("이메일 주소와 문의 내용을 확인해 주세요.");
            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(organization.Text)) lines.Add("기관명: " + organization.Text.Trim());
            if (!string.IsNullOrWhiteSpace(title.Text)) lines.Add("제목: " + title.Text.Trim());
            var body = lines.Count == 0 ? message.Text.Trim() : string.Join("\n", lines) + "\n\n" + message.Text.Trim();
            if (body.Length > 2000) throw new ArgumentException("기관명·제목을 포함한 문의 내용은 2,000자까지 입력할 수 있습니다.");
            await api.SubmitInquiryAsync(new { type = type.SelectedIndex switch { 0 => "ONBOARDING", 1 => "ERROR_REPORT", _ => "ETC" }, name = name.Text.Trim(), email = address.Address, message = body, website = "" }, Lifetime.Token);
            submitted = true;
            MessageBox.Show(this, "문의를 접수했습니다.", "문의하기"); DialogResult = true;
        }
        var cancel = Button("취소", () => { Close(); return Task.CompletedTask; }); cancel.IsCancel = false; PublicDialogDesign.StyleAction(cancel); actions.Children.Add(cancel);
        var send = Button("보내기", () => RunAsync(SubmitAsync), true); PublicDialogDesign.StyleAction(send, true); actions.Children.Add(send);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Close(); } };
    }
}
