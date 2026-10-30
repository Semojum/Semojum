using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Dialogs;

internal static class FooterDesign
{
    internal static void StyleAlignment(SegmentedChoice choice)
    {
        choice.Width = 288; choice.Height = 32; choice.FontSize = 12;
        var style = new Style(typeof(ListBoxItem));
        var surface = new FrameworkElementFactory(typeof(Border)); surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        surface.SetBinding(Border.BorderBrushProperty, new Binding(nameof(Control.BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); surface.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = surface }));
        style.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB"))); style.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA"))); style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#EFF6FC"))); selected.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#005FB8"))); selected.Setters.Add(new Setter(Control.ForegroundProperty, EditorModalDesign.Color("#003E92"))); style.Triggers.Add(selected);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45)); style.Triggers.Add(disabled); choice.ItemContainerStyle = style;
    }
    internal static FrameworkElement Spinner(TextBox field)
    {
        EditorModalDesign.Input(field); field.Padding = new Thickness(11, 0, 0, 0); field.MinHeight = 0;
        var grid = new Grid { Width = 96, Height = 32, HorizontalAlignment = HorizontalAlignment.Left }; grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
        Grid.SetColumnSpan(field, 2); grid.Children.Add(field); var arrows = new StackPanel(); Grid.SetColumn(arrows, 1); grid.Children.Add(arrows);
        foreach (var up in new[] { true, false })
        {
            var icon = up ? EditorModalDesign.Icon("footer-up", 9.60063, 5.19934) : EditorModalDesign.Icon("footer-down");
            if (up) { icon.RenderTransform = new ScaleTransform(1, -1); icon.RenderTransformOrigin = new Point(.5, .5); }
            var button = new Button { Content = icon, Height = 16, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = EditorModalDesign.Color("#F0F0F0"), Focusable = false, Template = EditorModalDesign.ButtonTemplate(typeof(Button)) };
            System.Windows.Automation.AutomationProperties.SetName(button, up ? "시작 면 번호 늘리기" : "시작 면 번호 줄이기");
            button.Click += (_, _) => { if (int.TryParse(field.Text, out var n)) field.Text = (n + (up ? 1 : -1)).ToString(); }; arrows.Children.Add(button);
        }
        return grid;
    }
    internal static void Row(StackPanel panel, string label, UIElement value)
    {
        if (panel.Children.Count > 0) ((FrameworkElement)panel.Children[panel.Children.Count - 1]).Margin = new Thickness(0, 0, 0, 4);
        var row = new DockPanel { Height = 32, LastChildFill = false }; row.Children.Add(new TextBlock { Text = label, Width = 134, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(value); panel.Children.Add(row);
    }
}

public sealed class HeaderInputDialog : CompactDialog
{
    private readonly TextBox input = new() { MaxLength = 200, Width = 298, Height = 32, MinHeight = 0, VerticalContentAlignment = VerticalAlignment.Center };
    public string Result => input.Text;
    public HeaderInputDialog(string initial) : base("머리말", 520, 252)
    {
        input.Text = initial; EditorModalDesign.Input(input); System.Windows.Automation.AutomationProperties.SetName(input, "머리말 내용");
        var alignment = new SegmentedChoice("왼쪽", "가운데", "오른쪽") { SelectedIndex = 1 }; FooterDesign.StyleAlignment(alignment);
        alignment.SetAvailable(0, false, "현재 머리말은 가운데 정렬만 지원합니다."); alignment.SetAvailable(2, false, "현재 머리말은 가운데 정렬만 지원합니다.");
        System.Windows.Automation.AutomationProperties.SetName(alignment, "머리말 정렬");
        var content = new StackPanel(); FooterDesign.Row(content, "내용", input); FooterDesign.Row(content, "정렬", alignment);
        var card = EditorModalDesign.Card(content, new Thickness(24, 16, 24, 0)); card.Padding = new Thickness(16, 12, 16, 8); card.VerticalAlignment = VerticalAlignment.Top; Body(card);
        Action("취소", () => DialogResult = false); Action("확인", () =>
        {
            if (input.Text.Contains("<!머리말>", StringComparison.Ordinal) || input.Text.Contains("<!/머리말>", StringComparison.Ordinal)) throw new ArgumentException("머리말 안에 머리말 태그를 넣을 수 없습니다.");
            DialogResult = true;
        }, true);
        EditorModalDesign.Frame(this, Root, Actions);
    }
}

public sealed class FooterInputDialog : CompactDialog
{
    private readonly TextBox text = new() { MaxLength = 200 }, start = new();
    private readonly SegmentedChoice align = new(["왼쪽", "가운데", "오른쪽"]);
    public FooterRange? Result { get; private set; }
    public FooterInputDialog(int page = 1) : base("꼬리말", 520, 288)
    {
        start.Text = Math.Max(1, page).ToString(); align.SelectedIndex = 1;
        var fields = new StackPanel();
        text.Width = 298; text.Height = 32; text.MinHeight = 0; text.VerticalContentAlignment = VerticalAlignment.Center; EditorModalDesign.Input(text);
        System.Windows.Automation.AutomationProperties.SetName(text, "내용"); System.Windows.Automation.AutomationProperties.SetName(start, "시작 면 번호");
        FooterDesign.StyleAlignment(align); System.Windows.Automation.AutomationProperties.SetName(align, "정렬");
        FooterDesign.Row(fields, "내용", text); FooterDesign.Row(fields, "정렬", align);
        var number = new StackPanel { Orientation = Orientation.Horizontal }; number.Children.Add(FooterDesign.Spinner(start));
        number.Children.Add(new TextBlock { Text = "면", Margin = new(8, 0, 0, 0), Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center });
        FooterDesign.Row(fields, "시작 면 번호", number);
        var card = EditorModalDesign.Card(fields, new Thickness(24, 16, 24, 0)); card.Padding = new Thickness(16, 12, 16, 8); card.VerticalAlignment = VerticalAlignment.Top; Body(card);
        Action("취소", Close);
        Action("확인", () =>
        {
            if (!int.TryParse(start.Text, out var pageNumber)) throw new ArgumentException("시작 면 번호를 입력해 주세요.");
            var value = new FooterRange(pageNumber, null, text.Text, align.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" });
            FooterRange.Validate([value]); Result = value; DialogResult = true;
        }, true);
        EditorModalDesign.Frame(this, Root, Actions);
    }
}

public sealed class FooterDialog : CompactDialog
{
    public sealed class Entry : INotifyPropertyChanged
    {
        private string start = "1", label = "", end = "끝";
        public string Start { get => start; set { start = value; PropertyChanged?.Invoke(this, new(nameof(Start))); } }
        public string RangeLabel { get => label; set { label = value; PropertyChanged?.Invoke(this, new(nameof(RangeLabel))); } }
        public string End { get => end; set { end = value; PropertyChanged?.Invoke(this, new(nameof(End))); } }
        public string Alignment { get; set; } = "기본";
        public string Text { get; set; } = "";
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public string Footer { get; private set; } = "";
    public string Alignment { get; private set; } = "center";
    public FooterRange[] Ranges { get; private set; } = [];
    public FooterDialog(DocumentSession session) : base("꼬리말 관리", 680, 326)
    {
        var fields = new StackPanel { Margin = new(16, 8, 16, 0) }; Body(fields);
        var points = FooterRange.ChangePoints(session.FooterRanges, session.FooterText, session.Layout.FooterAlign);
        if (points.Length == 0 && session.FooterText.Length > 0) points = [new(1, null, session.FooterText, session.Layout.FooterAlign)];
        var entries = new ObservableCollection<Entry>(points.Select(r => new Entry { Start = r.Start.ToString(), Text = r.Text, Alignment = r.Alignment switch { "left" => "왼쪽", "right" => "오른쪽", "center" => "가운데", _ => "기본" } }));
        void Labels()
        {
            var sorted = entries.OrderBy(e => int.TryParse(e.Start, out var n) ? n : int.MaxValue).ToArray();
            for (var i = 0; i < sorted.Length; i++)
            {
                sorted[i].End = i + 1 < sorted.Length && int.TryParse(sorted[i + 1].Start, out var end) ? (end - 1).ToString() : "끝";
                sorted[i].RangeLabel = sorted[i].Start + " ~ " + sorted[i].End;
            }
        }
        void Track(Entry entry) => entry.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Entry.Start)) Labels(); };
        foreach (var entry in entries) Track(entry);
        entries.CollectionChanged += (_, e) => { if (e.NewItems is not null) foreach (Entry entry in e.NewItems) Track(entry); Labels(); }; Labels();
        var grid = new DataGrid { ItemsSource = entries, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, CanUserSortColumns = false,
            CanUserReorderColumns = false, CanUserResizeColumns = false, HeadersVisibility = DataGridHeadersVisibility.Column, BorderThickness = new(0), Background = Brushes.Transparent,
            GridLinesVisibility = DataGridGridLinesVisibility.None, RowHeight = 32, ColumnHeaderHeight = 28, Height = 164, SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        grid.ToolTip = "시작 면 번호를 고칠 수 있습니다. 끝 면은 다음 꼬리말 직전까지 자동으로 정해지며 마지막 항목은 끝까지 적용됩니다.";
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
        var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#EFF6FC"))); rowStyle.Triggers.Add(selected); grid.RowStyle = rowStyle;
        var styledInput = new TextBox(); EditorModalDesign.Input(styledInput); var inputTemplate = styledInput.Template;
        DataTemplate Template(Type type, DependencyProperty property, string path, Thickness margin)
        {
            var element = new FrameworkElementFactory(type); element.SetBinding(property, new Binding(path) { Mode = type == typeof(TextBlock) ? BindingMode.OneWay : BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            element.SetValue(FrameworkElement.MarginProperty, margin); element.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            if (type != typeof(TextBlock)) { element.SetValue(FrameworkElement.HeightProperty, 24d); element.SetValue(FrameworkElement.MinHeightProperty, 24d); element.SetValue(Control.FontSizeProperty, 12d); }
            if (type == typeof(ComboBox))
            {
                element.SetValue(ItemsControl.ItemsSourceProperty, new[] { "기본", "왼쪽", "가운데", "오른쪽" }); element.SetValue(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); element.SetValue(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA"));
                element.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
                var wrapper = new FrameworkElementFactory(typeof(Grid)); wrapper.SetValue(FrameworkElement.WidthProperty, 138d); wrapper.SetValue(FrameworkElement.HeightProperty, 24d); wrapper.SetValue(FrameworkElement.MarginProperty, margin); wrapper.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); wrapper.AppendChild(element);
                var overlay = new FrameworkElementFactory(typeof(Border)); overlay.SetValue(FrameworkElement.WidthProperty, 24d); overlay.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 1, 1, 1)); overlay.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right); overlay.SetValue(Border.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); overlay.SetValue(UIElement.IsHitTestVisibleProperty, false);
                var arrow = new FrameworkElementFactory(typeof(SharpVectors.Converters.SvgViewbox)); arrow.SetValue(FrameworkElement.WidthProperty, 16d); arrow.SetValue(FrameworkElement.HeightProperty, 16d); arrow.SetValue(SharpVectors.Converters.SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/footer-chevron.svg")); arrow.SetValue(System.Windows.Controls.Viewbox.StretchProperty, Stretch.None); overlay.AppendChild(arrow); wrapper.AppendChild(overlay);
                return new DataTemplate { VisualTree = wrapper };
            }
            if (type == typeof(TextBox)) { element.SetValue(TextBox.MaxLengthProperty, 200); element.SetValue(Control.PaddingProperty, new Thickness(7, 0, 7, 0)); element.SetValue(Control.TemplateProperty, inputTemplate); element.SetValue(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA")); element.SetValue(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); }
            return new DataTemplate { VisualTree = element };
        }
        var range = new FrameworkElementFactory(typeof(StackPanel)); range.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); range.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var from = new FrameworkElementFactory(typeof(TextBox)); from.SetBinding(TextBox.TextProperty, new Binding(nameof(Entry.Start)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); from.SetValue(FrameworkElement.WidthProperty, 60d); from.SetValue(FrameworkElement.HeightProperty, 24d); from.SetValue(FrameworkElement.MinHeightProperty, 0d); from.SetValue(Control.PaddingProperty, new Thickness(8, 0, 8, 0)); from.SetValue(Control.TemplateProperty, inputTemplate); from.SetValue(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA")); from.SetValue(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); from.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "시작 면 번호"); range.AppendChild(from);
        var tilde = new FrameworkElementFactory(typeof(TextBlock)); tilde.SetValue(TextBlock.TextProperty, "~"); tilde.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 6, 0)); tilde.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); range.AppendChild(tilde);
        var endField = new FrameworkElementFactory(typeof(TextBox)); endField.SetBinding(TextBox.TextProperty, new Binding(nameof(Entry.End))); endField.SetValue(TextBox.IsReadOnlyProperty, true); endField.SetValue(FrameworkElement.WidthProperty, 60d); endField.SetValue(FrameworkElement.HeightProperty, 24d); endField.SetValue(FrameworkElement.MinHeightProperty, 0d); endField.SetValue(Control.PaddingProperty, new Thickness(8, 0, 8, 0)); endField.SetValue(Control.TemplateProperty, inputTemplate); endField.SetValue(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA")); endField.SetValue(Control.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); endField.SetValue(FrameworkElement.ToolTipProperty, "끝 면은 다음 시작 면으로 자동 계산됩니다."); endField.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "자동 계산된 끝 면 번호"); range.AppendChild(endField);
        grid.Columns.Add(new DataGridTemplateColumn { Header = "적용 범위", Width = 170, CellTemplate = new DataTemplate { VisualTree = range } });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "내용", Width = 280,
            CellTemplate = Template(typeof(TextBox), TextBox.TextProperty, nameof(Entry.Text), new(0, 0, 12, 0)) });
        grid.Columns.Add(new DataGridTemplateColumn { Header = "정렬", Width = 146,
            CellTemplate = Template(typeof(ComboBox), ComboBox.SelectedItemProperty, nameof(Entry.Alignment), new(0, 0, 8, 0)) });
        var remove = new FrameworkElementFactory(typeof(Button)); remove.SetValue(FrameworkElement.WidthProperty, 32d); remove.SetValue(FrameworkElement.HeightProperty, 24d); remove.SetValue(FrameworkElement.MinHeightProperty, 0d); remove.SetValue(Control.PaddingProperty, new Thickness(0)); remove.SetValue(Control.BorderThicknessProperty, new Thickness(0)); remove.SetValue(Control.BackgroundProperty, Brushes.Transparent); remove.SetValue(Control.TemplateProperty, EditorModalDesign.ButtonTemplate(typeof(Button))); remove.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, "이 꼬리말 삭제");
        var trash = new FrameworkElementFactory(typeof(SharpVectors.Converters.SvgViewbox)); trash.SetValue(FrameworkElement.WidthProperty, 16d); trash.SetValue(FrameworkElement.HeightProperty, 16d); trash.SetValue(SharpVectors.Converters.SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/footer-trash.svg")); trash.SetValue(System.Windows.Controls.Viewbox.StretchProperty, Stretch.None); remove.AppendChild(trash);
        remove.AddHandler(System.Windows.Controls.Button.ClickEvent, new RoutedEventHandler((sender, _) => { if (sender is Button { DataContext: Entry entry }) entries.Remove(entry); }));
        grid.Columns.Add(new DataGridTemplateColumn { Header = "", Width = 32, CellTemplate = new DataTemplate { VisualTree = remove } });
        fields.Children.Add(grid);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 6, 0, 0) }; fields.Children.Add(buttons);
        var add = Action("추가", () =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            var entry = new Entry { Start = (entries.Select(e => int.TryParse(e.Start, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1).ToString() };
            entries.Add(entry); grid.SelectedItem = entry; grid.ScrollIntoView(entry);
        });
        var delete = Action("지우기", () => { if (grid.SelectedItem is Entry entry) entries.Remove(entry); });
        foreach (var button in new[] { add, delete }) { Actions.Children.Remove(button); button.Margin = new(0, 0, 8, 0); buttons.Children.Add(button); }
        Action("취소", Close);
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
        EditorModalDesign.Frame(this, Root, Actions); if (entries.Count > 0) grid.SelectedIndex = 0;
    }
}
