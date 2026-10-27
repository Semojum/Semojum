using System.Net.Mail;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class PublicNoticesDialog : CompactDialog
{
    public PublicNoticesDialog(SemojumApiClient api) : base("공지사항", 620, 440)
    {
        var panel = new StackPanel { Margin = new(16) };
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Action("닫기", Close);
        OnLoad(async () =>
        {
            var result = await api.GetPublicAsync("/api/public/notices", Lifetime.Token);
            if (result.GetArrayLength() == 0) panel.Children.Add(new TextBlock { Text = "현재 표시할 공지가 없습니다." });
            foreach (var notice in result.EnumerateArray())
            {
                panel.Children.Add(new TextBlock { Text = notice.GetProperty("title").GetString(), FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 6) });
                panel.Children.Add(new TextBlock { Text = notice.GetProperty("body").GetString(), TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) });
            }
        });
    }
}
public sealed class PublicInquiryDialog : CompactDialog
{
    public PublicInquiryDialog(SemojumApiClient api) : base("문의하기", 560, 480)
    {
        var panel = new StackPanel { Margin = new(16) }; Body(panel);
        var type = Choices(["도입 문의", "오류 신고", "기타"], "기타"); var name = new TextBox { MaxLength = 50 }; var email = new TextBox { MaxLength = 100 };
        var message = new TextBox { MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Row(panel, "문의 유형", type); Row(panel, "이름 (선택)", name); Row(panel, "답변받을 이메일", email); panel.Children.Add(message);
        panel.Children.Add(new TextBlock { Text = "이름·이메일·문의 내용이 운영자에게 전달됩니다. 문의 이메일: contact@semo-jum.com", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        Action("문의 보내기", async () =>
        {
            if (!MailAddress.TryCreate(email.Text.Trim(), out var address) || address.Address != email.Text.Trim() || string.IsNullOrWhiteSpace(message.Text)) throw new ArgumentException("이메일 주소와 문의 내용을 확인해 주세요.");
            await api.SubmitInquiryAsync(new { type = type.SelectedIndex switch { 0 => "ONBOARDING", 1 => "ERROR_REPORT", _ => "ETC" }, name = name.Text.Trim(), email = address.Address, message = message.Text.Trim(), website = "" }, Lifetime.Token);
            MessageBox.Show(this, "문의를 접수했습니다.", "문의하기"); DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
    }
}
