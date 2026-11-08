using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

/// <summary>
/// Figma Case 3 (555x226): an encrypted PDF the server refused with JOB4018, or JOB4019 when the password was wrong.
/// The password lives only in this PasswordBox until <see cref="TakePassword"/> hands it to the retried upload; it is never saved or logged.
/// </summary>
internal sealed class PdfPasswordDialog : NoticeDialog
{
    internal PasswordBox PasswordInput { get; } = new() { BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(11, 0, 11, 0),
        FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };

    internal PdfPasswordDialog(bool wrongPassword = false) : base(wrongPassword ? "문서 암호가 맞지 않습니다." : "암호가 걸린 PDF 입니다.",
        wrongPassword ? "문서 암호를 다시 입력해 주세요. 암호는 저장하지 않습니다." : "문서 암호를 입력하면 변환을 이어서 합니다. 암호는 저장하지 않습니다.",
        ["확인", "취소"], error: wrongPassword, width: 555, height: 226)
    {
        MessageBody.Children.Add(new TextBlock { Text = "문서 암호", FontSize = 12, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, -2, 0, 0) });
        var field = new Grid(); field.Children.Add(PasswordInput);
        var underline = new Border { Height = 1, Background = FigmaDialogElements.Brush("#D1D1D1"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        field.Children.Add(underline);
        PasswordInput.GotKeyboardFocus += (_, _) => { underline.Height = 2; underline.Background = FigmaDialogElements.Brush("#005FB8"); };
        PasswordInput.LostKeyboardFocus += (_, _) => { underline.Height = 1; underline.Background = FigmaDialogElements.Brush("#D1D1D1"); };
        MessageBody.Children.Add(new Border { Height = 32, Margin = new(0, 2, 0, 0), Background = Brushes.White, BorderBrush = FigmaDialogElements.Brush("#EAEAEA"),
            BorderThickness = new(1), CornerRadius = new(4), Child = field });
        AutomationProperties.SetName(PasswordInput, "문서 암호");
        // 확인 sends the password, so it waits for one; Enter and Escape keep the base dialog's default and cancel actions.
        ActionButtons[0].IsEnabled = false;
        PasswordInput.PasswordChanged += (_, _) => ActionButtons[0].IsEnabled = PasswordInput.Password.Length > 0;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    /// <summary>The typed password when 확인 was chosen, otherwise null. The box is cleared either way.</summary>
    internal string? TakePassword()
    {
        var password = Choice == 0 && PasswordInput.Password.Length > 0 ? PasswordInput.Password : null;
        PasswordInput.Clear();
        return password;
    }
}
