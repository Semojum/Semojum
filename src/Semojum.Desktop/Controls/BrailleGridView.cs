using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

/// <summary>Only visible face cards are rendered. Cell hits retain block offsets, including wrapped lines.</summary>
public sealed class BrailleGridView : ListBox
{
    public event Action<GridCell>? CellSelected;
    public EditorBlock? ActiveBlock { get; set; }
    public int CaretOffset { get; set; }
    public bool ShowOutlines { get; set; } = true;
    public BrailleGridView()
    {
        Background = new SolidColorBrush(Color.FromRgb(244, 245, 247)); BorderThickness = new(0);
        SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        SetValue(ScrollViewer.CanContentScrollProperty, true);
        SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        var factory = new FrameworkElementFactory(typeof(Face));
        factory.SetBinding(Face.DataProperty, new System.Windows.Data.Binding());
        ItemTemplate = new DataTemplate { VisualTree = factory };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(4)));
        style.Setters.Add(new Setter(FocusableProperty, false)); ItemContainerStyle = style;
        SizeChanged += (_, _) => InvalidateFaces();
        AutomationPropertiesName();
    }
    private void AutomationPropertiesName() => System.Windows.Automation.AutomationProperties.SetName(this, "점자 모눈 편집 좌표");
    public void InvalidateFaces()
    {
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item) { item.InvalidateMeasure(); item.InvalidateVisual(); Redraw(item); }
        static void Redraw(DependencyObject root)
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is Face f) { f.InvalidateMeasure(); f.InvalidateVisual(); } else Redraw(child); } }
    }
    private sealed class Face : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(BrailleGridFace), typeof(Face), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public BrailleGridFace? Data { get => (BrailleGridFace?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
        private BrailleGridView? Owner
        {
            get { DependencyObject? p = this; while (p is not null && p is not BrailleGridView) p = VisualTreeHelper.GetParent(p); return p as BrailleGridView; }
        }
        private double CellWidth => Math.Max(3, (Math.Max(120, Owner?.ActualWidth - 34 ?? 450) - 30) / Math.Max(1, Data?.Rows[0].Length ?? 32));
        private double CellHeight => CellWidth * 1.55;
        protected override Size MeasureOverride(Size availableSize) => new(Math.Max(120, Owner?.ActualWidth - 34 ?? 450), 30 + (Data?.Rows.Length ?? 26) * CellHeight);
        protected override void OnRender(DrawingContext dc)
        {
            if (Data is not { } face) return;
            var owner = Owner; var cw = CellWidth; var ch = CellHeight; var cols = face.Rows[0].Length;
            dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
            void Text(string value, Point point, double size, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
            Text($"편집 {face.Number}면", new(30, 4), 12, Brushes.DimGray);
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 228, 232)), .5);
            for (var y = 0; y < face.Rows.Length; y++)
            {
                Text((y + 1).ToString(), new(2, 26 + y * ch), Math.Min(11, ch * .6), Brushes.Gray);
                for (var x = 0; x < cols; x++)
                {
                    var box = new Rect(30 + x * cw, 26 + y * ch, cw, ch); var cell = face.Rows[y][x];
                    if (cell is not null)
                    {
                        if (cell.Block == owner?.ActiveBlock) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(234, 244, 255)), null, box);
                        Text(cell.Value.ToString(), new(box.X, box.Y), cw * 1.45, cell.Block.HasManualBraille ? Brushes.Firebrick : Brushes.Black);
                        if (cell.Block == owner?.ActiveBlock && cell.Offset == owner.CaretOffset) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1.5), box);
                    }
                    dc.DrawRectangle(null, gridPen, box);
                }
            }
            if (owner?.ShowOutlines == true) dc.DrawRectangle(null, new Pen(Brushes.Gray, 1), new(30, 26, cols * cw, face.Rows.Length * ch));
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Data is null) return;
            var p = e.GetPosition(this); var row = (int)Math.Floor((p.Y - 26) / CellHeight); var col = (int)Math.Floor((p.X - 30) / CellWidth);
            if (row < 0 || row >= Data.Rows.Length || col < 0 || col >= Data.Rows[row].Length) return;
            var cell = Data.Rows[row][col];
            if (cell is null) cell = Data.Rows[row].Take(col).LastOrDefault(c => c is not null);
            if (cell is null) return;
            Owner?.CellSelected?.Invoke(cell); e.Handled = true;
        }
    }
}
