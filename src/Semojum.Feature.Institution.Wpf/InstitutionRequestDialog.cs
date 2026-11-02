using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace Semojum.Desktop.Dialogs;

/// <summary>The server receives a request for operator review, not an immediate account or credit change.</summary>
internal sealed class InstitutionRequestDialog : CompactDialog
{
    internal TextBox First { get; } = new();
    internal TextBox Second { get; } = new();
    internal ComboBox Option { get; } = new();
    internal Button Submit { get; }
    internal TextBlock EstimateAmount { get; } = new() { Text = "—", FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock EstimateNote { get; } = new() { Text = "부가세 포함", FontSize = 12, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    /// <summary>Typing waits this long before asking for an estimate, so each keystroke does not send a request.</summary>
    internal static readonly TimeSpan EstimateDelay = TimeSpan.FromMilliseconds(300);
    private readonly Func<long, CancellationToken, Task<InstitutionCreditEstimate>>? estimate;
    private CancellationTokenSource? estimateRun;
    internal Task EstimateTask { get; private set; } = Task.CompletedTask;

    /// <summary>Submits the request window's fields; a credit request shows the server's estimate for the typed amount.</summary>
    internal InstitutionRequestDialog(bool accountRequest, Func<string?, InstitutionRequestDetails, Task> submit, Func<long, CancellationToken, Task<InstitutionCreditEstimate>>? estimate = null)
        : base(accountRequest ? "계정 발급 요청" : "크레딧 추가 요청", 520, accountRequest ? 344 : 288, popover: true)
    {
        this.estimate = estimate;
        var title = accountRequest ? "계정 발급 요청" : "크레딧 추가 요청";
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); frame.Background = FigmaDialogElements.Brush("#F3F3F3"); }
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(55) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Right, Content = FigmaDialogElements.DesignIcon(accountRequest ? "institution-account-request-close.svg" : "institution-credit-request-close.svg", 16, 16, "Semojum.Feature.Institution.Wpf"), Template = FigmaDialogElements.SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close); layout.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 15, 24, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        body.Children.Add(new TextBlock { Text = accountRequest ? "새 계정" : "요청할 양", Foreground = FigmaDialogElements.Brush("#5F5F5F"), FontSize = 12, Margin = new(0, 0, 0, 4) });
        var form = new Grid { Margin = new(16, 12, 16, 4) }; form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        for (var i = 0; i < 3; i++) form.RowDefinitions.Add(new() { Height = new(36) });
        body.Children.Add(new Border { Height = 124, Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Child = form });
        void Field(int row, string label, FrameworkElement field)
        {
            var caption = new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }; Grid.SetRow(caption, row); form.Children.Add(caption);
            field.Height = 32; field.HorizontalAlignment = HorizontalAlignment.Stretch; AutomationProperties.SetName(field, label); Grid.SetRow(field, row); Grid.SetColumn(field, 1); form.Children.Add(field);
        }
        // The server accepts a 50-character alias and a 100-character recipient address.
        First.MaxLength = 50; Second.MaxLength = 100;
        if (accountRequest)
        {
            Field(0, "별칭", First); Field(1, "받을 사람 메일", Second);
            Option.ItemsSource = new[] { "기관 할당 공유", "계정별 한도 지정", "크레딧 없음 (읽기만)" }; Option.SelectedIndex = 0; Field(2, "쓸 수 있는 크레딧", Option);
            body.Children.Add(new TextBlock { Text = "요청하면 운영자 확인 뒤 메일로 첫 비밀번호가 갑니다.", Foreground = FigmaDialogElements.Brush("#8A8A8A"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 24, 0, 0) });
        }
        else
        {
            var number = new Grid { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
            number.ColumnDefinitions.Add(new()); number.ColumnDefinitions.Add(new() { Width = new(22) });
            AutomationProperties.SetName(First, "크레딧"); number.Children.Add(First);
            var spin = new Grid(); spin.RowDefinitions.Add(new()); spin.RowDefinitions.Add(new()); Grid.SetColumn(spin, 1); number.Children.Add(spin);
            void AddSpin(int row, int delta, string label, string asset, double width, double height)
            {
                var icon = FigmaDialogElements.DesignIcon(asset, width, height, "Semojum.Feature.Institution.Wpf");
                if (delta > 0) { icon.RenderTransformOrigin = new(.5, .5); icon.RenderTransform = new ScaleTransform(1, -1); }
                var button = new Button { Content = icon, Height = 15, MinHeight = 0, MinWidth = 0, Padding = new(0), Margin = new(0), Background = FigmaDialogElements.Brush("#F0F0F0"), BorderBrush = Brushes.Transparent, Template = FigmaDialogElements.SurfaceTemplate(0) };
                AutomationProperties.SetName(button, label); button.Click += (_, _) =>
                {
                    var parsed = int.TryParse(First.Text.Replace(",", "").Trim(), out var value) && value > 0 ? value : 0;
                    First.Text = Math.Clamp((long)parsed + delta, 1, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
                };
                Grid.SetRow(button, row); spin.Children.Add(button);
            }
            AddSpin(0, 1, "크레딧 1 증가", "institution-credit-spin-up.svg", 9.60063, 5.19934);
            AddSpin(1, -1, "크레딧 1 감소", "institution-credit-spin-down.svg", 16, 16);
            Field(0, "크레딧", number); number.HorizontalAlignment = HorizontalAlignment.Left;
            // 「₩780,000 · 부가세 포함」 comes from the server; until it answers for the typed amount the field shows 「—」.
            var price = new StackPanel { Orientation = Orientation.Horizontal }; price.Children.Add(EstimateAmount); price.Children.Add(EstimateNote);
            Field(1, "예상 금액", price); price.HorizontalAlignment = HorizontalAlignment.Left;
            First.TextChanged += (_, _) => EstimateTask = EstimateAsync();
            Closed += (_, _) => estimateRun?.Cancel();
            Option.ItemsSource = new[] { "기관 명의 발행", "개인 명의 발행", "발행 안 함" }; Option.SelectedIndex = 0; Field(2, "세금계산서", Option);
        }
        Option.Width = 240; Option.HorizontalAlignment = HorizontalAlignment.Left; Option.ItemContainerStyle = ChoiceItemStyle();
        Option.Loaded += (_, _) =>
        {
            Option.ApplyTemplate();
            if (Option.Template.FindName("PART_Popup", Option) is Popup { Child: Border surface })
            { surface.Background = FigmaDialogElements.Brush("#FBFBFB"); surface.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); surface.CornerRadius = new(6); surface.Padding = new(3, 6, 3, 6);
                surface.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 8, Direction = 270, Opacity = .14 }; }
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 0) }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var cancel = Button("취소", () => { Close(); return Task.CompletedTask; }); cancel.Width = 88; cancel.Height = 32; actions.Children.Add(cancel);
        Submit = Button("요청 보내기", async () => await RunAsync(async () =>
        {
            // The window's choices are sent as request fields, not as text in the message.
            string? message = null; InstitutionRequestDetails details;
            if (accountRequest)
            {
                if (string.IsNullOrWhiteSpace(First.Text) || !InstitutionClient.IsReceiptEmail(Second.Text.Trim())) throw new ArgumentException("별칭과 올바른 이메일을 입력해 주세요.");
                // The server has no value for 「계정별 한도 지정」 (the limit is set later in 계정 상세), so that choice travels as a note.
                var scope = Option.SelectedIndex switch { 0 => "SHARED", 2 => "NONE", _ => null };
                if (scope is null) message = $"쓸 수 있는 크레딧: {Option.SelectedItem}";
                details = new(AccountAlias: First.Text.Trim(), RecipientEmail: Second.Text.Trim(), CreditScope: scope);
            }
            else
            {
                if (Credits() is not { } amount) throw new ArgumentException("요청할 크레딧을 양의 정수로 입력해 주세요.");
                details = new(CreditAmount: amount, InvoiceType: Option.SelectedIndex switch { 1 => "PERSONAL", 2 => "NONE", _ => "ORG" });
            }
            await submit(message, details); DialogResult = true;
        }), true); Submit.Width = 110; Submit.Height = 32; actions.Children.Add(Submit);
    }
    private long? Credits() => int.TryParse(First.Text.Replace(",", "").Trim(), out var amount) && amount > 0 ? amount : null;
    /// <summary>Waits for typing to pause, cancels the previous estimate and shows only the answer for the current amount; any failure leaves 「—」.</summary>
    private async Task EstimateAsync()
    {
        estimateRun?.Cancel();
        var run = estimateRun = new CancellationTokenSource();
        if (estimate is null || Lifetime.IsCancellationRequested || Credits() is not { } credits) { ShowEstimate(null); return; }
        try
        {
            await Task.Delay(EstimateDelay, run.Token);
            var value = await estimate(credits, run.Token);
            if (!run.IsCancellationRequested) ShowEstimate(value.Credits == credits && value.AmountKrw >= 0 ? value.AmountKrw : null);
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested) { }
        catch (Exception) { if (!run.IsCancellationRequested) ShowEstimate(null); }
    }
    private void ShowEstimate(long? amountKrw)
    {
        EstimateAmount.Text = amountKrw is { } amount ? "₩" + amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "—";
        EstimateNote.Visibility = amountKrw is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private static Style ChoiceItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(HeightProperty, 32d)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 0, 12, 0)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, FigmaDialogElements.Brush("#1A1A1A")));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = border }));
        foreach (var property in new[] { ComboBoxItem.IsSelectedProperty, ComboBoxItem.IsHighlightedProperty })
        {
            var trigger = new Trigger { Property = property, Value = true }; trigger.Setters.Add(new Setter(Control.BackgroundProperty, FigmaDialogElements.Brush("#F0F0F0"))); style.Triggers.Add(trigger);
        }
        return style;
    }
}
