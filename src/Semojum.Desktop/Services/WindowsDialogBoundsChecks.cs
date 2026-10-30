using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Semojum.Desktop.Services;

internal static class WindowsDialogBoundsChecks
{
    internal static void CheckFitted(Window window, double designWidth, double designHeight, string name, Action<bool, string> check)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) || !GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException($"Cannot inspect {name} monitor work area and window bounds.");
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var width = info.Work.Right - info.Work.Left; var height = info.Work.Bottom - info.Work.Top;
        var expectedWidth = Math.Min(designWidth, Math.Max(300, width / scale));
        var expectedHeight = Math.Min(designHeight, Math.Max(200, height / scale));
        check(Math.Abs(window.Width - expectedWidth) < 1 && Math.Abs(window.Height - expectedHeight) < 1 &&
              bounds.Left >= info.Work.Left - 2 && bounds.Top >= info.Work.Top - 2 &&
              bounds.Right <= info.Work.Right + 2 && bounds.Bottom <= info.Work.Bottom + 2,
              $"{name} fits the current monitor work area at the largest available design size " +
              $"(window={window.Width:F1}x{window.Height:F1}, work={width}x{height}px, scale={scale:F2})");
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}
