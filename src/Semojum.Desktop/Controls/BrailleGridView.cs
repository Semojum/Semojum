using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

/// <summary>Only visible face cards are rendered. Cell hits retain block offsets, including wrapped lines.</summary>
public sealed partial class BrailleGridView : ListBox
{
    public event Action<GridCell>? CellSelected;
    public EditorBlock? ActiveBlock { get; set; }
    public int CaretOffset { get; set; }
    public bool ShowOutlines { get; set; } = true;
    public bool HighContrast { get; set; }
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
        InstallEditing();
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
        private EditorBlock? dragBlock;
        private Point dragStart;
        public Face() { AllowDrop = true; }
        private double CellWidth => Math.Max(3, (Math.Max(120, Owner?.ActualWidth - 34 ?? 450) - 30) / Math.Max(1, Data?.Rows[0].Length ?? 32));
        private double CellHeight => CellWidth * 1.55;
        protected override Size MeasureOverride(Size availableSize) => new(Math.Max(120, Owner?.ActualWidth - 34 ?? 450), 30 + (Data?.Rows.Length ?? 26) * CellHeight);
        protected override void OnRender(DrawingContext dc)
        {
            if (Data is not { } face) return;
            var owner = Owner; if (owner is not null) owner.drawSelection = owner.Selection; var cw = CellWidth; var ch = CellHeight; var cols = face.Rows[0].Length;
            dc.DrawRectangle(owner?.HighContrast == true ? Brushes.Black : Brushes.White, null, new Rect(RenderSize));
            void Text(string value, Point point, double size, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
            Text($"{face.Number}면", new(30, 4), 12, Brushes.DimGray);
            var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(225, 228, 232)), .5);
            for (var y = 0; y < face.Rows.Length; y++)
            {
                Text((y + 1).ToString(), new(2, 26 + y * ch), Math.Min(11, ch * .6), Brushes.Gray);
                if (face.Rows[y].FirstOrDefault(c => c is not null) is { } first &&
                    !face.Rows.Take(y).SelectMany(r => r).Any(c => c?.Block == first.Block))
                    Text("⋮", new(18, 26 + y * ch), Math.Min(14, ch), Brushes.SteelBlue);
                for (var x = 0; x < cols; x++)
                {
                    var box = new Rect(30 + x * cw, 26 + y * ch, cw, ch); var cell = face.Rows[y][x];
                    if (cell is not null)
                    {
                        if (owner?.IsSelected(cell) == true) dc.DrawRectangle(Brushes.LightSkyBlue, null, box);
                        else if (cell.Block == owner?.ActiveBlock) dc.DrawRectangle(owner.HighContrast ? Brushes.DarkBlue : new SolidColorBrush(Color.FromRgb(234, 244, 255)), null, box);
                        Text(cell.Value.ToString(), new(box.X, box.Y), cw * 1.45, cell.Block.HasManualBraille ? (owner?.HighContrast == true ? Brushes.Orange : Brushes.Firebrick) : owner?.HighContrast == true ? Brushes.White : Brushes.Black);
                        if (cell.Block == owner?.ActiveBlock && cell.Offset == owner.CaretOffset) dc.DrawRectangle(null, new Pen(Brushes.DodgerBlue, 1.5), box);
                    }
                    else if (face.Text is not null && x < face.Text[y].Length)
                        Text(face.Text[y][x].ToString(), new(box.X, box.Y), cw * 1.45, Brushes.Gray);
                    dc.DrawRectangle(null, gridPen, box);
                }
            }
            if (owner?.ShowOutlines == true) dc.DrawRectangle(null, new Pen(Brushes.Gray, 1), new(30, 26, cols * cw, face.Rows.Length * ch));
        }
        private GridCell? Hit(Point p)
        {
            if (Data is null || Owner?.CoordinatesCurrent != true) return null;
            var row = (int)Math.Floor((p.Y - 26) / CellHeight); var col = (int)Math.Floor((p.X - 30) / CellWidth);
            if (row < 0 || row >= Data.Rows.Length || col < 0 || col >= Data.Rows[row].Length) return null;
            return Data.Rows[row][col] ?? Data.Rows[row].Take(col).LastOrDefault(c => c is not null);
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Owner?.CoordinatesCurrent != true) return;
            var p = e.GetPosition(this);
            if (p.X is >= 16 and < 30 && Data is not null)
            {
                var row = (int)Math.Floor((p.Y - 26) / CellHeight);
                if (row >= 0 && row < Data.Rows.Length) dragBlock = Data.Rows[row].FirstOrDefault(c => c is not null)?.Block;
                dragStart = p; e.Handled = true; return;
            }
            if (Hit(p) is not { } cell || Owner is not { } owner) return;
            owner.Point(cell, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            owner.dragSelecting = true; owner.Focus(); e.Handled = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragBlock is not null && e.LeftButton == MouseButtonState.Pressed && (e.GetPosition(this) - dragStart).Length > 6)
            {
                var block = dragBlock; dragBlock = null;
                if (Owner?.IsReadOnly == false) DragDrop.DoDragDrop(this, new DataObject(typeof(EditorBlock), block), DragDropEffects.Move);
                return;
            }
            if (Owner is not { } owner || e.LeftButton != MouseButtonState.Pressed || !owner.dragSelecting) return;
            if (Hit(e.GetPosition(this)) is { } cell) owner.Point(cell, true);
        }
        protected override void OnDrop(DragEventArgs e)
        {
            e.Handled = true;
            if (Owner?.IsReadOnly != false || e.Data.GetData(typeof(EditorBlock)) is not EditorBlock moving) return;
            var p = e.GetPosition(this);
            if (Hit(new(Math.Max(30, p.X), p.Y)) is { } target)
                Owner.MoveBlock?.Invoke(moving, target.Block);
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        { dragBlock = null; if (Owner is { } owner) owner.dragSelecting = false; }
    }
}
