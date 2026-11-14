using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private double originalZoom = 1;
    private Point? originalDragStart;
    private Point originalDragOffsets;
    private bool originalDragged;

    private void OriginalScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange != 0) UpdateOriginalWidth();
    }
    private void UpdateOriginalWidth()
    {
        if (OriginalImageView is null || OriginalText is null) return;
        var width = OriginalScroll.ViewportWidth;
        if (!double.IsFinite(width) || width <= 0) return;
        var imageWidth = width * originalZoom;
        if (Math.Abs(OriginalImageView.Width - imageWidth) > .1 || double.IsNaN(OriginalImageView.Width))
            OriginalImageView.Width = imageWidth;
        OriginalText.MaxWidth = Math.Max(1, width - 32);
    }
    internal void SetOriginalZoom(double zoom)
    {
        var old = originalZoom;
        originalZoom = Math.Clamp(zoom, .25, 4);
        var x = (OriginalScroll.HorizontalOffset + OriginalScroll.ViewportWidth / 2) / old;
        var y = (OriginalScroll.VerticalOffset + OriginalScroll.ViewportHeight / 2) / old;
        UpdateOriginalWidth(); OriginalScroll.UpdateLayout();
        OriginalScroll.ScrollToHorizontalOffset(x * originalZoom - OriginalScroll.ViewportWidth / 2);
        OriginalScroll.ScrollToVerticalOffset(y * originalZoom - OriginalScroll.ViewportHeight / 2);
        OriginalZoomFit.ToolTip = $"{originalZoom:P0} · 클릭하면 너비에 맞춤 · 이미지를 드래그해서 이동";
    }
    private void OriginalZoomOut_Click(object sender, RoutedEventArgs e) => SetOriginalZoom(originalZoom / 1.25);
    private void OriginalZoomIn_Click(object sender, RoutedEventArgs e) => SetOriginalZoom(originalZoom * 1.25);
    private void OriginalZoomFit_Click(object sender, RoutedEventArgs e)
    {
        SetOriginalZoom(1);
        OriginalScroll.ScrollToHorizontalOffset(0); OriginalScroll.ScrollToVerticalOffset(0);
    }
    internal void BeginOriginalDrag(Point point)
    {
        originalDragStart = point;
        originalDragOffsets = new(OriginalScroll.HorizontalOffset, OriginalScroll.VerticalOffset);
        originalDragged = false;
    }
    internal void MoveOriginalDrag(Point point)
    {
        if (originalDragStart is not { } start) return;
        var delta = point - start;
        originalDragged |= Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance
            || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance;
        if (!originalDragged) return;
        OriginalScroll.ScrollToHorizontalOffset(originalDragOffsets.X - delta.X);
        OriginalScroll.ScrollToVerticalOffset(originalDragOffsets.Y - delta.Y);
    }
    private void OriginalImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        BeginOriginalDrag(e.GetPosition(OriginalScroll)); OriginalImageGrid.CaptureMouse(); e.Handled = true;
    }
    private void OriginalImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) MoveOriginalDrag(e.GetPosition(OriginalScroll));
    }
    private void OriginalImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var select = originalDragStart.HasValue && !originalDragged;
        originalDragStart = null; OriginalImageGrid.ReleaseMouseCapture();
        if (select) SelectOriginalImageRegion(sender, e);
        e.Handled = true;
    }
    private void OriginalImage_LostCapture(object sender, MouseEventArgs e) => originalDragStart = null;
}
