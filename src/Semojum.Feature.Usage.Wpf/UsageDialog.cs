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
        // An institution administrator opens my page from the 기관 관리 window and never reaches the editor.
        var administrator = api.Account?.Role == "ROLE_ORG_ADMIN";
        var fileMenu = new MenuItem { Header = "파일(_F)", Width = 64 }; menu.Items.Add(fileMenu);
        var closeItem = new MenuItem { Header = administrator ? "닫기" : "편집기로 돌아가기" }; closeItem.Click += (_, _) => Close(); fileMenu.Items.Add(closeItem);
        var viewMenu = new MenuItem { Header = "보기(_V)", Width = 64 }; menu.Items.Add(viewMenu);
        var infoMenu = new MenuItem { Header = "정보(_A)", Width = 64 }; menu.Items.Add(infoMenu);
        var helpItem = new MenuItem { Header = "도움말" }; helpItem.Click += (_, _) => CommonWindowServices.ShowHelp(this, "사용량과 작업 이력"); infoMenu.Items.Add(helpItem);
        DockPanel.SetDock(menu, Dock.Top); Root.Children.Add(menu);
        var statusLine = new DockPanel { Margin = new(20, 0, 16, 0) };
        var loginStatus = new TextBlock { Text = api.Account?.LoginId ?? "", Foreground = ColorBrush("#5F5F5F"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; statusLine.Children.Add(loginStatus);
        System.Windows.Automation.AutomationProperties.SetAutomationId(loginStatus, "UsageStatusLogin");
        var statusBorder = new Border { Height = 28, Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)), BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0), Child = statusLine };
        DockPanel.SetDock(statusBorder, Dock.Bottom); Root.Children.Add(statusBorder); Body(tabs);
        var panel = new StackPanel { Margin = new(24, 20, 24, 20) }; TextElement.SetFontWeight(panel, FontWeights.Normal);
        var usageScroll = new ScrollViewer { Content = panel, Background = ColorBrush("#F4F6F9"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var usageTab = new TabItem { Header = "사용량", Content = usageScroll }; tabs.Items.Add(usageTab);
        var usageHeading = new Grid { Height = 32, Margin = new(0, 0, 0, 8) };
        usageHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usageHeading.ColumnDefinitions.Add(new ColumnDefinition());
        usageHeading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        usageHeading.Children.Add(new TextBlock { Text = "사용량", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (api.Account?.Alias is { } displayName && !string.IsNullOrWhiteSpace(displayName))
        {
            var accountName = new TextBlock { Text = displayName, FontSize = 14, Foreground = (Brush)FindResource("Muted"), Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = displayName };
            System.Windows.Automation.AutomationProperties.SetAutomationId(accountName, "UsageAccountName");
            System.Windows.Automation.AutomationProperties.SetName(accountName, "사용량 계정 " + displayName);
            Grid.SetColumn(accountName, 1); usageHeading.Children.Add(accountName);
        }
        var period = new Semojum.Desktop.Controls.SegmentedChoice("이번 달", "지난달") { Width = 150, Height = 32 };
        System.Windows.Automation.AutomationProperties.SetName(period, "사용량 기간");
        Grid.SetColumn(period, 2); usageHeading.Children.Add(period);
        panel.Children.Add(usageHeading);
        Border Card(UIElement child, double height) => new() { Child = child, Height = height, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Background = Brushes.White };
        var summaryPanel = new StackPanel { Margin = new(15, 11, 15, 0) };
        summaryPanel.Children.Add(new TextBlock { Text = "기관 크레딧", Foreground = (Brush)FindResource("Muted") });
        var summaryRow = new DockPanel { Height = 50 }; summaryPanel.Children.Add(summaryRow);
        var summary = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, MinWidth = 110, VerticalAlignment = VerticalAlignment.Top }; summaryRow.Children.Add(summary);
        System.Windows.Automation.AutomationProperties.SetAutomationId(summary, "OrganizationCreditBalance");
        var orgBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Template = UsageProgressTemplate() }; System.Windows.Automation.AutomationProperties.SetName(orgBar, "기관 크레딧 사용률"); summaryPanel.Children.Add(orgBar);
        var orgLine = new DockPanel { Margin = new(0, 4, 0, 0) }; summaryPanel.Children.Add(orgLine);
        var orgPercentText = new TextBlock { Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(orgPercentText, Dock.Right); orgLine.Children.Add(orgPercentText);
        var orgUsage = new TextBlock { Foreground = (Brush)FindResource("Muted") }; orgLine.Children.Add(orgUsage);
        var cards = new Grid { Margin = new(0, 0, 0, 16) }; cards.ColumnDefinitions.Add(new()); cards.ColumnDefinitions.Add(new() { Width = new(16) }); cards.ColumnDefinitions.Add(new());
        cards.RowDefinitions.Add(new() { Height = new(116) }); cards.RowDefinitions.Add(new() { Height = new(0) }); cards.RowDefinitions.Add(new() { Height = new(0) }); panel.Children.Add(cards);
        var usedPanel = new StackPanel { Margin = new(15, 11, 15, 0) };
        usedPanel.Children.Add(new TextBlock { Text = "내가 쓴 크레딧", Foreground = (Brush)FindResource("Muted") });
        var usedTotal = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold }; usedPanel.Children.Add(usedTotal);
        var usedBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Template = UsageProgressTemplate() }; System.Windows.Automation.AutomationProperties.SetName(usedBar, "내 크레딧 사용률"); usedPanel.Children.Add(usedBar);
        var usageLine = new DockPanel { Margin = new(0, 4, 0, 0) }; usedPanel.Children.Add(usageLine);
        var usagePercentText = new TextBlock { Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(usagePercentText, Dock.Right); usageLine.Children.Add(usagePercentText);
        var usage = new TextBlock { Foreground = (Brush)FindResource("Muted") }; usageLine.Children.Add(usage); cards.Children.Add(Card(usedPanel, 116));
        var summaryCard = Card(summaryPanel, 116); Grid.SetColumn(summaryCard, 2); cards.Children.Add(summaryCard);
        bool? stacked = null, compact = null;
        cards.SizeChanged += (_, e) =>
        {
            // Keep both values readable as the window narrows.
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
                summary.MinWidth = 0; summary.TextWrapping = TextWrapping.Wrap;
                summaryRow.Height = double.NaN; summaryRow.MinHeight = 50;
                summaryCard.Height = double.NaN; summaryCard.MinHeight = 116;
            }
            else
            {
                summary.MinWidth = 110; summary.TextWrapping = TextWrapping.NoWrap;
                summaryRow.Height = 50; summaryRow.MinHeight = 0;
                summaryCard.Height = 116; summaryCard.MinHeight = 0;
            }
        };
        var billing = new StackPanel { Margin = new(24, 20, 24, 20) };
        var billingHeading = new DockPanel { Height = 32, Margin = new(0, 0, 0, 8) }; billing.Children.Add(billingHeading);
        var years = Enumerable.Range(2026, Math.Max(1, DateTime.Today.Year - 2025)).Reverse().Select(y => $"{y}년").ToArray();
        var year = Choices(years, years[0]); year.Width = 150; year.Height = year.MinHeight = 32; year.Padding = new(11, 0, 25, 0); System.Windows.Automation.AutomationProperties.SetName(year, "과금 연도");
        var yearField = new Grid { Width = 150, Height = 32 }; yearField.Children.Add(year);
        yearField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = ColorBrush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        yearField.Children.Add(new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Feature.Usage.Wpf;component/Assets/Figma/account-chevron.svg"), Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 5, 0), Stretch = Stretch.None, IsHitTestVisible = false });
        DockPanel.SetDock(yearField, Dock.Right); billingHeading.Children.Add(yearField);
        billingHeading.Children.Add(new TextBlock { Text = "과금 이력", FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        // Figma C0-8 monthly chart: 4 equal axis steps over 164px (baseline y=220), 56px bars centred in equal month slots.
        var monthly = new Grid();
        var monthlyTitle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(19, 15, 0, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left }; monthly.Children.Add(monthlyTitle);
        monthlyTitle.Children.Add(new TextBlock { Text = "달마다 쓴 크레딧", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var monthlyNotice = new TextBlock { Foreground = ColorBrush("#8A8A8A"), Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; monthlyTitle.Children.Add(monthlyNotice);
        System.Windows.Automation.AutomationProperties.SetAutomationId(monthlyNotice, "BillingMonthlyNotice");
        var currentLegend = new TextBlock { Text = "이번 달", Foreground = ColorBrush("#8A8A8A"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 37, 23, 0) }; monthly.Children.Add(currentLegend);
        var axis = new TextBlock[5];
        for (var i = 0; i < axis.Length; i++)
        {
            monthly.Children.Add(new Border { Height = 1, Background = ColorBrush(i == 0 ? "#D1D1D1" : "#EAEAEA"), VerticalAlignment = VerticalAlignment.Top, Margin = new(59, 219 - 41 * i, 23, 0) });
            axis[i] = new TextBlock { Foreground = ColorBrush("#8A8A8A"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new(19, 211 - 41 * i, 0, 0) }; monthly.Children.Add(axis[i]);
        }
        var monthBars = new Grid { Margin = new(59, 0, 23, 0) }; monthly.Children.Add(monthBars);
        System.Windows.Automation.AutomationProperties.SetAutomationId(monthBars, "BillingMonthlyBars");
        var monthlyCard = Card(monthly, 260); monthlyCard.Margin = new(0, 0, 0, 16); billing.Children.Add(monthlyCard);
        void DrawMonthly(int shownYear, int firstMonth, decimal?[] credits)
        {
            var thisMonth = ThisMonthInKorea(); var step = AxisStep(credits.Select(c => c ?? 0).DefaultIfEmpty().Max());
            for (var i = 0; i < axis.Length; i++) axis[i].Text = (step * i).ToString("#,0.##", CultureInfo.InvariantCulture);
            monthBars.Children.Clear(); monthBars.ColumnDefinitions.Clear(); currentLegend.Visibility = shownYear == thisMonth.Year ? Visibility.Visible : Visibility.Collapsed;
            for (var slot = 0; slot < credits.Length; slot++)
            {
                var month = firstMonth + slot; var value = credits[slot]; var height = value is { } v ? Math.Round((double)(v / (step * 4)) * 164) : 0;
                var current = shownYear == thisMonth.Year && month == thisMonth.Month;
                // 56px as in Figma, narrowing (still centred) only when a month slot is too small.
                var bar = new Border { MaxWidth = 56, Height = height, CornerRadius = new(4), Background = ColorBrush(current ? "#8FB4F5" : "#5B8DEF"), VerticalAlignment = VerticalAlignment.Top, Margin = new(4, 219 - height, 4, 0) };
                System.Windows.Automation.AutomationProperties.SetName(bar, $"{month}월 " + (value is { } n ? $"{n:#,0.##} 크레딧" : "불러오지 못함"));
                var amount = new TextBlock { Text = value is { } a ? a.ToString("#,0.##", CultureInfo.InvariantCulture) : "—", Foreground = ColorBrush("#5F5F5F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 201 - height, 0, 0) };
                var label = new TextBlock { Text = $"{month}월", Foreground = ColorBrush("#5F5F5F"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 225, 0, 0) };
                monthBars.ColumnDefinitions.Add(new ColumnDefinition());
                foreach (var element in new FrameworkElement[] { bar, amount, label }) { Grid.SetColumn(element, slot); monthBars.Children.Add(element); }
            }
        }
        CancellationTokenSource? monthlyLoad = null;
        async Task LoadMonthlyAsync()
        {
            if (year.SelectedItem is not string yearLabel || !int.TryParse(yearLabel.TrimEnd('년'), out var shownYear)) return;
            monthlyLoad?.Cancel(); var load = monthlyLoad = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);
            // Figma shows six months: this year's six up to the current KST month (from January if fewer), a past year's July–December.
            var thisMonth = ThisMonthInKorea(); var lastMonth = shownYear < thisMonth.Year ? 12 : shownYear == thisMonth.Year ? thisMonth.Month : 0;
            var firstMonth = Math.Max(1, lastMonth - 5); var months = Enumerable.Range(firstMonth, Math.Max(0, lastMonth - firstMonth + 1)).ToArray();
            monthlyNotice.Text = "불러오는 중입니다.";
            // Each month is its own request (no list API); future months are not requested and a failed month stays an empty bar.
            async Task<decimal?> MonthAsync(int month)
            {
                try
                {
                    var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage?month={shownYear:0000}-{month:00}", cancellationToken: load.Token);
                    return data.TryGetProperty("myCredits", out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var n) && n >= 0 ? n : null;
                }
                catch (Exception) when (!load.IsCancellationRequested) { return null; }
            }
            try
            {
                var credits = await Task.WhenAll(months.Select(MonthAsync));
                if (!ReferenceEquals(load, monthlyLoad)) return;
                DrawMonthly(shownYear, firstMonth, credits);
                var failed = months.Where((_, slot) => credits[slot] is null).Select(m => $"{m}월").ToArray();
                monthlyNotice.Text = failed.Length == 0 ? "" : $"{string.Join(", ", failed)} 사용량을 불러오지 못했습니다.";
            }
            catch (Exception) when (load.IsCancellationRequested) { }
            finally { if (!ReferenceEquals(load, monthlyLoad)) load.Dispose(); }
        }
        var billingDetail = new DockPanel { Margin = new(19, 11, 19, 15) };
        var detailHeading = new DockPanel { Height = 32, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(detailHeading, Dock.Top); billingDetail.Children.Add(detailHeading);
        var kind = Choices(["전체", "사용", "충전"], "전체"); kind.Width = 112; kind.Height = kind.MinHeight = 32; kind.Padding = new(11, 0, 25, 0); System.Windows.Automation.AutomationProperties.SetName(kind, "상세 내역 구분");
        var kindField = new Grid { Width = 112, Height = 32 }; kindField.Children.Add(kind);
        kindField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = ColorBrush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        kindField.Children.Add(new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Feature.Usage.Wpf;component/Assets/Figma/account-chevron.svg"), Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 5, 0), Stretch = Stretch.None, IsHitTestVisible = false });
        DockPanel.SetDock(kindField, Dock.Right); detailHeading.Children.Add(kindField);
        var detailHeadingTitle = new TextBlock { Text = "상세 내역", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(detailHeadingTitle, Dock.Left); detailHeading.Children.Add(detailHeadingTitle);
        var creditNotice = new TextBlock { Foreground = ColorBrush("#8A8A8A"), Margin = new(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; detailHeading.Children.Add(creditNotice);
        System.Windows.Automation.AutomationProperties.SetAutomationId(creditNotice, "BillingDetailNotice");
        var creditGrid = PaymentPanel((Style)FindResource("UsageJobsGrid")); billingDetail.Children.Add(creditGrid);
        billing.Children.Add(Card(billingDetail, 552));
        CancellationTokenSource? creditLoad = null;
        async Task LoadCreditsAsync()
        {
            if (year.SelectedItem is not string yearLabel || !int.TryParse(yearLabel.TrimEnd('년'), out var shownYear)) return;
            creditLoad?.Cancel(); var load = creditLoad = CancellationTokenSource.CreateLinkedTokenSource(Lifetime.Token);
            // The chosen year is the period (to today, KST, in the current year); 전체 / 사용 / 충전 is filtered by the server's type.
            var today = TodayInKorea(); var to = shownYear < today.Year ? new DateOnly(shownYear, 12, 31) : today;
            var type = kind.SelectedIndex switch { 1 => "USE", 2 => "CHARGE", _ => "ALL" };
            creditNotice.Text = "불러오는 중입니다.";
            try
            {
                var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage/credit-history?from={shownYear:0000}-01-01&to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&type={type}", cancellationToken: load.Token);
                if (!ReferenceEquals(load, creditLoad)) return;
                var rows = data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items.EnumerateArray().Select(ReadBillingRow).ToArray() : Array.Empty<BillingRow>();
                creditGrid.ItemsSource = rows; creditNotice.Text = rows.Length == 0 ? "내역이 없습니다." : "";
            }
            catch (Exception) when (load.IsCancellationRequested) { }
            catch (Exception ex) { if (ReferenceEquals(load, creditLoad)) { creditGrid.ItemsSource = Array.Empty<BillingRow>(); creditNotice.Text = "상세 내역을 불러오지 못했습니다. " + ex.Message; } }
            finally { if (!ReferenceEquals(load, creditLoad)) load.Dispose(); }
        }
        year.SelectionChanged += async (_, _) => await Task.WhenAll(LoadMonthlyAsync(), LoadCreditsAsync());
        kind.SelectionChanged += async (_, _) => await LoadCreditsAsync();
        var billingTab = new TabItem { Header = "과금 이력", Content = new ScrollViewer { Content = billing, Background = ColorBrush("#F4F6F9"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }; tabs.Items.Add(billingTab);
        tabs.SelectionChanged += async (_, e) => { if (e.Source == tabs && ReferenceEquals(tabs.SelectedItem, billingTab) && monthlyLoad is null) await Task.WhenAll(LoadMonthlyAsync(), LoadCreditsAsync()); };
        var account = new StackPanel { Margin = new(24) };
        Row(account, "아이디", new TextBlock { Text = api.Account?.LoginId ?? "—" });
        Row(account, "계정명", new TextBlock { Text = api.Account?.DisplayName ?? "—" });
        Row(account, "권한", new TextBlock { Text = api.Account?.Role == "ROLE_ADMIN" ? "운영자" : "사용자" });
        account.Children.Add(new TextBlock { Text = "계정 정보 변경은 기관 관리자에게 문의해 주세요.", Margin = new(0, 16, 0, 0) });
        tabs.Items.Add(new TabItem { Header = "계정 정보", Content = account });
        // 「기관 관리 · 관리자」 is shown to administrators only (Figma 263:6289-6290) and returns to their open 기관 관리 window.
        var institutionHeader = new DockPanel(); var administratorLabel = new TextBlock { Text = "관리자", Foreground = ColorBrush("#8A8A8A"), Margin = new(0, 0, 12, 0) };
        DockPanel.SetDock(administratorLabel, Dock.Right); institutionHeader.Children.Add(administratorLabel); institutionHeader.Children.Add(new TextBlock { Text = "기관 관리" });
        var institutionTab = new TabItem { Header = institutionHeader }; System.Windows.Automation.AutomationProperties.SetName(institutionTab, "기관 관리 (관리자)");
        if (administrator && actions?.OpenInstitution is not null) tabs.Items.Add(institutionTab);
        var boardClient = new BoardClient(api);
        var inquiryScreen = new Grid { Background = ColorBrush("#F4F6F9") }; var inquiryContent = new DockPanel { Margin = new(24, 20, 24, 24) }; inquiryScreen.Children.Add(inquiryContent);
        var inquiryHeading = new DockPanel { Height = 40 }; DockPanel.SetDock(inquiryHeading, Dock.Top); inquiryContent.Children.Add(inquiryHeading);
        inquiryHeading.Children.Add(new TextBlock { Text = "문의 내역", FontSize = 20, FontWeight = FontWeights.SemiBold });
        var inquiryColumns = new Grid(); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(420, GridUnitType.Star) }); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(16) }); inquiryColumns.ColumnDefinitions.Add(new() { Width = new(736, GridUnitType.Star) }); inquiryContent.Children.Add(inquiryColumns);
        var listLayout = new DockPanel { Margin = new(12, 8, 12, 12) };
        // Figma C0-9 has no paging buttons: the list shows the first ten inquiries and 「더 보기」 (shown only when more exist) adds the next ten below.
        var inquiryActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(inquiryActions, Dock.Bottom); listLayout.Children.Add(inquiryActions);
        var inquiryRows = new System.Collections.ObjectModel.ObservableCollection<BoardRow>();
        var inquiryList = new ListBox { BorderThickness = new(0), Background = Brushes.White, ItemsSource = inquiryRows, ItemTemplate = InquiryRowTemplate(), ItemContainerStyle = InquiryRowStyle() }; listLayout.Children.Add(inquiryList);
        var inquiryCard = new Border { Background = Brushes.White, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = listLayout }; inquiryColumns.Children.Add(inquiryCard);
        var detailLayout = new DockPanel { Margin = new(19, 15, 19, 19) };
        var detailTitle = new TextBlock { Text = "문의를 선택해 주세요.", FontSize = 18, TextWrapping = TextWrapping.Wrap }; DockPanel.SetDock(detailTitle, Dock.Top); detailLayout.Children.Add(detailTitle);
        var detailMetadata = new TextBlock { Foreground = (Brush)FindResource("Muted"), Margin = new(0, 8, 0, 8) }; DockPanel.SetDock(detailMetadata, Dock.Top); detailLayout.Children.Add(detailMetadata);
        var detailRule = new Border { Height = 1, Background = ColorBrush("#EAEAEA"), Margin = new(0, 0, 0, 7) }; DockPanel.SetDock(detailRule, Dock.Top); detailLayout.Children.Add(detailRule);
        var detailActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 8, 0, 0) }; DockPanel.SetDock(detailActions, Dock.Bottom); detailLayout.Children.Add(detailActions);
        // The thread (280) and the follow-up field (408) share the card height as in the 1440x1000 Figma frame.
        var detailRows = new Grid(); detailRows.RowDefinitions.Add(new() { Height = new(280, GridUnitType.Star) }); detailRows.RowDefinitions.Add(new() { Height = new(408, GridUnitType.Star) }); detailLayout.Children.Add(detailRows);
        var detailBody = new StackPanel(); detailRows.Children.Add(new ScrollViewer { Content = detailBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var followUp = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 10000, BorderThickness = new(0), Background = Brushes.Transparent, Padding = new(11, 7, 11, 7), VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(followUp, "이어서 물을 것을 적는다");
        var followUpHint = new TextBlock { Text = "이어서 물을 것을 적는다", Foreground = ColorBrush("#8A8A8A"), Margin = new(12, 8, 12, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        System.Windows.Automation.AutomationProperties.SetAutomationId(followUpHint, "InquiryFollowUpPlaceholder");
        var followUpLine = new Border { Height = 1, Background = ColorBrush("#D1D1D1"), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        followUp.GotKeyboardFocus += (_, _) => { followUpLine.Height = 2; followUpLine.Background = ColorBrush("#005FB8"); };
        followUp.LostKeyboardFocus += (_, _) => { followUpLine.Height = 1; followUpLine.Background = ColorBrush("#D1D1D1"); };
        var followUpBox = new Grid(); followUpBox.Children.Add(followUp); followUpBox.Children.Add(followUpHint); followUpBox.Children.Add(followUpLine);
        var followUpField = new Border { Background = Brushes.White, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(4), Child = followUpBox }; Grid.SetRow(followUpField, 1); detailRows.Children.Add(followUpField);
        var detailCard = new Border { Background = Brushes.White, BorderBrush = ColorBrush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8), Child = detailLayout }; Grid.SetColumn(detailCard, 2); inquiryColumns.Children.Add(detailCard);
        var boardPage = 1; var boardTotal = 0; var inquiryLoaded = false; var listRequest = 0; var selectionRequest = 0; var sending = false;
        Button? moreInquiries = null, sendFollowUp = null;
        void UpdateInquiryActions()
        {
            var more = inquiryLoaded && (long)boardPage * 10 < boardTotal;
            if (moreInquiries is not null) moreInquiries.Visibility = more ? Visibility.Visible : Visibility.Collapsed;
            var selected = inquiryList.SelectedItem is BoardRow; followUp.IsEnabled = selected && !sending;
            followUpHint.Visibility = followUp.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (sendFollowUp is not null) sendFollowUp.IsEnabled = selected && !sending && !string.IsNullOrWhiteSpace(followUp.Text);
        }
        followUp.TextChanged += (_, _) => UpdateInquiryActions();
        void ClearInquiryError() { if (Status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal) || Status.Text.StartsWith("이어 묻기를 보내지 못했습니다.", StringComparison.Ordinal)) Status.Text = ""; }
        async Task LoadInquiriesAsync(bool more = false, long? selectPost = null)
        {
            var page = more ? boardPage + 1 : 1; var request = ++listRequest;
            var data = await boardClient.ListAsync(page, "TITLE_CONTENT", "", true, Lifetime.Token);
            if (Lifetime.IsCancellationRequested || request != listRequest) return;
            boardPage = page; boardTotal = data.TotalCount; inquiryLoaded = true; ClearInquiryError();
            // A row moved onto the next page by a newer inquiry is not listed twice.
            if (more) { foreach (var row in data.Items) if (!inquiryRows.Any(r => r.PostId == row.PostId)) inquiryRows.Add(row); }
            else
            {
                inquiryRows.Clear(); foreach (var row in data.Items) inquiryRows.Add(row);
                if (inquiryRows.Count > 0) inquiryList.SelectedItem = inquiryRows.FirstOrDefault(r => r.PostId == selectPost) ?? inquiryRows[0];
                else { detailTitle.Text = "보낸 문의가 없습니다."; detailMetadata.Text = ""; detailBody.Children.Clear(); }
            }
            UpdateInquiryActions();
        }
        async Task SafeInquiryAsync(Func<Task> action)
        {
            try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { Status.Text = "문의 내역을 불러오지 못했습니다. " + ex.Message; }
        }
        async Task ShowInquiryAsync(BoardRow row, int request)
        {
            var post = await boardClient.ReadAsync(row.PostId, Lifetime.Token);
            if (Lifetime.IsCancellationRequested || request != selectionRequest || !ReferenceEquals(inquiryList.SelectedItem, row)) return;
            // Figma 「분류 · 날짜 · 답변 완료」; an older server without status keeps the operator answer count.
            var answered = post.Answers.Count(a => !a.FromAuthor);
            var state = post.Status is "ANSWERED" or "WAITING" ? InquiryState(post.Status, answered).Label : answered > 0 ? $"답변 {answered}건" : "답변 없음";
            detailTitle.Text = post.Title; detailMetadata.Text = string.Join("  ·  ", new[] { CategoryLabel(post.Category), post.CreatedAt, state }.Where(part => !string.IsNullOrEmpty(part))); detailBody.Children.Clear(); ClearInquiryError();
            void AddMessage(string title, string text)
            {
                detailBody.Children.Add(new TextBlock { Text = title, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 0, 0, 6) });
                detailBody.Children.Add(new Border { Background = ColorBrush("#F9F9F9"), CornerRadius = new(6), Padding = new(16, 12, 16, 12), Margin = new(0, 0, 0, 16), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 20 } });
            }
            // Operator answers and the writer's follow-ups come as one thread in writing order.
            AddMessage("보낸 글", post.Content); foreach (var answer in post.Answers) AddMessage((answer.FromAuthor ? "이어 물은 글" : "운영자 답변") + "  ·  " + answer.AnsweredAt, answer.Content);
            if (post.Attachments.Length > 0)
            {
                var attachmentNote = new TextBlock { Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap }; attachmentNote.Inlines.Add(new Run($"첨부 {post.Attachments.Length}개 · "));
                var openPost = new Hyperlink(new Run("게시판에서 보기")); attachmentNote.Inlines.Add(openPost);
                openPost.Click += async (_, _) => await SafeInquiryAsync(async () => { if (actions is null) throw new InvalidOperationException("문의 화면을 열 수 없습니다."); actions.OpenPost(this, post); await LoadInquiriesAsync(selectPost: post.PostId); });
                detailBody.Children.Add(attachmentNote);
            }
        }
        async Task SendFollowUpAsync()
        {
            if (sending || inquiryList.SelectedItem is not BoardRow row || string.IsNullOrWhiteSpace(followUp.Text)) return;
            sending = true; UpdateInquiryActions();
            try
            {
                await boardClient.FollowUpAsync(row.PostId, followUp.Text, Lifetime.Token);
                followUp.Clear(); ClearInquiryError();
                // A follow-up returns the inquiry to 처리 중 until an operator answers; reselecting reads the thread with the new message.
                var index = inquiryRows.IndexOf(row);
                if (index >= 0) { var waiting = row with { Status = "WAITING" }; inquiryRows[index] = waiting; inquiryList.SelectedItem = waiting; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Status.Text = "이어 묻기를 보내지 못했습니다. " + ex.Message; }
            finally { sending = false; UpdateInquiryActions(); }
        }
        var writeInquiry = Button("새 문의", () => SafeInquiryAsync(async () => { if (actions is null) throw new InvalidOperationException("문의 화면을 열 수 없습니다."); if (actions.NewInquiry(this)) await LoadInquiriesAsync(); }), true);
        PublicDialogDesign.StyleAction(writeInquiry, true); writeInquiry.Width = 108; writeInquiry.Margin = new(0); DockPanel.SetDock(writeInquiry, Dock.Right); inquiryHeading.Children.Insert(0, writeInquiry);
        inquiryList.SelectionChanged += async (_, _) =>
        {
            // A draft belongs to the inquiry it was written under.
            var request = ++selectionRequest; followUp.Clear();
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
        moreInquiries = Button("더 보기", () => SafeInquiryAsync(() => LoadInquiriesAsync(more: true))); PublicDialogDesign.StyleAction(moreInquiries); moreInquiries.Margin = new(0); inquiryActions.Children.Add(moreInquiries);
        sendFollowUp = Button("보내기", SendFollowUpAsync, true); PublicDialogDesign.StyleAction(sendFollowUp, true); sendFollowUp.Width = 92; sendFollowUp.Margin = new(0); sendFollowUp.IsDefault = false; detailActions.Children.Add(sendFollowUp);
        UpdateInquiryActions();
        var inquiryTab = new TabItem { Header = "문의 내역", Content = inquiryScreen }; var logoutTab = new TabItem { Header = "로그아웃" };
        tabs.Items.Add(inquiryTab); tabs.Items.Add(logoutTab);
        foreach (var tab in tabs.Items.Cast<TabItem>()) { tab.Padding = new(16, 0, 0, 0); }
        var logoutHeader = new StackPanel();
        logoutHeader.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new(16, 12, 12, 11) });
        logoutHeader.Children.Add(new TextBlock { Text = "로그아웃", Height = 32, Margin = new(16, 0, 0, 0), Foreground = ColorBrush("#C42B1C") });
        logoutTab.Header = logoutHeader; logoutTab.Height = 56; logoutTab.Padding = new(0);
        // Sections are compared by tab, not position: the administrator's 기관 관리 shifts 문의 내역 and 로그아웃.
        tabs.SelectionChanged += async (_, e) =>
        {
            if (e.Source != tabs) return;
            if (tabs.SelectedItem == inquiryTab && !inquiryLoaded) await SafeInquiryAsync(() => LoadInquiriesAsync());
            else if (tabs.SelectedItem == institutionTab) { DialogResult = true; actions?.OpenInstitution?.Invoke(this); }
            else if (tabs.SelectedItem == logoutTab) { LogoutRequested = true; DialogResult = true; }
        };
        var history = new DockPanel { Margin = new(19, 12, 19, 16) };
        var jobsHeading = new DockPanel { Height = 32 };
        var range = Choices(["최근 30일", "최근 90일", "올해"], "최근 30일"); range.Width = 112; range.Height = range.MinHeight = 32; range.Padding = new(11, 0, 25, 0);
        var rangeField = new Grid { Width = 112, Height = 32 }; rangeField.Children.Add(range);
        rangeField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = ColorBrush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        rangeField.Children.Add(new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Feature.Usage.Wpf;component/Assets/Figma/account-chevron.svg"), Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 0, 5, 0), Stretch = Stretch.None, IsHitTestVisible = false });
        DockPanel.SetDock(rangeField, Dock.Right); jobsHeading.Children.Add(rangeField); jobsHeading.Children.Add(new TextBlock { Text = "내 작업별 크레딧", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(jobsHeading, Dock.Top); history.Children.Add(jobsHeading);
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
        var openRow = new FrameworkElementFactory(typeof(Button), "OpenJob"); openRow.SetValue(ContentProperty, "열기"); openRow.SetValue(HeightProperty, 24d); openRow.SetValue(MinHeightProperty, 0d); openRow.SetValue(PaddingProperty, new Thickness(8, 0, 8, 0));
        openRow.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(JobRow.FileName)) { StringFormat = "{0} 열기" });
        openRow.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is FrameworkElement { DataContext: JobRow row }) OpenJob(row); }));
        var openTemplate = new DataTemplate { VisualTree = openRow }; var resumable = new DataTrigger { Binding = new Binding(nameof(JobRow.Status)), Value = "변환 중" };
        resumable.Setters.Add(new Setter(ContentProperty, "이어서 열기", "OpenJob"));
        resumable.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(JobRow.FileName)) { StringFormat = "{0} 이어서 열기" }, "OpenJob")); openTemplate.Triggers.Add(resumable);
        jobs.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 96, CellTemplate = openTemplate, Visibility = administrator ? Visibility.Collapsed : Visibility.Visible });
        history.Children.Add(jobs); var historyCard = Card(history, 420); panel.Children.Add(historyCard);
        System.Windows.Automation.AutomationProperties.SetAutomationId(historyCard, "UsageWorkHistoryCard");
        System.Windows.Automation.AutomationProperties.SetAutomationId(usageScroll, "UsageOverviewScroll");
        // Fill the normal viewport like the Figma work-history card while leaving
        // short windows scrollable.
        usageScroll.ScrollChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, usageScroll) || e.ViewportHeightChange == 0 || e.ViewportHeight <= 0) return;
            historyCard.Height = Math.Max(420, e.ViewportHeight - 212);
        };
        void ShowUsage(JsonElement data)
        {
            summary.Text = Number(data, "orgRemaining");
            usedTotal.Text = Number(data, "myCredits");
            summary.ToolTip = "기관에 남은 크레딧입니다. 문서 유형과 처리 방식에 따라 한 쪽당 차감량이 달라집니다.";
            var (personalPercent, orgPercent, orgUsed) = UsagePercentages(data);
            usedBar.Visibility = personalPercent is null ? Visibility.Collapsed : Visibility.Visible; usedBar.Value = personalPercent ?? 0;
            orgBar.Visibility = orgPercent is null ? Visibility.Collapsed : Visibility.Visible; orgBar.Value = orgPercent ?? 0;
            usage.Text = personalPercent is null ? "사용률 정보 없음" : $"{Number(data, "myCredits")} / {Number(data, "orgAllocated")} 크레딧"; usagePercentText.Text = personalPercent is null ? "" : $"{personalPercent:0}%";
            orgUsage.Text = orgPercent is null ? "기관 사용률 정보 없음" : $"{orgUsed:#,0.##} / {Number(data, "orgAllocated")} 크레딧"; orgPercentText.Text = orgPercent is null ? "" : $"{orgPercent:0}%";
        }
        var usageRequest = 0;
        async Task LoadUsageAsync()
        {
            // 이번 달 and 지난달 (KST) both come from the monthly summary, the same source as the billing graph,
            // so the cards and the graph's current-month bar always share one value and one ratio (myCredits ÷ orgAllocated).
            var request = ++usageRequest; var month = ThisMonthInKorea().AddMonths(period.SelectedIndex == 1 ? -1 : 0);
            try
            {
                var data = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage?month=" + month.ToString("yyyy-MM", CultureInfo.InvariantCulture), cancellationToken: Lifetime.Token);
                if (request == usageRequest) ShowUsage(data);
            }
            catch (Exception ex) when (ex is not OperationCanceledException && request == usageRequest)
            {
                // Never leave the other month's values under the newly chosen period.
                ShowUsage(JsonSerializer.SerializeToElement(new { })); throw;
            }
        }
        period.SelectionChanged += async (_, _) =>
        {
            try { await LoadUsageAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Status.Text = "사용량을 불러오지 못했습니다. " + ex.Message; }
        };
        async Task LoadJobsAsync()
        {
            var end = DateTime.Today; var start = range.SelectedIndex switch { 1 => end.AddDays(-89), 2 => new DateTime(end.Year, 1, 1), _ => end.AddDays(-29) };
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage/jobs?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", cancellationToken: Lifetime.Token);
            jobs.ItemsSource = data.GetProperty("items").EnumerateArray().Select(ReadJobRow).ToArray();
        }
        range.SelectionChanged += async (_, _) => await RunAsync(LoadJobsAsync);
        var jobsItem = new MenuItem { Header = "작업별 사용 내역" }; jobsItem.Click += async (_, _) => await RunAsync(async () => { tabs.SelectedItem = usageTab; await LoadJobsAsync(); historyCard.BringIntoView(); }); viewMenu.Items.Add(jobsItem);
        var usageItem = new MenuItem { Header = "사용량" }; usageItem.Click += (_, _) => { tabs.SelectedItem = usageTab; usageScroll.ScrollToTop(); }; viewMenu.Items.Add(usageItem);
        void OpenJob(JobRow j) { if (administrator || string.IsNullOrWhiteSpace(j.JobId) || j.JobId == "—") return; JobId = j.JobId; FileName = j.FileName; DialogResult = true; }
        void Open() { if (jobs.SelectedItem is JobRow j) OpenJob(j); }
        jobs.MouseDoubleClick += (_, _) => Open();

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
        selected.Setters.Add(new Setter(Control.BackgroundProperty, ColorBrush("#EFF6FC"))); selected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold)); style.Triggers.Add(selected); return style;
    }
    private static DataTemplate InquiryRowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(MarginProperty, new Thickness(8, 8, 8, 8));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding(nameof(BoardRow.DisplayTitle))); title.SetValue(FontSizeProperty, 14d); title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); panel.AppendChild(title);
        var metadata = new FrameworkElementFactory(typeof(DockPanel)); metadata.SetValue(MarginProperty, new Thickness(0, 5, 0, 0));
        // Figma C0-9 row: 분류 (100px) · 날짜 · state chip at the right (답변 완료 #0F7B0F, 처리 중 #9D5D00).
        var state = new InquiryRowConverter();
        var badge = new FrameworkElementFactory(typeof(Border)); badge.SetValue(DockPanel.DockProperty, Dock.Right); badge.SetValue(FrameworkElement.HeightProperty, 18d);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); badge.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        badge.SetBinding(Border.BorderBrushProperty, new Binding { Converter = state, ConverterParameter = "edge" }); badge.SetValue(Border.BackgroundProperty, Brushes.White);
        badge.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding { Converter = state });
        var count = new FrameworkElementFactory(typeof(TextBlock)); count.SetBinding(TextBlock.TextProperty, new Binding { Converter = state });
        count.SetBinding(TextBlock.ForegroundProperty, new Binding { Converter = state, ConverterParameter = "text" }); count.SetValue(TextBlock.FontSizeProperty, 12d);
        count.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center); count.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 8, 0));
        count.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); badge.AppendChild(count); metadata.AppendChild(badge);
        var category = new FrameworkElementFactory(typeof(TextBlock)); category.SetValue(DockPanel.DockProperty, Dock.Left); category.SetValue(FrameworkElement.WidthProperty, 100d);
        category.SetBinding(TextBlock.TextProperty, new Binding { Converter = state, ConverterParameter = "category" }); category.SetBinding(VisibilityProperty, new Binding { Converter = state, ConverterParameter = "categoryVisibility" });
        category.SetValue(TextBlock.ForegroundProperty, ColorBrush("#5F5F5F")); category.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); metadata.AppendChild(category);
        var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetBinding(TextBlock.TextProperty, new Binding(nameof(BoardRow.CreatedAt))); date.SetValue(TextBlock.ForegroundProperty, ColorBrush("#8A8A8A")); metadata.AppendChild(date); panel.AppendChild(metadata);
        return new DataTemplate(typeof(BoardRow)) { VisualTree = panel };
    }
    /// <summary>The server's inquiry status (WAITING · ANSWERED); an older server without status keeps the neutral answer count.</summary>
    internal static (string Label, string Edge, string Text) InquiryState(string? status, int answers) => status switch
    {
        "ANSWERED" => ("답변 완료", "#0F7B0F", "#0F7B0F"), "WAITING" => ("처리 중", "#9D5D00", "#9D5D00"), _ => ($"답변 {answers}건", "#8A8A8A", "#5F5F5F")
    };
    /// <summary>The four Figma inquiry categories; posts written before categories (null) show none.</summary>
    internal static string CategoryLabel(string? category) => category switch
    {
        "ACCOUNT" => "계정 · 발급", "RESULT" => "점역 결과", "BILLING" => "크레딧 · 결제", "USAGE" => "사용법", _ => ""
    };
    private sealed class InquiryRowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not BoardRow row) return DependencyProperty.UnsetValue;
            var (label, edge, text) = InquiryState(row.Status, row.AnswerCount); var category = CategoryLabel(row.Category);
            return parameter switch
            {
                "edge" => ColorBrush(edge), "text" => ColorBrush(text), "category" => category,
                "categoryVisibility" => category.Length > 0 ? Visibility.Visible : Visibility.Collapsed, _ => label
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
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
    }
    /// <summary>A 상세 내역 row from /api/users/usage/credit-history: 구분 is the server's USE (사용) or CHARGE (충전); 잔액 is the institution balance after the row (— without an institution).</summary>
    internal sealed record BillingRow(string Date, string Kind, string Description, string Pages, string Credits, string Balance);
    internal static BillingRow ReadBillingRow(JsonElement item)
    {
        static decimal? Amount(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var n) ? n : null;
        var kind = Value(item, "type") switch { "USE" => "사용", "CHARGE" => "충전", var other => other };
        return new(Value(item, "date"), kind, Value(item, "title").Normalize(System.Text.NormalizationForm.FormC),
            Amount(item, "pages") is { } pages ? pages.ToString("#,0", CultureInfo.InvariantCulture) : "—",
            Amount(item, "credits") is { } credits ? credits.ToString("+#,0.##;-#,0.##;0", CultureInfo.InvariantCulture) : "—",
            Amount(item, "balanceAfter") is { } balance ? balance.ToString("#,0.##", CultureInfo.InvariantCulture) : "—");
    }
    private static DataGrid PaymentPanel(Style style)
    {
        // Figma C0-8 상세 내역: 날짜 110 · 구분 chip 270 · 내용 280 · 쪽수 100 · 크레딧 120 (14px Bold, 충전 green) · 잔액, 32px rows.
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, RowHeight = 32, Style = style };
        grid.Columns.Add(new DataGridTextColumn { Header = "날짜", Binding = new Binding(nameof(BillingRow.Date)), Width = 110 });
        var chip = new FrameworkElementFactory(typeof(Border), "KindChip"); chip.SetValue(FrameworkElement.HeightProperty, 18d); chip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        chip.SetValue(Border.CornerRadiusProperty, new CornerRadius(9)); chip.SetValue(Border.BorderThicknessProperty, new Thickness(1)); chip.SetValue(Border.PaddingProperty, new Thickness(8, 0, 8, 0));
        chip.SetValue(Border.BackgroundProperty, Brushes.White); chip.SetValue(Border.BorderBrushProperty, ColorBrush("#EAEAEA"));
        chip.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new Binding(nameof(BillingRow.Kind)));
        var chipText = new FrameworkElementFactory(typeof(TextBlock), "KindText"); chipText.SetBinding(TextBlock.TextProperty, new Binding(nameof(BillingRow.Kind)));
        chipText.SetValue(TextBlock.ForegroundProperty, ColorBrush("#5F5F5F")); chipText.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); chip.AppendChild(chipText);
        var chipTemplate = new DataTemplate { VisualTree = chip }; var charged = new DataTrigger { Binding = new Binding(nameof(BillingRow.Kind)), Value = "충전" };
        charged.Setters.Add(new Setter(Border.BorderBrushProperty, ColorBrush("#0F7B0F"), "KindChip")); charged.Setters.Add(new Setter(TextBlock.ForegroundProperty, ColorBrush("#0F7B0F"), "KindText")); chipTemplate.Triggers.Add(charged);
        grid.Columns.Add(new DataGridTemplateColumn { Header = "구분", Width = 270, CellTemplate = chipTemplate });
        grid.Columns.Add(new DataGridTextColumn { Header = "내용", Binding = new Binding(nameof(BillingRow.Description)), Width = 280 });
        grid.Columns.Add(new DataGridTextColumn { Header = "쪽수", Binding = new Binding(nameof(BillingRow.Pages)), Width = 100 });
        var amount = new Style(typeof(TextBlock), DataGridTextColumn.DefaultElementStyle); amount.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d)); amount.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
        var green = new DataTrigger { Binding = new Binding(nameof(BillingRow.Kind)), Value = "충전" }; green.Setters.Add(new Setter(TextBlock.ForegroundProperty, ColorBrush("#0F7B0F"))); amount.Triggers.Add(green);
        grid.Columns.Add(new DataGridTextColumn { Header = "크레딧", Binding = new Binding(nameof(BillingRow.Credits)), Width = 120, ElementStyle = amount });
        grid.Columns.Add(new DataGridTextColumn { Header = "잔액", Binding = new Binding(nameof(BillingRow.Balance)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        System.Windows.Automation.AutomationProperties.SetAutomationId(grid, "BillingDetailGrid"); System.Windows.Automation.AutomationProperties.SetName(grid, "상세 내역"); return grid;
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
        var running = label is "변환 중" or "대기 중"; var finishedAt = Value(job, "finishedAt");
        // A date-only completion keeps the date alone instead of inventing 00:00.
        var finished = DateTimeOffset.TryParse(finishedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var at)
            ? at.ToLocalTime().ToString(finishedAt.Length > 10 ? "MM-dd HH:mm" : "MM-dd", CultureInfo.InvariantCulture) : string.IsNullOrWhiteSpace(finishedAt) ? "—" : finishedAt;
        return new JobRow(Value(job, "jobId"), Value(job, "fileName").Normalize(System.Text.NormalizationForm.FormC), mode, label, text,
            Value(job, "totalPages"), running ? "—" : Value(job, "credits"), running ? "—" : finished);
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
    /// <summary>The server's month boundary is KST, whatever the workstation's time zone.</summary>
    internal static DateOnly ThisMonthInKorea() { var today = TodayInKorea(); return new DateOnly(today.Year, today.Month, 1); }
    internal static DateOnly TodayInKorea() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time")).DateTime);
    /// <summary>One of four equal axis steps (Figma 0–4,000): the smallest 1, 2 or 5 × 10ⁿ that keeps the largest month under the top line.</summary>
    internal static decimal AxisStep(decimal largest)
    {
        var rough = Math.Max(largest / 4, 1); var magnitude = (decimal)Math.Pow(10, Math.Floor(Math.Log10((double)rough)));
        foreach (var factor in new[] { 1m, 2m, 5m }) if (factor * magnitude >= rough) return factor * magnitude;
        return 10 * magnitude;
    }
    internal static (double? Personal, double? Organization, decimal? OrganizationUsed) UsagePercentages(JsonElement data)
    {
        static decimal? Read(JsonElement element, string key) => element.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Number && field.TryGetDecimal(out var n) && n >= 0 ? n : null;
        var allocated = Read(data, "orgAllocated"); var remaining = Read(data, "orgRemaining"); var used = Read(data, "myCredits");
        if (allocated is not { } total || total <= 0) return (null, null, null);
        double? personal = null; double? organization = null; decimal? organizationUsed = null;
        // /api/users/usage sends no percentage: the personal bar is the month's myCredits ÷ the institution allocation.
        if (used is { } myUsed && myUsed <= total) personal = (double)(myUsed / total * 100);
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
