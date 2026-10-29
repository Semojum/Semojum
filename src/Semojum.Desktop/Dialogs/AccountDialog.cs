using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core.Integration;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

public sealed class AccountDialog : Window
{
    public AccountDialog(SemojumApiClient api) : this(api, null, null) { }
    internal AccountDialog(SemojumApiClient api, IAccountCredentialStore? credentials, string? initialMessage)
    {
        Title = "세모점 로그인"; Width = 420; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var fields = new StackPanel { Margin = new Thickness(24) }; Content = fields;
        var login = new TextBox { MinHeight = 32, Margin = new Thickness(0, 6, 0, 16) };
        var password = new PasswordBox { MinHeight = 32, Margin = new Thickness(0, 6, 0, 16) };
        var error = new TextBlock { Text = initialMessage ?? "", TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick };
        var submit = new Button { Content = "로그인", IsDefault = true, MinHeight = 36 };
        fields.Children.Add(new TextBlock { Text = "아이디" }); fields.Children.Add(login);
        fields.Children.Add(new TextBlock { Text = "비밀번호" }); fields.Children.Add(password);
        fields.Children.Add(error); fields.Children.Add(submit);
        fields.Children.Add(new TextBlock { Text = credentials is null ? "로그인 정보는 앱을 종료하면 지워집니다." : "자동 로그인을 위해 이 Windows 계정에 로그인 정보가 안전하게 저장됩니다.", Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap });
        var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 12, 0, 0) };
        links.Children.Add(CompactDialog.Button("공지사항", () => { new PublicNoticesDialog(api) { Owner = this }.ShowDialog(); return Task.CompletedTask; }));
        links.Children.Add(CompactDialog.Button("문의하기", () => { new PublicInquiryDialog(api) { Owner = this }.ShowDialog(); return Task.CompletedTask; })); fields.Children.Add(links);
        var lifetime = new CancellationTokenSource();
        Closed += (_, _) => { lifetime.Cancel(); password.Clear(); };
        submit.Click += async (_, _) =>
        {
            submit.IsEnabled = false; error.Text = "";
            try
            {
                var account = await api.LoginAsync(login.Text.Trim(), password.Password, lifetime.Token);
                password.Clear();
                if (!AutomaticLogin.IsEditorAllowed(account))
                {
                    await api.LogoutAsync(lifetime.Token);
                    error.Text = "이 계정은 에디터를 사용할 수 없습니다."; return;
                }
                if (credentials is not null)
                {
                    var storeError = await AutomaticLogin.RememberAsync(api, credentials);
                    if (storeError is not null) { error.Text = storeError; return; }
                }
                DialogResult = true;
            }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            { error.Text = "서버 응답이 지연되었습니다. 연결 상태를 확인하고 다시 시도해 주세요."; }
            catch (OperationCanceledException) { }
            catch (SemojumApiException ex)
            {
                if (ex.Code is "AUTH4001" or "AUTH4006")
                {
                    var rejected = FileNotices.LoginRejected(ex); rejected.Owner = this; rejected.ShowDialog();
                    if (rejected.Choice == 1) new PublicInquiryDialog(api) { Owner = this }.ShowDialog();
                    password.Focus(); password.SelectAll();
                    return;
                }
                error.Text = ex.Message;
                if (ex.Details is { ValueKind: System.Text.Json.JsonValueKind.Object } d)
                {
                    if (d.TryGetProperty("remainingAttempts", out var remaining)) error.Text += $"\n남은 시도: {remaining}";
                    if (d.TryGetProperty("unlockAt", out var unlock)) error.Text += $"\n다시 시도할 수 있는 시각: {unlock}";
                }
            }
            catch (HttpRequestException) { error.Text = "서버에 연결하지 못했습니다. 인터넷 연결 상태를 확인하고 다시 시도해 주세요."; }
            catch (ArgumentException ex) { error.Text = ex.Message; }
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
