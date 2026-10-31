using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Semojum.Desktop.Services;

public static class RawBraillePrinter
{
    // RAW sends the engine's BRF bytes unchanged. Paper/duplex are device settings, not text escape codes.
    public static async Task PrintAsync(Window owner, string brf, string title)
    {
        if (brf.Any(c => c > 127)) throw new ArgumentException("BRF ASCII 파일만 점자 프린터로 보낼 수 있습니다.");
        if (MessageBox.Show(owner, "BRF를 직접 지원하는 점자 프린터를 선택해 주세요. 프린터에서 용지 크기·양면 설정을 먼저 확인해야 합니다. 일반 프린터에는 점자로 인쇄되지 않습니다.", "점자 인쇄", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        var name = dialog.PrintQueue.FullName;
        var copies = Math.Clamp(dialog.PrintTicket.CopyCount ?? 1, 1, 100);
        var bytes = Encoding.ASCII.GetBytes(brf);
        await Task.Run(() => Send(name, title, bytes, copies));
    }
    private static void Send(string name, string title, byte[] bytes, int copies)
    {
        if (!OpenPrinter(name, out var printer, IntPtr.Zero)) throw new Win32Exception();
        var started = false;
        try
        {
            var info = new DocInfo { Name = title, DataType = "RAW" };
            if (StartDocPrinter(printer, 1, info) == 0) throw new Win32Exception();
            started = true;
            for (var copy = 0; copy < copies; copy++)
            {
                if (!StartPagePrinter(printer)) throw new Win32Exception();
                if (!WritePrinter(printer, bytes, bytes.Length, out var written) || written != bytes.Length) throw new Win32Exception("인쇄 데이터 전체를 전달하지 못했습니다.");
                if (!EndPagePrinter(printer)) throw new Win32Exception();
            }
            if (!EndDocPrinter(printer)) throw new Win32Exception();
            started = false;
        }
        finally { if (started) AbortPrinter(printer); ClosePrinter(printer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class DocInfo { [MarshalAs(UnmanagedType.LPWStr)] public string Name = ""; [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile; [MarshalAs(UnmanagedType.LPWStr)] public string DataType = "RAW"; }
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint StartDocPrinter(IntPtr handle, int level, [In] DocInfo info);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool StartPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool WritePrinter(IntPtr handle, byte[] data, int length, out int written);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndPagePrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool EndDocPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool AbortPrinter(IntPtr handle);
    [DllImport("winspool.drv", SetLastError = true)] private static extern bool ClosePrinter(IntPtr handle);
}
