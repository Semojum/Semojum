using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Semojum.Desktop.Services;

// Test-mode only. Records native stack modules; never handles or suppresses the fault.
internal static class SmokeNativeDiagnostics
{
    private delegate int Handler(IntPtr exceptionPointers);
    private static Handler? handler;
    private static int recording;
    [DllImport("kernel32.dll")] private static extern IntPtr AddVectoredExceptionHandler(uint first, Handler callback);
    [DllImport("kernel32.dll")] private static extern ushort RtlCaptureStackBackTrace(uint skip, uint count, [Out] IntPtr[] frames, IntPtr hash);
    internal static void Install(string directory)
    {
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>().Select(m => (m.ModuleName, Start: m.BaseAddress.ToInt64(), End: m.BaseAddress.ToInt64() + m.ModuleMemorySize)).ToArray();
        handler = pointers =>
        {
            if (unchecked((uint)Marshal.ReadInt32(Marshal.ReadIntPtr(pointers))) != 0xc000000d || Interlocked.Exchange(ref recording, 1) != 0) return 0;
            try
            {
                var frames = new IntPtr[80]; var count = RtlCaptureStackBackTrace(0, 80, frames, IntPtr.Zero);
                var lines = frames.Take(count).Select(f => { var address = f.ToInt64(); var module = modules.FirstOrDefault(m => address >= m.Start && address < m.End); return $"{module.ModuleName ?? "unknown"}+0x{address - module.Start:X}"; });
                File.WriteAllLines(Path.Combine(directory, "native-stack.txt"), lines);
            }
            catch { }
            return 0; // EXCEPTION_CONTINUE_SEARCH
        };
        _ = AddVectoredExceptionHandler(1, handler);
    }
}
