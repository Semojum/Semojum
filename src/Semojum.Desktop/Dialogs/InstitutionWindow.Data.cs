using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

public sealed partial class InstitutionWindow
{
    private InstitutionClient Client => new(api);
    private bool refreshing;
    private string? receiptEmail;
    private void ClearInstitutionData()
    {
        AccountRequest.IsEnabled = CreditRequest.IsEnabled = false;
        Identity.Text = "정보 없음"; Allocated.Text = Balance.Text = Contract.Text = "—"; CreditDetail.Text = "사용량 정보 없음"; UsagePercent.Text = "정보 없음";
        ContractDetail.Text = "계약 정보 없음"; ContractRemaining.Text = "정보 없음"; StatusSummary.Text = "기관 정보 없음"; UsageBar.Value = 0; UsageBar.Visibility = Visibility.Collapsed;
        RenderMonthlyUsage([]); Accounts.ItemsSource = Requests.ItemsSource = Notices.ItemsSource = Orders.ItemsSource = null; receiptEmail = null; ReceiptAddress.Text = "증빙 받는 사람 정보 없음";
        SessionIdentity.Text = api.Account?.LoginId ?? "로그인 정보 없음";
    }
    private async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true; Refresh.IsEnabled = false; ErrorText.Text = "";
        ClearInstitutionData();
        var errors = new List<string>();
        async Task Load(string label, Func<Task> load)
        {
            try { await load(); }
            catch (OperationCanceledException) when (Lifetime.IsCancellationRequested) { }
            catch (Exception ex) { errors.Add(label + ": " + ex.Message); }
        }
        try
        {
            await Load("기관 정보", async () =>
            {
                var value = await Client.DashboardAsync(Lifetime.Token);
                Identity.Text = value.OrgName + " · " + value.OrgCode;
                Allocated.Text = value.CreditAllocated.ToString("N0"); Balance.Text = value.CreditRemaining.ToString("N0");
                CreditDetail.Text = $"{value.CreditUsed:N0} / {value.CreditAllocated:N0} 크레딧";
                UsagePercent.Text = UsageRatio(value.CreditAllocated, value.CreditUsed);
                UsageBar.Visibility = UsagePercent.Text == "정보 없음" ? Visibility.Collapsed : Visibility.Visible;
                UsageBar.Value = value.CreditAllocated > 0 && value.CreditUsed >= 0 ? Math.Clamp(100d * value.CreditUsed / value.CreditAllocated, 0, 100) : 0;
                Contract.Text = ValidDate(value.ContractExpiresAt) ? value.ContractExpiresAt! : "정보 없음";
                ContractDetail.Text = ValidDate(value.ContractStartedAt) ? "시작 " + value.ContractStartedAt : "시작일 정보 없음";
                ContractRemaining.Text = RemainingDays(value.ContractExpiresAt, TodayInKorea());
                StatusSummary.Text = $"기관 {value.OrgName}  |  남은 크레딧 {value.CreditRemaining:N0}  |  계약 만료 {ContractRemaining.Text}";
                RenderMonthlyUsage(value.MonthlyUsage);
                AccountRequest.IsEnabled = CreditRequest.IsEnabled = true; AccountRequest.ToolTip = CreditRequest.ToolTip = "운영자에게 요청을 보냅니다.";
            });
            await Load("소속 계정", async () => Accounts.ItemsSource = (await Client.AccountsAsync(Lifetime.Token)).Items);
            await Load("요청 내역", async () => Requests.ItemsSource = await Client.RequestsAsync(Lifetime.Token));
            await Load("공지", async () => Notices.ItemsSource = await Client.NoticesAsync(Lifetime.Token));
            await Load("주문 내역", async () =>
            {
                var orders = await Client.OrdersAsync(Lifetime.Token); Orders.ItemsSource = orders.Items; receiptEmail = orders.ReceiptEmail;
                ReceiptAddress.Text = orders.ReceiptEmail is { Length: > 0 } email ? "증빙 받는 사람: " + email : "증빙 받는 사람 정보 없음";
            });
            if (api.Account?.Role != "ROLE_ORG_ADMIN")
            {
                ClearInstitutionData(); SessionIdentity.Text = "로그인 정보 없음";
                ErrorText.Text = "기관 관리자 권한을 다시 확인해야 합니다. 로그아웃 후 다시 로그인해 주세요.";
            }
            else if (errors.Count > 0) ErrorText.Text = string.Join("\n", errors);
        }
        finally { refreshing = false; Refresh.IsEnabled = true; }
    }
    internal static string UsageRatio(long allocated, long used) => allocated <= 0 || used < 0 ? "정보 없음"
        : decimal.Round((decimal)used * 100 / allocated, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%";
    internal static decimal? SixMonthAverage(IReadOnlyList<InstitutionMonthlyUsage> months)
    {
        if (months.Count != 6 || months.Any(m => m.Credits < 0)) return null;
        var parsed = new List<DateTime>(6);
        foreach (var month in months.OrderBy(m => m.Month, StringComparer.Ordinal))
        {
            if (!DateTime.TryParseExact(month.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
            parsed.Add(date);
        }
        parsed.Sort();
        if (parsed.Zip(parsed.Skip(1), (a, b) => (long)a.Year * 12 + a.Month + 1 == (long)b.Year * 12 + b.Month).Any(consecutive => !consecutive)) return null;
        return decimal.Round(months.Sum(m => (decimal)m.Credits) / 6m, 0, MidpointRounding.AwayFromZero);
    }
    internal void RenderMonthlyUsage(IReadOnlyList<InstitutionMonthlyUsage> months)
    {
        Chart.Children.Clear();
        var usable = months.Count > 0 && months.All(m => m.Credits >= 0);
        ChartArea.Visibility = usable ? Visibility.Visible : Visibility.Collapsed;
        ChartEmpty.Text = months.Count == 0 ? "월별 사용량 정보 없음" : "월별 사용량을 표시할 수 없습니다.";
        ChartEmpty.Visibility = usable ? Visibility.Collapsed : Visibility.Visible;
        var average = SixMonthAverage(months);
        ChartAverage.Text = average.HasValue ? $"6개월 평균 {average.Value:N0}" : "6개월 평균 정보 없음";
        ChartAverage.ToolTip = ChartAverage.Text;
        if (!usable) { ChartMaximum.Text = ChartMiddle.Text = ChartLower.Text = ""; return; }
        var maximum = months.Max(m => m.Credits);
        ChartMaximum.Text = maximum.ToString("N0");
        ChartMiddle.Text = decimal.Round((decimal)maximum * 2m / 3m, 0, MidpointRounding.AwayFromZero).ToString("N0");
        ChartLower.Text = decimal.Round((decimal)maximum / 3m, 0, MidpointRounding.AwayFromZero).ToString("N0");
        ChartMaximum.ToolTip = ChartMaximum.Text; ChartMiddle.ToolTip = ChartMiddle.Text; ChartLower.ToolTip = ChartLower.Text;
        foreach (var month in months.OrderBy(m => m.Month, StringComparer.Ordinal))
        {
            var column = new Grid { Width = 72, Height = 152, Margin = new(0, 0, 4, 0) };
            column.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); column.RowDefinitions.Add(new() { Height = new(22) });
            var bar = new Border { Height = maximum > 0 ? Math.Clamp(110d * month.Credits / maximum, 0, 110) : 0, Background = ChartBarBrush(), CornerRadius = new(3), Margin = new(5, 0, 5, 0), VerticalAlignment = VerticalAlignment.Bottom, ToolTip = month.Month + " · " + month.Credits.ToString("N0") };
            System.Windows.Automation.AutomationProperties.SetName(bar, month.Month + " 사용 크레딧 " + month.Credits.ToString("N0")); column.Children.Add(bar);
            var amount = new TextBlock { Text = month.Credits.ToString("N0"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = month.Credits.ToString("N0"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, bar.Height + 2) }; column.Children.Add(amount);
            var monthLabel = DateTime.TryParseExact(month.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.Month + "월" : month.Month;
            var label = new TextBlock { Text = monthLabel, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = month.Month, HorizontalAlignment = HorizontalAlignment.Center }; Grid.SetRow(label, 1); column.Children.Add(label); Chart.Children.Add(column);
        }
        SizeChartColumns(); ApplyChartContrast(chartHighContrast);
    }
    private System.Windows.Media.Brush ChartBarBrush() => chartHighContrast ? SystemColors.HighlightBrush : FigmaDialogElements.Brush("#5B8DEF");
    internal void ApplyChartContrast(bool highContrast)
    {
        chartHighContrast = highContrast;
        if (chartCard is not null)
        {
            chartCard.Background = highContrast ? SystemColors.WindowBrush : System.Windows.Media.Brushes.White;
            chartCard.BorderBrush = highContrast ? SystemColors.WindowTextBrush : FigmaDialogElements.Brush("#EAEAEA");
            if (chartCard.Child is StackPanel { Children.Count: > 0 } panel && panel.Children[0] is TextBlock title)
                title.Foreground = highContrast ? SystemColors.WindowTextBrush : FigmaDialogElements.Brush("#1A1A1A");
        }
        var text = highContrast ? SystemColors.WindowTextBrush : FigmaDialogElements.Brush("#5F5F5F");
        var axis = highContrast ? SystemColors.WindowTextBrush : FigmaDialogElements.Brush("#8A8A8A");
        ChartMaximum.Foreground = ChartMiddle.Foreground = ChartLower.Foreground = axis;
        ChartAverage.Foreground = ChartEmpty.Foreground = chartLegendText.Foreground = text;
        chartLegendDot.Background = ChartBarBrush();
        foreach (var column in Chart.Children.OfType<Grid>())
        foreach (var child in column.Children)
        {
            if (child is Border bar) bar.Background = ChartBarBrush();
            else if (child is TextBlock label) label.Foreground = text;
        }
    }
    private void SizeChartColumns()
    {
        if (Chart.Children.Count == 0) return;
        var available = ChartScroll.ActualWidth > 0 ? ChartScroll.ActualWidth : ChartScroll.ViewportWidth;
        var width = Math.Clamp(available / Chart.Children.Count - 4, 60, 100);
        foreach (var column in Chart.Children.OfType<FrameworkElement>()) column.Width = width;
    }
    private static bool ValidDate(string? date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    private static DateOnly TodayInKorea() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Korea Standard Time")).DateTime);
    internal static string RemainingDays(string? date, DateOnly today)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)) return "정보 없음";
        var days = expires.DayNumber - today.DayNumber;
        return days > 0 ? $"{days:N0}일 남음" : days == 0 ? "오늘 만료" : $"{-(long)days:N0}일 경과";
    }
    private async Task RequestAccountAsync() => await RequestAsync(true);
    private async Task RequestCreditAsync() => await RequestAsync(false);
    private async Task RequestAsync(bool account)
    {
        var dialog = new InstitutionRequestDialog(account, async message => { await Client.CreateRequestAsync(account ? "ACCOUNT_ISSUE" : "CREDIT_ADD", message, Lifetime.Token); }) { Owner = this };
        if (dialog.ShowDialog() == true) await RefreshAsync();
    }
    private async Task CancelSelectedRequestAsync()
    {
        if (Requests.SelectedItem is not InstitutionRequest { Status: "OPEN", Type: "CREDIT_ADD" or "ACCOUNT_ISSUE" or "ERROR_REPORT" } request) return;
        if (NoticeDialog.Choose(this, "이 요청을 취소하시겠습니까?", "처리 중인 요청만 취소할 수 있습니다.", ["요청 취소", "돌아가기"]) != 0) return;
        try { await Client.CancelRequestAsync(request.Id, Lifetime.Token); await RefreshAsync(); }
        catch (Exception ex) { ErrorText.Text = "요청을 취소하지 못했습니다. " + ex.Message; }
    }
    private async Task OpenAccountAsync(InstitutionAccount account)
    {
        try
        {
            var jobs = await Client.AccountJobsAsync(account.LoginId, ct: Lifetime.Token);
            var detail = new InstitutionAccountDetailDialog(account, jobs, async locked => { await Client.SetLockAsync(account.LoginId, locked, Lifetime.Token); }) { Owner = this };
            if (detail.ShowDialog() == true) await RefreshAsync();
        }
        catch (Exception ex) { ErrorText.Text = "계정 정보를 불러오지 못했습니다. " + ex.Message; }
    }
    private async Task OpenNoticeAsync(InstitutionNotice notice)
    {
        var dialog = new InstitutionNoticeDialog(notice, CreditRequest.IsEnabled) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.CreditRequested) await RequestCreditAsync();
    }
    private Task OpenOrderAsync(InstitutionOrder order)
    {
        var dialog = new InstitutionOrderDialog(order, receiptEmail, () => OpenReceiptForAsync(order)) { Owner = this };
        dialog.ShowDialog(); return Task.CompletedTask;
    }
    private async Task OpenReceiptAsync()
    {
        if (Orders.SelectedItem is not InstitutionOrder order || string.IsNullOrWhiteSpace(order.ReceiptFileName)) return;
        await OpenReceiptForAsync(order);
    }
    internal Action<string> ReceiptLauncher { get; set; } = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private async Task OpenReceiptForAsync(InstitutionOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.ReceiptFileName)) return;
        var marker = api.SessionMarker;
        var owner = api.Account;
        if (marker is null || marker != institutionSessionMarker || owner?.Role != "ROLE_ORG_ADMIN" || api.SessionMarker != marker) return;
        var lifetime = Lifetime.Token;
        try
        {
            var receipt = await Client.ReceiptAsync(order.Id, lifetime);
            // A late response from a closed window or an earlier login must never launch a URL.
            // A token refresh retains the marker; logout and even a same-account login replace it.
            if (lifetime.IsCancellationRequested || !IsVisible || api.SessionMarker != marker || api.Account is not { Role: "ROLE_ORG_ADMIN" } current || current.LoginId != owner.LoginId) return;
            ReceiptLauncher(receipt.Url);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { if (!lifetime.IsCancellationRequested && IsVisible && api.SessionMarker == marker && api.Account is { Role: "ROLE_ORG_ADMIN" } current && current.LoginId == owner.LoginId) ErrorText.Text = "증빙을 열지 못했습니다. " + ex.Message; }
    }
}
