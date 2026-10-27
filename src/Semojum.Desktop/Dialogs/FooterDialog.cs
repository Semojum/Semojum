using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed class FooterInputDialog : CompactDialog
{
    private readonly TextBox text = new() { MaxLength = 200 }, start = new();
    private readonly SegmentedChoice align = new(["왼쪽", "가운데", "오른쪽"]);
    public FooterRange? Result { get; private set; }
    public FooterInputDialog(int page = 1) : base("꼬리말 입력", 520, 210)
    {
        start.Text = Math.Max(1, page).ToString(); align.SelectedIndex = 1;
        var fields = new StackPanel { Margin = new(16, 12, 16, 0) }; Body(fields);
        void Field(string label, FrameworkElement input, double width)
        {
            var row = new DockPanel { Height = 44 };
            row.Children.Add(new TextBlock { Text = label, Width = 114, FontSize = 14, Margin = new(0, 4, 0, 0) });
            input.Width = width; input.Height = 32; input.VerticalAlignment = VerticalAlignment.Top; input.HorizontalAlignment = HorizontalAlignment.Left;
            System.Windows.Automation.AutomationProperties.SetName(input, label); row.Children.Add(input); fields.Children.Add(row);
        }
        Field("내용", text, 330); Field("정렬", align, 276);
        var number = new StackPanel { Orientation = Orientation.Horizontal }; start.Width = 72; start.Height = 32; number.Children.Add(start);
        number.Children.Add(new TextBlock { Text = "면", Margin = new(12, 4, 0, 0), Foreground = (Brush)FindResource("Muted") });
        Field("시작 면 번호", number, 120);
        Action("확인", () =>
        {
            if (!int.TryParse(start.Text, out var pageNumber)) throw new ArgumentException("시작 면 번호를 입력해 주세요.");
            var value = new FooterRange(pageNumber, null, text.Text, align.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" });
            FooterRange.Validate([value]); Result = value; DialogResult = true;
        }, true);
        Action("취소", Close);
    }
}

public sealed class FooterDialog : CompactDialog
{
    public sealed class Entry : INotifyPropertyChanged
    {
        private string start = "1", label = "";
        public string Start { get => start; set { start = value; PropertyChanged?.Invoke(this, new(nameof(Start))); } }
        public string RangeLabel { get => label; set { label = value; PropertyChanged?.Invoke(this, new(nameof(RangeLabel))); } }
        public string Alignment { get; set; } = "기본";
        public string Text { get; set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public string Footer { get; private set; } = "";
    public string Alignment { get; private set; } = "center";
    public FooterRange[] Ranges { get; private set; } = [];
    public FooterDialog(DocumentSession session) : base("꼬리말 관리", 560, 300)
    {
        var fields = new StackPanel { Margin = new(16, 8, 16, 0) }; Body(fields);
        var points = FooterRange.ChangePoints(session.FooterRanges, session.FooterText, session.Layout.FooterAlign);
        if (points.Length == 0 && session.FooterText.Length > 0) points = [new(1, null, session.FooterText, session.Layout.FooterAlign)];
        var entries = new ObservableCollection<Entry>(points.Select(r => new Entry { Start = r.Start.ToString(), Text = r.Text, Alignment = r.Alignment switch { "left" => "왼쪽", "right" => "오른쪽", "center" => "가운데", _ => "기본" } }));
        void Labels()
        {
            var sorted = entries.OrderBy(e => int.TryParse(e.Start, out var n) ? n : int.MaxValue).ToArray();
            for (var i = 0; i < sorted.Length; i++)
                sorted[i].RangeLabel = sorted[i].Start + " ~ " + (i + 1 < sorted.Length && int.TryParse(sorted[i + 1].Start, out var end) ? (end - 1).ToString() : "끝");
        }
        void Track(Entry entry) => entry.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Entry.Start)) Labels(); };
        foreach (var entry in entries) Track(entry);
        entries.CollectionChanged += (_, e) => { if (e.NewItems is not null) foreach (Entry entry in e.NewItems) Track(entry); Labels(); }; Labels();
        var grid = new DataGrid { ItemsSource = entries, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
            CanUserReorderColumns = false, CanUserResizeColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, BorderThickness = new(0), Background = Brushes.Transparent,
            GridLinesVisibility = DataGridGridLinesVisibility.None, RowHeight = 32, ColumnHeaderHeight = 28, Height = 164, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grid.ToolTip = "적용 범위를 더블클릭하면 시작 면 번호를 고칩니다. 다음 꼬리말 직전까지 이어지며 마지막 항목은 끝까지 적용됩니다.";
        var header = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        header.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        header.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("Muted")));
        header.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(181, 181, 181))));
        header.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        header.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 0, 0, 6))); grid.ColumnHeaderStyle = header;
        var cellStyle = new Style(typeof(DataGridCell)); cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)FindResource("Ink"))); cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center)); grid.CellStyle = cellStyle;
        var rowStyle = new Style(typeof(DataGridRow)); rowStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(204, 228, 247)))); rowStyle.Triggers.Add(selected); grid.RowStyle = rowStyle;
        DataTemplate Template(Type type, DependencyProperty property, string path, Thickness margin)
        {
            var element = new FrameworkElementFactory(type); element.SetBinding(property, new Binding(path) { Mode = type == typeof(TextBlock) ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            element.SetValue(FrameworkElement.MarginProperty, margin); element.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (type != typeof(TextBlock)) { element.SetValue(FrameworkElement.HeightProperty, 24d); element.SetValue(FrameworkElement.MinHeightProperty, 24d); element.SetValue(Control.FontSizeProperty, 12d); }
            if (type == typeof(ComboBox)) element.SetValue(ItemsControl.ItemsSourceProperty, new[] { "기본", "왼쪽", "가운데", "오른쪽" });
            if (type == typeof(TextBox)) { element.SetValue(TextBox.MaxLengthProperty, 200); element.SetValue(Control.PaddingProperty, new Thickness(7, 0, 7, 0)); }
            return new DataTemplate { VisualTree = element };
        }
        grid.Columns.Add(new DataGridTemplateColumn { Header = "적용 범위", Width = 88,
            CellTemplate = Template(typeof(TextBlock), TextBlock.TextProperty, nameof(Entry.RangeLabel), new(8, 0, 0, 0)),
            CellEditingTemplate = Template(typeof(TextBox), TextBox.TextProperty, nameof(Entry.Start), new(0, 0, 8, 0)) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "내용", Width = 300,
            CellTemplate = Template(typeof(TextBox), TextBox.TextProperty, nameof(Entry.Text), new(0, 0, 12, 0)) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "정렬", Width = 140,
            CellTemplate = Template(typeof(ComboBox), ComboBox.SelectedItemProperty, nameof(Entry.Alignment), new(0, 0, 8, 0)) });
        fields.Children.Add(grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 0) }; fields.Children.Add(buttons);
        var add = Action("추가", () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var entry = new Entry { Start = (entries.Select(e => int.TryParse(e.Start, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1).ToString() };
            entries.Add(entry); grid.SelectedItem = entry; grid.ScrollIntoView(entry); grid.CurrentCell = new DataGridCellInfo(entry, grid.Columns[0]); grid.BeginEdit();
        });
        var delete = Action("지우기", () => { if (grid.SelectedItem is Entry entry) entries.Remove(entry); });
        foreach (var button in new[] { add, delete }) { Actions.Children.Remove(button); button.Margin = new(0, 0, 8, 0); buttons.Children.Add(button); }
        Action("확인", () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var ranges = entries.Select(e =>
            {
                if (!int.TryParse(e.Start, out var start)) throw new ArgumentException("시작 면 번호를 정수로 입력해 주세요.");
                return new FooterRange(start, null, e.Text, e.Alignment switch { "왼쪽" => "left", "오른쪽" => "right", "가운데" => "center", _ => null });
            }).OrderBy(r => r.Start).ToArray();
            Ranges = FooterRange.Continue(ranges);
            var first = Ranges.FirstOrDefault(r => r.Start == 1); Footer = first?.Text ?? (Ranges.Length == 0 ? "" : session.FooterText);
            Alignment = first?.Alignment ?? session.Layout.FooterAlign; DialogResult = true;
        }, true);
        Action("취소", Close); if (entries.Count > 0) grid.SelectedIndex = 0;
    }
}
