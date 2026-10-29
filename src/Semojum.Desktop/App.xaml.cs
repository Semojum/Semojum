using System.Windows;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!e.Args.Contains("--smoke-test") && await AppUpdates.ActivateAsync(Semojum.Desktop.MainWindow.CurrentVersion, e.Args)) { Shutdown(0); return; }
        var smoke = e.Args.Contains("--smoke-test");
        // Smoke captures are compared 1:1 with Figma's 1x PNGs, so lay windows out at 96 DPI regardless of the display scale.
        if (smoke && Environment.GetEnvironmentVariable("SEMOJUM_SMOKE_DPI") != "native") SetThreadDpiAwarenessContext(new IntPtr(-1));
        var window = new MainWindow(smoke);
        if (!smoke && e.Args.Length == 1 && !e.Args[0].StartsWith("--", StringComparison.Ordinal))
            window.StartupFilePath = e.Args[0];
        MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            window.ContentRendered += async (_, _) =>
            {
                try { await WindowsSmoke.RunAsync(window, e.Args[1]); await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); Shutdown(0); }
                catch (Exception ex)
                {
                    System.IO.Directory.CreateDirectory(e.Args[1]);
                    System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1], "failure.txt"), ex.ToString());
                    window.PrepareSmokeShutdown(); Shutdown(1);
                }
            };
        }
        window.Show();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
