using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class ExportRangeDialog : CompactDialog
{
    private readonly SegmentedChoice scope = new("원본 페이지", "점자 페이지");
    private readonly ComboBox formatChoice = new();
    private readonly TextBox start = new(), end = new();
    private readonly TextBlock bounds = new();
    private readonly int[] originalNumbers;
    private readonly Func<Task<int>>? brailleCount;
    private int? pageCount;
    private bool loading;
    public ExportRange Result { get; private set; } = new();
    public string SelectedFormat => formatChoice.SelectedItem as string ?? ".brf";

    public ExportRangeDialog(int[] originalNumbers, Func<Task<int>>? brailleCount, bool brailleOnly = false, string format = ".brf", bool allowFormatChange = false)
        : base("범위 지정 저장", 520, 324, popover: true)
    {
        this.originalNumbers = originalNumbers; this.brailleCount = brailleCount;
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#EAEAEA"); frame.Background = Brush("#F3F3F3"); }
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = new(55) });
        layout.RowDefinitions.Add(new() { Height = new(204) });
        layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);

        var title = new Grid { Margin = new(24, 0, 24, 0) };
        title.Children.Add(new TextBlock { Text = "범위 지정 저장", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = DesignIcon("range-close.svg", 16, 16) };
        close.Template = SurfaceTemplate(0); AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); title.Children.Add(close);
        layout.Children.Add(title);

        var body = new StackPanel { Margin = new(24, 15, 24, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "범위", Foreground = Brush("#5F5F5F"), FontSize = 12, Margin = new(0, 0, 0, 4) });
        var form = new Grid { Margin = new(16, 12, 16, 4) };
        form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        for (var i = 0; i < 4; i++) form.RowDefinitions.Add(new() { Height = new(36) });
        body.Children.Add(new Border { Height = 160, CornerRadius = new(6), Background = Brush("#F9F9F9"), Child = form });
        void Field(int row, string label, FrameworkElement control)
        {
            var caption = new TextBlock { Text = label, FontSize = 14, Foreground = Brush("#1A1A1A"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(caption, row); form.Children.Add(caption);
            Grid.SetRow(control, row); Grid.SetColumn(control, 1); form.Children.Add(control);
        }
        scope.Width = 240; scope.Height = 32; scope.ItemContainerStyle = ScopeStyle();
        scope.SetAvailable(0, !brailleOnly, "미리보기에서는 점자 면을 선택합니다.");
        scope.SetAvailable(1, brailleCount is not null && format != ".txt — 원문", "원문 파일은 원본 페이지를 선택합니다.");
        AutomationProperties.SetName(scope, "범위 기준"); Field(0, "기준", scope);
        Field(1, "시작 페이지 번호", NumberField(start, "시작 페이지 번호"));
        Field(2, "끝 페이지 번호", NumberField(end, "끝 페이지 번호"));
        var formatField = new Grid { Width = 260, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        formatChoice.ItemsSource = allowFormatChange ? new[] { ".brf", ".txt — 원문", ".txt — 점자" } : new[] { format };
        formatChoice.SelectedItem = format; formatChoice.Padding = new(11, 0, 27, 0); formatChoice.VerticalContentAlignment = VerticalAlignment.Center;
        formatChoice.Background = Brush("#FBFBFB"); formatChoice.BorderBrush = Brush("#EAEAEA");
        formatChoice.ToolTip = allowFormatChange ? "변경한 형식에 맞춰 파일 확장자와 내용을 저장합니다." : "이 명령의 저장 형식입니다.";
        AutomationProperties.SetName(formatChoice, "형식"); formatField.Children.Add(formatChoice);
        formatField.Children.Add(new Border { Width = 22, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"),
            HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var formatIcon = DesignIcon("range-format-chevron.svg", 16, 16); formatIcon.HorizontalAlignment = HorizontalAlignment.Right;
        formatIcon.Margin = new(0, 0, 5, 0); formatIcon.IsHitTestVisible = false; formatField.Children.Add(formatIcon); Field(3, "형식", formatField);
        scope.ToolTip = brailleCount is null ? "원문 TXT는 원본 쪽 기준으로 저장합니다."
            : "점자 면은 표지를 포함한 첫 면부터 1, 2, 3…으로 셉니다. 인쇄된 쪽번호와 다를 수 있으며 쪽번호·꼬리말은 유지합니다.";
        start.ToolTip = end.ToolTip = bounds;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 16, 24, 16) };
        Grid.SetRow(actions, 2); layout.Children.Add(actions);
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; });
        var save = Button("저장", () => { if (Apply()) DialogResult = true; return Task.CompletedTask; }, true);
        foreach (var button in new[] { cancel, save }) { button.Width = 88; button.MinWidth = 0; button.Height = 32; button.MinHeight = 0; button.Template = SurfaceTemplate(4); actions.Children.Add(button); }
        cancel.BorderBrush = Brush("#EAEAEA");
        scope.SelectionChanged += async (_, _) => await UpdateBoundsAsync();
        formatChoice.SelectionChanged += (_, _) =>
        {
            var canUseBraille = brailleCount is not null && SelectedFormat != ".txt — 원문";
            scope.SetAvailable(1, canUseBraille, "원문 파일은 원본 페이지를 선택합니다.");
            if (!canUseBraille && scope.SelectedIndex == 1 && !brailleOnly) scope.SelectedIndex = 0;
        };
        scope.SelectedIndex = brailleOnly ? 1 : 0;
        _ = UpdateBoundsAsync();
    }

    private static FrameworkElement NumberField(TextBox input, string label)
    {
        var field = new Grid { Width = 104, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        field.ColumnDefinitions.Add(new() { Width = new(80) }); field.ColumnDefinitions.Add(new() { Width = new(24) });
        input.Padding = new(11, 0, 2, 0); input.VerticalContentAlignment = VerticalAlignment.Center;
        input.Background = Brush("#FBFBFB"); input.BorderBrush = Brush("#EAEAEA"); AutomationProperties.SetName(input, label); field.Children.Add(input);
        var arrows = new Grid { Background = Brush("#F0F0F0") }; arrows.RowDefinitions.Add(new()); arrows.RowDefinitions.Add(new());
        Grid.SetColumn(arrows, 1); field.Children.Add(arrows);
        void Step(int delta) { if (!int.TryParse(input.Text, out var n)) n = 0; input.Text = Math.Clamp((long)n + delta, 1, int.MaxValue).ToString(); }
        var upIcon = DesignIcon("range-spinner-up.svg", 9.60063, 5.19934); upIcon.RenderTransformOrigin = new(.5, .5); upIcon.RenderTransform = new ScaleTransform(1, -1);
        foreach (var (delta, icon, row) in new[] { (1, upIcon, 0), (-1, DesignIcon("range-spinner-down.svg", 16, 16), 1) })
        {
            var button = new RepeatButton { Content = icon, BorderThickness = new(0), Background = Brush("#F0F0F0"), Padding = new(0), MinHeight = 0 };
            button.SetBinding(IsEnabledProperty, new Binding(nameof(IsEnabled)) { Source = input });
            AutomationProperties.SetName(button, label + (delta > 0 ? " 증가" : " 감소")); button.Click += (_, _) => Step(delta); Grid.SetRow(button, row); arrows.Children.Add(button);
        }
        return field;
    }

    private async Task UpdateBoundsAsync()
    {
        Status.Text = "";
        if (scope.SelectedIndex == 0)
        {
            start.IsEnabled = end.IsEnabled = true;
            start.Text = originalNumbers.FirstOrDefault().ToString(); end.Text = originalNumbers.LastOrDefault().ToString();
            bounds.Text = $"원본 {start.Text}–{end.Text}쪽"; return;
        }
        if (pageCount is null && brailleCount is not null && !loading)
        {
            loading = true; scope.IsEnabled = formatChoice.IsEnabled = start.IsEnabled = end.IsEnabled = false; bounds.Text = "점자 면 수를 계산하고 있습니다…";
            try { pageCount = await brailleCount(); }
            catch (Exception ex) { Status.Text = ex.Message; bounds.Text = "면 수를 계산하지 못했습니다. 다른 범위를 선택할 수 있습니다."; }
            finally { loading = false; scope.IsEnabled = formatChoice.IsEnabled = true; start.IsEnabled = end.IsEnabled = true; }
        }
        if (pageCount is { } count) { start.Text = "1"; end.Text = count.ToString(); bounds.Text = $"총 {count}면 · 첫 면부터 센 순서"; }
    }

    private bool Apply()
    {
        if (loading) { Status.Text = "점자 면 수를 계산한 뒤 저장할 수 있습니다."; return false; }
        var selected = scope.SelectedIndex == 0 ? ExportScope.OriginalPages : ExportScope.BraillePages;
        if (!int.TryParse(start.Text, out var from) || !int.TryParse(end.Text, out var to))
        { Status.Text = "시작과 끝 번호를 정수로 입력해 주세요."; return false; }
        try
        {
            if (selected == ExportScope.BraillePages && pageCount is null) throw new ArgumentException("점자 면 수를 먼저 확인해 주세요.");
            var range = new ExportRange(selected, from, to);
            range.Validate(selected == ExportScope.OriginalPages ? originalNumbers.FirstOrDefault() : 1,
                selected == ExportScope.OriginalPages ? originalNumbers.LastOrDefault() : pageCount!.Value);
            Result = range; return true;
        }
        catch (ArgumentException ex)
        {
            var notice = FileNotices.InvalidRange(from, to, ex.Message); notice.Owner = this; notice.ShowDialog();
            if (notice.Choice != 0) Close(); else { start.Focus(); start.SelectAll(); }
            return false;
        }
    }
}
