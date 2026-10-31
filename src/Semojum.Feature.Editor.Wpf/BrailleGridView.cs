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
    public Brush CurrentLineBrush { get; set; } = new SolidColorBrush(Color.FromRgb(242, 246, 251));
    public Brush SelectionHighlightBrush { get; set; } = new SolidColorBrush(Color.FromRgb(204, 228, 247));
    public Brush SelectionTextBrush { get; set; } = new SolidColorBrush(Color.FromRgb(31, 31, 31));
    public bool ShowLineNumbers { get; set; } = true;
    public Brush LineNumberForeground { get; set; } = new SolidColorBrush(Color.FromRgb(138, 138, 138));
    public Brush LineNumberBackground { get; set; } = new SolidColorBrush(Color.FromRgb(250, 250, 250));
    public bool CustomLineNumberForeground { get; set; }
    public bool CustomLineNumberBackground { get; set; }
    internal const double NumberWidth = 32;
    internal const double MarkerStart = 33;
    internal const double MarkerSize = 16;
    private const double MinimumMarkerArea = 17;
    private const double TextInset = 12;
    private bool showMemos = true, showReviews = true;
    public bool ShowMemos { get => showMemos; set { if (showMemos != value) { showMemos = value; InvalidateMarkerCache(); InvalidateFaces(); } } }
    public bool ShowReviews { get => showReviews; set { if (showReviews != value) { showReviews = value; InvalidateMarkerCache(); InvalidateFaces(); } } }
    public Action<EditorBlock, int>? MemoRequested { get; set; }
    public Action<EditorBlock>? ReviewRequested { get; set; }
    public Action? ManualReviewRequested { get; set; }
    internal sealed record RowMarker(EditorBlock Block, int Line, bool Memo, bool Review, string Description);
    private Dictionary<BrailleGridFace, Dictionary<int, RowMarker>>? markerCache;
    private sealed record ManualFaceReviews(ReviewKind[][] Kinds, Dictionary<GridCell, ReviewMark> LastByCell);
    private Dictionary<BrailleGridFace, ManualFaceReviews>? manualReviewCache;
    internal int MarkerCacheBuildCount { get; private set; }
    internal int ManualReviewCacheBuildCount { get; private set; }
    private void InvalidateMarkerCache() { markerCache = null; manualReviewCache = null; }
    private void MarkerMemosChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { InvalidateMarkerCache(); InvalidateFaces(); }
    private void MarkerBlockChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorBlock.Braille) or nameof(EditorBlock.Translation)) { InvalidateMarkerCache(); InvalidateFaces(); }
    }
    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { InvalidateMarkerCache(); base.OnItemsChanged(e); }
    internal bool ActivateMarker(BrailleGridFace face, int row, double x)
    {
        if (!CoordinatesCurrent || x < MarkerStart || row < 0 || row >= face.Rows.Length) return false;
        MarkersFor(face).TryGetValue(row, out var marker);
        var baseSlots = (marker?.Memo == true ? 1 : 0) + (marker?.Review == true ? 1 : 0);
        var manual = ManualReviewKinds(face, row);
        var slot = (int)((x - MarkerStart) / MarkerSize);
        if (slot < 0 || slot >= baseSlots + manual.Length) return false;
        if (marker?.Memo == true && slot == 0) MemoRequested?.Invoke(marker.Block, marker.Line);
        else if (marker?.Review == true && slot == (marker.Memo ? 1 : 0)) ReviewRequested?.Invoke(marker.Block);
        else ManualReviewRequested?.Invoke();
        return true;
    }
    internal ReviewKind[] ManualReviewKinds(BrailleGridFace face, int row)
    {
        if (!ShowReviews || !CoordinatesCurrent || row < 0 || row >= face.Rows.Length) return [];
        return ManualReviewsFor(face).Kinds[row];
    }
    internal ReviewMark? ManualReviewForCell(BrailleGridFace face, GridCell cell)
    {
        if (!ShowReviews || !CoordinatesCurrent) return null;
        return ManualReviewsFor(face).LastByCell.TryGetValue(cell, out var mark) ? mark : null;
    }
    private ManualFaceReviews ManualReviewsFor(BrailleGridFace face)
    {
        manualReviewCache ??= new();
        if (manualReviewCache.TryGetValue(face, out var existing)) return existing;
        ManualReviewCacheBuildCount++;
        // Index each block's marks once per visible face. Sorting interval boundaries
        // avoids rescanning all marks for every cell in a heavily annotated document.
        var indexed = new Dictionary<EditorBlock, Dictionary<int, (ReviewMark Last, ReviewKind[] Kinds)>>();
        foreach (var group in face.Rows.SelectMany(row => row.OfType<GridCell>()).GroupBy(cell => cell.Block))
        {
            var marks = group.Key.ReviewMarks.Where(m => !m.IsStale && m.Panel == ReviewPanel.Braille).ToArray();
            if (marks.Length == 0) continue;
            var starts = Enumerable.Range(0, marks.Length).OrderBy(i => marks[i].Start).ToArray();
            var ends = Enumerable.Range(0, marks.Length).OrderBy(i => marks[i].Start + marks[i].Length).ToArray();
            var active = new SortedSet<int>();
            var byKind = new[] { new SortedSet<int>(), new SortedSet<int>(), new SortedSet<int>() };
            var positions = new Dictionary<int, (ReviewMark Last, ReviewKind[] Kinds)>();
            var startIndex = 0; var endIndex = 0;
            foreach (var offset in group.Select(cell => cell.Offset).Distinct().OrderBy(offset => offset))
            {
                while (startIndex < starts.Length && marks[starts[startIndex]].Start <= offset)
                {
                    var index = starts[startIndex++]; active.Add(index); byKind[(int)marks[index].Kind].Add(index);
                }
                while (endIndex < ends.Length && marks[ends[endIndex]].Start + marks[ends[endIndex]].Length <= offset)
                {
                    var index = ends[endIndex++]; active.Remove(index); byKind[(int)marks[index].Kind].Remove(index);
                }
                if (active.Count == 0) continue;
                var kinds = Enumerable.Range(0, byKind.Length).Where(i => byKind[i].Count > 0)
                    .OrderBy(i => byKind[i].Min).Select(i => (ReviewKind)i).ToArray();
                positions[offset] = (marks[active.Max], kinds);
            }
            indexed[group.Key] = positions;
        }
        var cells = new Dictionary<GridCell, ReviewMark>();
        var rows = new ReviewKind[face.Rows.Length][];
        for (var row = 0; row < face.Rows.Length; row++)
        {
            var kinds = new List<ReviewKind>();
            foreach (var cell in face.Rows[row].OfType<GridCell>())
            {
                if (!indexed.TryGetValue(cell.Block, out var positions) || !positions.TryGetValue(cell.Offset, out var found)) continue;
                cells[cell] = found.Last; // Preserve LastOrDefault priority for overlapping marks.
                if (!cell.IsInsertion)
                    foreach (var kind in found.Kinds) if (!kinds.Contains(kind)) kinds.Add(kind);
            }
            rows[row] = kinds.ToArray();
        }
        return manualReviewCache[face] = new(rows, cells);
    }
    private string MarkerDescription(BrailleGridFace face, int row)
    {
        MarkersFor(face).TryGetValue(row, out var marker);
        return string.Join("\n\n", new[] { marker?.Description ?? "", string.Join("\n", ManualReviewKinds(face, row).Select(ReviewVisuals.Label)) }.Where(s => s.Length > 0));
    }
    internal IReadOnlyDictionary<int, RowMarker> MarkersFor(BrailleGridFace face)
    {
        if (!CoordinatesCurrent) return new Dictionary<int, RowMarker>();
        if (markerCache is null)
        {
            MarkerCacheBuildCount++;
            markerCache = new();
            var offsets = new Dictionary<EditorBlock, Dictionary<int, int>>();
            var seen = new HashSet<(EditorBlock, int)>();
            foreach (var page in Items.OfType<BrailleGridFace>())
            {
                var markers = new Dictionary<int, RowMarker>(); markerCache[page] = markers;
                for (var row = 0; row < page.Rows.Length; row++)
                {
                    var candidates = new List<RowMarker>();
                    foreach (var cell in page.Rows[row].OfType<GridCell>().Where(c => IsRealLineCell(c, page, row)))
                    {
                        if (!offsets.TryGetValue(cell.Block, out var lines))
                            offsets[cell.Block] = lines = DocumentNavigation.LineOffsets(cell.Block.Braille).Select((offset, line) => (offset, line)).ToDictionary(x => x.offset, x => x.line);
                        if (!lines.TryGetValue(cell.Offset, out var line) || !seen.Add((cell.Block, line))) continue;
                        var memos = cell.Block.Memos.Where(m => m.Panel == "BRAILLE" && m.LineIndex == line && !m.Resolved).ToArray();
                        var memo = ShowMemos && memos.Length > 0;
                        var review = ShowReviews && line == 0 && cell.Block.Translation?.NeedsReview == true;
                        if (!memo && !review) continue;
                        var result = cell.Block.Translation;
                        var warning = review && result is not null ? MemoMargin.DescribeReview(result) : "";
                        var description = string.Join("\n\n", new[] { memo ? "메모\n" + string.Join("\n", memos.Select(m => $"{m.Author ?? "작성자 미상"}: {m.Body}")) : "", warning }.Where(s => s.Length > 0));
                        candidates.Add(new(cell.Block, line, memo, review, description));
                    }
                    // A shared visual row cannot faithfully identify two different logical lines.
                    if (candidates.Count == 1) markers[row] = candidates[0];
                }
            }
        }
        return markerCache.TryGetValue(face, out var found) ? found : new Dictionary<int, RowMarker>();
    }
    private static bool IsRealLineCell(GridCell cell, BrailleGridFace page, int row)
    {
        var text = cell.Block.Braille;
        if (cell.Offset < 0 || cell.Offset > text.Length) return false;
        if (!cell.IsInsertion) return cell.Offset < text.Length;
        if (cell.Prefix.Length != 0 || page.Kinds is { } kinds && row < kinds.Length && kinds[row] == "padding") return false;
        return cell.Offset < text.Length ? text[cell.Offset] is '\r' or '\n'
            : text.Length > 0 && text[^1] is '\r' or '\n';
    }
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
    private void AutomationPropertiesName()
    {
        System.Windows.Automation.AutomationProperties.SetName(this, "점자 모눈 편집 좌표");
        System.Windows.Automation.AutomationProperties.SetHelpText(this, "점자 줄의 메모는 메모 메뉴에서도 확인할 수 있습니다. 점역 검토가 필요한 항목은 쪽별 변환 상태에서 확인할 수 있습니다.");
    }
    public void InvalidateFaces()
    {
        for (var i = 0; i < Items.Count; i++)
            if (ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item) { item.InvalidateMeasure(); item.InvalidateVisual(); Redraw(item); }
        static void Redraw(DependencyObject root)
        { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is Face f) { f.ToolTip = null; f.InvalidateMeasure(); f.InvalidateVisual(); } else Redraw(child); } }
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
        private double GutterEdge => NumberWidth + Math.Max(MinimumMarkerArea, (Owner is { } owner && Data is { } face ? Enumerable.Range(0, face.Rows.Length).Select(row =>
            (owner.MarkersFor(face).TryGetValue(row, out var m) ? (m.Memo ? 1 : 0) + (m.Review ? 1 : 0) : 0) + owner.ManualReviewKinds(face, row).Length).DefaultIfEmpty(0).Max() : 0) * MarkerSize + 1);
        private double TextLeft => GutterEdge + TextInset;
        private double CellWidth => (Owner?.FontSize ?? 22) * 16 / 22;
        private double CellHeight => (Owner?.FontSize ?? 22) * 28 / 22;
        protected override Size MeasureOverride(Size availableSize) => new(Math.Max(120, TextLeft + (Data?.Rows[0].Length ?? 32) * CellWidth + 1), (Data?.Rows.Length ?? 26) * CellHeight + 1);
        protected override void OnRender(DrawingContext dc)
        {
            if (Data is not { } face) return;
            var owner = Owner; if (owner is not null) owner.drawSelection = owner.Selection; var cw = CellWidth; var ch = CellHeight; var cols = face.Rows[0].Length; var textLeft = TextLeft;
            dc.DrawRectangle(owner?.HighContrast == true ? Brushes.Black : Brushes.White, null, new Rect(RenderSize));
            void Text(string value, Point point, double size, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Symbol"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
            var gutterEdge = GutterEdge;
            dc.DrawRectangle(owner?.HighContrast == true ? Brushes.Black : owner?.LineNumberBackground ?? Brushes.WhiteSmoke, null, new Rect(0, 0, gutterEdge + 1, RenderSize.Height));
            dc.DrawLine(new Pen(owner?.HighContrast == true ? Brushes.White : new SolidColorBrush(Color.FromRgb(234, 234, 234)), 1), new(gutterEdge + .5, 0), new(gutterEdge + .5, RenderSize.Height));
            for (var y = 0; y < face.Rows.Length; y++)
            {
                var kinds = owner?.ManualReviewKinds(face, y) ?? [];
                var kind = kinds.Length > 0 ? kinds[^1] : (ReviewKind?)null;
                var contrast = owner?.HighContrast == true;
                var numberBrush = contrast ? Brushes.White : owner?.LineNumberForeground ?? Brushes.Gray;
                if (owner?.ShowLineNumbers != false)
                {
                    if (kind is { } marked)
                    {
                        if (!contrast && owner?.CustomLineNumberBackground != true) dc.DrawRectangle(ReviewVisuals.Background(marked), null, new Rect(0, y * ch, NumberWidth, ch));
                        dc.DrawRectangle(contrast ? Brushes.White : ReviewVisuals.Ink(marked), null, new Rect(0, y * ch, 3, ch));
                    }
                    var number = new FormattedText((y + 1).ToString(), CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface((FontFamily)Application.Current.FindResource("InterfaceFont"), FontStyles.Normal, kind is null ? FontWeights.Normal : FontWeights.Bold, FontStretches.Normal),
                        kind is null ? 12 : 14, kind is { } markedKind && !contrast && owner?.CustomLineNumberForeground != true ? ReviewVisuals.Ink(markedKind) : numberBrush,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);
                    dc.DrawText(number, new Point((NumberWidth - number.Width) / 2, y * ch + (ch - number.Height) / 2));
                }
                if (owner is not null && face.Rows[y].Any(c => c is not null && owner.IsCaret(c)))
                {
                    dc.DrawRectangle(owner.HighContrast ? Brushes.DarkBlue : owner.CurrentLineBrush, null, new Rect(gutterEdge, y * ch, Math.Max(0, RenderSize.Width - gutterEdge), ch));
                    dc.DrawRectangle(owner.HighContrast ? Brushes.White : new SolidColorBrush(Color.FromRgb(0, 95, 184)), null, new Rect(gutterEdge, y * ch, 2, ch));
                }
                if (owner is not null && owner.MarkersFor(face).TryGetValue(y, out var marker))
                {
                    var top = y * ch + (ch - 16) / 2;
                    void Icon(Drawing drawing, double x) { dc.PushTransform(new TranslateTransform(x, top)); dc.DrawDrawing(drawing); dc.Pop(); }
                    if (marker.Memo) Icon(MemoMargin.MemoDrawing, MarkerStart);
                    if (marker.Review) Icon(MemoMargin.WarningDrawing, MarkerStart + (marker.Memo ? MarkerSize : 0));
                }
                if (owner is not null)
                {
                    owner.MarkersFor(face).TryGetValue(y, out var rowMarker);
                    var xIcon = MarkerStart + ((rowMarker?.Memo == true ? 1 : 0) + (rowMarker?.Review == true ? 1 : 0)) * MarkerSize;
                    foreach (var rowKind in kinds)
                    {
                        var top = y * ch + (ch - 16) / 2;
                        dc.PushTransform(new TranslateTransform(xIcon, top)); dc.DrawDrawing(ReviewVisuals.Icon(rowKind)); dc.Pop();
                        xIcon += 16;
                    }
                    if (kinds.Length > 0) dc.DrawRectangle(owner.HighContrast ? Brushes.White : ReviewVisuals.Ink(kinds[^1]), null, new Rect(gutterEdge, y * ch, 2, ch));
                }
                for (var x = 0; x < cols; x++)
                {
                    var box = new Rect(textLeft + x * cw, y * ch, cw, ch); var cell = face.Rows[y][x];
                    if (cell is not null)
                    {
                        var mark = owner?.ManualReviewForCell(face, cell);
                        if (mark is not null && owner?.HighContrast != true) dc.DrawRectangle(ReviewVisuals.Background(mark.Kind), null, box);
                        var highlighted = owner is not null && (owner.IsLinkedCell(cell) || owner.IsSelected(cell));
                        if (highlighted) dc.DrawRectangle(owner!.HighContrast ? Brushes.DarkBlue : owner.SelectionHighlightBrush, null, box);
                        var textBrush = highlighted ? owner!.HighContrast ? Brushes.White : owner.SelectionTextBrush
                            : cell.Block.HasManualBraille ? (owner?.HighContrast == true ? Brushes.Orange : Brushes.Firebrick)
                            : owner?.HighContrast == true ? Brushes.White : Brushes.Black;
                        Text(cell.Value.ToString(), new(box.X, box.Y), owner?.FontSize ?? 22, textBrush);
                        if (mark is not null && mark.Kind != ReviewKind.HumanConfirmed)
                        {
                            var lineY = mark.Kind == ReviewKind.SuggestCorrection ? box.Bottom - 2 : box.Top + box.Height / 2;
                            dc.DrawLine(new Pen(owner?.HighContrast == true ? Brushes.White : ReviewVisuals.Ink(mark.Kind), 1), new Point(box.Left, lineY), new Point(box.Right, lineY));
                        }
                        if (owner?.IsCaret(cell) == true && owner.IsKeyboardFocusWithin)
                        {
                            // A two-tone caret remains visible against both user-selected dark and light colors.
                            dc.DrawRectangle(Brushes.Black, null, new Rect(box.Left - 1, box.Top, 3, box.Height));
                            dc.DrawRectangle(Brushes.White, null, new Rect(box.Left, box.Top, 1, box.Height));
                        }
                    }
                    else if (face.Text is not null && x < face.Text[y].Length)
                        Text(face.Text[y][x].ToString(), new(box.X, box.Y), owner?.FontSize ?? 22, Brushes.Gray);
                }
            }
            if (owner?.ShowOutlines == true)
            {
                dc.DrawLine(new Pen(owner.HighContrast ? Brushes.White : new SolidColorBrush(Color.FromRgb(242, 93, 93)), 1), new(TextLeft + cols * cw, 0), new(TextLeft + cols * cw, face.Rows.Length * ch));
                dc.DrawLine(new Pen(Brushes.LightGray, 1), new(gutterEdge, face.Rows.Length * ch), new(RenderSize.Width, face.Rows.Length * ch));
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
            if (Owner is { } markerOwner && Data is { } markerFace && markerOwner.ActivateMarker(markerFace, (int)Math.Floor(p.Y / CellHeight), p.X))
            {
                e.Handled = true; return;
            }
            if (p.X >= 26 && p.X < MarkerStart && Data is not null)
            {
                var row = (int)Math.Floor(p.Y / CellHeight);
                if (row >= 0 && row < Data.Rows.Length) dragBlock = Data.Rows[row].FirstOrDefault(c => c is not null)?.Block;
                dragStart = p; e.Handled = true; return;
            }
            if (Hit(p) is not { } cell || Owner is not { } owner) return;
            owner.Point(cell, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            owner.dragSelecting = true; owner.Focus(); e.Handled = true;
        }
        private void UpdateMarkerToolTip(Point point)
        {
            var description = Owner is { } markerOwner && Data is { } markerFace && point.X >= MarkerStart && point.X < GutterEdge
                ? markerOwner.MarkerDescription(markerFace, (int)Math.Floor(point.Y / CellHeight)) : "";
            ToolTip = description.Length > 0 ? description : point.X >= 26 && point.X < MarkerStart ? "드래그·Shift 선택 · 6점 입력 · Ctrl+Z · 줄 번호 옆에서 블록 이동" : null;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            UpdateMarkerToolTip(e.GetPosition(this));
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
