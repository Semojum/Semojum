using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Semojum.Desktop.Services;

// Test-only assistance for a human running the interactive smoke suite. CI and
// ordinary smoke runs never wait or modify the test windows.
internal static class WindowsInteractiveFocusWait
{
    internal static async Task<bool> WaitAsync(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("SEMOJUM_WAIT_FOR_TEST_FOCUS") != "1") return true;

        var title = window.Title;
        var showInTaskbar = window.ShowInTaskbar;
        try
        {
            window.Title = $"누리닷 검사: {name} — 이 창을 선택하세요";
            window.ShowInTaskbar = true;
            Console.WriteLine($"WAIT FOR FOCUS: {name}. Select the test window titled '{window.Title}' within 45 seconds; do not interact with its contents.");
            var handle = new WindowInteropHelper(window).Handle;
            var stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (handle != IntPtr.Zero && GetForegroundWindow() == handle && window.IsActive)
                {
                    Console.WriteLine($"FOCUS READY: {name}");
                    return true;
                }
                await Task.Delay(100);
            }
            Console.WriteLine($"FOCUS TIMEOUT: {name}; the existing test will skip if focus is still unavailable.");
            return false;
        }
        finally
        {
            window.Title = title;
            window.ShowInTaskbar = showInTaskbar;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
