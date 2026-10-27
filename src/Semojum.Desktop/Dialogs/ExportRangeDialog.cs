using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class ExportRangeDialog : CompactDialog
{
    private readonly ComboBox scope = new();
    private readonly TextBox start = new(), end = new();
    private readonly TextBlock bounds = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    private readonly int[] originalNumbers;
    private readonly Func<Task<int>>? brailleCount;
    private int? pageCount;
    private bool loading;
    private TextBlock Error => Status;
    public ExportRange Result { get; private set; } = new();

    public ExportRangeDialog(int[] originalNumbers, Func<Task<int>>? brailleCount, bool brailleOnly = false, string format = ".brf") : base("범위 지정 저장", 520, 240)
    {
        this.originalNumbers = originalNumbers; this.brailleCount = brailleCount;
        scope.Items.Add("전체 문서"); scope.Items.Add(new ComboBoxItem { Content = "원본 쪽 범위", IsEnabled = !brailleOnly });
        if (brailleCount is not null) scope.Items.Add("점자 면 범위");
        var fields = new StackPanel { Margin = new(16, 12, 16, 0) }; Body(fields);
        void Field(string label, FrameworkElement value)
        {
            var row = new DockPanel { Height = 32, Margin = new(0, 0, 0, 8) }; row.Children.Add(new TextBlock { Text = label, Width = 120, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
            value.Height = 32; value.MinHeight = 32; value.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(value); fields.Children.Add(row);
            System.Windows.Automation.AutomationProperties.SetName(value, label);
        }
        Field("기준", scope); Field("시작 페이지 번호", start); Field("끝 페이지 번호", end);
        Field("형식", new TextBox { Text = format, IsReadOnly = true, ToolTip = "파일 저장 창에서 선택한 형식입니다." });
        scope.ToolTip = brailleCount is null
            ? "원문 TXT는 원본 쪽 기준으로 저장합니다."
            : "점자 면은 표지를 포함해 조판 결과의 첫 면부터 1, 2, 3…으로 셉니다. 인쇄된 쪽번호와 다를 수 있습니다. 선택한 면의 쪽번호·꼬리말은 유지합니다.\n원본 쪽을 고르면 그 쪽만 별도 조판하며 점자 번호는 현재 판면의 시작 번호를 사용합니다.";
        start.ToolTip = end.ToolTip = bounds;
        Action("저장", () => { if (Apply()) DialogResult = true; }, true); Action("취소", Close);
        scope.SelectionChanged += async (_, _) => await UpdateBoundsAsync();
        scope.SelectedIndex = 0;
    }

    private async Task UpdateBoundsAsync()
    {
        Error.Text = ""; start.IsEnabled = end.IsEnabled = scope.SelectedIndex != 0;
        if (scope.SelectedIndex == 0) { bounds.Text = "전체 문서를 저장합니다."; return; }
        if (scope.SelectedIndex == 1)
        {
            start.Text = originalNumbers.FirstOrDefault().ToString(); end.Text = originalNumbers.LastOrDefault().ToString();
            bounds.Text = $"원본 {start.Text}–{end.Text}쪽"; return;
        }
        if (pageCount is null && brailleCount is not null)
        {
            loading = true; scope.IsEnabled = start.IsEnabled = end.IsEnabled = false; bounds.Text = "전체 문서의 점자 면 수를 계산하고 있습니다…";
            try { pageCount = await brailleCount(); }
            catch (Exception ex) { Error.Text = ex.Message; bounds.Text = "면 수를 계산하지 못했습니다. 다른 범위를 선택할 수 있습니다."; }
            finally { loading = false; scope.IsEnabled = true; start.IsEnabled = end.IsEnabled = true; }
        }
        if (pageCount is { } count)
        {
            start.Text = "1"; end.Text = count.ToString(); bounds.Text = $"총 {count}면 · 첫 면부터 센 순서";
        }
    }

    private bool Apply()
    {
        if (loading) { Error.Text = "점자 면 수를 계산한 뒤 저장할 수 있습니다."; return false; }
        var selected = (ExportScope)scope.SelectedIndex;
        if (selected == ExportScope.All) { Result = new(); return true; }
        if (!int.TryParse(start.Text, out var from) || !int.TryParse(end.Text, out var to))
        { Error.Text = "시작과 끝 번호를 정수로 입력해 주세요."; return false; }
        try
        {
            if (selected == ExportScope.BraillePages && pageCount is null) throw new ArgumentException("전체 점자 면 수를 먼저 확인해 주세요.");
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
