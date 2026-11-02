using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

internal abstract class InstitutionDetailsDialog : CompactDialog
{
    protected readonly Grid Layout = new();
    protected readonly StackPanel ContentPanel = new() { Margin = new(24, 15, 24, 0) };
    protected readonly StackPanel Footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 0) };
    protected InstitutionDetailsDialog(string title, double height, string closeAsset) : base(title, 560, height, popover: true)
    {
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); frame.Background = FigmaDialogElements.Brush("#F3F3F3"); }
        Layout.RowDefinitions.Add(new() { Height = new(55) }); Layout.RowDefinitions.Add(new()); Layout.RowDefinitions.Add(new() { Height = new(64) }); Body(Layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 36, 0) });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right,
            Content = FigmaDialogElements.DesignIcon(closeAsset, 16, 16, "Semojum.Feature.Institution.Wpf"), Template = FigmaDialogElements.SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close); Layout.Children.Add(header);
        var scroll = new ScrollViewer { Content = ContentPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); Layout.Children.Add(scroll); Grid.SetRow(Footer, 2); Layout.Children.Add(Footer);
    }
    protected void Done()
    {
        var done = Button("닫기", () => { Close(); return Task.CompletedTask; }, true); done.Width = 88; done.Height = 32; Footer.Children.Add(done);
    }
}

internal sealed class InstitutionNoticeDialog : InstitutionDetailsDialog
{
    internal Button CreditRequestButton { get; }
    internal bool CreditRequested { get; private set; }
    internal InstitutionNoticeDialog(InstitutionNotice notice, bool canRequest) : base(notice.Title, 276, "institution-notice-detail-close.svg")
    {
        var metadata = new DockPanel { Margin = new(0, 0, 0, 16) };
        var category = notice.Scope switch { "ORG" => "우리 기관", "ALL" => "전체", _ => notice.Scope };
        var badge = new Border { Background = Brushes.White, BorderBrush = FigmaDialogElements.Brush(notice.Scope == "ORG" ? "#C42B1C" : "#5F5F5F"), BorderThickness = new(1), CornerRadius = new(10), Padding = new(9, 1, 9, 1), Child = new TextBlock { Text = category, Foreground = FigmaDialogElements.Brush(notice.Scope == "ORG" ? "#C42B1C" : "#5F5F5F") } };
        DockPanel.SetDock(badge, Dock.Left); metadata.Children.Add(badge);
        var sent = InstitutionUsage.InKorea(notice.CreatedAt)?.ToString("yyyy. MM. dd.", System.Globalization.CultureInfo.InvariantCulture) ?? notice.CreatedAt;
        metadata.Children.Add(new TextBlock { Text = sent + "  ·  보낸 사람 운영자", Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); ContentPanel.Children.Add(metadata);
        ContentPanel.Children.Add(new Border { Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Padding = new(16, 12, 16, 12), Child = new TextBlock { Text = notice.Body, TextWrapping = TextWrapping.Wrap, FontSize = 12 } });
        CreditRequestButton = Button("크레딧 추가 요청", () => { CreditRequested = true; DialogResult = true; return Task.CompletedTask; }); CreditRequestButton.Width = 146; CreditRequestButton.Height = 32; CreditRequestButton.IsEnabled = canRequest;
        CreditRequestButton.ToolTip = canRequest ? "운영자에게 크레딧 추가를 요청합니다." : "기관 권한을 확인한 뒤 요청할 수 있습니다."; Footer.Children.Add(CreditRequestButton); Done();
    }
}

internal sealed class InstitutionOrderDialog : InstitutionDetailsDialog
{
    internal Button QuoteButton { get; }
    /// <summary>「주문 (크레딧 추가)」: the kind is the description before its first 「 · 」 (크레딧 추가 · 3,000).</summary>
    internal static string TitleOf(InstitutionOrder order) => order.Description.Split(" · ", 2)[0].Trim() is { Length: > 0 } kind ? $"주문 ({kind})" : "주문";
    internal InstitutionOrderDialog(InstitutionOrder order, string? receiptEmail, Func<Task> openQuote) : base(TitleOf(order), 448, "institution-order-detail-close.svg")
    {
        void Section(string? title, params (string Label, object Value)[] fields)
        {
            if (title is not null) ContentPanel.Children.Add(new TextBlock { Text = title, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
            var rows = new StackPanel { Margin = new(16, 8, 16, 8) };
            foreach (var (label, value) in fields)
            {
                var row = new Grid { MinHeight = 36 }; row.ColumnDefinitions.Add(new() { Width = new(134) }); row.ColumnDefinitions.Add(new());
                row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var data = value as FrameworkElement ?? new TextBlock { Text = value as string, FontSize = 14, TextWrapping = TextWrapping.Wrap };
                data.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(data, 1); row.Children.Add(data); rows.Children.Add(row);
            }
            ContentPanel.Children.Add(new Border { Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Child = rows, Margin = new(0, 0, 0, 16) });
        }
        // The server amount already includes VAT; the requester is 「kblib00 · 관리자」 and the request time is shown in Korean time.
        var amount = new TextBlock { Text = "₩" + order.AmountKrw.ToString("N0") + "  (부가세 포함)", FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
        var requester = string.IsNullOrWhiteSpace(order.RequestedBy) ? "정보 없음" : order.RequestedBy + (order.RequestedByRole == "ROLE_ORG_ADMIN" ? " · 관리자" : "");
        var requested = InstitutionUsage.InKorea(order.RequestedAt)?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? order.OrderDate;
        Section(null, ("내용", order.Description), ("금액", amount), ("요청한 사람", requester), ("요청한 날", requested));
        // No payment status field exists; a recorded payment date means the order is paid.
        var paid = !string.IsNullOrWhiteSpace(order.PaidAt); var paidColor = FigmaDialogElements.Brush(paid ? "#0F7B0F" : "#C42B1C");
        var payment = new Border { Background = Brushes.White, BorderBrush = paidColor, BorderThickness = new(1), CornerRadius = new(10), Padding = new(9, 1, 9, 1), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = paid ? "완납" : "미납", Foreground = paidColor, FontSize = 12 } };
        Section("결제와 증빙", ("결제", payment), ("세금계산서", InstitutionStatusTextConverter.Label(order.InvoiceStatus, "invoice") + (string.IsNullOrWhiteSpace(receiptEmail) ? "" : "  ·  " + receiptEmail)));
        // The receipt stays in the order table's 증빙 column; the detail offers the quote, hidden when the server has none.
        QuoteButton = Button("견적서 내려받기", async () => await RunAsync(openQuote)); QuoteButton.Width = 138; QuoteButton.Height = 32; QuoteButton.ToolTip = order.QuoteFileName;
        QuoteButton.Visibility = string.IsNullOrWhiteSpace(order.QuoteFileName) ? Visibility.Collapsed : Visibility.Visible; Footer.Children.Add(QuoteButton); Done();
    }
}
