using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Semojum.Desktop.Controls;

/// <summary>Mutually exclusive settings with native selection, arrow keys and automation.</summary>
public sealed class SegmentedChoice : ListBox
{
    public SegmentedChoice(params string[] labels)
    {
        Width = 280; Height = 24; FontSize = 12; Padding = new(0); BorderThickness = new(0);
        Background = Brushes.Transparent; HorizontalAlignment = HorizontalAlignment.Left;
        SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var panel = new FrameworkElementFactory(typeof(UniformGrid)); panel.SetValue(UniformGrid.RowsProperty, 1);
        ItemsPanel = new ItemsPanelTemplate(panel);
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(181, 181, 181)));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content);
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        style.Setters.Add(new Setter(BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(Color.FromRgb(95, 100, 109))));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(BackgroundProperty, new SolidColorBrush(Color.FromRgb(204, 228, 247))));
        selected.Setters.Add(new Setter(ForegroundProperty, new SolidColorBrush(Color.FromRgb(31, 31, 31)))); style.Triggers.Add(selected);
        ItemContainerStyle = style;
        foreach (var label in labels) Items.Add(new ListBoxItem { Content = label });
        SelectedIndex = 0;
    }
    public void SetAvailable(int index, bool available, string? reason = null)
    {
        var item = (ListBoxItem)Items[index]; item.IsEnabled = available; item.ToolTip = reason;
        ToolTipService.SetShowOnDisabled(item, true);
    }
}
