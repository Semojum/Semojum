using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Semojum.Desktop.Services;

public static class WindowBounds
{
    public static (int X, int Y, int Width, int Height) MaximizeArea(int monitorLeft, int monitorTop, int workLeft, int workTop, int workRight, int workBottom)
        => (workLeft - monitorLeft, workTop - monitorTop, Math.Max(1, workRight - workLeft), Math.Max(1, workBottom - workTop));

    public static bool TryGetWorkArea(Window window, out Rect area)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) { area = Rect.Empty; return false; }
        area = new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        return true;
    }

    // WindowChrome's custom frame must maximize to the current monitor's work area,
    // including taskbars on any edge and monitors with negative coordinates.
    public static void UseMonitorWorkArea(Window window)
    {
        void Attach(object? _, EventArgs __)
        {
            if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is { } source) source.AddHook(WorkAreaHook);
        }
        if (new WindowInteropHelper(window).Handle == IntPtr.Zero) window.SourceInitialized += Attach;
        else Attach(null, EventArgs.Empty);
    }

    private static IntPtr WorkAreaHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0024 || lParam == IntPtr.Zero) return IntPtr.Zero; // WM_GETMINMAXINFO
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref info)) return IntPtr.Zero;
        var area = MaximizeArea(info.Monitor.Left, info.Monitor.Top, info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition = new NativePoint { X = area.X, Y = area.Y };
        bounds.MaxSize = new NativePoint { X = area.Width, Y = area.Height };
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    public static void CenterOwned(Window owner, Window dialog)
    {
        var parent = new WindowInteropHelper(owner).Handle;
        var child = new WindowInteropHelper(dialog).Handle;
        if (parent == IntPtr.Zero || child == IntPtr.Zero || !GetWindowRect(parent, out var parentRect)
            || !GetWindowRect(child, out var childRect)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(parent, 2), ref info)) return;
        var width = childRect.Right - childRect.Left; var height = childRect.Bottom - childRect.Top;
        var left = parentRect.Left + (parentRect.Right - parentRect.Left - width) / 2;
        var top = parentRect.Top + (parentRect.Bottom - parentRect.Top - height) / 2;
        left = Math.Clamp(left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - width));
        top = Math.Clamp(top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - height));
        SetWindowPos(child, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    // Keep title bars and action rows reachable on small / scaled desktops.
    public static void Fit(Window window, bool constrainFutureResize = true)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var width = info.Work.Right - info.Work.Left; var height = info.Work.Bottom - info.Work.Top;
        var maxHeight = Math.Max(200, height / scale);
        var maxWidth = Math.Max(300, width / scale);
        // The editor may move to a larger monitor after startup. Constrain its initial
        // size to this work area without permanently capping later resizing or snap.
        if (constrainFutureResize) { window.MaxHeight = maxHeight; window.MaxWidth = maxWidth; }
        window.MinHeight = Math.Min(window.MinHeight, maxHeight);
        window.MinWidth = Math.Min(window.MinWidth, maxWidth);
        if (!double.IsNaN(window.Height)) window.Height = Math.Min(window.Height, maxHeight);
        if (!double.IsNaN(window.Width)) window.Width = Math.Min(window.Width, maxWidth);
        if (window.WindowState != WindowState.Normal) return;
        window.UpdateLayout();
        if (!GetWindowRect(handle, out var bounds)) return;
        var left = Math.Clamp(bounds.Left, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - (bounds.Right - bounds.Left)));
        var top = Math.Clamp(bounds.Top, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - (bounds.Bottom - bounds.Top)));
        SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
