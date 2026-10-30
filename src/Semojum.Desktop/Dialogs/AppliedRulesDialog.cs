using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class AppliedRulesDialog : CompactDialog
{
    internal DataGrid Rules { get; } = new() { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single };
    public AppliedRulesDialog(SemojumApiClient api, AppliedRuleRow[] rows, bool stale, string sourceText = "", string selectedBraille = "", string? position = null)
        : base("적용 규정" + (position is null ? "" : " — " + position), 900, 488, popover: true)
    {
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = ExportRangeDialog.Brush("#EAEAEA"); frame.Background = ExportRangeDialog.Brush("#F3F3F3"); }
        var layout = new Grid(); layout.RowDefinitions.Add(new() { Height = new(56) }); layout.RowDefinitions.Add(new()); layout.RowDefinitions.Add(new() { Height = new(64) }); Body(layout);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = "적용 규정", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var dismiss = new Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = ExportRangeDialog.DesignIcon("applied-rules-close.svg", 16, 16), Template = ExportRangeDialog.SurfaceTemplate(0) };
        AutomationProperties.SetName(dismiss, "닫기"); dismiss.Click += (_, _) => Close(); header.Children.Add(dismiss); layout.Children.Add(header);

        var body = new DockPanel { Margin = new(16, 6, 16, 0) }; Grid.SetRow(body, 1); layout.Children.Add(body);
        var selection = new StackPanel(); DockPanel.SetDock(selection, Dock.Top); body.Children.Add(selection);
        selection.Children.Add(new TextBlock { Text = position is null ? "원문" : "원문 · " + position, Height = 22, Foreground = ExportRangeDialog.Brush("#5F5F5F") });
        var preview = new Grid { Margin = new(15, 8, 15, 8) };
        preview.ColumnDefinitions.Add(new()); preview.ColumnDefinitions.Add(new() { Width = new(1) }); preview.ColumnDefinitions.Add(new());
        var source = new StackPanel(); source.Children.Add(new TextBlock { Text = "묵자", Height = 20, Foreground = ExportRangeDialog.Brush("#5F5F5F") });
        var sourceValue = new TextBlock { Text = sourceText, FontSize = 18, Height = 24, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = sourceText };
        AutomationProperties.SetAutomationId(sourceValue, "RulesSourcePreview"); source.Children.Add(sourceValue); preview.Children.Add(source);
        var separator = new Border { Background = ExportRangeDialog.Brush("#EAEAEA"), Margin = new(0, 3, 0, 3) }; Grid.SetColumn(separator, 1); preview.Children.Add(separator);
        var braille = new StackPanel { Margin = new(15, 0, 0, 0) }; braille.Children.Add(new TextBlock { Text = "점자", Height = 20, Foreground = ExportRangeDialog.Brush("#5F5F5F") });
        var brailleText = selectedBraille.Length > 0 ? selectedBraille : string.Concat(rows.Select(r => r.Cells));
        var brailleValue = new TextBlock { Text = brailleText, FontFamily = new("Segoe UI Symbol"), FontSize = 22, Height = 24, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = brailleText };
        AutomationProperties.SetAutomationId(brailleValue, "RulesBraillePreview"); braille.Children.Add(brailleValue); Grid.SetColumn(braille, 2); preview.Children.Add(braille);
        selection.Children.Add(new Border { Height = 72, CornerRadius = new(6), Background = ExportRangeDialog.Brush("#F9F9F9"), BorderBrush = ExportRangeDialog.Brush("#EAEAEA"), BorderThickness = new(1), Child = preview });
        Rules.Margin = new(0, 16, 0, 0); Rules.Style = (Style)FindResource("BoardGrid"); Rules.MinRowHeight = 56;
        Rules.Background = Rules.RowBackground = Rules.AlternatingRowBackground = ExportRangeDialog.Brush("#F3F3F3");
        var baseCell = (Style)Rules.Style.Setters.OfType<Setter>().Single(s => s.Property == DataGrid.CellStyleProperty).Value;
        var cells = new Style(typeof(DataGridCell), baseCell); cells.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent)); cells.Triggers.Add(selected);
        var focused = new MultiTrigger(); focused.Conditions.Add(new System.Windows.Condition(DataGridCell.IsSelectedProperty, true)); focused.Conditions.Add(new System.Windows.Condition(IsKeyboardFocusWithinProperty, true));
        focused.Setters.Add(new Setter(Control.BackgroundProperty, ExportRangeDialog.Brush("#CCE4F7"))); cells.Triggers.Add(focused); Rules.CellStyle = cells;
        var baseHeader = (Style)Rules.Style.Setters.OfType<Setter>().Single(s => s.Property == DataGrid.ColumnHeaderStyleProperty).Value;
        var headers = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader), baseHeader); headers.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); headers.Setters.Add(new Setter(Control.BackgroundProperty, ExportRangeDialog.Brush("#F3F3F3"))); Rules.ColumnHeaderStyle = headers;
        DataGridTextColumn Column(string caption, string field, double width, bool brailleColumn = false)
        {
            var style = new Style(typeof(TextBlock)); style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
            style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty, new Binding(field)));
            if (brailleColumn) { style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("Segoe UI Symbol"))); style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 22d)); }
            if (field == "Symbol")
            {
                style.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14d)); style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
                foreach (var trigger in new[] { new DataTrigger { Binding = new Binding(field), Value = "줄 전체" }, new DataTrigger { Binding = new Binding(nameof(AppliedRuleRow.IsBlockEvidence)), Value = true } })
                {
                    trigger.Setters.Add(new Setter(TextBlock.FontSizeProperty, 12d)); trigger.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Normal));
                    trigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, ExportRangeDialog.Brush("#5F5F5F"))); style.Triggers.Add(trigger);
                }
            }
            return new DataGridTextColumn { Header = caption, Binding = new Binding(field), Width = width, ElementStyle = style };
        }
        Rules.Columns.Add(Column("걸린 자리", "Symbol", 80)); Rules.Columns.Add(Column("점형", "Cells", 74, true));
        var description = new FrameworkElementFactory(typeof(StackPanel));
        var title = new FrameworkElementFactory(typeof(TextBlock)); title.SetBinding(TextBlock.TextProperty, new Binding("Title")); title.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold); title.SetValue(TextBlock.FontSizeProperty, 14d); description.AppendChild(title);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        var sourceSection = new MultiBinding { StringFormat = "{0} · {1}" }; sourceSection.Bindings.Add(new Binding("Source")); sourceSection.Bindings.Add(new Binding("Section"));
        detail.SetBinding(TextBlock.TextProperty, sourceSection); detail.SetValue(TextBlock.ForegroundProperty, ExportRangeDialog.Brush("#5F5F5F")); detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis); description.AppendChild(detail);
        Rules.Columns.Add(new DataGridTemplateColumn { Header = "규정", Width = 230, CellTemplate = new DataTemplate { VisualTree = description } });
        var textColumn = Column("조항 원문", "Text", 304); textColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Star); Rules.Columns.Add(textColumn);
        Root.Children.Remove(Status); Status.Margin = new(0, 4, 0, 4); DockPanel.SetDock(Status, Dock.Bottom); body.Children.Add(Status);
        Rules.ItemsSource = rows; body.Children.Add(Rules);
        if (rows.Length > 0)
        {
            Rules.SelectedIndex = 0;
            if (rows.Any(row => row.IsBlockEvidence))
                Status.Text = "원본 변환에서 전달된 근거가 포함되어 있습니다. 현재 초안이나 선택한 점자 칸에 적용되는 위치는 확인되지 않았습니다.";
        }
        else Status.Text = stale ? "편집 후에는 적용 규정의 위치가 달라질 수 있습니다. 재점역하면 현재 결과의 근거를 확인할 수 있습니다." : "선택한 위치에 전달된 적용 규정이 없습니다.";
        var footer = new Border { BorderBrush = ExportRangeDialog.Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0) }; Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var footerLayout = new Grid { Margin = new(16, 0, 24, 0) };
        footerLayout.ColumnDefinitions.Add(new()); footerLayout.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); footer.Child = footerLayout;
        var reason = new TextBlock { FontSize = 12, Foreground = ExportRangeDialog.Brush("#5F5F5F"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 8, 0) };
        AutomationProperties.SetAutomationId(reason, "AppliedRulesOpenUnavailableReason");
        AutomationProperties.SetLiveSetting(reason, AutomationLiveSetting.Polite);
        footerLayout.Children.Add(reason);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 15, 0, 16) }; Grid.SetColumn(actions, 1); footerLayout.Children.Add(actions);
        var close = Action("닫기", Close); Actions.Children.Remove(close);
        Button open = null!;
        void UpdateOpenAvailability()
        {
            var message = api.Account is null ? "규정 전문을 보려면 로그인해 주세요."
                : Rules.SelectedItem is not AppliedRuleRow ? "전문을 볼 규정 행을 선택해 주세요."
                : string.IsNullOrWhiteSpace(((AppliedRuleRow)Rules.SelectedItem).RuleId) ? "선택한 규정의 식별자가 없어 전문을 열 수 없습니다."
                : "";
            open.IsEnabled = message.Length == 0;
            open.ToolTip = message.Length == 0 ? null : message;
            AutomationProperties.SetHelpText(open, message);
            reason.Text = message;
            reason.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        open = Action("규정 전문 열기", () =>
        {
            UpdateOpenAvailability();
            if (open.IsEnabled && Rules.SelectedItem is AppliedRuleRow row)
                new RuleDialog(api, [], "", "", row.RuleId) { Owner = this }.ShowDialog();
        }, true);
        Actions.Children.Remove(open);
        ToolTipService.SetShowOnDisabled(open, true);
        Rules.SelectionChanged += (_, _) => UpdateOpenAvailability();
        Activated += (_, _) => UpdateOpenAvailability();
        // Authentication can expire while this modal remains open. Keep the
        // visible action in sync without changing the selected evidence row.
        var accountCheck = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        accountCheck.Tick += (_, _) => UpdateOpenAvailability();
        Loaded += (_, _) => accountCheck.Start();
        Closed += (_, _) => accountCheck.Stop();
        UpdateOpenAvailability();
        foreach (var (button, width) in new[] { (close, 88d), (open, 132d) })
        { button.Width = width; button.MinWidth = 0; button.Height = 32; button.MinHeight = 0; button.Padding = new(0); button.BorderBrush = button == close ? ExportRangeDialog.Brush("#EAEAEA") : ExportRangeDialog.Brush("#005FB8"); button.Template = ExportRangeDialog.SurfaceTemplate(4); actions.Children.Add(button); }
    }
}
