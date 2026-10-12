using System.Windows;
using Semojum.Desktop.Services;

namespace Semojum.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            window.ContentRendered += async (_, _) =>
            {
                try { await WindowsSmoke.RunAsync(window, e.Args[1]); window.PrepareSmokeShutdown(); Shutdown(0); }
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
}
