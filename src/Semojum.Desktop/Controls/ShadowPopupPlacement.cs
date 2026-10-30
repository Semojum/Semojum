using System.Windows;
using System.Windows.Controls.Primitives;

namespace Semojum.Desktop.Controls;

public static class ShadowPopupPlacement
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ShadowPopupPlacement), new PropertyMetadata(false, Changed));

    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is Popup popup)
            popup.CustomPopupPlacementCallback = (bool)args.NewValue ? (size, targetSize, offset) => Place(popup, size, targetSize, offset) : null;
    }

    private static CustomPopupPlacement[] Place(Popup popup, Size size, Size target, Point offset)
    {
        // Popup callback sizes use screen coordinates, which can differ from WPF's
        // reported DPI when the thread's DPI awareness changes after startup.
        var a = popup.PointToScreen(new Point());
        var b = popup.PointToScreen(new Point(100, 100));
        var marginX = Math.Abs(b.X - a.X) / 100 * 20;
        var marginY = Math.Abs(b.Y - a.Y) / 100 * 20;
        return [
        new(new Point(target.Width - marginX, -marginY), PopupPrimaryAxis.Horizontal),
        new(new Point(-size.Width - marginX, -marginY), PopupPrimaryAxis.Horizontal)
        ];
    }
}
