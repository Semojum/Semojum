using System.Windows;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var focused = e.Args.Length > 0 && e.Args[0] == "--check-area";
        if (focused && (e.Args.Length != 3 || !WindowsFocusedChecks.IsKnownArea(e.Args[1])))
        {
            Console.Error.WriteLine("Usage: --check-area <board|institution|editor|appearance> <output-directory>");
            Shutdown(2);
            return;
        }
        if (!focused && !e.Args.Contains("--smoke-test") && await AppUpdates.ActivateAsync(Semojum.Desktop.MainWindow.CurrentVersion, e.Args)) { Shutdown(0); return; }
        var smoke = focused || e.Args.Contains("--smoke-test");
        // Smoke captures are compared 1:1 with Figma's 1x PNGs, so lay windows out at 96 DPI regardless of the display scale.
        if (smoke && Environment.GetEnvironmentVariable("SEMOJUM_SMOKE_DPI") != "native") SetThreadDpiAwarenessContext(new IntPtr(-1));
        var window = new MainWindow(smoke);
        if (!smoke && e.Args.Length == 1 && !e.Args[0].StartsWith("--", StringComparison.Ordinal))
            window.StartupFilePath = e.Args[0];
        MainWindow = window;
        if (focused || (e.Args.Length == 2 && e.Args[0] == "--smoke-test"))
        {
            var output = focused ? e.Args[2] : e.Args[1];
            window.ContentRendered += async (_, _) =>
            {
                try
                {
                    if (focused) await WindowsFocusedChecks.RunAsync(e.Args[1], output);
                    else await WindowsSmoke.RunAsync(window, output);
                    await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); Shutdown(0);
                }
                catch (Exception ex)
                {
                    System.IO.Directory.CreateDirectory(output);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(output, "failure.txt"), ex.ToString());
                    window.PrepareSmokeShutdown(); Shutdown(1);
                }
            };
        }
        window.Show();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
