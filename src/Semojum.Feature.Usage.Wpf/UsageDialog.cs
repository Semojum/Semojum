using System.Net.Http;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using SharpVectors.Converters;
using Semojum.Core.Integration;
using Semojum.Feature.Usage.Wpf;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

public sealed class UsageDialog : CompactDialog
{
    private sealed record JobRow(string JobId, string FileName, string Mode, string Status, string BadgeText, string Pages, string Credits, string Finished);
    public string? JobId { get; private set; }
    public string? FileName { get; private set; }
    public bool LogoutRequested { get; private set; }
    internal TabControl Navigation { get; } = new() { TabStripPlacement = Dock.Left };
    public UsageDialog(SemojumApiClient api, string workInfo = "", UsageActions? actions = null) : base("마이페이지 - 누리닷", 1440, 1000, editor: true)
    {
        Background = Brushes.White; ApplyCaptionDesign(); var tabs = Navigation;
        tabs.Style = (Style)FindResource("UsageNavigation");
        tabs.ItemContainerStyle = NavigationTabStyle();
        var menu = new Menu { Height = 32, Padding = new(4, 0, 0, 0) };
        var fileMenu = new MenuItem { Header = "파일(_F)", Width = 64 }; menu.Items.Add(fileMenu);
        var closeItem = new MenuItem { Header = "편집기로 돌아가기" }; closeItem.Click += (_, _) => Close(); fileMenu.Items.Add(closeItem);
        var viewMenu = new MenuItem { Header = "보기(_V)", Width = 64 }; menu.Items.Add(viewMenu);
        var infoMenu = new MenuItem { Header = "정보(_A)", Width = 64 }; menu.Items.Add(infoMenu);
        var helpItem = new MenuItem { Header = "도움말" }; helpItem.Click += (_, _) => CommonWindowServices.ShowHelp(this, "사용량과 작업 이력"); infoMenu.Items.Add(helpItem);
        DockPanel.SetDock(menu, Dock.Top); Root.Children.Add(menu);
        var statusLine = new DockPanel { Margin = new(20, 0, 16, 0) };
        var balanceStatus = new TextBlock(); statusLine.Children.Add(balanceStatus);
        var statusBorder = new Border { Height = 24, Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 1, 0, 0), Child = statusLine };
        DockPanel.SetDock(statusBorder, Dock.Bottom); Root.Children.Add(statusBorder); Body(tabs);
        var panel = new StackPanel { Margin = new(24, 20, 24, 24) }; TextElement.SetFontWeight(panel, FontWeights.Normal);
        var usageScroll = new ScrollViewer { Content = panel, Background = ColorBrush("#F4F6F9"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var usageTab = new TabItem { Header = "사용량", Content = usageScroll }; tabs.Items.Add(usageTab);
        var usageHeading = new Grid { Height = 28 };
        usageHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usageHeading.ColumnDefinitions.Add(new ColumnDefinition());
        usageHeading.Children.Add(new TextBlock { Text = "사용량", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (api.Account?.DisplayName is { } displayName && !string.IsNullOrWhiteSpace(displayName))
        {
            var accountName = new TextBlock { Text = displayName, FontSize = 14, Foreground = (Brush)FindResource("Muted"), Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = displayName };
            System.Windows.Automation.AutomationProperties.SetAutomationId(accountName, "UsageAccountName");
            System.Windows.Automation.AutomationProperties.SetName(accountName, "사용량 계정 " + displayName);
            Grid.SetColumn(accountName, 1); usageHeading.Children.Add(accountName);
        }
        panel.Children.Add(usageHeading);
        var periodText = new TextBlock { Height = 16, Margin = new(0, 2, 0, 8), Foreground = (Brush)FindResource("Muted") }; panel.Children.Add(periodText);
        Border Card(UIElement child, double height) => new() { Child = child, Height = height, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Background = Brushes.White };
        var summaryPanel = new StackPanel { Margin = new(15, 11, 15, 0) };
        summaryPanel.Children.Add(new TextBlock { Text = "기관 크레딧", Foreground = (Brush)FindResource("Muted") });
        var summaryRow = new DockPanel { Height = 50 }; summaryPanel.Children.Add(summaryRow);
        var purchase = Button("쪽 구매", () => { if (actions is null) Status.Text = "문의 화면을 열 수 없습니다."; else actions.Purchase(this); return Task.CompletedTask; }, true);
        purchase.Width = 120; purchase.Margin = new(0, 4, 4, 0); purchase.VerticalAlignment = VerticalAlignment.Top; purchase.IsDefault = false;
        purchase.ToolTip = "쪽 구매 문의를 작성합니다."; DockPanel.SetDock(purchase, Dock.Right); summaryRow.Children.Add(purchase);
        var summary = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, MinWidth = 110, VerticalAlignment = VerticalAlignment.Top }; summaryRow.Children.Add(summary);
        var allocated = new TextBlock { FontSize = 12, Margin = new(0, 16, 0, 0), Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Top, TextWrapping = TextWrapping.Wrap }; summaryRow.Children.Add(allocated);
        System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "OrganizationCreditBalance");
        System.Windows.Automation.AutomationProperties.SetAutomationId(allocated, "OrganizationCreditAllocation");
        System.Windows.Automation.AutomationProperties.SetAutomationId(purchase, "OrganizationCreditPurchase");
        var purchaseRow = new Grid { Height = 0, Visibility = Visibility.Collapsed };
        summaryPanel.Children.Add(purchaseRow);
        var orgBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Template = UsageProgressTemplate() }; System.Windows.Automation.AutomationProperties.SetName(orgBar, "기관 크레딧 사용률"); summaryPanel.Children.Add(orgBar);
        var orgUsage = new TextBlock { Margin = new(0, 4, 0, 0), Foreground = (Brush)FindResource("Muted") }; summaryPanel.Children.Add(orgUsage);
        var cards = new Grid { Margin = new(0, 0, 0, 16) }; cards.ColumnDefinitions.Add(new()); cards.ColumnDefinitions.Add(new() { Width = new(16) }); cards.ColumnDefinitions.Add(new());
        cards.RowDefinitions.Add(new() { Height = new(116) }); cards.RowDefinitions.Add(new() { Height = new(0) }); cards.RowDefinitions.Add(new() { Height = new(0) }); panel.Children.Add(cards);
        var usedPanel = new StackPanel { Margin = new(15, 11, 15, 0) };
        usedPanel.Children.Add(new TextBlock { Text = "내가 쓴 크레딧", Foreground = (Brush)FindResource("Muted") });
        var usedTotal = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold }; usedPanel.Children.Add(usedTotal);
        var usedBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Template = UsageProgressTemplate() }; System.Windows.Automation.AutomationProperties.SetName(usedBar, "내 크레딧 사용률"); usedPanel.Children.Add(usedBar);
        var usage = new TextBlock { Margin = new(0, 4, 0, 0), Foreground = (Brush)FindResource("Muted") }; usedPanel.Children.Add(usage); cards.Children.Add(Card(usedPanel, 116));
        var summaryCard = Card(summaryPanel, 116); Grid.SetColumn(summaryCard, 2); cards.Children.Add(summaryCard);
        bool? stacked = null, compact = null;
        cards.SizeChanged += (_, e) =>
        {
            // Keep both values readable and the purchase action reachable as the window narrows.
            var nextStacked = e.NewSize.Width < 850;
            if (stacked != nextStacked)
            {
                stacked = nextStacked;
                cards.ColumnDefinitions[1].Width = new GridLength(nextStacked ? 0 : 16);
                cards.ColumnDefinitions[2].Width = nextStacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
                cards.RowDefinitions[1].Height = new GridLength(nextStacked ? 16 : 0);
                cards.RowDefinitions[2].Height = nextStacked ? GridLength.Auto : new GridLength(0);
                Grid.SetColumn(summaryCard, nextStacked ? 0 : 2);
                Grid.SetRow(summaryCard, nextStacked ? 2 : 0);
            }
            var nextCompact = nextStacked && e.NewSize.Width < 320;
            if (compact == nextCompact) return;
            compact = nextCompact;
            if (nextCompact)
            {
                summaryRow.Children.Remove(purchase);
                purchase.HorizontalAlignment = HorizontalAlignment.Right;
                purchase.Margin = new Thickness(0, 0, 4, 0);
                purchaseRow.Children.Add(purchase);
                purchaseRow.Height = 32; purchaseRow.Visibility = Visibility.Visible;
                DockPanel.SetDock(summary, Dock.Top); summary.MinWidth = 0; summary.TextWrapping = TextWrapping.Wrap;
                allocated.Margin = new Thickness(0); summaryRow.Height = double.NaN; summaryRow.MinHeight = 50;
                summaryCard.Height = double.NaN; summaryCard.MinHeight = 116;
            }
            else
            {
                purchaseRow.Children.Remove(purchase);
                purchase.HorizontalAlignment = HorizontalAlignment.Stretch;
                purchase.Margin = new Thickness(0, 4, 4, 0);
                if (!summaryRow.Children.Contains(purchase)) summaryRow.Children.Insert(0, purchase);
                purchaseRow.Height = 0; purchaseRow.Visibility = Visibility.Collapsed;
                DockPanel.SetDock(summary, Dock.Left); summary.MinWidth = 110; summary.TextWrapping = TextWrapping.NoWrap;
                allocated.Margin = new Thickness(0, 16, 0, 0); summaryRow.Height = 50; summaryRow.MinHeight = 0;
                summaryCard.Height = 116; summaryCard.MinHeight = 0;
            }
        };
        var period = new Semojum.Desktop.Controls.SegmentedChoice("이 달", "지난 3달", "올해") { Width = 210 };
        var chartPanel = new DockPanel { Margin = new(15, 11, 19, 10) };
        var chartHeading = new DockPanel { Height = 28 }; DockPanel.SetDock(chartHeading, Dock.Top); chartPanel.Children.Add(chartHeading);
        period.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(period, Dock.Right); chartHeading.Children.Add(period);
        chartHeading.Children.Add(new TextBlock { Text = "일별 사용량", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var chartDates = new DockPanel { Height = 16, Margin = new(0, 4, 0, 0) }; DockPanel.SetDock(chartDates, Dock.Bottom); chartPanel.Children.Add(chartDates);
        var endDate = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(endDate, Dock.Right); chartDates.Children.Add(endDate);
        var startDate = new TextBlock { Foreground = (Brush)FindResource("Muted") }; chartDates.Children.Add(startDate);
        var bars = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Bottom, Height = 160 }; chartPanel.Children.Add(bars);
        var chartCard = Card(chartPanel, 230); chartCard.Margin = new(0, 0, 0, 16);
        var lower = new Grid(); lower.ColumnDefinitions.Add(new()); lower.ColumnDefinitions.Add(new() { Width = new(16) }); lower.ColumnDefinitions.Add(new());
        var sources = new StackPanel { Margin = new(15, 11, 9, 0) };
        lower.Children.Add(Card(sources, 170));
        var payments = new StackPanel { Margin = new(15, 11, 15, 0) }; payments.Children.Add(new TextBlock { Text = "최근 결제", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 24) }); payments.Children.Add(PaymentPanel());
        var paymentCard = Card(payments, 170); Grid.SetColumn(paymentCard, 2); lower.Children.Add(paymentCard);
        var billing = new StackPanel { Margin = new(24, 20, 24, 24) };
        billing.Children.Add(new TextBlock { Text = "과금 이력", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) });
        var monthly = new StackPanel { Margin = new(19, 15, 19, 15) };
        monthly.Children.Add(new TextBlock { Text = "달마다 쓴 크레딧", FontSize = 14, FontWeight = FontWeights.SemiBold });
        monthly.Children.Add(new TextBlock { Text = "이 계정에서는 월별 과금 내역을 조회할 수 없습니다. 기관 관리자에게 확인해 주세요.", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 0) });
        var monthlyCard = Card(monthly, 130); monthlyCard.Margin = new(0, 0, 0, 16); billing.Children.Add(monthlyCard);
        var billingDetail = new StackPanel { Margin = new(19, 15, 19, 15) };
        billingDetail.Children.Add(new TextBlock { Text = "상세 내역", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) }); billingDetail.Children.Add(PaymentPanel());
        billing.Children.Add(Card(billingDetail, 180));
        tabs.Items.Add(new TabItem { Header = "과금 이력", Content = new ScrollViewer { Content = billing, Background = ColorBrush("#F4F6F9"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        var account = new StackPanel { Margin = new(24) };
        Row(account, "아이디", new TextBlock { Text = api.Account?.LoginId ?? "—" });
        Row(account, "계정명", new TextBlock { Text = api.Account?.DisplayName ?? "—" });
        Row(account, "권한", new TextBlock { Text = api.Account?.Role == "ROLE_ADMIN" ? "운영자" : "사용자" });
        account.Children.Add(new TextBlock { Text = "계정 정보 변경은 기관 관리자에게 문의해 주세요.", Margin = new(0, 16, 0, 0) });
        tabs.Items.Add(new TabItem { Header = "계정 정보", Content = account });
        var boardClient = new BoardClient(api);
        var inquiryScreen = new Grid { Background = ColorBrush("#F4F6F9") }; var inquiryContent = new DockPanel { Margin = new(24, 20, 24, 24) }; inquiryScreen.Children.Add(inquiryContent);
        var inquiryHeading = new DockPanel { Height = 40 }; DockPanel.SetDock(inquiryHeading, Dock.Top); inquiryContent.Children.Add(inquiryHeading);
        inquiryHeading.Children.Add(new TextBlock { Text = "문의 내역", FontSize = 20, FontWeight = FontWeights.SemiBold });
        var inquiryColumns = new Grid(); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(420, GridUnitType.Star) }); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(16) }); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(736, GridUnitType.Star) }); inquiryContent.Children.Add(inquiryColumns);
        var listLayout = new DockPanel { Margin = new(12, 8, 12, 12) };
        var inquiryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left }; DockPanel.SetDock(inquiryActions, Dock.Bottom); listLayout.Children.Add(inquiryActions);
        var inquiryList = new ListBox { BorderThickness = new(0), Background = Brushes.White, ItemTemplate = InquiryRowTemplate(), ItemContainerStyle = InquiryRowStyle() }; listLayout.Children.Add(inquiryList);
        var inquiryCard = new Border { Background = Brushes.White, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = listLayout }; inquiryColumns.Children.Add(inquiryCard);
        var detailLayout = new DockPanel { Margin = new(19, 15, 19, 16) };
        var detailTitle = new TextBlock { Text = "문의를 선택해 주세요.", FontSize = 20, TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(detailTitle, Dock.Top); detailLayout.Children.Add(detailTitle);
        var detailMetadata = new TextBlock { Foreground = (Brush)FindResource("Muted"), Margin = new(0, 8, 0, 12) }; DockPanel.SetDock(detailMetadata, Dock.Top); detailLayout.Children.Add(detailMetadata);
        var detailActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(detailActions, Dock.Bottom); detailLayout.Children.Add(detailActions);
        var detailBody = new StackPanel(); detailLayout.Children.Add(new ScrollViewer { Content = detailBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var detailCard = new Border { Background = Brushes.White, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = detailLayout }; Grid.SetColumn(detailCard, 2); inquiryColumns.Children.Add(detailCard);
        var boardPage = 1; var boardTotal = 0; var inquiryLoaded = false; var listRequest = 0; var selectionRequest = 0;
        Button? previousInquiry = null, nextInquiry = null, selectedInquiry = null;
        void UpdateInquiryActions()
        {
            if (previousInquiry is not null) previousInquiry.IsEnabled = inquiryLoaded && boardPage > 1;
            if (nextInquiry is not null) nextInquiry.IsEnabled = inquiryLoaded && (long)boardPage * 10 < boardTotal;
            if (selectedInquiry is not null) selectedInquiry.IsEnabled = inquiryList.SelectedItem is BoardRow;
        }
        void ClearInquiryError() { if (Status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal)) Status.Text = ""; }
        async Task LoadInquiriesAsync(int? requestedPage = null)
        {
            var page = requestedPage ?? boardPage; var request = ++listRequest;
            var data = await boardClient.ListAsync(page, "TITLE_CONTENT", "", true, Lifetime.Token);
            if (Lifetime.IsCancellationRequested || request != listRequest) return;
            boardPage = page; boardTotal = data.TotalCount; inquiryList.ItemsSource = data.Items; inquiryLoaded = true; ClearInquiryError();
            if (data.Items.Length > 0) inquiryList.SelectedIndex = 0;
            else { detailTitle.Text = "보낸 문의가 없습니다."; detailMetadata.Text = ""; detailBody.Children.Clear(); }
            UpdateInquiryActions();
        }
        async Task ShowInquiryAsync(BoardRow row, int request)
        {
            var post = await boardClient.ReadAsync(row.PostId, Lifetime.Token);
            if (Lifetime.IsCancellationRequested || request != selectionRequest || !ReferenceEquals(inquiryList.SelectedItem, row)) return;
            detailTitle.Text = post.Title; detailMetadata.Text = $"{post.CreatedAt}  ·  {(post.Answers.Length > 0 ? $"답변 {post.Answers.Length}건" : "답변 없음")}"; detailBody.Children.Clear(); ClearInquiryError();
            void AddMessage(string title, string text)
            {
                detailBody.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 0, 0, 6) });
                detailBody.Children.Add(new Border { Background = ColorBrush("#F9F9F9"), CornerRadius = new(6), Padding = new(16, 12, 16, 12), Margin = new(0, 0, 0, 16), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 } });
            }
            AddMessage("보낸 글", post.Content); foreach (var answer in post.Answers) AddMessage("운영자 답변 · " + answer.AnsweredAt, answer.Content);
            if (post.Attachments.Length > 0) detailBody.Children.Add(new TextBlock { Text = $"첨부 {post.Attachments.Length}개 · ‘상세 열기’에서 확인", Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap });
        }
        async Task SafeInquiryAsync(Func<Task> action)
        {
            try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { Status.Text = "문의 내역을 불러오지 못했습니다. " + ex.Message; }
        }
        var writeInquiry = Button("새 문의", () => SafeInquiryAsync(async () => { if (actions is null) throw new InvalidOperationException("문의 화면을 열 수 없습니다."); if (actions.NewInquiry(this)) await LoadInquiriesAsync(); }), true);
        PublicDialogDesign.StyleAction(writeInquiry, true); writeInquiry.Width = 108; writeInquiry.Margin = new(0); DockPanel.SetDock(writeInquiry, Dock.Right); inquiryHeading.Children.Insert(0, writeInquiry);
        inquiryList.SelectionChanged += async (_, _) =>
        {
            var request = ++selectionRequest;
            UpdateInquiryActions();
            if (inquiryList.SelectedItem is not BoardRow row) return;
            detailTitle.Text = "문의 내용을 불러오는 중입니다."; detailMetadata.Text = ""; detailBody.Children.Clear();
            try { await ShowInquiryAsync(row, request); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (request != selectionRequest || !ReferenceEquals(inquiryList.SelectedItem, row) || Lifetime.IsCancellationRequested) return;
                detailTitle.Text = "문의 내용을 불러오지 못했습니다."; detailMetadata.Text = ""; detailBody.Children.Clear();
                Status.Text = "문의 내역을 불러오지 못했습니다. " + ex.Message;
            }
        };
        previousInquiry = Button("이전", () => SafeInquiryAsync(async () => { if (boardPage > 1) await LoadInquiriesAsync(boardPage - 1); })); inquiryActions.Children.Add(previousInquiry);
        nextInquiry = Button("다음", () => SafeInquiryAsync(async () => { if ((long)boardPage * 10 < boardTotal) await LoadInquiriesAsync(boardPage + 1); })); inquiryActions.Children.Add(nextInquiry);
        selectedInquiry = Button("상세 열기", () => SafeInquiryAsync(async () => { if (inquiryList.SelectedItem is BoardRow row) { var post = await boardClient.ReadAsync(row.PostId, Lifetime.Token); if (actions is null) throw new InvalidOperationException("문의 화면을 열 수 없습니다."); actions.OpenPost(this, post); await LoadInquiriesAsync(); } })); detailActions.Children.Add(selectedInquiry);
        detailActions.Children.Add(Button("게시판 열기", () => { return SafeInquiryAsync(async () => { if (actions is null) throw new InvalidOperationException("게시판을 열 수 없습니다."); actions.OpenBoard(this); await LoadInquiriesAsync(); }); }));
        UpdateInquiryActions();
        tabs.Items.Add(new TabItem { Header = "문의 내역", Content = inquiryScreen }); tabs.Items.Add(new TabItem { Header = "로그아웃" });
        foreach (var tab in tabs.Items.Cast<TabItem>()) { tab.Padding = new(16, 0, 0, 0); }
        var logoutHeader = new StackPanel();
        logoutHeader.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new(16, 12, 12, 11) });
        logoutHeader.Children.Add(new TextBlock { Text = "로그아웃", Height = 32, Margin = new(16, 0, 0, 0) });
        ((TabItem)tabs.Items[4]).Header = logoutHeader; ((TabItem)tabs.Items[4]).Height = 56; ((TabItem)tabs.Items[4]).Padding = new(0);
        tabs.SelectionChanged += async (_, e) =>
        {
            if (e.Source != tabs) return;
            if (tabs.SelectedIndex == 3 && !inquiryLoaded) await SafeInquiryAsync(() => LoadInquiriesAsync());
            else if (tabs.SelectedIndex == 4) { LogoutRequested = true; DialogResult = true; }
        };
        var history = new DockPanel { Margin = new(19, 12, 19, 16) };
        var jobsHeading = new DockPanel { Height = 32 };
        var range = Choices(["최근 30일", "최근 90일", "올해"], "최근 30일"); range.Width = 112; range.Height = range.MinHeight = 32; range.Padding = new(11, 0, 25, 0);
        var rangeField = new Grid { Width = 112, Height = 32 }; rangeField.Children.Add(range);
        rangeField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = ColorBrush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        rangeField.Children.Add(new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Feature.Usage.Wpf;component/Assets/Figma/account-chevron.svg"), Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 5, 0), Stretch = Stretch.None, IsHitTestVisible = false });
        DockPanel.SetDock(rangeField, Dock.Right); jobsHeading.Children.Add(rangeField); jobsHeading.Children.Add(new TextBlock { Text = "내 작업별 크레딧", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(jobsHeading, Dock.Top); history.Children.Add(jobsHeading);
        var dates = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(dates, Dock.Top); history.Children.Add(dates);
        var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-29), Width = 150 }; var to = new DatePicker { SelectedDate = DateTime.Today, Width = 150 };
        dates.Children.Add(from); dates.Children.Add(new TextBlock { Text = " ~ " }); dates.Children.Add(to);
        var jobs = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, Margin = new(0, 12, 0, 0), RowHeight = 40, Style = (Style)FindResource("UsageJobsGrid") };
        foreach (var (label, property, width) in new[] { ("작업명", "FileName", 250d), ("모드", "Mode", 100d), ("쪽수", "Pages", 80d), ("크레딧", "Credits", 90d), ("완료", "Finished", 120d) })
            jobs.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width });
        var badge = new FrameworkElementFactory(typeof(Border)); badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(10)); badge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        badge.SetValue(FrameworkElement.HeightProperty, 20d); badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); badge.SetValue(Border.PaddingProperty, new Thickness(6, 0, 6, 0)); badge.SetValue(Border.BackgroundProperty, Brushes.White);
        badge.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(JobRow.BadgeText)));
        var color = new StatusBadgeBrushConverter(); badge.SetBinding(Border.BorderBrushProperty, new Binding(nameof(JobRow.Status)) { Converter = color });
        badge.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(JobRow.BadgeText)));
        var badgeText = new FrameworkElementFactory(typeof(TextBlock)); badgeText.SetBinding(TextBlock.TextProperty, new Binding(nameof(JobRow.BadgeText))); badgeText.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(JobRow.Status)) { Converter = color });
        badgeText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); badgeText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); badge.AppendChild(badgeText);
        jobs.Columns.Add(new DataGridTemplateColumn { Header = "상태", Width = 150, CellTemplate = new DataTemplate { VisualTree = badge } });
        jobs.Columns.Add(new DataGridTextColumn { Header = "", Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true });
        var openRow = new FrameworkElementFactory(typeof(Button)); openRow.SetValue(ContentProperty, "열기"); openRow.SetValue(HeightProperty, 24d); openRow.SetValue(MinHeightProperty, 0d); openRow.SetValue(PaddingProperty, new Thickness(8, 0, 8, 0));
        openRow.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(JobRow.FileName)) { StringFormat = "{0} 열기" });
        openRow.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is FrameworkElement { DataContext: JobRow row }) OpenJob(row); }));
        jobs.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 80, CellTemplate = new DataTemplate { VisualTree = openRow } });
        history.Children.Add(jobs); var historyCard = Card(history, 420); historyCard.Margin = new(0, 0, 0, 16); panel.Children.Add(historyCard);
        System.Windows.Automation.AutomationProperties.SetAutomationId(historyCard, "UsageWorkHistoryCard");
        System.Windows.Automation.AutomationProperties.SetAutomationId(usageScroll, "UsageOverviewScroll");
        // Fill the normal viewport like the Figma work-history card while leaving
        // short windows scrollable and preserving the expandable usage breakdown.
        usageScroll.ScrollChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, usageScroll) || e.ViewportHeightChange == 0 || e.ViewportHeight <= 0) return;
            historyCard.Height = Math.Max(420, e.ViewportHeight - 212);
        };
        var breakdown = new Expander { Header = "일별 사용량과 사용 내역", Content = new StackPanel(), Margin = new(0, 0, 0, 16) };
        ((StackPanel)breakdown.Content).Children.Add(chartCard); ((StackPanel)breakdown.Content).Children.Add(lower); panel.Children.Add(breakdown);
        async Task LoadUsageAsync()
        {
            var code = period.SelectedIndex switch { 1 => "QUARTER", 2 => "YEAR", _ => "MONTH" };
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage/daily?period=" + code, cancellationToken: Lifetime.Token);
            summary.Text = Number(data, "orgRemaining");
            usedTotal.Text = Number(data, "usedCredits");
            allocated.Text = $"잔여  /  할당 {Number(data, "orgAllocated")} 크레딧";
            periodText.Text = $"{Value(data, "from")} ~ {Value(data, "to")}";
            balanceStatus.Text = $"남은 크레딧 {Number(data, "orgRemaining")}     |     이 기간 사용 {Number(data, "usedCredits")}";
            startDate.Text = Value(data, "from"); endDate.Text = Value(data, "to");
            summary.ToolTip = "기관에 남은 크레딧입니다. 문서 유형과 처리 방식에 따라 한 쪽당 차감량이 달라집니다.";
            var (personalPercent, orgPercent, orgUsed) = UsagePercentages(data);
            usedBar.Visibility = personalPercent is null ? Visibility.Collapsed : Visibility.Visible; usedBar.Value = personalPercent ?? 0;
            orgBar.Visibility = orgPercent is null ? Visibility.Collapsed : Visibility.Visible; orgBar.Value = orgPercent ?? 0;
            usage.Text = personalPercent is null ? "사용률 정보 없음" : $"{Number(data, "usedCredits")} / {Number(data, "orgAllocated")} 크레딧  ·  {personalPercent:0.##}%";
            orgUsage.Text = orgPercent is null ? "기관 사용률 정보 없음" : $"{orgUsed:#,0.##} / {Number(data, "orgAllocated")} 크레딧  ·  {orgPercent:0.##}%";
            bars.Children.Clear(); var daily = data.GetProperty("daily").EnumerateArray().ToArray(); var maximum = daily.Select(d => d.GetProperty("credits").GetDouble()).DefaultIfEmpty(1).Max();
            foreach (var day in daily)
            {
                var number = day.GetProperty("credits").GetDouble();
                bars.Children.Add(new Border { Background = number > 0 ? new SolidColorBrush(Color.FromRgb(0, 95, 184)) : Brushes.LightGray,
                    Height = Math.Max(3, 134.4 * number / Math.Max(1, maximum)), Margin = new(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{Value(day, "date")}: {number} 크레딧" });
            }
            var sourceRows = data.GetProperty("bySource").EnumerateArray().ToArray(); var sourceTotal = sourceRows.Sum(s => s.GetProperty("credits").GetDouble());
            sources.Children.Clear(); sources.Children.Add(new TextBlock { Text = "사용 내역", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 20) });
            foreach (var source in sourceRows)
            {
                var share = sourceTotal == 0 ? 0 : source.GetProperty("credits").GetDouble() / sourceTotal * 100;
                var label = Value(source, "source") switch { "PDF_HWP" => "PDF · HWP", "IMAGE" => "이미지", "TXT" or "TEXT" => "TXT · 새 문서", var other => other };
                var row = new DockPanel { Height = 32 };
                row.Children.Add(new TextBlock { Text = label, Width = 120 });
                row.Children.Add(new TextBlock { Text = $"{Number(source, "credits")} 크레딧", Width = 110, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new ProgressBar { Maximum = 100, Value = share, Height = 8, Style = (Style)FindResource("FlatUsageBar"), VerticalAlignment = VerticalAlignment.Center }); sources.Children.Add(row);
            }
        }
        async Task LoadJobsAsync()
        {
            if (from.SelectedDate is not { } start || to.SelectedDate is not { } end || start > end) throw new ArgumentException("조회 시작일과 종료일을 확인해 주세요.");
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage/jobs?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", cancellationToken: Lifetime.Token);
            jobs.ItemsSource = data.GetProperty("items").EnumerateArray().Select(ReadJobRow).ToArray();
        }
        period.SelectionChanged += async (_, _) => await RunAsync(LoadUsageAsync);
        range.SelectionChanged += async (_, _) => await RunAsync(async () =>
        {
            to.SelectedDate = DateTime.Today;
            from.SelectedDate = range.SelectedIndex switch { 1 => DateTime.Today.AddDays(-89), 2 => new DateTime(DateTime.Today.Year, 1, 1), _ => DateTime.Today.AddDays(-29) };
            await LoadJobsAsync();
        });
        dates.Children.Add(Button("조회", async () => await RunAsync(LoadJobsAsync)));
        var jobsItem = new MenuItem { Header = "작업별 사용 내역" }; jobsItem.Click += async (_, _) => await RunAsync(async () => { tabs.SelectedIndex = 0; await LoadJobsAsync(); historyCard.BringIntoView(); }); viewMenu.Items.Add(jobsItem);
        var usageItem = new MenuItem { Header = "사용량" }; usageItem.Click += (_, _) => { tabs.SelectedIndex = 0; usageScroll.ScrollToTop(); }; viewMenu.Items.Add(usageItem);
        void OpenJob(JobRow j) { if (string.IsNullOrWhiteSpace(j.JobId) || j.JobId == "—") return; JobId = j.JobId; FileName = j.FileName; DialogResult = true; }
        void Open() { if (jobs.SelectedItem is JobRow j) OpenJob(j); }
        jobs.MouseDoubleClick += (_, _) => Open();
        dates.Children.Add(Button("선택 작업 열기", () => { Open(); return Task.CompletedTask; }));

        OnLoad(async () => { await LoadUsageAsync(); try { await LoadJobsAsync(); } catch (Exception ex) when (ex is not OperationCanceledException) { Status.Text = "작업 내역을 불러오지 못했습니다. " + ex.Message; } });
    }
    private static Brush ColorBrush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static Style NavigationTabStyle()
    {
        var style = new Style(typeof(TabItem)); style.Setters.Add(new Setter(HeightProperty, 32d)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var surface = new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); surface.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(ContentPresenter.ContentSourceProperty, "Header"); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); surface.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(TabItem)) { VisualTree = surface }));
        var selected = new Trigger { Property = System.Windows.Controls.Primitives.Selector.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, ColorBrush("#EFF6FC"))); selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); style.Triggers.Add(selected); return style;
    }
    private static DataTemplate InquiryRowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(MarginProperty, new Thickness(8, 8, 8, 8));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(BoardRow.DisplayTitle))); title.SetValue(FontSizeProperty, 14d); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); panel.AppendChild(title);
        var metadata = new FrameworkElementFactory(typeof(DockPanel)); metadata.SetValue(MarginProperty, new Thickness(0, 5, 0, 0));
        var badge = new FrameworkElementFactory(typeof(Border)); badge.SetValue(DockPanel.DockProperty, Dock.Right);
        badge.SetValue(FrameworkElement.HeightProperty, 18d); badge.SetValue(FrameworkElement.MinWidthProperty, 69d);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); badge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        badge.SetValue(Border.BorderBrushProperty, ColorBrush("#8A8A8A")); badge.SetValue(Border.BackgroundProperty, Brushes.White);
        badge.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(BoardRow.AnswerCount)) { StringFormat = "답변 {0}건" });
        var count = new FrameworkElementFactory(typeof(TextBlock)); count.SetBinding(TextBlock.TextProperty, new Binding(nameof(BoardRow.AnswerCount)) { StringFormat = "답변 {0}건" });
        count.SetValue(TextBlock.ForegroundProperty, ColorBrush("#5F5F5F")); count.SetValue(TextBlock.FontSizeProperty, 12d);
        count.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center); count.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0));
        count.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); badge.AppendChild(count); metadata.AppendChild(badge);
        var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetBinding(TextBlock.TextProperty, new Binding(nameof(BoardRow.CreatedAt))); date.SetValue(TextBlock.ForegroundProperty, ColorBrush("#8A8A8A")); metadata.AppendChild(date); panel.AppendChild(metadata);
        return new DataTemplate(typeof(BoardRow)) { VisualTree = panel };
    }
    private static Style InquiryRowStyle()
    {
        var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(HeightProperty, 72d)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var surface = new FrameworkElementFactory(typeof(Border), "InquirySurface"); surface.SetValue(Border.BackgroundProperty, Brushes.White); surface.SetValue(Border.BorderBrushProperty, ColorBrush("#EAEAEA")); surface.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        surface.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter))); var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = surface };
        var selected = new Trigger { Property = System.Windows.Controls.Primitives.Selector.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, ColorBrush("#EFF6FC"), "InquirySurface")); template.Triggers.Add(selected);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush("#005FB8"), "InquirySurface")); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
    private void ApplyCaptionDesign()
    {
        if (Content is not Grid frame || frame.Children[0] is not Border { Child: DockPanel heading }) return;
        if (heading.Children.OfType<StackPanel>().FirstOrDefault() is { } buttons)
        {
            buttons.Margin = new(0);
            foreach (var (button, name) in buttons.Children.OfType<Button>().Zip(new[] { "min", "max", "close" }))
            {
                button.Width = 46; button.Height = 32; button.Content = new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Feature.Usage.Wpf;component/Assets/Figma/account-caption-{name}.svg"), Width = 16, Height = 16, Stretch = Stretch.None };
            }
        }
        if (heading.Children.OfType<Border>().FirstOrDefault() is { } logo)
        {
            logo.Width = 16; logo.CornerRadius = new(4); logo.Background = ColorBrush("#005FB8"); logo.Margin = new(12, 0, 8, 0);
            if (logo.Child is TextBlock text) { text.Text = "S"; text.FontFamily = new FontFamily("Segoe UI Symbol"); }
        }
    }
    private static UIElement PaymentPanel()
    {
        var panel = new StackPanel(); var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 24, BorderThickness = new(0), Background = Brushes.White, HeadersVisibility = DataGridHeadersVisibility.Column };
        foreach (var label in new[] { "날짜", "내용", "금액" }) grid.Columns.Add(new DataGridTextColumn { Header = label, Width = new(1, DataGridLengthUnitType.Star) });
        panel.Children.Add(grid); panel.Children.Add(new TextBlock { Text = "이 계정에서는 결제 내역을 조회할 수 없습니다. 기관 관리자에게 확인해 주세요.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new(0, 8, 0, 12) }); return panel;
    }
    internal static string StatusLabel(string status, bool failed) => status switch
    {
        "COMPLETED" or "DONE" => failed ? "일부 쪽 실패" : "완료", "FAILED" => "변환 실패", "PARTIALLY_COMPLETED" or "PARTIAL_SUCCESS" => "일부 쪽 실패",
        "CANCELLED" or "CANCELED" => "취소됨", "PENDING" or "QUEUED" => "대기 중", "IN_PROGRESS" or "PROCESSING" => "변환 중", _ => "상태 확인 필요"
    };
    private static JobRow ReadJobRow(JsonElement job)
    {
        var status = Value(job, "status");
        var total = Count(job, "totalPages");
        var done = Count(job, "donePages");
        var failed = Count(job, "failedPages");
        var hasFailedValue = job.TryGetProperty("failedPages", out var rawFailed) && rawFailed.ValueKind != JsonValueKind.Null;
        var invalidFailed = hasFailedValue && (failed is null || total is > 0 && failed > total ||
            (status is "COMPLETED" or "DONE") && total is > 0 && failed == total);
        var label = (status is "COMPLETED" or "DONE") && invalidFailed ? "상태 확인 필요" : StatusLabel(status, failed is > 0);
        var text = label;
        if (label == "변환 중" && total is > 0 && done is >= 0 && done <= total)
            text = $"진행 중 {done}/{total}";
        else if (label == "일부 쪽 실패" && total is > 0 && failed is > 0 && failed < total)
            text = $"부분 실패 {failed}쪽";
        var mode = Value(job, "mode") switch { "c" => "통합 변환", "b" => "점역 변환", var other => other };
        return new JobRow(Value(job, "jobId"), Value(job, "fileName"), mode, label, text,
            Value(job, "donePages") + "/" + Value(job, "totalPages"), Value(job, "credits"), Value(job, "finishedAt"));
    }
    private static int? Count(JsonElement job, string name) =>
        job.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var count) && count >= 0 ? count : null;
    private sealed class StatusBadgeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ColorBrush((value as string) switch
        {
            "완료" => "#0F7B0F", "일부 쪽 실패" or "변환 실패" => "#C42B1C", "변환 중" => "#9D5D00", _ => "#5F5F5F"
        });
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
    internal static (double? Personal, double? Organization, decimal? OrganizationUsed) UsagePercentages(JsonElement data)
    {
        static decimal? Read(JsonElement element, string key) => element.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var n) && n >= 0 ? n : null;
        var allocated = Read(data, "orgAllocated"); var remaining = Read(data, "orgRemaining"); var used = Read(data, "usedCredits"); var reported = Read(data, "usedPercent");
        if (allocated is not { } total || total <= 0) return (null, null, null);
        double? personal = null; double? organization = null; decimal? organizationUsed = null;
        if (used is { } myUsed && myUsed <= total && reported is >= 0 and <= 100 && Math.Abs((double)(myUsed / total * 100 - reported.Value)) <= .11)
            personal = (double)reported.Value;
        if (remaining is { } rest && rest <= total)
        { organizationUsed = total - rest; organization = (double)(organizationUsed.Value / total * 100); }
        return (personal, organization, organizationUsed);
    }
    private static ControlTemplate UsageProgressTemplate()
    {
        var template = new ControlTemplate(typeof(ProgressBar));
        var track = new FrameworkElementFactory(typeof(Border), "UsageTrack"); track.SetValue(Border.BackgroundProperty, ColorBrush("#EDEFF3")); track.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var fill = new FrameworkElementFactory(typeof(Border), "RangeFill"); fill.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Left); fill.SetValue(Border.BackgroundProperty, ColorBrush("#5B8DEF")); fill.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        var width = new MultiBinding { Converter = new Semojum.Desktop.Controls.ProgressWidthConverter() };
        width.Bindings.Add(new Binding("ActualWidth") { ElementName = "UsageTrack" });
        foreach (var property in new[] { "Value", "Minimum", "Maximum" }) width.Bindings.Add(new Binding(property) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        fill.SetBinding(WidthProperty, width); track.AppendChild(fill); template.VisualTree = track; return template;
    }
    private static string Number(JsonElement j, string field) => j.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n.ToString("#,0.##") : "—";
    private static string Value(JsonElement j, string field) => j.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "—";
}
