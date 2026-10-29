using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class AppliedRulesDialog : CompactDialog
{
    internal DataGrid Rules { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
    public AppliedRulesDialog(SemojumApiClient api, AppliedRuleRow[] rows, bool stale, string sourceText = "", string selectedBraille = "", string? position = null) : base("적용 규정" + (position is null ? "" : " — " + position), 620, 440)
    {
        var body = new DockPanel { Margin = new(16, 12, 16, 12) }; Body(body);
        var selection = new StackPanel(); DockPanel.SetDock(selection, Dock.Top); body.Children.Add(selection);
        selection.Children.Add(new TextBlock { Text = "고른 자리", Height = 18, Foreground = (Brush)FindResource("Muted") });
        var preview = new StackPanel { Margin = new(11, 7, 11, 0) };
        preview.Children.Add(new TextBlock { Text = "묵자", Height = 16, Foreground = (Brush)FindResource("Muted") });
        preview.Children.Add(new TextBlock { Text = sourceText, FontSize = 14, Height = 20, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sourceText });
        preview.Children.Add(new TextBlock { Text = "점자", Height = 14, Foreground = (Brush)FindResource("Muted") });
        preview.Children.Add(new TextBlock { Text = selectedBraille.Length > 0 ? selectedBraille : string.Concat(rows.Select(r => r.Cells)), FontFamily = new("Segoe UI Symbol"), FontSize = 22, Height = 26, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = selectedBraille });
        selection.Children.Add(new Border { Height = 86, Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(1), Child = preview });
        Rules.Margin = new(0, 8, 0, 0); Rules.Style = (Style)FindResource("BoardGrid"); Rules.RowHeight = 52;
        var baseCell = (Style)Rules.Style.Setters.OfType<Setter>().Single(s => s.Property == DataGrid.CellStyleProperty).Value;
        var cells = new Style(typeof(DataGridCell), baseCell); cells.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); Rules.CellStyle = cells;
        var baseHeader = (Style)Rules.Style.Setters.OfType<Setter>().Single(s => s.Property == DataGrid.ColumnHeaderStyleProperty).Value;
        var headers = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseHeader); headers.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); Rules.ColumnHeaderStyle = headers;
        foreach (var (header, field, width) in new[] { ("기호", "Symbol", 104d), ("점형", "Cells", 80d), ("조항", "Section", 100d), ("조항 원문", "Text", 304d) })
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(field)));
            if (field == "Cells") { style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI Symbol"))); style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 22d)); }
            Rules.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(field), Width = width, ElementStyle = style });
        }
        Rules.ItemsSource = rows; body.Children.Add(Rules);
        if (rows.Length > 0)
        {
            Rules.SelectedIndex = 0;
            if (rows.Any(row => row.IsBlockEvidence))
                Status.Text = "원본 변환에서 전달된 근거가 포함되어 있습니다. 현재 초안이나 선택한 점자 칸에 적용되는 위치는 확인되지 않았습니다.";
        }
        else Status.Text = stale ? "편집 후에는 적용 규정의 위치가 달라질 수 있습니다. 재점역하면 현재 결과의 근거를 확인할 수 있습니다." : "선택한 위치에 전달된 적용 규정이 없습니다.";
        var open = Action("규정 전문 열기", () =>
        {
            if (Rules.SelectedItem is AppliedRuleRow row) new RuleDialog(api, [], "", "", row.RuleId) { Owner = this }.ShowDialog();
        }, true);
        open.Width = 118; open.IsEnabled = api.Account is not null && rows.Length > 0;
        Action("닫기", Close);
    }
}
