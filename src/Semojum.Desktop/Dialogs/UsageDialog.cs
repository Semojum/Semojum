using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class UsageDialog : CompactDialog
{
    private sealed record JobRow(string JobId, string FileName, string Status, string Pages, string Credits, string Finished);
    public string? JobId { get; private set; }
    public string? FileName { get; private set; }
    public bool LogoutRequested { get; private set; }
    internal TabControl Navigation { get; } = new() { TabStripPlacement = Dock.Left };
    public UsageDialog(SemojumApiClient api, string workInfo = "") : base("마이페이지 - 세모점", 1440, 1000, editor: true)
    {
        Background = Brushes.White; var tabs = Navigation;
        tabs.Style = (Style)FindResource("UsageNavigation");
        var menu = new Menu { Height = 32, Padding = new(4, 0, 0, 0) };
        var fileMenu = new MenuItem { Header = "파일(_F)", Width = 64 }; menu.Items.Add(fileMenu);
        var closeItem = new MenuItem { Header = "편집기로 돌아가기" }; closeItem.Click += (_, _) => Close(); fileMenu.Items.Add(closeItem);
        var viewMenu = new MenuItem { Header = "보기(_V)", Width = 64 }; menu.Items.Add(viewMenu);
        var infoMenu = new MenuItem { Header = "정보(_A)", Width = 64 }; menu.Items.Add(infoMenu);
        var helpItem = new MenuItem { Header = "도움말" }; helpItem.Click += (_, _) => new HelpWindow("사용량과 작업 이력") { Owner = this }.Show(); infoMenu.Items.Add(helpItem);
        DockPanel.SetDock(menu, Dock.Top); Root.Children.Add(menu);
        var statusLine = new DockPanel { Margin = new(20, 0, 16, 0) };
        var login = new TextBlock { Text = api.Account?.LoginId ?? "로그인 안 됨", HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("Muted") };
        DockPanel.SetDock(login, Dock.Right); statusLine.Children.Add(login); var balanceStatus = new TextBlock(); statusLine.Children.Add(balanceStatus);
        var statusBorder = new Border { Height = 24, Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 1, 0, 0), Child = statusLine };
        DockPanel.SetDock(statusBorder, Dock.Bottom); Root.Children.Add(statusBorder); Body(tabs);
        var panel = new StackPanel { Margin = new(24, 20, 24, 24) }; TextElement.SetFontWeight(panel, FontWeights.Normal);
        var usageScroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var usageTab = new TabItem { Header = "사용량", Content = usageScroll }; tabs.Items.Add(usageTab);
        panel.Children.Add(new TextBlock { Text = "사용량", FontSize = 20, FontWeight = FontWeights.SemiBold, Height = 28 });
        var periodText = new TextBlock { Height = 16, Margin = new(0, 2, 0, 26), Foreground = (Brush)FindResource("Muted") }; panel.Children.Add(periodText);
        Border Card(UIElement child, double height) => new() { Child = child, Height = height, BorderBrush = new SolidColorBrush(Color.FromRgb(181, 181, 181)), BorderThickness = new(1), Background = Brushes.White };
        var summaryPanel = new StackPanel { Margin = new(15, 11, 15, 0) };
        var summaryRow = new DockPanel { Height = 50 }; summaryPanel.Children.Add(summaryRow);
        var purchase = Button("쪽 구매", () => { new PublicInquiryDialog(api) { Owner = this }.ShowDialog(); return Task.CompletedTask; }, true);
        purchase.Width = 120; purchase.Margin = new(0, 4, 4, 0); purchase.VerticalAlignment = VerticalAlignment.Top; purchase.IsDefault = false;
        purchase.ToolTip = "쪽 구매 문의를 작성합니다."; DockPanel.SetDock(purchase, Dock.Right); summaryRow.Children.Add(purchase);
        var summary = new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, Width = 110, VerticalAlignment = VerticalAlignment.Top }; summaryRow.Children.Add(summary);
        var allocated = new TextBlock { FontSize = 14, Margin = new(0, 16, 0, 0), Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Top }; summaryRow.Children.Add(allocated);
        var usedBar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 8, Style = (Style)FindResource("FlatUsageBar") }; summaryPanel.Children.Add(usedBar);
        var usage = new TextBlock { Margin = new(0, 8, 0, 0), Foreground = (Brush)FindResource("Muted") }; summaryPanel.Children.Add(usage);
        var summaryCard = Card(summaryPanel, 110); summaryCard.Margin = new(0, 0, 0, 16); panel.Children.Add(summaryCard);
        var period = new Semojum.Desktop.Controls.SegmentedChoice("이 달", "지난 3달", "올해") { Width = 210 };
        var chartPanel = new DockPanel { Margin = new(15, 11, 19, 10) };
        var chartHeading = new DockPanel { Height = 28 }; DockPanel.SetDock(chartHeading, Dock.Top); chartPanel.Children.Add(chartHeading);
        period.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(period, Dock.Right); chartHeading.Children.Add(period);
        chartHeading.Children.Add(new TextBlock { Text = "일별 사용량", FontSize = 14, FontWeight = FontWeights.SemiBold });
        var chartDates = new DockPanel { Height = 16, Margin = new(0, 4, 0, 0) }; DockPanel.SetDock(chartDates, Dock.Bottom); chartPanel.Children.Add(chartDates);
        var endDate = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, Foreground = (Brush)FindResource("Muted") }; DockPanel.SetDock(endDate, Dock.Right); chartDates.Children.Add(endDate);
        var startDate = new TextBlock { Foreground = (Brush)FindResource("Muted") }; chartDates.Children.Add(startDate);
        var bars = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, VerticalAlignment = VerticalAlignment.Bottom, Height = 160 }; chartPanel.Children.Add(bars);
        var chartCard = Card(chartPanel, 230); chartCard.Margin = new(0, 0, 0, 16); panel.Children.Add(chartCard);
        var lower = new Grid(); lower.ColumnDefinitions.Add(new()); lower.ColumnDefinitions.Add(new() { Width = new(16) }); lower.ColumnDefinitions.Add(new()); panel.Children.Add(lower);
        var sources = new StackPanel { Margin = new(15, 11, 9, 0) };
        lower.Children.Add(Card(sources, 170));
        var payments = new StackPanel { Margin = new(15, 11, 15, 0) }; payments.Children.Add(new TextBlock { Text = "최근 결제", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 24) }); payments.Children.Add(PaymentPanel());
        var paymentCard = Card(payments, 170); Grid.SetColumn(paymentCard, 2); lower.Children.Add(paymentCard);
        tabs.Items.Add(new TabItem { Header = "과금 이력", Content = PaymentPanel() });
        var account = new StackPanel { Margin = new(24) };
        Row(account, "아이디", new TextBlock { Text = api.Account?.LoginId ?? "—" });
        Row(account, "계정명", new TextBlock { Text = api.Account?.DisplayName ?? "—" });
        Row(account, "권한", new TextBlock { Text = api.Account?.Role == "ROLE_ADMIN" ? "운영자" : "사용자" });
        account.Children.Add(new TextBlock { Text = "계정 정보 변경은 기관 관리자에게 문의해 주세요.", Margin = new(0, 16, 0, 0) });
        tabs.Items.Add(new TabItem { Header = "계정 정보", Content = account });
        tabs.Items.Add(new TabItem { Header = "문의 내역" }); tabs.Items.Add(new TabItem { Header = "로그아웃" });
        foreach (var tab in tabs.Items.Cast<TabItem>()) { tab.Padding = new(16, 0, 0, 0); }
        var logoutHeader = new StackPanel();
        logoutHeader.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new(16, 12, 12, 11) });
        logoutHeader.Children.Add(new TextBlock { Text = "로그아웃", Height = 32, Margin = new(16, 0, 0, 0) });
        ((TabItem)tabs.Items[4]).Header = logoutHeader; ((TabItem)tabs.Items[4]).Height = 56; ((TabItem)tabs.Items[4]).Padding = new(0);
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source != tabs) return;
            if (tabs.SelectedIndex == 3) { new BoardDialog(new(api), workInfo, initiallyMine: true) { Owner = this }.ShowDialog(); tabs.SelectedIndex = 0; }
            else if (tabs.SelectedIndex == 4) { LogoutRequested = true; DialogResult = true; }
        };
        var history = new DockPanel { Margin = new(16) };
        var dates = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(dates, Dock.Top); history.Children.Add(dates);
        var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-29), Width = 150 }; var to = new DatePicker { SelectedDate = DateTime.Today, Width = 150 };
        dates.Children.Add(from); dates.Children.Add(new TextBlock { Text = " ~ " }); dates.Children.Add(to);
        var jobs = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, Margin = new(0, 12, 0, 0), RowHeight = 32 };
        foreach (var (label, property, width) in new[] { ("파일", "FileName", 240d), ("상태", "Status", 130d), ("쪽", "Pages", 100d), ("차감 쪽수", "Credits", 85d), ("완료", "Finished", 170d) })
            jobs.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width });
        history.Children.Add(jobs);
        async Task LoadUsageAsync()
        {
            var code = period.SelectedIndex switch { 1 => "QUARTER", 2 => "YEAR", _ => "MONTH" };
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage/daily?period=" + code, cancellationToken: Lifetime.Token);
            summary.Text = Number(data, "orgRemaining");
            allocated.Text = $"남은 쪽  /  전체 {Number(data, "orgAllocated")}쪽";
            periodText.Text = $"{Value(data, "from")} ~ {Value(data, "to")}";
            balanceStatus.Text = $"남은 쪽 {Number(data, "orgRemaining")}     |     이 기간 사용 {Number(data, "usedCredits")}";
            startDate.Text = Value(data, "from"); endDate.Text = Value(data, "to");
            summary.ToolTip = "기관에 배정된 사용량입니다. 문서 유형과 처리 방식에 따라 한 쪽당 차감량이 달라집니다.";
            usedBar.Value = data.TryGetProperty("usedPercent", out var percent) && percent.TryGetDouble(out var p) ? Math.Clamp(p, 0, 100) : 0;
            usage.Text = $"사용 {Number(data, "usedCredits")}쪽 ({Value(data, "usedPercent")}%)";
            bars.Children.Clear(); var daily = data.GetProperty("daily").EnumerateArray().ToArray(); var maximum = daily.Select(d => d.GetProperty("credits").GetDouble()).DefaultIfEmpty(1).Max();
            foreach (var day in daily)
            {
                var number = day.GetProperty("credits").GetDouble();
                bars.Children.Add(new Border { Background = number > 0 ? new SolidColorBrush(Color.FromRgb(0, 95, 184)) : Brushes.LightGray,
                    Height = Math.Max(3, 134.4 * number / Math.Max(1, maximum)), Margin = new(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{Value(day, "date")}: {number}쪽" });
            }
            var sourceRows = data.GetProperty("bySource").EnumerateArray().ToArray(); var sourceTotal = sourceRows.Sum(s => s.GetProperty("credits").GetDouble());
            sources.Children.Clear(); sources.Children.Add(new TextBlock { Text = "사용 내역", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 20) });
            foreach (var source in sourceRows)
            {
                var share = sourceTotal == 0 ? 0 : source.GetProperty("credits").GetDouble() / sourceTotal * 100;
                var label = Value(source, "source") switch { "PDF_HWP" => "PDF · HWP", "IMAGE" => "이미지", "TXT" or "TEXT" => "TXT · 새 문서", var other => other };
                var row = new DockPanel { Height = 32 };
                row.Children.Add(new TextBlock { Text = label, Width = 120 });
                row.Children.Add(new TextBlock { Text = $"{Number(source, "credits")}쪽", Width = 70, FontWeight = FontWeights.SemiBold });
                row.Children.Add(new ProgressBar { Maximum = 100, Value = share, Height = 8, Style = (Style)FindResource("FlatUsageBar"), VerticalAlignment = VerticalAlignment.Center }); sources.Children.Add(row);
            }
        }
        async Task LoadJobsAsync()
        {
            if (from.SelectedDate is not { } start || to.SelectedDate is not { } end || start > end) throw new ArgumentException("조회 시작일과 종료일을 확인해 주세요.");
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage/jobs?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", cancellationToken: Lifetime.Token);
            jobs.ItemsSource = data.GetProperty("items").EnumerateArray().Select(j => new JobRow(Value(j, "jobId"), Value(j, "fileName"),
                StatusLabel(Value(j, "status"), Value(j, "failedPages") is not ("—" or "0")),
                Value(j, "donePages") + "/" + Value(j, "totalPages"), Value(j, "credits"), Value(j, "finishedAt"))).ToArray();
        }
        period.SelectionChanged += async (_, _) => await RunAsync(LoadUsageAsync);
        dates.Children.Add(Button("조회", async () => await RunAsync(LoadJobsAsync)));
        dates.Children.Add(Button("사용량으로", () => { usageTab.Content = usageScroll; return Task.CompletedTask; }));
        var jobsItem = new MenuItem { Header = "작업별 사용 내역" }; jobsItem.Click += async (_, _) => await RunAsync(async () => { await LoadJobsAsync(); usageTab.Content = history; }); viewMenu.Items.Add(jobsItem);
        var usageItem = new MenuItem { Header = "사용량" }; usageItem.Click += (_, _) => { usageTab.Content = usageScroll; tabs.SelectedIndex = 0; }; viewMenu.Items.Add(usageItem);
        void Open() { if (jobs.SelectedItem is JobRow j) { JobId = j.JobId; FileName = j.FileName; DialogResult = true; } }
        jobs.MouseDoubleClick += (_, _) => Open();
        dates.Children.Add(Button("선택 작업 열기", () => { Open(); return Task.CompletedTask; }));

        OnLoad(LoadUsageAsync);
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
    private static string Number(JsonElement j, string field) => j.TryGetProperty(field, out var v) && v.TryGetDecimal(out var n) ? n.ToString("#,0.##") : "—";
    private static string Value(JsonElement j, string field) => j.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "—";
}
