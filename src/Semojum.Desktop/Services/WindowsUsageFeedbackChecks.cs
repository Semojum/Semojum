using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using Semojum.Feature.Usage.Wpf;

namespace Semojum.Desktop.Services;

internal static class WindowsUsageFeedbackChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        static (double? Personal, double? Organization, decimal? OrganizationUsed) Ratios(string text)
        { using var document = JsonDocument.Parse(text); return UsageDialog.UsagePercentages(document.RootElement); }
        var valid = Ratios("{\"month\":\"2026-10\",\"myCredits\":100,\"orgAllocated\":1000,\"orgUsed\":400,\"orgRemaining\":600}");
        check(valid is { Personal: 10, Organization: 40, OrganizationUsed: 400 }, "Personal and institution bars use their own server-supported numerators (myCredits ÷ orgAllocated)");
        check(Ratios("{\"myCredits\":10,\"orgAllocated\":null,\"orgUsed\":null,\"orgRemaining\":null}") == (null, null, null)
            && Ratios("{\"myCredits\":0,\"orgAllocated\":0,\"orgUsed\":0,\"orgRemaining\":0}") == (null, null, null),
            "No allocation or zero denominator never invents a usage percentage");
        var inconsistent = Ratios("{\"myCredits\":120,\"orgAllocated\":100,\"orgUsed\":0,\"orgRemaining\":120}");
        check(inconsistent.Personal is null && inconsistent.Organization is null, "Contradictory personal and institution usage values do not produce bars");
        var zero = Ratios("{\"myCredits\":0,\"orgAllocated\":100,\"orgUsed\":0,\"orgRemaining\":100}");
        check(zero is { Personal: 0, Organization: 0, OrganizationUsed: 0 }, "Verified zero usage is shown as zero rather than missing data");

        var thisMonth = UsageDialog.ThisMonthInKorea();
        // The first of the six shown months fails (when it is not the current month) to prove one failed month does not spoil the graph.
        var firstShown = Math.Max(1, thisMonth.Month - 5);
        var fixture = new Fixture { LoginAlias = "긴 계정 이름을 끝까지 확인할 수 있는 별칭", FailedUsageMonth = firstShown < thisMonth.Month ? $"{thisMonth.Year:0000}-{firstShown:00}" : null };
        using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        Window? inquiryOwner = null, postOwner = null;
        long? openedPost = null; var inquiryWritten = false;
        var actions = new UsageActions(_ => { }, owner => { inquiryOwner = owner; return inquiryWritten; },
            (owner, post) => { postOwner = owner; openedPost = post.PostId; }, _ => { }, _ => { });
        await api.LoginAsync("fixture", "fixture"); var dialog = new UsageDialog(api, actions: actions); dialog.Show();
        try
        {
            check(dialog.Navigation.Items.Cast<TabItem>().Select(Section).SequenceEqual(["사용량", "과금 이력", "계정 정보", "문의 내역", "로그아웃"]),
                "A user (점역사) account's my page hides the administrator-only 기관 관리 section");
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "600"));
            check(!Descendants(dialog).OfType<Button>().Any(b => Equals(b.Content, "쪽 구매")), "The institution credit card has no purchase button, as in the final design");
            var accountName = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageAccountName");
            check(accountName.Text == fixture.LoginAlias && Equals(accountName.ToolTip, fixture.LoginAlias)
                && System.Windows.Automation.AutomationProperties.GetName(accountName) == "사용량 계정 " + fixture.LoginAlias,
                "Usage heading shows the authenticated alias and exposes its full value");
            if (Environment.GetEnvironmentVariable("MYPAGE_TITLE_CAPTURE") is { Length: > 0 } titleCapture)
                await WindowsSmoke.RenderElementAsync(dialog, titleCapture);
            dialog.WindowState = WindowState.Normal;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var initialWidth = dialog.Width; dialog.Width = 480; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var accountBounds = accountName.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, accountName.ActualWidth, accountName.ActualHeight));
            check(accountName.IsVisible && accountName.TextTrimming == TextTrimming.CharacterEllipsis && accountBounds.Left >= 0 && accountBounds.Right <= dialog.ActualWidth + 1,
                "Long authenticated aliases remain within the narrow usage window");
            dialog.Width = initialWidth; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var bars = Descendants(dialog).OfType<ProgressBar>().ToArray();
            var personal = bars.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "내 크레딧 사용률");
            var organization = bars.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "기관 크레딧 사용률");
            check(personal.Value == 10 && organization.Value == 40 && personal.IsVisible && organization.IsVisible,
                "The visible personal and institution bars display 10% and 40% from distinct real data");
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "100 / 1,000 크레딧") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "400 / 1,000 크레딧")
                && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "10%") && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "40%"),
                "Both usage cards label their actual credit numerators and common allocation, with the percentage kept separate");
            var jobs = Descendants(dialog).OfType<DataGrid>().Single(g => g.Columns.Any(c => Equals(c.Header, "모드")));
            await Wait(() => jobs.Items.Count > 0);
            var job = jobs.Items[0];
            check(Equals(job.GetType().GetProperty("Mode")?.GetValue(job), "통합 변환") && jobs.Columns.Any(c => c is DataGridTemplateColumn && Equals(c.Header, "")),
                "The job list uses the server mode and provides a per-row open action");
            var historyCard = Descendants(dialog).OfType<Border>().Single(b => System.Windows.Automation.AutomationProperties.GetAutomationId(b) == "UsageWorkHistoryCard");
            var overviewScroll = Descendants(dialog).OfType<ScrollViewer>().Single(s => System.Windows.Automation.AutomationProperties.GetAutomationId(s) == "UsageOverviewScroll");
            var expectedHeight = Math.Max(420, overviewScroll.ViewportHeight - 212);
            check(Math.Abs(historyCard.ActualHeight - expectedHeight) < 2,
                $"Work history follows the available viewport without clipping its minimum height (height={historyCard.ActualHeight:F1}, viewport={overviewScroll.ViewportHeight:F1})");
            var oldWidth = dialog.Width; var oldHeight = dialog.Height;
            if (dialog.MaxWidth >= 1440 && dialog.MaxHeight >= 1000)
            {
                dialog.Width = 1440; dialog.Height = 1000;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
                check(historyCard.ActualHeight >= 680 && historyCard.ActualHeight <= 710,
                    $"At 1440x1000 work history matches the Figma 696px card (height={historyCard.ActualHeight:F1})");
            }
            dialog.Width = 960; dialog.Height = 400; dialog.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!Descendants(dialog).OfType<Expander>().Any() && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text is "일별 사용량" or "사용 내역" or "최근 결제"),
                "The usage page has only the heading, two credit cards and the work-history card, as in the final design");
            overviewScroll.ScrollToBottom(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var historyRect = historyCard.TransformToAncestor(overviewScroll).TransformBounds(new Rect(0, 0, historyCard.ActualWidth, historyCard.ActualHeight));
            check(historyCard.ActualHeight >= 420 && overviewScroll.ScrollableHeight > 0 && historyRect.Bottom <= overviewScroll.ViewportHeight + 2 && jobs.Items.Count > 0,
                "A short usage window scrolls to the end of the loaded work history");
            dialog.Width = oldWidth; dialog.Height = oldHeight; overviewScroll.ScrollToTop();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            dialog.Navigation.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            Grid? monthBars = null;
            await Wait(() => (monthBars = Descendants(dialog).OfType<Grid>().FirstOrDefault(g => System.Windows.Automation.AutomationProperties.GetAutomationId(g) == "BillingMonthlyBars")) is { Children.Count: > 0 });
            var monthColumns = monthBars!.Children.OfType<Border>().ToArray();
            var monthlyNotice = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "BillingMonthlyNotice").Text;
            check(monthColumns.Length == Math.Min(6, thisMonth.Month) && (fixture.FailedUsageMonth is null ? monthlyNotice.Length == 0
                    : monthlyNotice == $"{firstShown}월 사용량을 불러오지 못했습니다." && monthColumns[0].Height == 0 && monthColumns.Skip(1).All(b => b.Height > 0)
                      && monthBars.Children.OfType<TextBlock>().Any(t => Grid.GetColumn(t) == 0 && t.Text == "—")),
                "A month whose usage request fails stays an empty bar with a one-line notice while the other months are still drawn");
            dialog.Navigation.SelectedIndex = 3;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            await Wait(() => Descendants(dialog).OfType<ListBox>().Any());
            var list = Descendants(dialog).OfType<ListBox>().Single(); await Wait(() => list.Items.Count == 10);
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            check(list.Items.Count == 10 && fixture.MineOnly, "A long first inquiry page shows only the current user's server results");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var status = (TextBlock)typeof(CompactDialog).GetField("Status", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!;
            Border? Chip(int index) => list.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject row ? Descendants(row).OfType<Border>().FirstOrDefault(b => b is { Height: 18, CornerRadius.TopLeft: 9 }) : null;
            string ChipName(int index) => Chip(index) is { } chip ? System.Windows.Automation.AutomationProperties.GetName(chip) : "";
            Color? ChipColor(int index) => (Chip(index)?.BorderBrush as SolidColorBrush)?.Color;
            check(ChipName(0) == "답변 완료" && ChipColor(0) == Color.FromRgb(15, 123, 15) && Chip(0) is { IsVisible: true, BorderThickness.Left: 1, Child: TextBlock { Foreground: SolidColorBrush answeredText } } && answeredText.Color == Color.FromRgb(15, 123, 15),
                "An answered inquiry shows the server's ANSWERED as the green 답변 완료 chip");
            check(ChipName(1) == "처리 중" && ChipColor(1) == Color.FromRgb(157, 93, 0) && Chip(1) is { IsVisible: true },
                "A waiting inquiry shows the server's WAITING as the 처리 중 chip");
            check(ChipName(2) == "답변 0건" && ChipColor(2) == Color.FromRgb(138, 138, 138),
                "An older server without status keeps the neutral answer-count badge instead of inferring a state");
            TextBlock? CategoryOf(int index) => list.ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject row ? Descendants(row).OfType<TextBlock>().FirstOrDefault(t => t.Width == 100) : null;
            check(CategoryOf(0) is { Text: "계정 · 발급", IsVisible: true } && CategoryOf(1) is { Text: "점역 결과", IsVisible: true } && CategoryOf(2) is { IsVisible: false },
                "Inquiry rows show the Figma 분류 before the date and leave it out for posts written before categories");
            var firstDetail = Descendants(dialog).OfType<TextBlock>().Select(t => t.Text).ToArray();
            var threadOrder = new[] { "보낸 글", "운영자 답변  ·  2026-10-02", "이어 물은 글  ·  2026-10-03", "운영자 답변  ·  2026-10-04" }.Select(label => Array.IndexOf(firstDetail, label)).ToArray();
            check(firstDetail.Contains("계정 · 발급  ·  2026-10-01  ·  답변 완료") && threadOrder.All(i => i >= 0) && threadOrder.SequenceEqual(threadOrder.Order()),
                "Inquiry detail reads 분류 · 날짜 · 답변 완료 and keeps operator answers and the writer's follow-up in writing order");
            if (Environment.GetEnvironmentVariable("MYPAGE_INQUIRY_CAPTURE") is { Length: > 0 } inquiryCapture)
                await WindowsSmoke.RenderElementAsync(dialog, inquiryCapture);
            list.SelectedIndex = 2; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "답변 없는 문의 본문"));
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "2026-10-01  ·  답변 없음"),
                "An older inquiry without status or category reports no answers without assuming its processing status");
            list.SelectedIndex = 0; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            list.SelectedIndex = 1; await Wait(() => fixture.SecondStarted);
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "문의 내용을 불러오는 중입니다.") && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"),
                "Changing the selected inquiry clears the previous detail while the new response is pending");
            list.SelectedIndex = 0; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            fixture.FailSecond(); await Task.Delay(80);
            check(list.SelectedIndex == 0 && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"), "A late failure from a previous selection cannot replace the current detail");
            list.SelectedIndex = 1; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "문의 내용을 불러오지 못했습니다."));
            check(!Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문") && list.SelectedIndex == 1,
                "A failed selected inquiry keeps the selection without mislabelling the previous content");

            list.SelectedIndex = 0; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var followUp = Descendants(dialog).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "이어서 물을 것을 적는다");
            var send = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
            var hint = Descendants(dialog).OfType<TextBlock>().Single(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "InquiryFollowUpPlaceholder");
            check(followUp.IsEnabled && !send.IsEnabled && hint.IsVisible && !status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal),
                "The follow-up field opens empty under its placeholder and 보내기 waits for text");
            followUp.Text = "   "; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(!send.IsEnabled && !hint.IsVisible, "A blank follow-up cannot be sent");
            const string question = "12쪽도 같은 자리에서 밀립니다";
            fixture.FollowUpFails = true; followUp.Text = question; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(send.IsEnabled, "Typing a follow-up enables 보내기");
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => status.Text.StartsWith("이어 묻기를 보내지 못했습니다.", StringComparison.Ordinal));
            check(followUp.Text == question && fixture.FollowUps.Count == 0 && ChipName(0) == "답변 완료" && send.IsEnabled,
                "A rejected follow-up keeps the draft and the inquiry state for another try");
            fixture.FollowUpFails = false; send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == question) && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "계정 · 발급  ·  2026-10-01  ·  처리 중"));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            check(fixture.FollowUps.SequenceEqual([question]) && followUp.Text.Length == 0 && list.SelectedIndex == 0 && ChipName(0) == "처리 중" && ChipColor(0) == Color.FromRgb(157, 93, 0)
                  && Descendants(dialog).OfType<TextBlock>().Count(t => t.Text.StartsWith("이어 물은 글  ·  ", StringComparison.Ordinal)) == 2 && status.Text.Length == 0,
                "보내기 posts the follow-up, clears the draft, appends it to the thread and turns the inquiry back to 처리 중");

            var more = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "더 보기"));
            check(more.IsVisible && !Descendants(dialog).OfType<Button>().Any(b => b.Content is "이전" or "다음" or "상세 열기" or "게시판 열기"),
                "The final list has no 이전 / 다음 / 상세 열기 / 게시판 열기 buttons, only 더 보기 while more than ten inquiries exist");
            list.SelectedIndex = 2; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "답변 없는 문의 본문"));
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => fixture.PageTwoRequests >= 1);
            await Wait(() => status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal));
            status.Text = ""; more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Wait(() => fixture.PageTwoRequests >= 2);
            await Wait(() => status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal)); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(fixture.PageTwoRequests == 2 && list.SelectedIndex == 2 && list.Items.Count == 10 && more.IsVisible,
                "A failed 더 보기 request keeps the ten rows and the selection, and retries the same next page");
            fixture.PageTwoFails = false; more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => list.Items.Count == 11); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            check(list.SelectedIndex == 2 && ((BoardRow)list.Items[10]).PostId == 11 && !more.IsVisible && !status.Text.StartsWith("문의 내역을 불러오지 못했습니다.", StringComparison.Ordinal),
                "더 보기 adds the eleventh inquiry below without moving the selection, clears the earlier error and hides itself at the end");

            list.SelectedIndex = 0; await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            status.Text = "다른 화면의 상태 메시지";
            var attachmentLink = Descendants(dialog).OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Hyperlink>()).Single();
            attachmentLink.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
            await Wait(() => openedPost is not null && list.Items.Count == 10 && Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문"));
            check(ReferenceEquals(postOwner, dialog) && openedPost == 1 && list.SelectedIndex == 0, "An inquiry with attachments still opens its board post from the attachment line, then rereads the list");
            check(status.Text == "다른 화면의 상태 메시지", "Successful inquiry loading does not erase unrelated status messages");
            Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "새 문의")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(ReferenceEquals(inquiryOwner, dialog), "New inquiry action is routed to the supplied owner");
            fixture.NoPosts = true; inquiryWritten = true;
            Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "새 문의")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "보낸 문의가 없습니다."));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            check(list.Items.Count == 0 && !Descendants(dialog).OfType<TextBlock>().Any(t => t.Text == "첫 문의 본문") && !followUp.IsEnabled && !send.IsEnabled && !more.IsVisible,
                "An empty inquiry history clears the former detail and leaves nothing to send or load");
            if (Environment.GetEnvironmentVariable("USAGE_EMPTY_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(dialog, capture);
            fixture.NoPosts = false; inquiryWritten = false;
        }
        finally { dialog.Close(); }

        var fallbackFixture = new Fixture(); using var fallbackHttp = new HttpClient(fallbackFixture); var fallbackApi = new SemojumApiClient(fallbackHttp);
        await fallbackApi.LoginAsync("fallback-id", "fixture");
        var fallbackDialog = new UsageDialog(fallbackApi, actions: DesktopUsageActions.Create(fallbackApi, "")); fallbackDialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); fallbackDialog.UpdateLayout();
            check(!Descendants(fallbackDialog).OfType<TextBlock>().Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageAccountName")
                && Descendants(fallbackDialog).OfType<TextBlock>().Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageStatusLogin" && t.Text == "fallback-id"),
                "Without an alias the heading leaves the nickname empty and the login ID stays only in the status bar");
        }
        finally { fallbackDialog.Close(); }

        using var anonymousHttp = new HttpClient(new Fixture()); var anonymousApi = new SemojumApiClient(anonymousHttp);
        var anonymousDialog = new UsageDialog(anonymousApi, actions: DesktopUsageActions.Create(anonymousApi, "")); anonymousDialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); anonymousDialog.UpdateLayout();
            check(!Descendants(anonymousDialog).OfType<TextBlock>().Any(t => System.Windows.Automation.AutomationProperties.GetAutomationId(t) == "UsageAccountName"),
                "Usage heading never invents a name without an authenticated account");
        }
        finally { anonymousDialog.Close(); }

        // An administrator's my page has six sections; sections are found by tab, so 문의 내역 and 로그아웃 still work after the inserted 기관 관리.
        var adminFixture = new Fixture { LoginRole = "ROLE_ORG_ADMIN" }; using var adminHttp = new HttpClient(adminFixture); var adminApi = new SemojumApiClient(adminHttp);
        await adminApi.LoginAsync("org-admin", "fixture");
        Window? institutionOwner = null; string[] adminSections = []; var adminLabel = ""; var adminInquiries = false; var adminOpenColumn = Visibility.Visible;
        var adminDialog = new UsageDialog(adminApi, actions: new UsageActions(_ => { }, _ => false, (_, _) => { }, _ => { }, owner => institutionOwner = owner));
        var adminResult = await DriveModalAsync(adminDialog, async () =>
        {
            var tabs = adminDialog.Navigation.Items.Cast<TabItem>().ToArray(); adminSections = tabs.Select(Section).ToArray();
            adminLabel = tabs.FirstOrDefault(t => Section(t) == "기관 관리")?.Header is Panel header ? header.Children.OfType<TextBlock>().First().Text : "";
            DataGrid? jobs = null;
            await Wait(() => (jobs = Descendants(adminDialog).OfType<DataGrid>().FirstOrDefault(g => g.Columns.Any(c => Equals(c.Header, "모드")))) is { Items.Count: > 0 });
            adminOpenColumn = jobs!.Columns.Last().Visibility;
            adminDialog.Navigation.SelectedItem = tabs.Single(t => Section(t) == "문의 내역");
            await Wait(() => Descendants(adminDialog).OfType<ListBox>().Any(l => l.Items.Count == 10)); adminInquiries = true;
            adminDialog.Navigation.SelectedItem = tabs.Single(t => Section(t) == "기관 관리");
        });
        check(adminSections.SequenceEqual(["사용량", "과금 이력", "계정 정보", "기관 관리", "문의 내역", "로그아웃"]) && adminLabel == "관리자",
            "An administrator's my page shows the six final sections with 「기관 관리 · 관리자」 between 계정 정보 and 문의 내역");
        check(adminInquiries && adminFixture.MineOnly, "With 기관 관리 inserted, the 문의 내역 section still loads the inquiry history");
        check(adminOpenColumn == Visibility.Collapsed, "An administrator's work list offers no 열기 into the editor");
        check(adminResult == true && ReferenceEquals(institutionOwner, adminDialog) && !adminDialog.LogoutRequested && adminDialog.JobId is null,
            "Choosing 기관 관리 closes my page and brings the administrator's 기관 관리 window forward instead of logging out");
        var adminLogoutDialog = new UsageDialog(adminApi, actions: new UsageActions(_ => { }, _ => false, (_, _) => { }, _ => { }, _ => { }));
        var logoutResult = await DriveModalAsync(adminLogoutDialog, () =>
        {
            adminLogoutDialog.Navigation.SelectedItem = adminLogoutDialog.Navigation.Items.Cast<TabItem>().Single(t => Section(t) == "로그아웃");
            return Task.CompletedTask;
        });
        check(logoutResult == true && adminLogoutDialog.LogoutRequested, "The administrator's 로그아웃 section, now the sixth tab, still requests logout");
    }
    private static string Section(TabItem tab) => tab.Header as string ?? Descendants((Panel)tab.Header).OfType<TextBlock>().Last().Text;
    // Shows a my page modally (its sections close it through DialogResult) while the interaction runs inside the modal loop.
    private static async Task<bool?> DriveModalAsync(Window dialog, Func<Task> interaction)
    {
        Exception? failure = null;
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await interaction(); }
            catch (Exception ex) { failure = ex; }
            finally { if (dialog.IsVisible) dialog.DialogResult = false; }
        }), DispatcherPriority.ApplicationIdle);
        var result = await dialog.Dispatcher.InvokeAsync(dialog.ShowDialog);
        if (failure is not null) throw failure;
        return result;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static async Task Wait(Func<bool> predicate)
    { for (var i = 0; i < 250 && !predicate(); i++) await Task.Delay(20); if (!predicate()) throw new TimeoutException("Usage feedback fixture did not reach its expected state."); }
    private sealed class Fixture : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> second = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool MineOnly; internal bool SecondStarted; internal bool PageTwoFails = true; internal int PageTwoRequests; internal string? LoginAlias; internal string LoginRole = "ROLE_USER"; internal string? FailedUsageMonth;
        // Inquiry 1 starts answered (an operator answer, the writer's follow-up, another answer); a follow-up sent here returns it to WAITING.
        internal bool FollowUpFails; internal bool NoPosts; internal List<string> FollowUps { get; } = []; private string firstStatus = "ANSWERED";
        internal void FailSecond() => second.TrySetResult(Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"DENIED\",\"message\":\"접근할 수 없습니다\"}"));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/board/posts/2") { SecondStarted = true; return second.Task; }
            if (path == "/api/board/posts/1/follow-ups" && request.Method == HttpMethod.Post) return FollowUpAsync(request, token);
            if (path == "/api/users/usage" && FailedUsageMonth is { } failedMonth && request.RequestUri.Query.Contains("month=" + failedMonth, StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"ERROR\",\"message\":\"실패\"}"));
            if (path == "/api/board/posts" && request.RequestUri.Query.Contains("page=2", StringComparison.OrdinalIgnoreCase))
            {
                PageTwoRequests++;
                if (PageTwoFails) return Task.FromResult(Json(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"ERROR\",\"message\":\"실패\"}"));
                return Ok(new { page = 2, totalCount = 11, notices = Array.Empty<object>(), items = new[] { new { postId = 11, title = "문의 11", category = "USAGE", answerCount = 0, author = "나", secret = false, createdAt = "2026-09-01", viewCount = 1, status = "WAITING" } } });
            }
            object result = path switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = LoginRole, alias = LoginAlias },
                "/api/users/usage" => new { myCredits = 100, orgAllocated = 1000, orgUsed = 400, orgRemaining = 600 },
                "/api/users/usage/jobs" => new { items = new[] { new { jobId = "job", fileName = "작업.pdf", mode = "c", status = "COMPLETED", donePages = 1, totalPages = 1, failedPages = 0, credits = 1, finishedAt = "2026-10-30" } } },
                "/api/board/posts" => Posts(request.RequestUri.Query),
                "/api/board/posts/1" => new { postId = 1, title = "첫 문의", content = "첫 문의 본문", category = "ACCOUNT", author = "나", mine = true, secret = false, hidden = false, createdAt = "2026-10-01", updatedAt = "2026-10-01", viewCount = 1,
                    attachments = new[] { new { attachmentId = 5, fileName = "화면.png", size = 1024, url = "https://example.invalid/attachment.png" } },
                    answers = new[] { new { answerId = 1L, content = "첫 답변", mine = false, answeredAt = "2026-10-02", updatedAt = "2026-10-02", fromAuthor = false }, new { answerId = 2L, content = "추가 질문", mine = true, answeredAt = "2026-10-03", updatedAt = "2026-10-03", fromAuthor = true },
                        new { answerId = 3L, content = "둘째 답변", mine = false, answeredAt = "2026-10-04", updatedAt = "2026-10-04", fromAuthor = false } }
                        .Concat(FollowUps.Select((text, i) => new { answerId = 10L + i, content = text, mine = true, answeredAt = "2026-11-01T21:40:00", updatedAt = "2026-11-01T21:40:00", fromAuthor = true })).ToArray(), status = firstStatus },
                "/api/board/posts/3" => new { postId = 3, title = "셋째 문의", content = "답변 없는 문의 본문", author = "나", mine = true, secret = false, hidden = false, createdAt = "2026-10-01", updatedAt = "2026-10-01", viewCount = 1, attachments = Array.Empty<object>(), answers = Array.Empty<object>() },
                _ => new { }
            };
            return Ok(result);
        }
        private object Posts(string query)
        {
            MineOnly = query.Contains("mine=true", StringComparison.OrdinalIgnoreCase);
            if (NoPosts) return new { page = 1, totalCount = 0, notices = Array.Empty<object>(), items = Array.Empty<object>() };
            // 1 answered (계정 · 발급), 2 waiting (점역 결과), 3 from an older server without category or status.
            return new { page = 1, totalCount = 11, notices = Array.Empty<object>(), items = Enumerable.Range(1, 10).Select(i => new { postId = i, title = $"문의 {i}",
                category = i switch { 1 => "ACCOUNT", 2 => "RESULT", 3 => null, _ => (string?)"USAGE" }, answerCount = i == 1 ? 2 : i > 3 ? 1 : 0, author = "나", secret = false, createdAt = "2026-10-01", viewCount = 1,
                status = i switch { 1 => firstStatus, 2 => "WAITING", 3 => null, _ => (string?)"ANSWERED" } }).ToArray() };
        }
        private async Task<HttpResponseMessage> FollowUpAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (FollowUpFails) return Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"COMMON4003\",\"message\":\"권한이 없습니다.\"}");
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var content = body.RootElement.GetProperty("content").GetString()!; FollowUps.Add(content); firstStatus = "WAITING";
            return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { isSuccess = true, result = new { answerId = 9 + FollowUps.Count, content, mine = true, answeredAt = "2026-11-01T21:40:00", updatedAt = "2026-11-01T21:40:00", fromAuthor = true } }));
        }
        private static Task<HttpResponseMessage> Ok(object result) => Task.FromResult(Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { isSuccess = true, result })));
        private static HttpResponseMessage Json(HttpStatusCode status, string text) => new(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
