using System.Net;
using System.Net.Http;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsUsageDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture");
        var dialog = new UsageDialog(api, actions: DesktopUsageActions.Create(api, "")); dialog.Show();
        try
        {
            await WaitAsync(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "8,251"));
            await WaitAsync(() => fixture.JobRequests > 0);
            check(dialog.WindowState == WindowState.Maximized, "QA1110: My page opens maximized");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            WindowsDialogBoundsChecks.CheckMaximized(dialog, check);
            check(!Descendants(dialog).OfType<MenuItem>().Any(m => m.Header?.ToString() is "보기(_V)" or "정보(_A)"),
                "QA1110: My page omits View and Info menus");
            var logoutIcon = Descendants(dialog).OfType<SvgViewbox>().SingleOrDefault(i => i.Source?.ToString().EndsWith("account-logout.svg", StringComparison.Ordinal) == true);
            check(logoutIcon is { Width: 20, Height: 20, ActualWidth: > 0, ActualHeight: > 0, Drawings.Bounds.IsEmpty: false }, "QA1110: My page logout shows original Figma icon in 20px slot");
            dialog.WindowState = WindowState.Normal;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var asset in new[] { "account-caption-min.svg", "account-caption-max.svg", "account-caption-close.svg", "account-chevron.svg" })
            {
                var icon = Descendants(dialog).OfType<SvgViewbox>().FirstOrDefault(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true);
                check(icon is { Width: 16, Height: 16, ActualWidth: > 0, ActualHeight: > 0, Drawings.Bounds.IsEmpty: false }, $"{asset} uses the non-empty Figma drawing at its 16px design size");
            }
            var usageBar = Descendants(dialog).OfType<ProgressBar>().FirstOrDefault(b => b.Value == 17.49);
            var fill = usageBar?.Template.FindName("RangeFill", usageBar) as FrameworkElement;
            var track = usageBar?.Template.FindName("UsageTrack", usageBar) as FrameworkElement;
            var balance = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "OrganizationCreditBalance");
            check(!Descendants(dialog).OfType<FrameworkElement>().Any(e => System.Windows.Automation.AutomationProperties.GetAutomationId(e) is "OrganizationCreditAllocation" or "OrganizationCreditPurchase")
                  && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.Contains("할당", StringComparison.Ordinal)),
                "The institution credit card has no allocation caption or purchase button, as in the final design");
            var percents = Descendants(dialog).OfType<TextBlock>().Where(t => t.Text == "17%").ToArray();
            check(percents.Length == 2 && percents.All(t => DockPanel.GetDock(t) == Dock.Right && t.IsVisible),
                "Both credit cards show the whole percentage separately at the right under their bars");
            var period = Descendants(dialog).OfType<Semojum.Desktop.Controls.SegmentedChoice>().Single();
            check(period.Items.Cast<ListBoxItem>().Select(i => i.Content).SequenceEqual(new object[] { "이번 달", "지난달" }) && period is { SelectedIndex: 0, Width: 150, Height: 32 }
                  && ((ListBoxItem)period.Items[1]).IsEnabled && Grid.GetColumn(period) == 2,
                "The heading row carries the 이번 달 / 지난달 choice and both months can be chosen");
            check(!Descendants(dialog).OfType<DatePicker>().Any() && !Descendants(dialog).OfType<Button>().Any(b => b.Content is "조회" or "선택 작업 열기"),
                "The work-history card is ruled only by its 최근 30일 choice, without date fields or query buttons");
            var loginStatus = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageStatusLogin");
            check(loginStatus is { Text: "fixture", HorizontalAlignment: HorizontalAlignment.Right } && VisualTreeHelper.GetParent(loginStatus) is DockPanel { Parent: Border { Height: 28 } }
                  && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text.StartsWith("남은 크레딧", StringComparison.Ordinal)),
                "The 28px status bar shows only the login ID at the right");
            var originalWidth = dialog.Width;
            foreach (var requestedWidth in new[] { originalWidth, 960d, 480d })
            {
                dialog.Width = requestedWidth;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
                var trackWidth = track?.ActualWidth ?? 0;
                var fillWidth = fill?.ActualWidth ?? 0;
                var trackBounds = track?.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, trackWidth, track.ActualHeight)) ?? Rect.Empty;
                check(usageBar is { Value: 17.49, IsVisible: true } && trackWidth > 0 && fillWidth > 0 &&
                      Math.Abs(fillWidth - trackWidth * 0.1749) <= 1.5 &&
                      trackBounds.Left >= -1 && trackBounds.Right <= dialog.ActualWidth + 1,
                    $"The returned 17.49% usage remains proportional and visible at {dialog.ActualWidth:0.#}px (fill={fillWidth:0.##}, track={trackWidth:0.##})");
                var balanceBounds = balance.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, balance.ActualWidth, balance.ActualHeight));
                check(balance.IsVisible && balance.ActualWidth > 0 && balanceBounds.Left >= -1 && balanceBounds.Right <= dialog.ActualWidth + 1,
                    $"Organization balance remains readable at {dialog.ActualWidth:0.#}px (balance={balanceBounds})");
            }
            var originalBalance = balance.Text;
            balance.Text = "2,147,483,647";
            dialog.Width = 480;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var longBalance = balance.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, balance.ActualWidth, balance.ActualHeight));
            var measured = new FormattedText(balance.Text, CultureInfo.CurrentCulture, balance.FlowDirection,
                new Typeface(balance.FontFamily, balance.FontStyle, balance.FontWeight, balance.FontStretch), balance.FontSize,
                balance.Foreground, VisualTreeHelper.GetDpi(balance).PixelsPerDip) { MaxTextWidth = Math.Max(1, balance.ActualWidth) };
            check(balance.ActualWidth > 0 && balance.ActualHeight + 2 >= measured.Height && longBalance.Left >= -1 && longBalance.Right <= dialog.ActualWidth + 1,
                $"A long credit balance remains readable at 480px (balance={longBalance})");
            balance.Text = originalBalance;
            dialog.Width = originalWidth;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var thisMonth = UsageDialog.ThisMonthInKorea();
            check(fixture.UsageMonths.SequenceEqual(new[] { Fixture.Month(thisMonth) }), "이번 달 cards request the current KST month from /api/users/usage, the same source as the monthly graph");
            var personalBar = Descendants(dialog).OfType<ProgressBar>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "내 크레딧 사용률");
            period.SelectedIndex = 1; await WaitAsync(() => personalBar.Value == 26.4);
            check(fixture.UsageMonths.Last() == Fixture.Month(thisMonth.AddMonths(-1)) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "2,640")
                  && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "2,640 / 10,000 크레딧") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "26%") && balance.Text == "8,251",
                "지난달 loads the previous KST month: my credits and myCredits ÷ orgAllocated change while the cumulative institution balance stays");
            period.SelectedIndex = 0; await WaitAsync(() => personalBar.Value == 17.49);
            check(fixture.UsageMonths.Last() == Fixture.Month(thisMonth) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "1,749 / 10,000 크레딧"),
                "Returning to 이번 달 restores the current month's card values");
            var cardRequests = fixture.UsageMonths.Count;
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "내 작업별 크레딧") && fixture.JobRequests > 0,
                "The usage page displays the real job list without switching views");
            check(Descendants(dialog).OfType<DataGrid>().Any(g => g.Items.Cast<object>().Any(x => x.ToString()!.Contains("실제 작업.pdf", StringComparison.Ordinal))), "The job list shows the returned document");
            var badges = Descendants(dialog).OfType<Border>().Where(b => b.Child is TextBlock t && new[] { "완료", "부분 실패 1쪽", "진행 중 2/4", "대기 중" }.Contains(t.Text)).ToArray();
            check(badges.Length == 4 && badges.All(b => b.CornerRadius.TopLeft >= 9 && b.BorderThickness.Left == 1), "Returned job states are shown as four distinct Figma pill badges");
            var expectedColors = new Dictionary<string, Color> { ["완료"] = Color.FromRgb(15, 123, 15), ["부분 실패 1쪽"] = Color.FromRgb(196, 43, 28), ["진행 중 2/4"] = Color.FromRgb(157, 93, 0), ["대기 중"] = Color.FromRgb(95, 95, 95) };
            check(badges.Length == 4 && badges.All(b => b.Child is TextBlock t && b.BorderBrush is SolidColorBrush edge && t.Foreground is SolidColorBrush text && edge.Color == expectedColors[t.Text] && text.Color == edge.Color), "Each real job status uses its corresponding Figma label and color");
            check(badges.Length == 4 && badges.All(b => b.Child is TextBlock t && System.Windows.Automation.AutomationProperties.GetName(b) == t.Text), "Badge accessibility names include the same actual status and counts");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageSavedCopyHint" && t.IsVisible && t.Text.Contains("PC에만 저장한 수정본", StringComparison.Ordinal) && t.Text.Contains("수정 저장 시각", StringComparison.Ordinal)), "QA1110: work history distinguishes server save from local-only edits and conversion completion time");
            var jobGrid = Descendants(dialog).OfType<DataGrid>().Single(g => g.Items.Cast<object>().Any(x => x.ToString()!.Contains("실제 작업.pdf", StringComparison.Ordinal)));
            string Row(string filename, string property)
            {
                var row = jobGrid.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("FileName")?.GetValue(x), filename));
                return row.GetType().GetProperty(property)?.GetValue(row)?.ToString() ?? "";
            }
            check(Row("진행 중.pdf", "BadgeText") == "진행 중 2/4" && Row("진행 중.pdf", "Pages") == "4" && Row("진행 중.pdf", "Credits") == "—" && Row("진행 중.pdf", "Finished") == "—",
                "Progress appears only in the badge; the pages column shows the total and a running job shows — for credits and completion");
            check(Row("실제 작업.pdf", "Pages") == "4" && Row("실제 작업.pdf", "Finished") == "10-21 10:24" && Row("부분 실패.pdf", "Finished") == "10-21",
                "Completion uses the Figma MM-dd HH:mm form and a date-only value is not given an invented time");
            string? OpenLabel(string filename) => Descendants(jobGrid).OfType<Button>()
                .FirstOrDefault(b => b.DataContext is { } row && Equals(row.GetType().GetProperty("FileName")?.GetValue(row), filename))?.Content as string;
            check(OpenLabel("진행 중.pdf") == "이어서 열기" && OpenLabel("실제 작업.pdf") == "열기" && OpenLabel("대기.pdf") == "열기",
                "Only a converting job offers 이어서 열기; finished and waiting jobs keep 열기");
            check(Row("부분 실패.pdf", "BadgeText") == "부분 실패 1쪽", "The partial failure badge uses the actual server-provided count");
            check(Row("완료 일부 실패.pdf", "BadgeText") == "부분 실패 1쪽", "A completed job with failed pages shows partial failure as in the server contract");
            check(Row("진행 0.pdf", "BadgeText") == "진행 중 0/4", "A valid zero progress count is displayed");
            check(new[] { "진행 없음.pdf", "진행 null.pdf", "진행 음수.pdf", "진행 초과.pdf", "진행 문자.pdf", "전체 0.pdf" }.All(n => Row(n, "BadgeText") == "변환 중"), "Missing, null, negative, excessive, nonnumeric or zero-total progress never fabricates a count");
            check(new[] { "실패 없음.pdf", "실패 0.pdf", "실패 초과.pdf", "실패 음수.pdf", "실패 전체.pdf", "실패 소수.pdf", "실패 총수 없음.pdf" }.All(n => Row(n, "BadgeText") == "일부 쪽 실패"), "Partial failure omits unverifiable or inconsistent counts");
            check(Row("완료 0.pdf", "BadgeText") == "완료" && Row("완료 null.pdf", "BadgeText") == "완료", "Completed jobs keep their state for valid zero and server-null failure counts");
            check(Row("완료 오류.pdf", "BadgeText") == "상태 확인 필요", "A malformed completed failure count is not misreported as successful completion");
            check(Row("완전 실패.pdf", "BadgeText") == "변환 실패", "A fully failed job is not described as a partial failure");
            check(Row("한글.pdf", "BadgeText") == "변환 실패", "A decomposed Korean file name from the server is listed in composed form");
            check(!fixture.Paths.Any(p => p.StartsWith("/api/org/", StringComparison.Ordinal)), "My page does not call privileged organization APIs");
            if (Environment.GetEnvironmentVariable("USAGE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(dialog, capture);
            dialog.Navigation.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "월별 크레딧")
                && !Descendants(dialog).OfType<TextBlock>().Any(t => t.IsVisible && t.Text is "달마다 쓴 크레딧" or "이번 달"),
                "QA1110: billing uses monthly credits label without this-month legend");
            Grid? monthBars = null;
            await WaitAsync(() => (monthBars = Descendants(dialog).OfType<Grid>().FirstOrDefault(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "BillingMonthlyBars")) is { Children.Count: > 0 });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var firstShown = Math.Max(1, thisMonth.Month - 5);
            var shownMonths = monthBars!.Children.OfType<Border>().ToArray(); var expectedMonths = Enumerable.Range(firstShown, thisMonth.Month - firstShown + 1).Select(m => new DateOnly(thisMonth.Year, m, 1)).ToArray();
            check(shownMonths.Length == Math.Min(6, thisMonth.Month) && monthBars.Children.OfType<TextBlock>().Where(t => t.Text.EndsWith('월')).Select(t => t.Text).SequenceEqual(expectedMonths.Select(m => $"{m.Month}월"))
                  && fixture.UsageMonths.Skip(cardRequests).Order(StringComparer.Ordinal).SequenceEqual(expectedMonths.Select(Fixture.Month)),
                "This year's graph requests and draws only the six months ending with the current KST month (from January when fewer), never a future month");
            var light = Color.FromRgb(0x8F, 0xB4, 0xF5); var regular = Color.FromRgb(0x5B, 0x8D, 0xEF);
            check(shownMonths.Select(b => (b.Background as SolidColorBrush)?.Color).SequenceEqual(expectedMonths.Select(m => (Color?)(m == thisMonth ? light : regular)))
                  && shownMonths.All(b => b is { MaxWidth: 56, CornerRadius.TopLeft: 4, ActualWidth: > 0 and <= 56 }),
                "Only the current month's 56px bar uses the lighter Figma #8FB4F5; the other months use #5B8DEF");
            var expectedCredits = expectedMonths.Select(m => Fixture.Credits(Fixture.Month(m))).ToArray(); var axisTop = UsageDialog.AxisStep(expectedCredits.Max()) * 4;
            check(shownMonths.Zip(expectedCredits).All(p => Math.Abs(p.First.Height - Math.Round((double)(p.Second / axisTop) * 164)) < .5)
                  && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == axisTop.ToString("#,0", CultureInfo.InvariantCulture))
                  && monthBars.Parent is Grid { Parent: Border { Height: 260 } }
                  && Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "BillingMonthlyNotice").Text.Length == 0,
                "Bars follow each month's returned credits on a four-step axis (164px) inside the 260px Figma card");
            var kind = Descendants(dialog).OfType<ComboBox>().SingleOrDefault(c => System.Windows.Automation.AutomationProperties.GetName(c) == "상세 내역 구분");
            check(kind is { Width: 112, Height: 32, SelectedItem: "전체", ItemsSource: IEnumerable<string> kinds } && kinds.SequenceEqual(new[] { "전체", "사용", "충전" })
                  && kind.Parent is Grid { Width: 112, Height: 32 } kindField && kindField.Children.OfType<SvgViewbox>().Any(i => i is { Width: 16, Height: 16 } && i.Source?.ToString().EndsWith("account-chevron.svg", StringComparison.Ordinal) == true),
                "The 상세 내역 heading carries the final 112px 전체 / 사용 / 충전 combo styled like 최근 30일");
            var billingGrid = Descendants(dialog).OfType<DataGrid>().Single(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "BillingDetailGrid");
            var billingNotice = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "BillingDetailNotice");
            UsageDialog.BillingRow[] Shown() => billingGrid.Items.Cast<UsageDialog.BillingRow>().ToArray();
            await WaitAsync(() => Shown().Length == 3); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var shownYear = int.Parse(((string)Descendants(dialog).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "과금 연도").SelectedItem).TrimEnd('년'), CultureInfo.InvariantCulture);
            var today = UsageDialog.TodayInKorea(); var periodEnd = shownYear < today.Year ? new DateOnly(shownYear, 12, 31) : today;
            check(fixture.CreditQueries.Count == 1 && fixture.CreditQueries[0] == $"?from={shownYear:0000}-01-01&to={periodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&type=ALL" && billingNotice.Text.Length == 0,
                "상세 내역 asks /api/users/usage/credit-history once for the chosen year (to today, KST) with type=ALL");
            check(billingGrid.Columns.Select(c => c.Header as string).SequenceEqual(new[] { "날짜", "구분", "내용", "쪽수", "크레딧", "잔여 크레딧" })
                  && billingGrid.Columns.Take(5).Select(c => c.Width.Value).SequenceEqual(new[] { 110d, 270, 280, 100, 120 }) && billingGrid.RowHeight == 32
                  && billingGrid.Parent is DockPanel { Parent: Border { Height: 552 } },
                "상세 내역 is the final six-column table (날짜 · 구분 · 내용 · 쪽수 · 크레딧 · 잔여 크레딧) with 32px rows in the 552px Figma card");
            var shown = Shown();
            check(shown.Select(r => (r.Date, r.Kind, r.Description, r.Pages, r.Credits, r.Balance)).SequenceEqual(new[] {
                    ("2026-08-13", "사용", "수능특강_생명Ⅱ.pdf", "14", "-11", "5,400"), ("2026-08-01", "충전", "크레딧 추가 (주문 20260801-02)", "—", "+3,000", "5,544"), ("2026-07-28", "사용", "고2_수학Ⅰ_3단원.pdf", "26", "-26", "—") }),
                "Rows show the server's 구분, title, pages (— for a charge), signed credits and balanceAfter (— when the account has no institution)");
            var chips = Descendants(billingGrid).OfType<Border>().Where(b => b is { Height: 18, CornerRadius.TopLeft: 9 }).ToArray();
            var charge = chips.FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "충전"); var use = chips.FirstOrDefault(b => System.Windows.Automation.AutomationProperties.GetName(b) == "사용");
            var chargeAmount = Descendants(billingGrid).OfType<TextBlock>().FirstOrDefault(t => t.Text == "+3,000"); var useAmount = Descendants(billingGrid).OfType<TextBlock>().FirstOrDefault(t => t.Text == "-11");
            Color? EdgeOf(Border? b) => (b?.BorderBrush as SolidColorBrush)?.Color; Color? InkOf(TextBlock? t) => (t?.Foreground as SolidColorBrush)?.Color;
            check(EdgeOf(charge) == Color.FromRgb(15, 123, 15) && InkOf(charge?.Child as TextBlock) == Color.FromRgb(15, 123, 15) && EdgeOf(use) == Color.FromRgb(234, 234, 234) && InkOf(use?.Child as TextBlock) == Color.FromRgb(95, 95, 95)
                  && chargeAmount is { FontSize: 14 } && chargeAmount.FontWeight == FontWeights.Bold && InkOf(chargeAmount) == Color.FromRgb(15, 123, 15)
                  && useAmount is { FontSize: 14 } && useAmount.FontWeight == FontWeights.Bold && InkOf(useAmount) == Color.FromRgb(196, 43, 28),
                "구분 chips and 14px bold credits follow Figma: 사용 red amount with grey chip, 충전 green");
            if (Environment.GetEnvironmentVariable("BILLING_CAPTURE") is { Length: > 0 } billCapture) await WindowsSmoke.RenderElementAsync(dialog, billCapture);
            kind!.SelectedItem = "사용"; await WaitAsync(() => fixture.CreditQueries.Last().EndsWith("&type=USE", StringComparison.Ordinal) && Shown().Length == 2); var used = Shown();
            kind.SelectedItem = "충전"; await WaitAsync(() => fixture.CreditQueries.Last().EndsWith("&type=CHARGE", StringComparison.Ordinal) && Shown().Length == 1); var charged = Shown();
            kind.SelectedItem = "전체"; await WaitAsync(() => fixture.CreditQueries.Last().EndsWith("&type=ALL", StringComparison.Ordinal) && Shown().Length == 3);
            check(used.All(r => r.Kind == "사용") && charged.Single().Kind == "충전" && fixture.CreditQueries.Count == 4,
                "전체 / 사용 / 충전 asks the server's type=ALL / USE / CHARGE instead of guessing from the credit sign");
            fixture.CreditFails = true; kind.SelectedItem = "사용";
            await WaitAsync(() => billingNotice.Text.StartsWith("상세 내역을 불러오지 못했습니다.", StringComparison.Ordinal));
            check(billingGrid.Items.Count == 0, "A failed 상세 내역 request shows a one-line notice instead of the previous kind's rows");
            fixture.CreditFails = false; kind.SelectedItem = "전체"; await WaitAsync(() => Shown().Length == 3 && billingNotice.Text.Length == 0);
            dialog.Navigation.SelectedIndex = 3;
            await WaitAsync(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "서버가 돌려준 문의 내용"));
            check(fixture.MineRequested && fixture.PostRequests == 1, "Inquiry history loads the user's posts and selected post from the server");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "실제 운영자 답변"), "Inquiry details show the returned operator answer");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var inquiryChip = Descendants(dialog).OfType<ListBox>().SelectMany(l => Descendants(l)).OfType<Border>().FirstOrDefault(b => b is { Height: 18, CornerRadius.TopLeft: 9 });
            check(inquiryChip is { BorderBrush: SolidColorBrush answeredEdge } && answeredEdge.Color == Color.FromRgb(15, 123, 15) && System.Windows.Automation.AutomationProperties.GetName(inquiryChip) == "답변 완료"
                  && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "계정 · 발급" && t.IsVisible)
                  && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "계정 · 발급  ·  2026-10-21  ·  답변 완료") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "운영자 답변  ·  2026-10-22"),
                "Inquiry rows and detail follow Figma C0-9: 분류, date and the green 답변 완료 chip from the server status");
            var followUpBox = Descendants(dialog).OfType<TextBox>().SingleOrDefault(t => System.Windows.Automation.AutomationProperties.GetName(t) == "이어서 물을 것을 적는다");
            var sendButton = Descendants(dialog).OfType<Button>().SingleOrDefault(b => Equals(b.Content, "보내기"));
            check(followUpBox is { IsEnabled: true, AcceptsReturn: true, MaxLength: 10000 } && sendButton is { Width: 92, Height: 32, IsEnabled: false, IsDefault: false }
                  && Descendants(dialog).OfType<TextBlock>().Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "InquiryFollowUpPlaceholder" && t.Text == "이어서 물을 것을 적는다" && t.IsVisible)
                  && !Descendants(dialog).OfType<Button>().Any(b => b.Content is "이전" or "다음" or "상세 열기" or "게시판 열기" || b.Content is "더 보기" && b.IsVisible),
                "Inquiry detail ends with the Figma follow-up field and 92px 보내기 instead of the old 이전 / 다음 / 상세 열기 / 게시판 열기 buttons");
            if (Environment.GetEnvironmentVariable("INQUIRY_CAPTURE") is { Length: > 0 } inquiryCapture) await WindowsSmoke.RenderElementAsync(dialog, inquiryCapture);
        }
        finally { dialog.Close(); }
    }
    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right - .5 && a.Right > b.Left + .5 &&
        a.Top < b.Bottom - .5 && a.Bottom > b.Top + .5;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static async Task WaitAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new InvalidOperationException("Usage fixture did not load");
    }
    private sealed class Fixture : HttpMessageHandler
    {
        internal int JobRequests { get; private set; }
        internal int PostRequests { get; private set; }
        internal bool MineRequested { get; private set; }
        internal List<string> Paths { get; } = [];
        internal List<string> UsageMonths { get; } = [];
        internal List<string> CreditQueries { get; } = [];
        internal bool CreditFails;
        internal static string Month(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        /// <summary>The Figma C0-8 rows: two uses and an institution charge; the last row comes from a time without institution (balanceAfter null).</summary>
        private object CreditHistory(string query)
        {
            CreditQueries.Add(query);
            var type = query.Split('&').Select(p => p.TrimStart('?')).FirstOrDefault(p => p.StartsWith("type=", StringComparison.Ordinal))?[5..] ?? "ALL";
            var items = new[]
            {
                new { date = "2026-08-13", type = "USE", title = "수능특강_생명Ⅱ.pdf", pages = (int?)14, credits = -11L, jobId = (string?)"job_1", orderId = (string?)null, balanceAfter = (long?)5400 },
                new { date = "2026-08-01", type = "CHARGE", title = "크레딧 추가 (주문 20260801-02)", pages = (int?)null, credits = 3000L, jobId = (string?)null, orderId = (string?)"7f0c3e7e-0d0b-4b8e-9d55-1f2a3b4c5d6e", balanceAfter = (long?)5544 },
                new { date = "2026-07-28", type = "USE", title = "고2_수학Ⅰ_3단원.pdf", pages = (int?)26, credits = -26L, jobId = (string?)"job_2", orderId = (string?)null, balanceAfter = (long?)null }
            };
            return new { from = "2026-01-01", to = "2026-08-31", type, orgRemaining = 5400, items = items.Where(i => type == "ALL" || i.type == type).ToArray() };
        }
        /// <summary>This month 1,749 and last month 2,640 (the Figma 4월 value); earlier months 1,000 + 10 × month.</summary>
        internal static decimal Credits(string month)
        {
            var thisMonth = UsageDialog.ThisMonthInKorea();
            return month == Month(thisMonth) ? 1749 : month == Month(thisMonth.AddMonths(-1)) ? 2640 : 1000 + 10 * (month.Length == 7 ? int.Parse(month[5..], CultureInfo.InvariantCulture) : 0);
        }
        private object Usage(string query)
        {
            var month = query.StartsWith("?month=", StringComparison.Ordinal) ? query[7..] : ""; UsageMonths.Add(month);
            return new { month, myCredits = Credits(month), orgAllocated = 10000, orgUsed = 1749, orgRemaining = 8251 };
        }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path == "/api/users/usage/credit-history" && CreditFails)
            {
                CreditQueries.Add(request.RequestUri.Query);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"COMMON5000\",\"message\":\"실패\"}", Encoding.UTF8, "application/json") });
            }
            object result = path switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER", alias = "검수자" },
                "/api/users/usage" => Usage(request.RequestUri.Query),
                "/api/users/usage/jobs" => Jobs(),
                "/api/users/usage/credit-history" => CreditHistory(request.RequestUri.Query),
                "/api/board/posts" => Posts(request.RequestUri.Query),
                "/api/board/posts/42" => Post(),
                _ => new { }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
        private object Jobs()
        {
            JobRequests++;
            return new { items = new object[] {
                new { jobId = "job:1", fileName = "실제 작업.pdf", status = "COMPLETED", failedPages = 0, donePages = 4, totalPages = 4, credits = 4, finishedAt = "2026-10-21T10:24:00" },
                new { jobId = "job:2", fileName = "부분 실패.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1, donePages = 3, totalPages = 4, credits = 3, finishedAt = "2026-10-21" },
                new { jobId = "job:3", fileName = "진행 중.pdf", status = "IN_PROGRESS", failedPages = 0, donePages = 2, totalPages = 4, credits = 2, finishedAt = "" },
                new { jobId = "job:4", fileName = "대기.pdf", status = "PENDING", failedPages = 0, donePages = 0, totalPages = 4, credits = 0, finishedAt = "" },
                new { jobId = "job:5", fileName = "진행 0.pdf", status = "IN_PROGRESS", donePages = 0, totalPages = 4 },
                new { jobId = "job:6", fileName = "진행 없음.pdf", status = "IN_PROGRESS", totalPages = 4 },
                new { jobId = "job:7", fileName = "진행 null.pdf", status = "IN_PROGRESS", donePages = (int?)null, totalPages = 4 },
                new { jobId = "job:8", fileName = "진행 음수.pdf", status = "IN_PROGRESS", donePages = -1, totalPages = 4 },
                new { jobId = "job:9", fileName = "진행 초과.pdf", status = "IN_PROGRESS", donePages = 5, totalPages = 4 },
                new { jobId = "job:10", fileName = "진행 문자.pdf", status = "IN_PROGRESS", donePages = "2", totalPages = 4 },
                new { jobId = "job:11", fileName = "전체 0.pdf", status = "IN_PROGRESS", donePages = 0, totalPages = 0 },
                new { jobId = "job:12", fileName = "실패 없음.pdf", status = "PARTIALLY_COMPLETED", totalPages = 4 },
                new { jobId = "job:13", fileName = "실패 0.pdf", status = "PARTIALLY_COMPLETED", failedPages = 0, totalPages = 4 },
                new { jobId = "job:14", fileName = "실패 초과.pdf", status = "PARTIALLY_COMPLETED", failedPages = 5, totalPages = 4 },
                new { jobId = "job:15", fileName = "실패 음수.pdf", status = "PARTIALLY_COMPLETED", failedPages = -1, totalPages = 4 },
                new { jobId = "job:16", fileName = "실패 전체.pdf", status = "PARTIALLY_COMPLETED", failedPages = 4, totalPages = 4 },
                new { jobId = "job:17", fileName = "실패 소수.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1.5, totalPages = 4 },
                new { jobId = "job:18", fileName = "실패 총수 없음.pdf", status = "PARTIALLY_COMPLETED", failedPages = 1 },
                new { jobId = "job:19", fileName = "완료 0.pdf", status = "COMPLETED", failedPages = 0, totalPages = 4 },
                new { jobId = "job:20", fileName = "완료 null.pdf", status = "COMPLETED", failedPages = (int?)null, totalPages = 4 },
                new { jobId = "job:21", fileName = "완료 오류.pdf", status = "COMPLETED", failedPages = -1, totalPages = 4 },
                new { jobId = "job:22", fileName = "완전 실패.pdf", status = "FAILED", failedPages = 4, totalPages = 4 },
                new { jobId = "job:23", fileName = "완료 일부 실패.pdf", status = "COMPLETED", failedPages = 1, totalPages = 4 },
                new { jobId = "job:24", fileName = "\u1112\u1161\u11AB\u1100\u1173\u11AF.pdf", status = "FAILED", totalPages = 1 }
            } };
        }
        private object Posts(string query)
        {
            MineRequested = query.Contains("mine=true", StringComparison.OrdinalIgnoreCase);
            return new { page = 1, totalCount = 1, notices = Array.Empty<object>(), items = new[] { new { postId = 42, title = "서버 문의", category = "ACCOUNT", answerCount = 1, author = "검수자", secret = false, createdAt = "2026-10-21", viewCount = 1, status = "ANSWERED" } } };
        }
        private object Post()
        {
            PostRequests++;
            return new { postId = 42, title = "서버 문의", content = "서버가 돌려준 문의 내용", category = "ACCOUNT", status = "ANSWERED", author = "검수자", mine = true, secret = false, hidden = false, createdAt = "2026-10-21", updatedAt = "2026-10-21", viewCount = 1,
                attachments = Array.Empty<object>(), answers = new[] { new { answerId = 7, content = "실제 운영자 답변", mine = false, answeredAt = "2026-10-22", updatedAt = "2026-10-22", fromAuthor = false } } };
        }
    }
}
