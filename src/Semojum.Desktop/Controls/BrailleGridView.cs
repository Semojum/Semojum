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
    public bool ShowLineNumbers { get; set; } = true;
    public BrailleGridView()
    {
        Background = Brushes.White; BorderThickness = new(0); Padding = new(0); FontSize = 22;
        SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);
        SetValue(ScrollViewer.CanContentScrollProperty, true);
        SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        var scrollBarStyle = new Style(typeof(System.Windows.Controls.Primitives.ScrollBar));
        var vertical = new Trigger { Property = System.Windows.Controls.Primitives.ScrollBar.OrientationProperty, Value = Orientation.Vertical };
        vertical.Setters.Add(new Setter(WidthProperty, 8d)); scrollBarStyle.Triggers.Add(vertical);
        Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = scrollBarStyle;
        var factory = new FrameworkElementFactory(typeof(Face));
        factory.SetBinding(Face.DataProperty, new System.Windows.Data.Binding());
        ItemTemplate = new DataTemplate { VisualTree = factory };
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
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
        private const double TextLeft = 52;
        private double CellWidth => (Owner?.FontSize ?? 22) * 16 / 22;
        private double CellHeight => (Owner?.FontSize ?? 22) * 28 / 22;
        protected override Size MeasureOverride(Size availableSize) => new(Math.Max(120, TextLeft + (Data?.Rows[0].Length ?? 32) * CellWidth + 1), (Data?.Rows.Length ?? 26) * CellHeight + 1);
        protected override void OnRender(DrawingContext dc)
        {
            if (Data is not { } face) return;
            var owner = Owner; if (owner is not null) owner.drawSelection = owner.Selection; var cw = CellWidth; var ch = CellHeight; var cols = face.Rows[0].Length;
            dc.DrawRectangle(owner?.HighContrast == true ? Brushes.Black : Brushes.White, null, new Rect(RenderSize));
            void Text(string value, Point point, double size, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
            dc.DrawRectangle(owner?.HighContrast == true ? Brushes.Black : new SolidColorBrush(Color.FromRgb(250, 250, 250)), null, new Rect(0, 0, 40, RenderSize.Height));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(214, 214, 214)), 1), new(39.5, 0), new(39.5, RenderSize.Height));
            for (var y = 0; y < face.Rows.Length; y++)
            {
                if (owner?.ShowLineNumbers != false)
                    dc.DrawText(new FormattedText((y + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface((FontFamily)Application.Current.FindResource("InterfaceFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                        12, Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(12, 14 + y * ch));
                if (face.Rows[y].FirstOrDefault(c => c is not null) is { } first &&
                    !face.Rows.Take(y).SelectMany(r => r).Any(c => c?.Block == first.Block))
                    ToolTip = "드래그·Shift 선택 · 6점 입력 · Ctrl+Z · 줄 번호 옆에서 블록 이동";
                if (owner is not null && face.Rows[y].Any(c => c is not null && owner.IsCaret(c)))
                    dc.DrawRectangle(owner.HighContrast ? Brushes.DarkBlue : new SolidColorBrush(Color.FromRgb(255, 245, 163)), null, new Rect(40, y * ch, Math.Max(0, RenderSize.Width - 40), ch));
                for (var x = 0; x < cols; x++)
                {
                    var box = new Rect(TextLeft + x * cw, y * ch, cw, ch); var cell = face.Rows[y][x];
                    if (cell is not null)
                    {
                        if (owner?.IsLinkedCell(cell) == true) dc.DrawRectangle(owner.HighContrast ? Brushes.DarkBlue : new SolidColorBrush(Color.FromRgb(255, 245, 163)), null, box);
                        if (owner?.IsSelected(cell) == true) dc.DrawRectangle(Brushes.LightSkyBlue, null, box);
                        Text(cell.Value.ToString(), new(box.X, box.Y), owner?.FontSize ?? 22, cell.Block.HasManualBraille ? (owner?.HighContrast == true ? Brushes.Orange : Brushes.Firebrick) : owner?.HighContrast == true ? Brushes.White : Brushes.Black);
                        if (owner?.IsCaret(cell) == true && owner.IsKeyboardFocusWithin) dc.DrawLine(new Pen(Brushes.Black, 1), box.TopLeft, box.BottomLeft);
                    }
                    else if (face.Text is not null && x < face.Text[y].Length)
                        Text(face.Text[y][x].ToString(), new(box.X, box.Y), owner?.FontSize ?? 22, Brushes.Gray);
                }
            }
            if (owner?.ShowOutlines == true)
            {
                dc.DrawLine(new Pen(owner.HighContrast ? Brushes.White : new SolidColorBrush(Color.FromRgb(242, 93, 93)), 1), new(TextLeft + cols * cw, 0), new(TextLeft + cols * cw, face.Rows.Length * ch));
                dc.DrawLine(new Pen(Brushes.LightGray, 1), new(40, face.Rows.Length * ch), new(RenderSize.Width, face.Rows.Length * ch));
            }
        }
        private GridCell? Hit(Point p)
        {
            if (Data is null || Owner?.CoordinatesCurrent != true) return null;
            var row = (int)Math.Floor(p.Y / CellHeight); var col = (int)Math.Floor((p.X - TextLeft) / CellWidth);
            if (row < 0 || row >= Data.Rows.Length || col < 0 || col >= Data.Rows[row].Length) return null;
            return Data.Rows[row][col] ?? Data.Rows[row].Take(col).LastOrDefault(c => c is not null);
        }
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Owner?.CoordinatesCurrent != true) return;
            var p = e.GetPosition(this);
            if (p.X is >= 26 and < 40 && Data is not null)
            {
                var row = (int)Math.Floor(p.Y / CellHeight);
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
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            if (Hit(e.GetPosition(this)) is not { } cell || Owner is not { } owner) return;
            if (!owner.IsSelected(cell)) owner.Point(cell, false);
            owner.Focus();
        }
        protected override void OnDrop(DragEventArgs e)
        {
            e.Handled = true;
            if (Owner?.IsReadOnly != false || e.Data.GetData(typeof(EditorBlock)) is not EditorBlock moving) return;
            var p = e.GetPosition(this);
            if (Hit(new(Math.Max(TextLeft, p.X), p.Y)) is { } target)
                Owner.MoveBlock?.Invoke(moving, target.Block);
        }
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        { dragBlock = null; if (Owner is { } owner) owner.dragSelecting = false; }
    }
}
