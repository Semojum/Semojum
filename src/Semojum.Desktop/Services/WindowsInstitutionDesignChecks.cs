using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsInstitutionDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var handler = new Fixture(); using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var api = new SemojumApiClient(http) { RefreshRequiresIdentity = a => a.Role == "ROLE_ORG_ADMIN" };
        await api.LoginAsync("org", "fixture"); var allowLogout = false; var logoutCalls = 0;
        var window = new InstitutionWindow(api, () => { logoutCalls++; return Task.FromResult(allowLogout); }) { Width = 960, Height = 650 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await WaitFor(() => !window.Refreshing && window.Accounts.Items.Count > 0);
            var table = window.Accounts;
            var header = new DataGridColumnHeader { Style = table.ColumnHeaderStyle };
            var cell = new DataGridCell { Style = table.CellStyle };
            check(header.Background is SolidColorBrush headerFill && headerFill.Color == Color.FromRgb(0xF4, 0xF6, 0xF9)
                && header.Foreground is SolidColorBrush headerText && headerText.Color == Color.FromRgb(0x5F, 0x5F, 0x5F)
                && header.Padding == new Thickness(8, 0, 8, 0) && table.ColumnHeaderHeight == 24
                && table.HorizontalGridLinesBrush is SolidColorBrush separator && separator.Color == Color.FromRgb(0xEA, 0xEA, 0xEA)
                && cell.Padding == new Thickness(8, 0, 8, 0),
                "Institution tables use the Figma header, 8px content inset and row separator colors");
            check(window.Identity.Text == "테스트 기관 · O1" && window.Allocated.Text == "1,000" && window.Balance.Text == "700" && window.UsageBar.Value == 30,
                "Institution dashboard displays actual server organization and credit values");
            // TextBlock.Text does not follow Inlines edits, so the status line is read from its runs; " | " marks each vertical rule.
            string StatusLine() => string.Concat(window.StatusSummary.Inlines.Select(i => (i as System.Windows.Documents.Run)?.Text ?? " | "));
            check(window.UsagePercent.Text == "30%" && window.ContractRemaining.Text != "정보 없음"
                && StatusLine() is var statusLine && statusLine.StartsWith("기관 테스트 기관 | 남은 크레딧 700 | 계약 만료 ", StringComparison.Ordinal)
                && statusLine.EndsWith(" | ", StringComparison.Ordinal) && !statusLine.Contains("남음", StringComparison.Ordinal)
                && window.SessionIdentity.IsVisible && window.SessionIdentity.Text == (api.Account?.LoginId ?? "로그인 정보 없음"),
                "Institution percentage, contract days and the status bar (기관 | 남은 크레딧 | 계약 만료 N일 전 | ID) use the actual dashboard response");
            check(window.Chart.Children.Count == 1 && window.ChartMaximum.Text == "300" && window.ChartAverage.Text == "6개월 평균 정보 없음",
                "An incomplete monthly response shows its real axis and does not invent a six-month average");
            var sixMonths = new[] { new InstitutionMonthlyUsage("2026-03", 2100), new("2026-04", 2600), new("2026-05", 3100), new("2026-06", 2800), new("2026-07", 3900), new("2026-08", 4600) };
            window.RenderMonthlyUsage(sixMonths); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(window.Chart.Children.Count == 6 && window.ChartMaximum.Text == "4,600" && window.ChartMiddle.Text == "3,067" && window.ChartLower.Text == "1,533" && window.ChartAverage.Text == "6개월 평균 3,183",
                "The monthly chart shows the six real values, proportional axis ticks and calculated Figma average");
            if (Environment.GetEnvironmentVariable("INSTITUTION_MONTHLY_CAPTURE") is { Length: > 0 } monthlyCapture)
            {
                window.Width = 1440; window.Height = 1000; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                await WindowsSmoke.RenderElementAsync(window, monthlyCapture);
            }
            window.Width = 480; window.Height = 650; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var legendLabel = Descendants(window).OfType<TextBlock>().Single(t => t.Text == "월 사용 크레딧");
            var legendRect = legendLabel.TransformToAncestor(window).TransformBounds(new Rect(0, 0, legendLabel.ActualWidth, legendLabel.ActualHeight));
            var averageRect = window.ChartAverage.TransformToAncestor(window).TransformBounds(new Rect(0, 0, window.ChartAverage.ActualWidth, window.ChartAverage.ActualHeight));
            check(window.ChartScroll.ScrollableWidth > 0 && legendLabel.IsVisible && window.ChartAverage.IsVisible
                && (legendRect.Bottom <= averageRect.Top + .5 || averageRect.Bottom <= legendRect.Top + .5 || legendRect.Right <= averageRect.Left + .5 || averageRect.Right <= legendRect.Left + .5),
                "At 480px the chart scrolls and the real legend and average remain readable without overlapping");
            window.Width = 960; window.Height = 650; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(InstitutionUsage.SixMonthAverage([]) is null && InstitutionUsage.SixMonthAverage(sixMonths[..5]) is null
                && InstitutionUsage.SixMonthAverage([sixMonths[0], sixMonths[0], .. sixMonths[2..]]) is null
                && InstitutionUsage.SixMonthAverage([.. sixMonths[..5], new("2026-09", 1)]) is null
                && InstitutionUsage.SixMonthAverage([.. sixMonths[..5], new("invalid", 1)]) is null
                && InstitutionUsage.SixMonthAverage([.. sixMonths[..5], new("2026-08", -1)]) is null,
                "The monthly average is unavailable for missing, duplicate, discontinuous, malformed or negative values");
            var zeroMonths = Enumerable.Range(0, 6).Select(i => new InstitutionMonthlyUsage(new DateTime(2025, 11, 1).AddMonths(i).ToString("yyyy-MM"), 0)).ToArray();
            var hugeMonths = zeroMonths.Select(m => m with { Credits = long.MaxValue }).ToArray();
            check(InstitutionUsage.SixMonthAverage(zeroMonths) == 0 && InstitutionUsage.SixMonthAverage(hugeMonths) == long.MaxValue,
                "Six-month averages handle zero and maximum 64-bit credit amounts without overflow");
            window.RenderMonthlyUsage(hugeMonths); window.UpdateLayout();
            check(window.ChartMaximum.Text == long.MaxValue.ToString("N0") && window.ChartAverage.Text.Contains(long.MaxValue.ToString("N0"))
                && window.Chart.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Border>()).All(b => b.Height is >= 0 and <= 110),
                "Large chart values retain exact labels and bounded bar heights");
            window.ApplyChartContrast(true);
            check(window.ChartMaximum.Foreground == SystemColors.WindowTextBrush && window.Chart.Children.OfType<Grid>().SelectMany(g => g.Children.OfType<Border>()).All(b => b.Background == SystemColors.HighlightBrush),
                "The monthly chart uses readable system colors in high contrast");
            window.ApplyChartContrast(false); window.RenderMonthlyUsage([]);
            check(window.ChartEmpty.IsVisible && window.ChartArea.Visibility == Visibility.Collapsed && window.ChartAverage.Text == "6개월 평균 정보 없음",
                "An empty monthly response has an honest empty state");
            window.RenderMonthlyUsage([new("2026-10", 300)]); window.UpdateLayout();
            check(InstitutionUsage.UsageRatio(0, 0) == "정보 없음" && InstitutionUsage.UsageRatio(-1, 0) == "정보 없음" && InstitutionUsage.UsageRatio(100, -1) == "정보 없음"
                && InstitutionUsage.UsageRatio(100, 0) == "0%" && InstitutionUsage.UsageRatio(100, 120) == "120%", "Usage percentage handles zero, negative and over-allocation inputs without inventing data");
            var date = new DateOnly(2026, 10, 30);
            check(InstitutionUsage.RemainingDays("2026-10-31", date) == "1일 남음" && InstitutionUsage.RemainingDays("2026-10-30", date) == "오늘 만료"
                && InstitutionUsage.RemainingDays("2026-10-29", date) == "1일 경과" && InstitutionUsage.RemainingDays("2026-02-30", date) == "정보 없음"
                && InstitutionUsage.RemainingDays("2026-10-30T00:00:00", date) == "정보 없음" && InstitutionUsage.RemainingDays(null, date) == "정보 없음",
                "Contract date uses strict ISO dates and handles future, expiry, past and invalid dates");
            var seenAt = new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.FromHours(9));
            check(InstitutionUsage.LastSeen("2026-10-30T00:12:00Z", seenAt) == "오늘 09:12" && InstitutionUsage.LastSeen("2026-10-29T15:30:00Z", seenAt) == "오늘 00:30"
                && InstitutionUsage.LastSeen("2026-10-29T08:02:00Z", seenAt) == "어제" && InstitutionUsage.LastSeen("2026-10-29T08:02:00Z", seenAt, withTime: true) == "어제 17:02"
                && InstitutionUsage.LastSeen("2026-07-22", seenAt) == "07-22" && InstitutionUsage.LastSeen("2025-12-31T10:00:00Z", seenAt) == "2025-12-31"
                && InstitutionUsage.LastSeen(null, seenAt) == "—" && InstitutionUsage.LastSeen("unknown", seenAt) == "—"
                && ((DataGridTextColumn)window.Accounts.Columns[3]).Binding is System.Windows.Data.Binding { Converter: InstitutionLastSeenConverter },
                "Last login turns the server's UTC time into Korean 오늘 09:12, 어제, 07-22 or —");
            check(window.Accounts.Items.Count == 3 && window.Orders.Items.Count == 1 && window.Notices.Items.Count == 1,
                "Institution lists display only the API-provided accounts, the open account request, orders and notices");
            check(InstitutionStatusTextConverter.Label("ACTIVE", "account") == "활성" && InstitutionStatusTextConverter.Label("INACTIVE", "account") == "잠김"
                && InstitutionStatusTextConverter.Label("ORG", "notice") == "우리 기관" && InstitutionStatusTextConverter.Label("FUTURE_STATUS", "account") == "FUTURE_STATUS"
                && InstitutionStatusTextConverter.Label("FUTURE_SCOPE", "notice") == "FUTURE_SCOPE", "Known institution status labels are translated while unknown server codes remain intact");
            check(InstitutionStatusTextConverter.Label("PENDING", "invoice") == "발행 대기" && InstitutionStatusTextConverter.Label("ISSUED", "invoice") == "발행 완료"
                && InstitutionStatusTextConverter.Label("FUTURE_INVOICE", "invoice") == "FUTURE_INVOICE",
                "Invoice statuses translate only the server's known values without inventing success");
            check(window.Orders.Columns.LastOrDefault() is DataGridTemplateColumn { Header: "증빙" }
                && window.Orders.Columns.OfType<DataGridTextColumn>().Single(c => Equals(c.Header, "계산서")).Binding is System.Windows.Data.Binding { Converter: InstitutionStatusTextConverter, ConverterParameter: "invoice" },
                "The order table has translated invoice status and a dedicated receipt column");
            var statusBrush = new InstitutionStatusBrushConverter();
            Color? Tone(string? value, string kind) => (statusBrush.Convert(value!, typeof(Brush), kind, System.Globalization.CultureInfo.InvariantCulture) as SolidColorBrush)?.Color;
            check(InstitutionStatusTextConverter.Label("2026-02-24", "payment") == "완납" && InstitutionStatusTextConverter.Label(null, "payment") == "미납"
                && Tone("2026-02-24", "payment") == Color.FromRgb(0x0F, 0x7B, 0x0F) && Tone(null, "payment") == Color.FromRgb(0xC4, 0x2B, 0x1C) && Tone("08-12 요청", "account") == Color.FromRgb(0x9D, 0x5D, 0x00)
                && window.Orders.Columns.OfType<DataGridTemplateColumn>().Any(c => Equals(c.Header, "결제")) && !window.Orders.Columns.Any(c => Equals(c.Header, "결제일")),
                "The order table shows a green 완납 or red 미납 chip in the 결제 column and a pending account request is amber");
            var issueRow = window.Accounts.Items.OfType<InstitutionAccountRow>().SingleOrDefault(r => r.Request is not null);
            check(issueRow is { LoginId: "발급 요청 중", Alias: "국어 담당", Status: "08-12 요청", LastLoginAt: null, UsedCredits: null, Control: "요청 취소", Account: null }
                && window.PendingRequests.Text == "발급 요청 1건 처리 중" && window.PendingRequests.IsVisible,
                "An open account-issue request is a row of the account table and is counted beside the request button");
            check(!Descendants(window).OfType<Expander>().Any() && !Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "새로 고침")),
                "The institution page has no separate request-history expander or refresh button");
            window.UpdateLayout();
            var controlColumn = (DataGridTemplateColumn)window.Accounts.Columns[^1];
            Button? RowControl(InstitutionAccountRow? row) => row is not null && controlColumn.GetCellContent(row) is { } cell ? Descendants(cell).OfType<Button>().FirstOrDefault() : null;
            TextBlock? RowId(InstitutionAccountRow? row) => row is null ? null : window.Accounts.Columns[0].GetCellContent(row) as TextBlock;
            var selfRow = window.Accounts.Items.OfType<InstitutionAccountRow>().FirstOrDefault(r => r.LoginId == "org");
            var memberRow = window.Accounts.Items.OfType<InstitutionAccountRow>().FirstOrDefault(r => r.LoginId == "worker");
            check(RowControl(selfRow) is { IsVisible: false } && RowControl(memberRow) is { IsVisible: true, Content: "잠금" } && RowControl(issueRow) is { IsVisible: true, Content: "요청 취소" }
                && !Descendants(window.Accounts).OfType<Button>().Any(b => Equals(b.Content, "상세")),
                "Each account row shows its own 잠금 or 요청 취소 control, none on the administrator's row, and no 상세 button");
            check(RowId(memberRow) is { FontSize: 14, Foreground: SolidColorBrush { Color: var idColor } } memberId && memberId.FontWeight == FontWeights.Bold && idColor == Color.FromRgb(0x00, 0x5F, 0xB8)
                && RowId(issueRow) is { Foreground: SolidColorBrush { Color: var pendingColor } } && pendingColor == Color.FromRgb(0x5F, 0x5F, 0x5F),
                "Account IDs are 14px bold blue and the pending request line is gray");
            foreach (var scroll in Descendants(window).OfType<ScrollViewer>().Where(s => s.Content is StackPanel)) scroll.ScrollToTop();
            check(window.AccountRequest.IsEnabled && window.CreditRequest.IsEnabled && window.ReceiptAddress is { Text: "billing@example.test", IsEnabled: true },
                "Available request actions and receipt recipient reflect the current institution response");
            check(window.CreditRequest.Background is SolidColorBrush { Color: var orange } && orange == Color.FromRgb(232, 115, 12)
                && window.Accounts.RowHeight == 40 && window.Accounts.GridLinesVisibility == DataGridGridLinesVisibility.Horizontal
                && window.UsageBar.Template.FindName("InstitutionTrack", window.UsageBar) is Border { Background: SolidColorBrush { Color: var track } } && track == Color.FromRgb(237, 239, 243),
                "Institution controls use the Figma orange request button, light table rows and rounded credit track");
            var icons = Descendants(window).OfType<SvgViewbox>().Where(i => i.Source?.ToString().Contains("institution-caption-") == true).ToArray();
            check(icons.Length == 3 && icons.All(i => i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false }), "Institution caption renders all three original Figma SVGs");
            check(InstitutionAccountRow.Of(new InstitutionAccount("me", null, "ACTIVE", "ROLE_ORG_ADMIN", null, 0, true)).Control is null
                && InstitutionAccountRow.Of(new InstitutionAccount("a", null, "ACTIVE", "ROLE_USER", null, 0, false)).Control == "잠금"
                && InstitutionAccountRow.Of(new InstitutionAccount("b", null, "INACTIVE", "ROLE_USER", null, 0, false)).Control == "잠금 해제"
                && InstitutionAccountRow.Of(new InstitutionAccount("c", null, "FUTURE_STATUS", "ROLE_USER", null, 0, false)).Control is null
                && InstitutionAccountRow.Of(new InstitutionRequest(Guid.NewGuid(), "ACCOUNT_ISSUE", "OPEN", "별칭: 국어 담당", "2026-08-12")) is { Control: "요청 취소", Alias: "국어 담당", Status: "08-12 요청" },
                "The control column offers lock, unlock or request cancel by row state and nothing on the administrator's own row");
            if (Environment.GetEnvironmentVariable("INSTITUTION_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
            if (Environment.GetEnvironmentVariable("INSTITUTION_FULL_CAPTURE") is { Length: > 0 } fullCapture)
            {
                window.Width = 1440; window.Height = 1000; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var orderTop = window.Orders.TransformToAncestor(window).Transform(new Point()).Y;
                check(window.Orders.IsVisible && window.Orders.Items.Count == 1 && orderTop >= 0 && orderTop + window.Orders.ActualHeight <= window.ActualHeight - 28,
                    "At the 1440px Figma window size the real order list is visible without scrolling");
                await WindowsSmoke.RenderElementAsync(window, fullCapture); window.Width = 960; window.Height = 650; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            // 증빙 받는 사람 (F-AC-8): the Figma label and 220×32 field with no save button; Enter or leaving the field saves.
            window.UpdateLayout();
            Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var receiptLabel = Descendants(window).OfType<TextBlock>().SingleOrDefault(t => t.Text == "증빙 받는 사람");
            var receiptField = window.ReceiptAddress; var fieldBounds = Bounds(receiptField);
            check(receiptLabel is { IsVisible: true, FontSize: 12, Foreground: SolidColorBrush { Color: var receiptLabelColor } } && receiptLabelColor == Color.FromRgb(0x5F, 0x5F, 0x5F)
                && receiptField is { IsVisible: true, FontSize: 12, Text: "billing@example.test" } && Math.Abs(receiptField.ActualWidth - 220) < .5 && Math.Abs(receiptField.ActualHeight - 32) < .5
                && Bounds(receiptLabel) is var labelBounds && Math.Abs(fieldBounds.Left - labelBounds.Right - 12) < 1 && Math.Abs((labelBounds.Top + labelBounds.Bottom) / 2 - (fieldBounds.Top + fieldBounds.Bottom) / 2) < 1
                && System.Windows.Automation.AutomationProperties.GetName(receiptField) == "증빙 받는 사람"
                && !Descendants(window).OfType<TextBlock>().Any(t => t.Text.StartsWith("증빙 받는 사람:", StringComparison.Ordinal) || t.Text == "증빙 받는 사람 정보 없음")
                && !Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "저장")),
                "The receipt recipient is a 12px gray label and a 220x32 field holding the server address, 12px apart, with no save button");
            void PressReceipt(Key key) => receiptField.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(receiptField)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            static string? SentEmail(string body) { using var json = System.Text.Json.JsonDocument.Parse(body); return json.RootElement.GetProperty("email").GetString(); }
            var receiptSent = handler.ReceiptBodies.Count;
            receiptField.Text = "account@kblib.or.kr"; PressReceipt(Key.Enter); await WaitFor(() => !window.SavingReceipt);
            check(handler.ReceiptBodies.Count == receiptSent + 1 && SentEmail(handler.ReceiptBodies[^1]) == "account@kblib.or.kr" && receiptField.Text == "account@kblib.or.kr" && !receiptField.IsReadOnly,
                "Enter in the receipt field sends PATCH /api/org/receipt-email with { email } and keeps the saved address");
            handler.FailReceipt = true; receiptSent = handler.ReceiptBodies.Count;
            receiptField.Text = "other@example.test"; PressReceipt(Key.Enter); await WaitFor(() => !window.SavingReceipt);
            check(handler.ReceiptBodies.Count == receiptSent + 1 && receiptField.Text == "account@kblib.or.kr"
                && window.ErrorText.Text.StartsWith("증빙 받는 사람을 바꾸지 못했습니다.", StringComparison.Ordinal) && window.ErrorText.Text.Contains("권한이 없습니다."),
                "A rejected receipt change (403 COMMON4003) restores the saved address and reports the server message in the window's error line");
            handler.FailReceipt = false; receiptSent = handler.ReceiptBodies.Count;
            receiptField.Text = "account@"; PressReceipt(Key.Enter); window.UpdateLayout();
            check(handler.ReceiptBodies.Count == receiptSent && receiptField.Text == "account@" && window.ReceiptHint is { IsVisible: true } && window.ReceiptHint.Text.Contains("형식")
                && Bounds(window.ReceiptHint) is var hintBounds && hintBounds.Top >= fieldBounds.Bottom - .5 && Math.Abs(hintBounds.Left - fieldBounds.Left) < .5,
                "A malformed receipt address is not sent, stays in the field and shows a format hint under it");
            PressReceipt(Key.Escape); PressReceipt(Key.Enter); window.UpdateLayout();
            check(handler.ReceiptBodies.Count == receiptSent && receiptField.Text == "account@kblib.or.kr" && !window.ReceiptHint.IsVisible,
                "Esc restores the saved receipt address and Enter on an unchanged address sends nothing");
            receiptField.Text = "";
            receiptField.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, receiptField, window.Orders) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            await WaitFor(() => !window.SavingReceipt);
            check(handler.ReceiptBodies.Count == receiptSent + 1 && SentEmail(handler.ReceiptBodies[^1]) is null && handler.ReceiptBodies[^1].Contains("\"email\":null") && receiptField.Text == "" && window.ErrorText.Text == "",
                "Leaving an emptied receipt field removes the recipient with email null and clears the earlier failure");
            check(!Descendants(window).OfType<Button>().Any(b => b.Content is string text && text.StartsWith("선택한 ", StringComparison.Ordinal)),
                "Institution tables open rows directly and show no separate selected-row buttons");
            bool OpensOrder(Action act)
            {
                InstitutionOrderDialog? opened = null;
                window.Dispatcher.BeginInvoke(() => { opened = Application.Current.Windows.OfType<InstitutionOrderDialog>().FirstOrDefault(w => w.IsVisible); opened?.Close(); }, DispatcherPriority.ApplicationIdle);
                act(); return opened is not null;
            }
            // PreviewMouseLeftButtonUp is re-raised from the tunnelling PreviewMouseUp on every element of the route, so the table sees the cell as its source.
            static MouseButtonEventArgs Release() => new(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseUpEvent };
            window.UpdateLayout();
            check(window.Orders.Columns[0].GetCellContent(window.Orders.Items[0]) is { } orderCell && OpensOrder(() => orderCell.RaiseEvent(Release())),
                "One click on an order row opens its detail");
            window.Orders.SelectedIndex = 0; window.Orders.Focus();
            check(OpensOrder(() => window.Orders.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.Orders)!, Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent })),
                "Enter on a selected order row opens its detail, like a single click");
            var originalOrders = window.Orders.ItemsSource;
            var hasReceipt = new InstitutionOrder(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82"), "2026-01-02", "실제 주문", 2000, null, null, "ISSUED", "증빙.pdf");
            var noReceipt = new InstitutionOrder(Guid.NewGuid(), "2026-01-03", "증빙 없음", 3000, null, null, "PENDING", null);
            window.Orders.ItemsSource = new[] { hasReceipt, noReceipt }; window.Width = 480; window.Orders.BringIntoView();
            var receiptColumn = (DataGridTemplateColumn)window.Orders.Columns.Last();
            window.Orders.ScrollIntoView(hasReceipt, receiptColumn); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var receiptCell = receiptColumn.GetCellContent(hasReceipt);
            var rowButton = receiptCell is null ? null : Descendants(receiptCell).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "내려받기"));
            check(rowButton is { IsVisible: true, IsEnabled: true, Focusable: true } && System.Windows.Automation.AutomationProperties.GetName(rowButton).Contains("실제 주문"),
                "The row's actual receipt action remains keyboard accessible in a 480px window");
            check(rowButton is not null && !OpensOrder(() => rowButton.RaiseEvent(Release())), "Pressing a row's 내려받기 link does not also open the order detail");
            rowButton?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => handler.LastReceiptPath is not null && window.ErrorText.Text.Contains("증빙을 열지 못했습니다"));
            check(handler.LastReceiptPath == "/api/org/orders/3f56ae22-d121-48b1-a804-a3b48e3d3b82/receipt",
                "A row receipt action requests its own order and rejects an unsafe server URL before launching it");
            window.Orders.ScrollIntoView(noReceipt, receiptColumn); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var emptyCell = receiptColumn.GetCellContent(noReceipt);
            check(emptyCell is not null && Descendants(emptyCell).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "—")
                && !Descendants(emptyCell).OfType<Button>().Any(b => b.IsVisible), "An order without a receipt displays a dash without an actionable link");
            window.Orders.ItemsSource = originalOrders; window.Width = 960; window.ErrorText.Text = "";
            window.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
            check(logoutCalls == 1 && window.IsVisible && !window.LogoutRequested, "Cancelled or failed logout keeps the institution window open");

            // The final page has no refresh button; F5 is the remaining manual reload.
            void PressF5() => window.Accounts.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window.Accounts)!, Environment.TickCount, Key.F5) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            handler.FailDashboard = true; PressF5();
            await WaitFor(() => !window.Refreshing && window.ErrorText.Text.Contains("기관 정보"));
            check(window.Identity.Text == "정보 없음" && window.Balance.Text == "—" && window.UsagePercent.Text == "정보 없음" && window.ContractRemaining.Text == "정보 없음"
                && StatusLine() == "기관 정보 없음" && window.UsageBar.Visibility == Visibility.Collapsed,
                "A dashboard refresh error clears old credit, date, graph and status values");
            handler.FailDashboard = false; PressF5();
            await WaitFor(() => !window.Refreshing && window.Identity.Text.Contains("테스트 기관"));

            handler.FailAccountsOnRefresh = true;
            PressF5(); await WaitFor(() => !window.Refreshing && api.Account is null);
            check(api.Account is null && window.Identity.Text == "정보 없음" && window.Balance.Text == "—" && window.Accounts.Items.Count == 0 && window.Orders.Items.Count == 0
                && !window.AccountRequest.IsEnabled && !window.CreditRequest.IsEnabled && window.PendingRequests.Text == "" && window.ReceiptAddress is { IsEnabled: false, Text: "" }
                && window.SessionIdentity.Text == "로그인 정보 없음" && StatusLine() == "기관 정보 없음" && window.ErrorText.Text.Contains("다시 로그인"),
                "A role-unverified refresh clears previously loaded institution data and disables changes");
            allowLogout = true; window.LogoutItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(30);
            check(logoutCalls == 2 && window.LogoutRequested && !window.IsVisible, "The logout callback can return an expired institution session to login");
        }
        finally { if (window.IsVisible) window.Close(); }
        await ReceiptSessionChecks(check);

        var attempts = 0; string? submitted = null;
        var dialog = new InstitutionRequestDialog(true, message => { attempts++; if (attempts == 1) throw new InvalidOperationException("fixture failure"); submitted = message; return Task.CompletedTask; });
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(async () =>
        {
            dialog.First.Text = "국어 담당"; dialog.Second.Text = "reader@example.test";
            dialog.Submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(50);
            check(dialog.IsVisible && dialog.First.Text == "국어 담당" && dialog.Second.Text == "reader@example.test", "A failed account request preserves entered fields for retry");
            dialog.Submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        });
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; watchdog.Tick += (_, _) => { watchdog.Stop(); if (dialog.IsVisible) dialog.Close(); };
        watchdog.Start(); var result = dialog.ShowDialog(); watchdog.Stop();
        check(result == true && attempts == 2 && submitted?.Contains("reader@example.test") == true, "An account request closes successfully only after its actual callback succeeds");
        var detail = new InstitutionAccountDetailDialog(new("self", "본인", "ACTIVE", "ROLE_ORG_ADMIN", null, 0, true), new("self", "본인", "2026-01-01", "2026-01-31", [], 0, 0), _ => Task.CompletedTask);
        check(!detail.LockButton.IsEnabled, "The institution administrator cannot lock their own account"); detail.Close();
    }
    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 100 && !predicate(); i++) await Task.Delay(20);
        if (!predicate()) throw new TimeoutException("Institution fixture did not reach the expected state.");
    }
    private static async Task ReceiptSessionChecks(Action<bool, string> check)
    {
        foreach (var mode in new[] { "same session", "refresh", "logout", "same-account relogin", "close", "relogin before click" })
        {
            var fixture = new Fixture { HoldReceipt = true, RefreshProbe = mode == "refresh" };
            using var http = new HttpClient(fixture) { BaseAddress = new Uri("https://example.invalid") };
            var api = new SemojumApiClient(http); await api.LoginAsync("org", "fixture");
            var window = new InstitutionWindow(api, () => Task.FromResult(false)); var launched = 0;
            window.ReceiptLauncher = _ => launched++; window.Show();
            try
            {
                await WaitFor(() => !window.Refreshing);
                var order = new InstitutionOrder(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82"), "2026-01-01", "증빙", 1, null, null, "ISSUED", "receipt.pdf");
                var open = typeof(InstitutionWindow).GetMethod("OpenReceiptForAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                if (mode == "relogin before click")
                {
                    await api.LogoutAsync(); await api.LoginAsync("org", "fixture");
                    await ((Task)open.Invoke(window, [order])!).WaitAsync(TimeSpan.FromSeconds(5));
                    check(fixture.LastReceiptPath is null && launched == 0, "An old institution window cannot request a receipt after the same account logs in again");
                    continue;
                }
                var pending = (Task)open.Invoke(window, [order])!;
                await WaitFor(() => fixture.HeldReceipt is not null);
                if (mode == "refresh") await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/org/refresh-probe");
                if (mode is "logout" or "same-account relogin") await api.LogoutAsync();
                if (mode == "same-account relogin") await api.LoginAsync("org", "fixture");
                if (mode == "close") window.Close();
                fixture.HeldReceipt!.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"fileName\":\"receipt.pdf\",\"url\":\"https://example.invalid/receipt\"}}", Encoding.UTF8, "application/json") });
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                check(launched == (mode is "same session" or "refresh" ? 1 : 0), $"Receipt opening respects the {mode} session after an async response");
            }
            finally { if (window.IsVisible) window.Close(); }
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private sealed class Fixture : HttpMessageHandler
    {
        internal bool FailAccountsOnRefresh;
        internal bool FailDashboard;
        internal string? LastReceiptPath;
        internal bool HoldReceipt, RefreshProbe, ProbeRejected, FailReceipt;
        internal readonly List<string> ReceiptBodies = [];
        internal TaskCompletionSource<HttpResponseMessage>? HeldReceipt;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"accessToken\":\"old\",\"refreshToken\":\"refresh\",\"role\":\"ROLE_ORG_ADMIN\"}}");
            if (path.EndsWith("/refresh")) return Json(HttpStatusCode.OK, RefreshProbe
                ? "{\"isSuccess\":true,\"result\":{\"accessToken\":\"new\",\"loginId\":\"org\",\"role\":\"ROLE_ORG_ADMIN\",\"alias\":\"새 이름\"}}"
                : "{\"isSuccess\":true,\"result\":{\"accessToken\":\"new\"}}");
            if (path.EndsWith("/refresh-probe") && !ProbeRejected) { ProbeRejected = true; return Json(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}"); }
            if (path.EndsWith("/dashboard") && FailDashboard) return Json(HttpStatusCode.InternalServerError, "{\"isSuccess\":false,\"code\":\"TEST\",\"message\":\"조회 실패\"}");
            if (path.EndsWith("/accounts") && FailAccountsOnRefresh) return Json(HttpStatusCode.Unauthorized, "{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}");
            if (path.EndsWith("/receipt"))
            {
                LastReceiptPath = path;
                if (HoldReceipt) { HeldReceipt = new(TaskCreationOptions.RunContinuationsAsynchronously); return HeldReceipt.Task; }
                return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":{\"fileName\":\"receipt.pdf\",\"url\":\"http://example.invalid/receipt\"}}");
            }
            if (path == "/api/org/receipt-email" && request.Method == HttpMethod.Patch) return ReceiptEmail(request, cancellationToken);
            var result = path switch
            {
                "/api/org/dashboard" => "{\"orgName\":\"테스트 기관\",\"orgCode\":\"O1\",\"contractType\":\"ANNUAL\",\"contractStartedAt\":\"2026-01-01\",\"contractExpiresAt\":\"2026-12-31\",\"creditAllocated\":1000,\"creditUsed\":300,\"creditRemaining\":700,\"monthlyUsage\":[{\"month\":\"2026-10\",\"credits\":300}]}",
                "/api/org/accounts" => "{\"usageSince\":\"2026-10-01\",\"items\":[{\"loginId\":\"org\",\"alias\":\"관리자\",\"status\":\"ACTIVE\",\"role\":\"ROLE_ORG_ADMIN\",\"lastLoginAt\":null,\"usedCredits\":100,\"self\":true},{\"loginId\":\"worker\",\"alias\":null,\"status\":\"ACTIVE\",\"role\":\"ROLE_USER\",\"lastLoginAt\":null,\"usedCredits\":200,\"self\":false}]}",
                "/api/org/requests" => "[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198072\",\"type\":\"CREDIT_ADD\",\"status\":\"OPEN\",\"message\":\"요청\",\"createdAt\":\"2026-10-01\"},"
                    + "{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198073\",\"type\":\"ACCOUNT_ISSUE\",\"status\":\"OPEN\",\"message\":\"별칭: 국어 담당\\n받을 사람 메일: reader@example.test\",\"createdAt\":\"2026-08-12T01:00:00Z\"},"
                    + "{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198076\",\"type\":\"ACCOUNT_ISSUE\",\"status\":\"CANCELED\",\"message\":null,\"createdAt\":\"2026-10-02\"}]",
                "/api/org/notices" => "[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198074\",\"scope\":\"ORG\",\"title\":\"공지\",\"body\":\"내용\",\"createdAt\":\"2026-10-03\"}]",
                "/api/org/orders" => "{\"receiptEmail\":\"billing@example.test\",\"items\":[{\"id\":\"ad769b7f-5e74-40ad-94d3-1cccd1198075\",\"orderDate\":\"2026-01-01\",\"description\":\"계약\",\"amountKrw\":1000,\"creditAmount\":1000,\"paidAt\":null,\"invoiceStatus\":\"PENDING\",\"receiptFileName\":null}]}",
                _ => "{}"
            };
            return Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"result\":" + result + "}");
        }
        private async Task<HttpResponseMessage> ReceiptEmail(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ReceiptBodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return await (FailReceipt ? Json(HttpStatusCode.Forbidden, "{\"isSuccess\":false,\"code\":\"COMMON4003\",\"message\":\"권한이 없습니다.\",\"result\":null}")
                : Json(HttpStatusCode.OK, "{\"isSuccess\":true,\"code\":\"COMMON2000\",\"message\":\"성공입니다.\",\"result\":null}"));
        }
        private static Task<HttpResponseMessage> Json(HttpStatusCode status, string json) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
