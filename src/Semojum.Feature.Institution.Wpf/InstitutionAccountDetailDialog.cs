using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Desktop.Services;

namespace Semojum.Desktop.Dialogs;

internal sealed class InstitutionAccountDetailDialog : CompactDialog
{
    internal Button LockButton { get; }
    internal DataGrid Jobs { get; }
    internal InstitutionAccount Account { get; }
    internal InstitutionAccountDetailDialog(InstitutionAccount account, InstitutionAccountJobs jobs, Func<bool, Task> setLocked)
        : base("계정 상세", 560, 564, popover: true)
    {
        Account = account;
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = FigmaDialogElements.Brush("#EAEAEA"); frame.Background = FigmaDialogElements.Brush("#F3F3F3"); }
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(55) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) }; header.Children.Add(new TextBlock { Text = account.LoginId + (string.IsNullOrWhiteSpace(account.Alias) ? "" : " · " + account.Alias), FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right,
            Content = FigmaDialogElements.DesignIcon("institution-account-detail-close.svg", 16, 16, "Semojum.Feature.Institution.Wpf"), Template = FigmaDialogElements.SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close); layout.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 15, 24, 0) };
        var bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(bodyScroll, 1); layout.Children.Add(bodyScroll);
        void Section(string title, params (string Label, string Value)[] fields)
        {
            body.Children.Add(new TextBlock { Text = title, Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
            var rows = new StackPanel { Margin = new(16, 8, 16, 8) };
            foreach (var (label, value) in fields)
            {
                var line = new Grid { MinHeight = 36 }; line.ColumnDefinitions.Add(new() { Width = new(134) }); line.ColumnDefinitions.Add(new());
                line.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }; Grid.SetColumn(text, 1); line.Children.Add(text); rows.Children.Add(line);
            }
            body.Children.Add(new Border { Background = FigmaDialogElements.Brush("#F9F9F9"), CornerRadius = new(6), Child = rows, Margin = new(0, 0, 0, 16) });
        }
        Section("계정", ("별칭", account.Alias ?? "정보 없음"), ("메일", "제공되지 않음"), ("만든 날", "제공되지 않음"));
        Section("이 달 쓴 양", ("크레딧", account.UsedCredits.ToString("N0")), ("마지막 로그인", account.LastLoginAt ?? "정보 없음"));
        body.Children.Add(new TextBlock { Text = $"최근 작업 · {jobs.From} ~ {jobs.To}", Foreground = FigmaDialogElements.Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
        var jobBackground = FigmaDialogElements.Brush("#F9F9F9");
        Jobs = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.None, BorderThickness = new(0), Background = jobBackground,
            RowBackground = jobBackground, AlternatingRowBackground = jobBackground, GridLinesVisibility = DataGridGridLinesVisibility.None, RowHeight = 32,
            Style = (Style)FindResource("InstitutionJobsGrid"), ItemsSource = jobs.Items, MinHeight = 80, MaxHeight = 130 };
        var baseCell = (Style)Jobs.Style.Setters.OfType<Setter>().Single(s => s.Property == DataGrid.CellStyleProperty).Value;
        var nameCell = new Style(typeof(DataGridCell), baseCell); nameCell.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(16, 0, 8, 0)));
        var detailCell = new Style(typeof(DataGridCell), baseCell); detailCell.Setters.Add(new Setter(Control.ForegroundProperty, FigmaDialogElements.Brush("#5F5F5F")));
        var nameText = new Style(typeof(TextBlock)); nameText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        nameText.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding("FileName")));
        Jobs.Columns.Add(new DataGridTextColumn { Header = "파일", Binding = new Binding("FileName"), Width = new(1, DataGridLengthUnitType.Star), MinWidth = 80, CellStyle = nameCell, ElementStyle = nameText });
        Jobs.Columns.Add(new DataGridTextColumn { Header = "쪽", Binding = new Binding("TotalPages") { StringFormat = "{0}쪽" }, Width = 70, CellStyle = detailCell });
        Jobs.Columns.Add(new DataGridTextColumn { Header = "크레딧", Binding = new Binding("Credits") { TargetNullValue = "— 크레딧", StringFormat = "{0} 크레딧" }, Width = 118, CellStyle = detailCell });
        AutomationProperties.SetName(Jobs, "해당 계정의 최근 작업"); body.Children.Add(new Border { Background = jobBackground, CornerRadius = new(6), Padding = new(0, 10, 0, 10), Child = Jobs });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 0) }; Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var lockState = account.Status == "INACTIVE";
        LockButton = Button(lockState ? "잠금 해제" : "잠금", async () => await RunAsync(async () =>
        {
            var message = lockState ? "이 계정의 잠금을 해제하시겠습니까?" : "계정을 잠그면 로그인 세션이 종료되고 진행 중인 작업이 취소될 수 있습니다. 계속하시겠습니까?";
            if (NoticeDialog.Choose(this, message, "변경은 서버에 즉시 적용됩니다.", ["계속", "취소"], error: !lockState) != 0) return;
            await setLocked(!lockState); DialogResult = true;
        }));
        LockButton.Width = 88; LockButton.Height = 32; LockButton.IsEnabled = !account.Self && account.Status is "ACTIVE" or "INACTIVE";
        LockButton.ToolTip = account.Self ? "본인 계정은 여기에서 잠글 수 없습니다." : LockButton.IsEnabled ? null : "현재 계정 상태에서는 잠금 변경을 할 수 없습니다."; actions.Children.Add(LockButton);
        var done = Button("닫기", () => { Close(); return Task.CompletedTask; }, true); done.Width = 88; done.Height = 32; done.IsDefault = true; actions.Children.Add(done);
    }
}
