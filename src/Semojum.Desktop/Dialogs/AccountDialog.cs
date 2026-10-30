using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;
using Semojum.Core.Integration;
using Semojum.Desktop.Services;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class AccountDialog : Window
{
    public AccountDialog(SemojumApiClient api) : this(api, null, null) { }
    internal AccountDialog(SemojumApiClient api, IAccountCredentialStore? credentials, string? initialMessage, bool allowInstitution = false)
    {
        Title = "세모점 로그인"; Width = 1440; Height = 1000; MinWidth = 420; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.CanResize; WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 32, GlassFrameThickness = new(0), CornerRadius = new(0),
            ResizeBorderThickness = new(4), UseAeroCaptionButtons = false });
        Background = Brush("#F7F9FC");
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new(32) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        Content = root;
        var heading = new DockPanel { Background = Brush("#F3F3F3") };
        var captionButtons = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(captionButtons, Dock.Right); heading.Children.Add(captionButtons);
        void Caption(string asset, string label, Action action)
        {
            var button = new Button { Content = DesignIcon("login-caption-" + asset + ".svg", 16), Width = 46, Height = 32, MinWidth = 0, MinHeight = 0,
                Padding = new(0), Margin = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Style = (Style)FindResource("ToolButton"), ToolTip = label };
            AutomationProperties.SetName(button, label); AutomationProperties.SetAutomationId(button, "LoginCaption" + asset);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true); button.Click += (_, _) => action(); captionButtons.Children.Add(button);
        }
        Caption("min", "최소화", () => SystemCommands.MinimizeWindow(this));
        Caption("max", "최대화 / 복원", () => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); });
        Caption("close", "닫기", () => SystemCommands.CloseWindow(this));
        heading.Children.Add(new TextBlock { Text = "세모점", FontSize = 12, Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(new Border { Background = Brush("#F3F3F3"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 0, 0, 1), Child = heading });
        var screen = new Grid { Background = Brush("#F7F9FC") }; Grid.SetRow(screen, 1); root.Children.Add(screen);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center };
        screen.Children.Add(scroll);
        var group = new StackPanel { Width = 420, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new(12, 48, 12, 20) };
        scroll.Content = group;
        var card = new Border { Width = 420, Background = Brushes.White, BorderBrush = Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 8, Opacity = 0.18 } };
        group.Children.Add(card);
        var fields = new Grid(); card.Child = fields;
        foreach (var height in new[] { 43d, 96, 12, 40, 20, 16, 2, 40, 10, 16, 2, 40, 0, 18, 40, 8, 20, 37 })
            fields.RowDefinitions.Add(new RowDefinition { Height = height == 0 ? GridLength.Auto : new GridLength(height) });
        void Place(UIElement child, int row, bool inset = true)
        {
            Grid.SetRow(child, row); if (inset && child is FrameworkElement element) element.Margin = new(59, 0, 59, 0); fields.Children.Add(child);
        }
        var logo = DesignIcon("login-logo.svg", 96); logo.HorizontalAlignment = HorizontalAlignment.Center;
        AutomationProperties.SetName(logo, "세모점 로고"); Place(logo, 1, false);
        Place(new TextBlock { Text = "세모점", FontSize = 28, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center }, 3, false);
        Place(new TextBlock { Text = "아이디", FontSize = 12, Foreground = Brush("#5F5F5F") }, 5);
        var login = new TextBox { BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(11, 0, 11, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(login, "아이디"); Place(InputField(login), 7);
        Place(new TextBlock { Text = "비밀번호", FontSize = 12, Foreground = Brush("#5F5F5F") }, 9);
        var password = new PasswordBox { BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(11, 0, 11, 0), VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(password, "비밀번호"); Place(InputField(password), 11);
        var error = new TextBlock { Text = initialMessage ?? "", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Firebrick, Margin = new(59, 6, 59, 0) };
        error.Visibility = string.IsNullOrEmpty(error.Text) ? Visibility.Collapsed : Visibility.Visible;
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(error,
            (_, _) => error.Visibility = string.IsNullOrEmpty(error.Text) ? Visibility.Collapsed : Visibility.Visible);
        Place(error, 12, false);
        var submit = new Button { Content = "로그인", IsDefault = true, Height = 40, MinHeight = 40, FontSize = 14, FontWeight = FontWeights.Bold,
            Background = Brush("#005FB8"), BorderBrush = Brush("#005FB8"), Foreground = Brushes.White, Style = RoundedButtonStyle() };
        Place(submit, 14);
        var remember = new CheckBox { Content = "로그인 상태 유지", IsChecked = credentials is not null, IsEnabled = credentials is not null,
            Height = 20, Margin = new(59, 0, 59, 0), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, Style = RememberStyle() };
        AutomationProperties.SetName(remember, "로그인 상태 유지"); Place(remember, 16, false);
        if (credentials is null) remember.ToolTip = "이 창에서는 로그인 정보를 저장하지 않습니다.";
        var links = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 40, 0, 0) };
        Button Link(string label, string icon, Action action)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(DesignIcon(icon, 16)); content.Children.Add(new TextBlock { Text = label, FontSize = 14, Margin = new(8, 0, 0, 0) });
            var button = new Button { Content = content, Width = 180, Height = 40, MinHeight = 40, Background = Brushes.White,
                BorderBrush = Brush("#EAEAEA"), Style = RoundedButtonStyle() };
            AutomationProperties.SetName(button, label); button.Click += (_, _) => action(); return button;
        }
        links.Children.Add(Link("공지사항", "login-bell.svg", () => new PublicNoticesDialog(api) { Owner = this }.ShowDialog()));
        var inquiry = Link("문의하기", "login-mail.svg", () => new PublicInquiryDialog(api) { Owner = this }.ShowDialog()); inquiry.Margin = new(16, 0, 0, 0);
        links.Children.Add(inquiry); group.Children.Add(links);
        var version = new TextBlock { Text = "V" + MainWindow.CurrentVersion.ToString(3), FontSize = 12, Foreground = Brush("#8A8A8A"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 40, 20), IsHitTestVisible = false };
        screen.Children.Add(version);
        var lifetime = new CancellationTokenSource();
        Closed += (_, _) => { lifetime.Cancel(); password.Clear(); };
        submit.Click += async (_, _) =>
        {
            var keepSignedIn = remember.IsChecked == true;
            submit.IsEnabled = false; remember.IsEnabled = false; error.Text = "";
            try
            {
                SessionCredential? previous = null;
                if (credentials is not null && !keepSignedIn)
                {
                    try { previous = credentials.Read(); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
                    { error.Text = "저장된 로그인 정보를 확인하지 못했습니다. 다시 시도해 주세요."; return; }
                }
                var account = await api.LoginAsync(login.Text.Trim(), password.Password, lifetime.Token);
                password.Clear();
                if (lifetime.IsCancellationRequested)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    try { await api.LogoutAsync(timeout.Token); } catch (Exception logout) when (logout is HttpRequestException or SemojumApiException or OperationCanceledException) { }
                    return;
                }
                if (!(allowInstitution ? AutomaticLogin.IsDesktopAllowed(account) : AutomaticLogin.IsEditorAllowed(account)))
                {
                    await api.LogoutAsync(lifetime.Token);
                    error.Text = "이 계정은 에디터를 사용할 수 없습니다."; return;
                }
                if (credentials is not null && keepSignedIn)
                {
                    var storeError = await AutomaticLogin.RememberAsync(api, credentials);
                    if (storeError is not null) { error.Text = storeError; return; }
                }
                else if (credentials is not null && previous?.LoginId == account.LoginId)
                {
                    try { credentials.DeleteIfMatches(previous); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                        try { await api.LogoutAsync(timeout.Token); } catch (Exception logout) when (logout is HttpRequestException or SemojumApiException or OperationCanceledException) { }
                        error.Text = "기존 로그인 정보를 삭제하지 못해 로그인을 취소했습니다."; return;
                    }
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
            finally { submit.IsEnabled = true; remember.IsEnabled = credentials is not null; }
        };
        Loaded += (_, _) => { WindowBounds.Fit(this); login.Focus(); };
    }

    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static SvgViewbox DesignIcon(string name, double size) => new() { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}"),
        Width = size, Height = size, Stretch = Stretch.None, IsHitTestVisible = false };
    private static Border InputField(Control input)
    {
        var grid = new Grid(); grid.Children.Add(input);
        var underline = new Border { Height = 1, Background = Brush("#D1D1D1"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        grid.Children.Add(underline);
        input.GotKeyboardFocus += (_, _) => { underline.Height = 2; underline.Background = Brush("#005FB8"); };
        input.LostKeyboardFocus += (_, _) => { underline.Height = 1; underline.Background = Brush("#D1D1D1"); };
        return new Border { Background = Brush("#FBFBFB"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(4), Child = grid };
    }

    private static Style RoundedButtonStyle()
    {
        var style = new Style(typeof(Button), Application.Current.FindResource(typeof(Button)) as Style);
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "Surface");
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter); template.VisualTree = border;
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#1C5AA8"), "Surface")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, 0.45)); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }

    private static Style RememberStyle()
    {
        var style = new Style(typeof(CheckBox), Application.Current.FindResource(typeof(CheckBox)) as Style);
        var template = new ControlTemplate(typeof(CheckBox));
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var square = new FrameworkElementFactory(typeof(Border), "Square"); square.SetValue(WidthProperty, 20d); square.SetValue(HeightProperty, 20d);
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); square.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        square.SetValue(Border.BorderBrushProperty, Brush("#A0A0A0")); square.SetValue(Border.BackgroundProperty, Brushes.White);
        var icon = new FrameworkElementFactory(typeof(SvgViewbox), "Tick"); icon.SetValue(SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/login-check.svg"));
        icon.SetValue(WidthProperty, 16d); icon.SetValue(HeightProperty, 16d); icon.SetValue(VisibilityProperty, Visibility.Collapsed); square.AppendChild(icon); panel.AppendChild(square);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(MarginProperty, new Thickness(8, 0, 0, 0)); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(content);
        template.VisualTree = panel;
        var selected = new Trigger { Property = CheckBox.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#005FB8"), "Square")); selected.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#005FB8"), "Square"));
        selected.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible, "Tick")); template.Triggers.Add(selected);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Square")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, 0.45)); template.Triggers.Add(disabled);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
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
