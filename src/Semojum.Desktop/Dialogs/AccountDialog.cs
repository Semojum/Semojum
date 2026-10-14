using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class AccountDialog : Window
{
    public AccountDialog(SemojumApiClient api)
    {
        Title = "세모점 로그인"; Width = 420; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var fields = new StackPanel { Margin = new Thickness(24) }; Content = fields;
        var login = new TextBox { MinHeight = 32, Margin = new Thickness(0, 6, 0, 16) };
        var password = new PasswordBox { MinHeight = 32, Margin = new Thickness(0, 6, 0, 16) };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
        var submit = new Button { Content = "로그인", IsDefault = true, MinHeight = 36 };
        fields.Children.Add(new TextBlock { Text = "아이디" }); fields.Children.Add(login);
        fields.Children.Add(new TextBlock { Text = "비밀번호" }); fields.Children.Add(password);
        fields.Children.Add(error); fields.Children.Add(submit);
        fields.Children.Add(new TextBlock { Text = "로그인 정보는 앱을 종료하면 지워집니다.", Margin = new Thickness(0, 14, 0, 0) });
        var lifetime = new CancellationTokenSource();
        Closed += (_, _) => { lifetime.Cancel(); password.Clear(); };
        submit.Click += async (_, _) =>
        {
            submit.IsEnabled = false; error.Text = "";
            try
            {
                var account = await api.LoginAsync(login.Text.Trim(), password.Password, lifetime.Token);
                password.Clear();
                if (account.Role == "ROLE_ORG_ADMIN" || account is { Role: "ROLE_ADMIN", AdminScope: "WEB" })
                {
                    await api.LogoutAsync(lifetime.Token);
                    error.Text = "이 계정은 에디터를 사용할 수 없습니다."; return;
                }
                DialogResult = true;
            }
            catch (OperationCanceledException) { }
            catch (SemojumApiException ex)
            {
                error.Text = ex.Message;
                if (ex.Details is { ValueKind: System.Text.Json.JsonValueKind.Object } d)
                {
                    if (d.TryGetProperty("remainingAttempts", out var remaining)) error.Text += $"\n남은 시도: {remaining}";
                    if (d.TryGetProperty("unlockAt", out var unlock)) error.Text += $"\n다시 시도할 수 있는 시각: {unlock}";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or ArgumentException) { error.Text = ex.Message; }
            catch (Exception) { error.Text = "로그인 응답을 처리하지 못했습니다. 다시 시도해 주세요."; }
            finally { submit.IsEnabled = true; }
        };
        Loaded += (_, _) => login.Focus();
    }
}

public sealed class TextValueDialog : EditorDialog
{
    private readonly TextBox input;
    public string Value => input.Text;
    public TextValueDialog(string title, string label, string value = "", int maxLength = 0) : base(title)
    {
        input = new TextBox { Text = value, MaxLength = maxLength }; Row(label, input);
    }
    protected override bool Apply() => true;
}
