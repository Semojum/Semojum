using System.IO;
using System.Runtime.InteropServices;

namespace Semojum.Desktop.Services;

// The optional CI helper stays entirely native after the CLR has shut down.
internal static class SmokeNativeDiagnostics
{
    [DllImport("semojum-native-diagnostics.dll", EntryPoint = "Install", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
    private static extern void InstallNative(string directory);
    internal static void Install(string directory)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "semojum-native-diagnostics.dll"))) InstallNative(directory);
    }
}
