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
            Content = FigmaDialogElements.DesignIcon(closeAsset, 16, 16), Template = FigmaDialogElements.SurfaceTemplate(0) };
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
        metadata.Children.Add(new TextBlock { Text = notice.CreatedAt, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); ContentPanel.Children.Add(metadata);
        ContentPanel.Children.Add(new Border { Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Padding = new(16, 12, 16, 12), Child = new TextBlock { Text = notice.Body, TextWrapping = TextWrapping.Wrap, FontSize = 12 } });
        CreditRequestButton = Button("크레딧 추가 요청", () => { CreditRequested = true; DialogResult = true; return Task.CompletedTask; }); CreditRequestButton.Width = 146; CreditRequestButton.Height = 32; CreditRequestButton.IsEnabled = canRequest;
        CreditRequestButton.ToolTip = canRequest ? "운영자에게 크레딧 추가를 요청합니다." : "기관 권한을 확인한 뒤 요청할 수 있습니다."; Footer.Children.Add(CreditRequestButton); Done();
    }
}

internal sealed class InstitutionOrderDialog : InstitutionDetailsDialog
{
    internal Button ReceiptButton { get; }
    internal InstitutionOrderDialog(InstitutionOrder order, string? receiptEmail, Func<Task> openReceipt) : base("주문 상세", 448, "institution-order-detail-close.svg")
    {
        void Section(string? title, params (string Label, string Value)[] fields)
        {
            if (title is not null) ContentPanel.Children.Add(new TextBlock { Text = title, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
            var rows = new StackPanel { Margin = new(16, 8, 16, 8) };
            foreach (var (label, value) in fields)
            {
                var row = new Grid { MinHeight = 36 }; row.ColumnDefinitions.Add(new() { Width = new(134) }); row.ColumnDefinitions.Add(new());
                row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var data = new TextBlock { Text = value, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(data, 1); row.Children.Add(data); rows.Children.Add(row);
            }
            ContentPanel.Children.Add(new Border { Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Child = rows, Margin = new(0, 0, 0, 16) });
        }
        Section(null, ("내용", order.Description), ("금액", "₩" + order.AmountKrw.ToString("N0")), ("요청한 사람", "정보 없음"), ("요청한 날", order.OrderDate));
        Section("결제와 증빙", ("결제일", string.IsNullOrWhiteSpace(order.PaidAt) ? "정보 없음" : order.PaidAt), ("세금계산서", InstitutionStatusTextConverter.Label(order.InvoiceStatus, "invoice") + (string.IsNullOrWhiteSpace(receiptEmail) ? "" : " · " + receiptEmail)));
        ReceiptButton = Button("증빙 열기", async () => await RunAsync(openReceipt)); ReceiptButton.Width = 138; ReceiptButton.Height = 32;
        ReceiptButton.IsEnabled = !string.IsNullOrWhiteSpace(order.ReceiptFileName); ReceiptButton.ToolTip = ReceiptButton.IsEnabled ? "서버가 제공한 증빙을 엽니다." : "제공된 증빙이 없습니다."; Footer.Children.Add(ReceiptButton); Done();
    }
}
