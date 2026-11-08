using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core.Integration;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

/// <summary>Institution administrator's top-level desktop window 「기관 관리 - 누리닷」; administrators do not use the editor.</summary>
public sealed partial class InstitutionWindow : CompactDialog
{
    private Window ModalOwner => Window.GetWindow(Root) ?? this;
    private readonly SemojumApiClient api;
    private readonly Guid? institutionSessionMarker;
    private readonly Func<Task<bool>> logout;
    private bool loggingOut;
    public bool LogoutRequested { get; private set; }
    internal TextBlock Identity { get; } = new() { Foreground = FigmaDialogElements.Brush("#5F5F5F") };
    internal TextBlock Allocated { get; } = new() { Text = "—", FontSize = 28, FontWeight = FontWeights.Bold };
    internal TextBlock Balance { get; } = new() { Text = "—", FontSize = 28, FontWeight = FontWeights.Bold };
    internal TextBlock Contract { get; } = new() { Text = "—", FontSize = 28, FontWeight = FontWeights.Bold };
    internal TextBlock ContractDetail { get; } = new() { Text = "계약 정보 없음", Foreground = FigmaDialogElements.Brush("#5F5F5F") };
    internal TextBlock ContractRemaining { get; } = new() { Text = "정보 없음", Foreground = FigmaDialogElements.Brush("#C42B1C"), FontSize = 14, FontWeight = FontWeights.Bold };
    internal TextBlock CreditDetail { get; } = new() { Text = "사용량 정보 없음", Foreground = FigmaDialogElements.Brush("#5F5F5F") };
    internal TextBlock UsagePercent { get; } = new() { Text = "정보 없음", Foreground = FigmaDialogElements.Brush("#5F5F5F"), HorizontalAlignment = HorizontalAlignment.Right };
    internal TextBlock ErrorText { get; } = new() { Foreground = FigmaDialogElements.Brush("#C42B1C"), TextWrapping = TextWrapping.Wrap };
    internal ProgressBar UsageBar { get; } = new() { Minimum = 0, Maximum = 100, Height = 8, Template = UsageBarTemplate() };
    internal DataGrid Accounts { get; }
    internal DataGrid Orders { get; }
    internal DataGrid Notices { get; }
    internal TextBlock PendingRequests { get; } = new() { Foreground = FigmaDialogElements.Brush("#5F5F5F"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    internal TextBox ReceiptAddress { get; } = new() { Width = 220, Padding = new(11, 0, 11, 0), MaxLength = 254, IsEnabled = false, Background = Brushes.White, BorderBrush = FigmaDialogElements.Brush("#D1D1D1"), HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock ReceiptHint { get; } = new() { Text = "이메일 주소 형식을 확인해 주세요. 예: account@kblib.or.kr", Foreground = FigmaDialogElements.Brush("#C42B1C"), Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
    internal TextBlock SessionIdentity { get; } = new() { Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center };
    internal TextBlock StatusSummary { get; } = new() { Text = "기관 정보 없음", Margin = new(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    internal StackPanel Chart { get; } = new() { Orientation = Orientation.Horizontal };
    internal TextBlock ChartEmpty { get; } = new() { Text = "월별 사용량 정보 없음", Foreground = FigmaDialogElements.Brush("#5F5F5F"), TextWrapping = TextWrapping.Wrap };
    internal TextBlock ChartMaximum { get; } = new() { Foreground = FigmaDialogElements.Brush("#8A8A8A"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    internal TextBlock ChartMiddle { get; } = new() { Foreground = FigmaDialogElements.Brush("#8A8A8A"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    internal TextBlock ChartLower { get; } = new() { Foreground = FigmaDialogElements.Brush("#8A8A8A"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    internal TextBlock ChartAverage { get; } = new() { Text = "6개월 평균 정보 없음", Foreground = FigmaDialogElements.Brush("#5F5F5F"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Right };
    internal ScrollViewer ChartScroll { get; } = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal Grid ChartArea { get; } = new();
    private Border? chartCard;
    private readonly Border chartLegendDot = new() { Width = 8, Height = 8, CornerRadius = new(4), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0) };
    private readonly TextBlock chartLegendText = new() { Text = "월 사용 크레딧", FontSize = 12 };
    private bool chartHighContrast;
    internal Button AccountRequest { get; }
    internal Button CreditRequest { get; }
    internal MenuItem LogoutItem { get; }
    internal MenuItem? MyPageItem { get; }

    public InstitutionWindow(SemojumApiClient api, Func<Task<bool>> logout, Action<InstitutionWindow>? openMyPage = null) : base("기관 관리 - 누리닷", 1440, 1000, editor: true)
    {
        this.api = api; this.logout = logout; institutionSessionMarker = api.SessionMarker;
        MinWidth = 480; MinHeight = 360; Root.Background = Brushes.White; ApplyCaption();
        var menu = new Menu { Height = 32, Padding = new(4, 0, 0, 0) };
        var file = new MenuItem { Header = "파일(_F)", Width = 64 }; menu.Items.Add(file);
        LogoutItem = new MenuItem { Header = "로그아웃" }; LogoutItem.Click += async (_, _) => await LogoutAsync(); file.Items.Add(LogoutItem);
        var exit = new MenuItem { Header = "닫기" }; exit.Click += (_, _) => Close(); file.Items.Add(exit);
        menu.Items.Add(new MenuItem { Header = "보기(_V)", Width = 64 });
        var information = new MenuItem { Header = "정보(_A)", Width = 64 }; menu.Items.Add(information);
        var help = new MenuItem { Header = "도움말" }; help.Click += (_, _) => CommonWindowServices.ShowHelp(ModalOwner, "사용량과 작업 이력"); information.Items.Add(help);
        // Like the editor's 정보 menu (D1), my page sits below help; its 기관 관리 section returns here.
        if (openMyPage is not null)
        {
            information.Items.Add(new Separator { Style = TryFindResource("DesktopMenuSeparator") as Style });
            MyPageItem = new MenuItem { Header = "마이페이지 · 사용량" }; MyPageItem.Click += (_, _) => openMyPage(this); information.Items.Add(MyPageItem);
        }
        Root.PreviewKeyDown += async (_, e) =>
        {
            // The final page has no refresh button; F5 keeps the manual reload.
            if (e.Key == System.Windows.Input.Key.F5) { e.Handled = true; await RefreshAsync(); return; }
            if (e.Key != System.Windows.Input.Key.F1 || ReferenceEquals(ModalOwner, this)) return;
            CommonWindowServices.ShowHelp(ModalOwner, "사용량과 작업 이력"); e.Handled = true;
        };
        DockPanel.SetDock(menu, Dock.Top); Root.Children.Add(menu);
        SessionIdentity.Text = api.Account?.LoginId ?? "로그인 정보 없음";
        var statusLine = new DockPanel(); DockPanel.SetDock(SessionIdentity, Dock.Right); statusLine.Children.Add(SessionIdentity); statusLine.Children.Add(StatusSummary);
        var status = new Border { Height = 28, Background = FigmaDialogElements.Brush("#F3F3F3"), BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0), Child = statusLine };
        DockPanel.SetDock(status, Dock.Bottom); Root.Children.Add(status);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = FigmaDialogElements.Brush("#F4F6F9") }; Body(scroll);
        var content = new StackPanel { Margin = new(24, 16, 24, 24) }; scroll.Content = content;
        var heading = new DockPanel { Margin = new(0, 0, 0, 12) }; heading.Children.Add(new TextBlock { Text = "기관 관리", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new(0, 0, 12, 0) }); heading.Children.Add(Identity); content.Children.Add(heading);
        content.Children.Add(ErrorText);
        var summary = new Grid { Margin = new(0, 0, 0, 16) }; for (var i = 0; i < 3; i++) summary.ColumnDefinitions.Add(new()); content.Children.Add(summary);
        CreditRequest = Button("＋ 크레딧 추가 요청", () => RequestCreditAsync()); CreditRequest.Height = 32; CreditRequest.IsEnabled = false; CreditRequest.ToolTip = "기관 정보를 불러온 뒤 요청할 수 있습니다.";
        CreditRequest.Background = CreditRequest.BorderBrush = FigmaDialogElements.Brush("#E8730C"); CreditRequest.Foreground = Brushes.White; CreditRequest.Template = FigmaDialogElements.SurfaceTemplate(6);
        var creditCard = SummaryCard("남은 크레딧", Balance, CreditRequest); creditCard.MinHeight = 128; creditCard.Margin = new(8, 0, 8, 0); Grid.SetColumn(creditCard, 1);
        var allocatedContent = new StackPanel(); allocatedContent.Children.Add(Allocated); allocatedContent.Children.Add(UsageBar);
        var creditLine = new DockPanel(); DockPanel.SetDock(UsagePercent, Dock.Right); creditLine.Children.Add(UsagePercent); creditLine.Children.Add(CreditDetail); allocatedContent.Children.Add(creditLine);
        summary.Children.Add(SummaryCard("할당 크레딧", allocatedContent)); summary.Children.Add(creditCard);
        var contractContent = new StackPanel(); contractContent.Children.Add(Contract); contractContent.Children.Add(ContractRemaining); contractContent.Children.Add(ContractDetail); var contractCard = SummaryCard("계약", contractContent); Grid.SetColumn(contractCard, 2); summary.Children.Add(contractCard);
        var middle = new Grid { Margin = new(0, 0, 0, 16) }; middle.ColumnDefinitions.Add(new() { Width = new(724, GridUnitType.Star) }); middle.ColumnDefinitions.Add(new() { Width = new(652, GridUnitType.Star) }); content.Children.Add(middle);
        var graph = new StackPanel();
        ChartArea.ColumnDefinitions.Add(new() { Width = new(46) }); ChartArea.ColumnDefinitions.Add(new());
        var axis = new Grid { Height = 132, VerticalAlignment = VerticalAlignment.Top };
        axis.RowDefinitions.Add(new()); axis.RowDefinitions.Add(new()); axis.RowDefinitions.Add(new());
        axis.Children.Add(ChartMaximum); Grid.SetRow(ChartMiddle, 1); axis.Children.Add(ChartMiddle); Grid.SetRow(ChartLower, 2); axis.Children.Add(ChartLower);
        ChartArea.Children.Add(axis); ChartScroll.Content = Chart; Grid.SetColumn(ChartScroll, 1); ChartArea.Children.Add(ChartScroll); graph.Children.Add(ChartArea); graph.Children.Add(ChartEmpty);
        ChartScroll.SizeChanged += (_, _) => SizeChartColumns();
        var legend = new Grid { Margin = new(0, 6, 0, 0) };
        legend.ColumnDefinitions.Add(new()); legend.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        legend.RowDefinitions.Add(new() { Height = GridLength.Auto }); legend.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var legendText = new StackPanel { Orientation = Orientation.Horizontal };
        legendText.Children.Add(chartLegendDot); legendText.Children.Add(chartLegendText);
        legend.Children.Add(legendText); Grid.SetColumn(ChartAverage, 1); legend.Children.Add(ChartAverage);
        legend.SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 245;
            Grid.SetColumn(ChartAverage, narrow ? 0 : 1); Grid.SetRow(ChartAverage, narrow ? 1 : 0); Grid.SetColumnSpan(ChartAverage, narrow ? 2 : 1);
        };
        graph.Children.Add(legend);
        var graphCard = Card("월별 사용 추이", graph); chartCard = graphCard; graphCard.Margin = new(0, 0, 8, 0); graphCard.MinHeight = 230; middle.Children.Add(graphCard);
        ApplyChartContrast(SystemParameters.HighContrast);
        System.ComponentModel.PropertyChangedEventHandler contrastChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.BeginInvoke(new Action(() => ApplyChartContrast(SystemParameters.HighContrast)));
        };
        SystemParameters.StaticPropertyChanged += contrastChanged; Closed += (_, _) => SystemParameters.StaticPropertyChanged -= contrastChanged;
        Notices = Table(("받은 날", "CreatedAt"), ("제목", "Title")); Notices.Columns.Insert(1, StatusColumn("분류", "Scope", "notice"));
        OpenOnClick<InstitutionNotice>(Notices, OpenNoticeAsync);
        var noticeCard = Card("공지", Notices); noticeCard.Margin = new(8, 0, 0, 0); noticeCard.MinHeight = 230; Grid.SetColumn(noticeCard, 1); middle.Children.Add(noticeCard);
        AccountRequest = Button("＋ 계정 발급 요청", () => RequestAccountAsync()); AccountRequest.Width = 150; AccountRequest.Height = 32; AccountRequest.IsEnabled = false; AccountRequest.ToolTip = "기관 정보를 불러온 뒤 요청할 수 있습니다.";
        Accounts = Table(("계정 ID", "LoginId"), ("별칭", "Alias"), ("마지막 로그인", "LastLoginAt"), ("사용", "UsedCredits")); Accounts.Columns.Insert(2, StatusColumn("상태", "Status", "account"));
        ((DataGridTextColumn)Accounts.Columns[3]).Binding = new Binding("LastLoginAt") { Converter = new InstitutionLastSeenConverter() };
        ((DataGridTextColumn)Accounts.Columns[4]).Binding = new Binding("UsedCredits") { StringFormat = "N0", TargetNullValue = "—" };
        var loginId = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle); loginId.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d)); loginId.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        loginId.Setters.Add(new Setter(TextBlock.ForegroundProperty, FigmaDialogElements.Brush("#005FB8")));
        var requestLine = new DataTrigger { Binding = new Binding(nameof(InstitutionAccountRow.Account)), Value = null }; requestLine.Setters.Add(new Setter(TextBlock.ForegroundProperty, FigmaDialogElements.Brush("#5F5F5F"))); loginId.Triggers.Add(requestLine);
        ((DataGridTextColumn)Accounts.Columns[0]).ElementStyle = loginId;
        var accountAction = new FrameworkElementFactory(typeof(Button)); accountAction.SetBinding(ContentProperty, new Binding("Control")); accountAction.SetValue(HeightProperty, 20d); accountAction.SetValue(MinHeightProperty, 0d); accountAction.SetValue(MinWidthProperty, 0d);
        accountAction.SetValue(PaddingProperty, new Thickness(8, 0, 8, 0)); accountAction.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        accountAction.SetBinding(VisibilityProperty, new Binding("Control") { Converter = new InstitutionReceiptVisibilityConverter() });
        accountAction.SetBinding(AutomationProperties.NameProperty, new MultiBinding { StringFormat = "{0} {1}", Bindings = { new Binding("LoginId"), new Binding("Control") } });
        accountAction.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(async (sender, e) => { e.Handled = true; if (sender is FrameworkElement { DataContext: InstitutionAccountRow row }) await RunRowControlAsync(row); }));
        Accounts.Columns.Add(new DataGridTemplateColumn { Header = "제어", Width = 96, CanUserSort = false, CellTemplate = new DataTemplate { VisualTree = accountAction } });
        OpenOnClick<InstitutionAccountRow>(Accounts, row => row.Account is { } account ? OpenAccountAsync(account) : Task.CompletedTask);
        // The open-request count and the request button sit on the card's title line.
        PendingRequests.Margin = new(0, 0, 12, 0);
        var accountTools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, -28, 0, 6) };
        accountTools.Children.Add(PendingRequests); accountTools.Children.Add(AccountRequest);
        var accountsCard = Card("소속 계정", accountTools, Accounts); accountsCard.MinHeight = 232; accountsCard.Margin = new(0, 0, 0, 16); content.Children.Add(accountsCard);
        Orders = Table(("일자", "OrderDate"), ("내용", "Description"), ("금액", "AmountKrw"), ("계산서", "InvoiceStatus")); Orders.Columns.Insert(3, StatusColumn("결제", "PaidAt", "payment"));
        ((DataGridTextColumn)Orders.Columns[^1]).Binding = new Binding("InvoiceStatus") { Converter = new InstitutionStatusTextConverter(), ConverterParameter = "invoice" };
        var receiptCell = new FrameworkElementFactory(typeof(Grid));
        var receiptLink = new FrameworkElementFactory(typeof(Button)); receiptLink.SetValue(ContentProperty, "내려받기"); receiptLink.SetValue(HeightProperty, 28d); receiptLink.SetValue(MinHeightProperty, 0d); receiptLink.SetValue(MinWidthProperty, 0d);
        receiptLink.SetValue(PaddingProperty, new Thickness(2, 0, 2, 0)); receiptLink.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left);
        receiptLink.SetValue(Control.ForegroundProperty, FigmaDialogElements.Brush("#005FB8")); receiptLink.SetValue(Control.BackgroundProperty, Brushes.Transparent); receiptLink.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        receiptLink.SetValue(Control.TemplateProperty, FigmaDialogElements.SurfaceTemplate(0));
        receiptLink.SetBinding(VisibilityProperty, new Binding("ReceiptFileName") { Converter = new InstitutionReceiptVisibilityConverter() });
        receiptLink.SetBinding(ToolTipProperty, new Binding("ReceiptFileName"));
        receiptLink.SetBinding(AutomationProperties.NameProperty, new Binding("Description") { StringFormat = "주문 {0} 증빙 내려받기" });
        receiptLink.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler(async (sender, e) => { e.Handled = true; if (sender is FrameworkElement { DataContext: InstitutionOrder order }) await OpenReceiptForAsync(order); }));
        receiptCell.AppendChild(receiptLink);
        var noReceipt = new FrameworkElementFactory(typeof(TextBlock)); noReceipt.SetValue(TextBlock.TextProperty, "—"); noReceipt.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        noReceipt.SetBinding(VisibilityProperty, new Binding("ReceiptFileName") { Converter = new InstitutionReceiptVisibilityConverter(), ConverterParameter = "missing" }); receiptCell.AppendChild(noReceipt);
        Orders.Columns.Add(new DataGridTemplateColumn { Header = "증빙", Width = 94, CanUserSort = false, CellTemplate = new DataTemplate { VisualTree = receiptCell } });
        OpenOnClick<InstitutionOrder>(Orders, OpenOrderAsync);
        // 증빙 받는 사람 is a label and a 220px field with no save button: Enter or leaving the field saves, Esc restores, an empty field removes it.
        var receiptLine = new Grid { Margin = new(0, 12, 0, 0) }; receiptLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); receiptLine.ColumnDefinitions.Add(new());
        receiptLine.RowDefinitions.Add(new() { Height = GridLength.Auto }); receiptLine.RowDefinitions.Add(new() { Height = GridLength.Auto });
        receiptLine.Children.Add(new TextBlock { Text = "증빙 받는 사람", Foreground = FigmaDialogElements.Brush("#5F5F5F"), VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        AutomationProperties.SetName(ReceiptAddress, "증빙 받는 사람"); Grid.SetColumn(ReceiptAddress, 1); receiptLine.Children.Add(ReceiptAddress);
        Grid.SetColumn(ReceiptHint, 1); Grid.SetRow(ReceiptHint, 1); receiptLine.Children.Add(ReceiptHint);
        ReceiptAddress.TextChanged += (_, _) => ReceiptHint.Visibility = Visibility.Collapsed;
        ReceiptAddress.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; ReceiptAddress.Text = receiptEmail ?? ""; }
            else if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await SaveReceiptEmailAsync(); }
        };
        ReceiptAddress.LostKeyboardFocus += async (_, _) => await SaveReceiptEmailAsync();
        var orderCard = Card("주문 내역", Orders, receiptLine); orderCard.MinHeight = 202; content.Children.Add(orderCard);
        Root.Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>A row opens with one click or Enter; its own buttons (내려받기, 잠금 …) keep their action instead.</summary>
    private static void OpenOnClick<T>(DataGrid table, Func<T, Task> open) where T : class
    {
        bool InButton(object source)
        {
            for (var node = source as DependencyObject; node is not null && node != table; node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is System.Windows.Controls.Primitives.ButtonBase) return true;
            return false;
        }
        table.PreviewMouseLeftButtonUp += async (_, e) =>
        {
            if (!InButton(e.OriginalSource) && ItemsControl.ContainerFromElement(table, e.OriginalSource as DependencyObject) is DataGridRow { Item: T item }) await open(item);
        };
        table.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter || InButton(e.OriginalSource) || table.SelectedItem is not T item) return;
            e.Handled = true; await open(item);
        };
    }

    internal async Task LogoutAsync()
    {
        if (loggingOut) return;
        loggingOut = true; LogoutItem.IsEnabled = false;
        try { if (await logout()) { LogoutRequested = true; Close(); } }
        catch (Exception ex) { ErrorText.Text = "로그아웃하지 못했습니다. " + ex.Message; }
        finally { loggingOut = false; LogoutItem.IsEnabled = true; }
    }
    private void ApplyCaption()
    {
        if (Content is not Grid frame || frame.Children[0] is not Border { Child: DockPanel heading }) return;
        if (heading.Children.OfType<StackPanel>().FirstOrDefault() is { } buttons)
        {
            buttons.Margin = new(0);
            foreach (var (button, name) in buttons.Children.OfType<Button>().Zip(new[] { "min", "max", "close" }))
            { button.Width = 46; button.Height = 32; button.Content = FigmaDialogElements.DesignIcon($"institution-caption-{name}.svg", 16, 16, "Semojum.Feature.Institution.Wpf"); }
        }
        if (heading.Children.OfType<Border>().FirstOrDefault() is { } logo)
        { logo.Width = 16; logo.CornerRadius = new(4); logo.Background = FigmaDialogElements.Brush("#005FB8"); logo.Margin = new(12, 0, 8, 0); if (logo.Child is TextBlock text) { text.Text = "S"; text.FontFamily = new FontFamily("Segoe UI Symbol"); } }
    }
    private static Border Card(string title, params UIElement[] children)
    {
        var body = new StackPanel { Margin = new(15, 11, 15, 11) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 8) });
        foreach (var child in children) body.Children.Add(child);
        return new Border { Background = Brushes.White, BorderBrush = FigmaDialogElements.Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = body };
    }
    private static Border SummaryCard(string title, params UIElement[] children)
    {
        var card = Card(title, children);
        if (card.Child is StackPanel panel && panel.Children[0] is TextBlock heading) { heading.FontSize = 12; heading.FontWeight = FontWeights.Normal; heading.Foreground = FigmaDialogElements.Brush("#5F5F5F"); }
        return card;
    }
    private static DataGridTemplateColumn StatusColumn(string title, string property, string kind)
    {
        var pill = new FrameworkElementFactory(typeof(Border)); pill.SetValue(Border.BackgroundProperty, Brushes.White); pill.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        pill.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); pill.SetValue(HeightProperty, 18d); pill.SetValue(MinWidthProperty, 40d);
        pill.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left); pill.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));
        pill.SetBinding(Border.BorderBrushProperty, new Binding(property) { Converter = new InstitutionStatusBrushConverter(), ConverterParameter = kind });
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); text.SetValue(FontSizeProperty, 12d);
        text.SetBinding(TextBlock.TextProperty, new Binding(property) { Converter = new InstitutionStatusTextConverter(), ConverterParameter = kind });
        text.SetBinding(TextBlock.ForegroundProperty, new Binding(property) { Converter = new InstitutionStatusBrushConverter(), ConverterParameter = kind });
        pill.AppendChild(text); return new DataGridTemplateColumn { Header = title, Width = new DataGridLength(1, DataGridLengthUnitType.Star), CellTemplate = new DataTemplate { VisualTree = pill } };
    }
    private static DataGrid Table(params (string Label, string Path)[] columns)
    {
        var table = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, BorderThickness = new(0), Background = Brushes.White,
            MinHeight = 80, MaxHeight = 180, RowHeight = 40, Style = (Style)Application.Current.FindResource("InstitutionListGrid"), HorizontalGridLinesBrush = FigmaDialogElements.Brush("#EAEAEA") };
        foreach (var (label, path) in columns) table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(path) { TargetNullValue = "—" }, Width = new(1, DataGridLengthUnitType.Star) });
        AutomationProperties.SetName(table, string.Join(" · ", columns.Select(c => c.Label))); return table;
    }
    private static ControlTemplate UsageBarTemplate()
    {
        var template = new ControlTemplate(typeof(ProgressBar));
        var track = new FrameworkElementFactory(typeof(Border)); track.Name = "InstitutionTrack"; track.SetValue(Border.BackgroundProperty, FigmaDialogElements.Brush("#EDEFF3")); track.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var fill = new FrameworkElementFactory(typeof(Border)); fill.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); fill.SetValue(Border.BackgroundProperty, FigmaDialogElements.Brush("#5B8DEF")); fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var width = new MultiBinding { Converter = new Controls.ProgressWidthConverter() };
        width.Bindings.Add(new Binding("ActualWidth") { ElementName = "InstitutionTrack" });
        foreach (var property in new[] { "Value", "Minimum", "Maximum" }) width.Bindings.Add(new Binding(property) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        fill.SetBinding(WidthProperty, width); track.AppendChild(fill); template.VisualTree = track; return template;
    }
}
