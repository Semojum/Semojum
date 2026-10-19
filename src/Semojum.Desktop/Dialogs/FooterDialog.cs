using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class FooterDialog : CompactDialog
{
    public sealed class Entry
    {
        public string Start { get; set; } = "1";
        public string End { get; set; } = "";
        public string Alignment { get; set; } = "기본";
        public string Text { get; set; } = "";
    }
    public string Footer { get; private set; } = "";
    public string Alignment { get; private set; } = "center";
    public FooterRange[] Ranges { get; private set; } = [];
    public FooterDialog(DocumentSession session) : base("꼬리말 관리", 820, 560)
    {
        var fields = new StackPanel { Margin = new(16, 8, 16, 8) }; DockPanel.SetDock(fields, Dock.Top); Root.Children.Add(fields);
        var text = new TextBox { Text = session.FooterText, MaxLength = 200 };
        var align = Choices(["왼쪽", "가운데", "오른쪽"], session.Layout.FooterAlign switch { "left" => "왼쪽", "right" => "오른쪽", _ => "가운데" });
        Row(fields, "기본 꼬리말", text); Row(fields, "파일 전체 정렬", align);
        fields.Children.Add(new TextBlock { Text = "아래 범위가 기본 꼬리말을 덮어씁니다. 끝 면을 비우면 끝까지 적용합니다. 면 번호는 표지를 제외한 인쇄 번호이며, 빈 내용은 해당 범위의 꼬리말을 숨깁니다.", TextWrapping = TextWrapping.Wrap });
        fields.Children.Add(new TextBlock { Text = "번호 표시와 별도로 꼬리말 출력 설정을 따릅니다. 범위 설정은 .smj와 내보내기에 보존되며 서버에는 기본 꼬리말만 저장됩니다.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
        var entries = new ObservableCollection<Entry>(session.FooterRanges.Select(r => new Entry { Start = r.Start.ToString(), End = r.End?.ToString() ?? "", Text = r.Text, Alignment = r.Alignment switch { "left" => "왼쪽", "right" => "오른쪽", "center" => "가운데", _ => "기본" } }));
        var grid = new DataGrid { ItemsSource = entries, AutoGenerateColumns = false, CanUserAddRows = false, Margin = new(16), SelectionMode = DataGridSelectionMode.Single };
        grid.Columns.Add(new DataGridTextColumn { Header = "시작 면", Binding = new Binding(nameof(Entry.Start)), Width = 100 });
        grid.Columns.Add(new DataGridTextColumn { Header = "끝 면 (비우면 끝까지)", Binding = new Binding(nameof(Entry.End)), Width = 180 });
        grid.Columns.Add(new DataGridComboBoxColumn { Header = "정렬", ItemsSource = new[] { "기본", "왼쪽", "가운데", "오른쪽" }, SelectedItemBinding = new Binding(nameof(Entry.Alignment)), Width = 90 });
        grid.Columns.Add(new DataGridTextColumn { Header = "꼬리말 내용", Binding = new Binding(nameof(Entry.Text)), Width = new(1, DataGridLengthUnitType.Star) }); Body(grid);
        Action("범위 추가", () => { var entry = new Entry(); entries.Add(entry); grid.SelectedItem = entry; grid.ScrollIntoView(entry); });
        Action("범위 삭제", () => { if (grid.SelectedItem is Entry entry) entries.Remove(entry); });
        Action("적용", () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var ranges = entries.Select(e =>
            {
                if (!int.TryParse(e.Start, out var start) || !string.IsNullOrWhiteSpace(e.End) && !int.TryParse(e.End, out _)) throw new ArgumentException("면 번호를 정수로 입력해 주세요.");
                return new FooterRange(start, string.IsNullOrWhiteSpace(e.End) ? null : int.Parse(e.End), e.Text, e.Alignment switch { "왼쪽" => "left", "오른쪽" => "right", "가운데" => "center", _ => null });
            }).OrderBy(r => r.Start).ToArray();
            FooterRange.Validate(ranges); Ranges = ranges; Footer = text.Text;
            Alignment = align.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" }; DialogResult = true;
        }, true);
        Action("취소", Close);
    }
}
