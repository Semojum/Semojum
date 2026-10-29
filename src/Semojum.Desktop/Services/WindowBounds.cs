using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Semojum.Desktop.Services;

internal static class WindowBounds
{
    // Keep title bars and action rows reachable on small / scaled desktops.
    internal static void Fit(Window window)
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
        if (window is not MainWindow) { window.MaxHeight = maxHeight; window.MaxWidth = maxWidth; }
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
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
