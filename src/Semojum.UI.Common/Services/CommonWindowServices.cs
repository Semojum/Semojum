using System.Windows;

namespace Semojum.Desktop.Services;

/// <summary>Host adapters for shared WPF dialogs, installed once when the desktop assembly loads.</summary>
public static class CommonWindowServices
{
    private static Func<Window, bool>? usesBackdrop;
    private static Action<Window, string>? showHelp;

    public static void Configure(Func<Window, bool> backdrop, Action<Window, string> help)
    {
        usesBackdrop = backdrop ?? throw new ArgumentNullException(nameof(backdrop));
        showHelp = help ?? throw new ArgumentNullException(nameof(help));
    }

    public static bool UsesBackdrop(Window dialog) => usesBackdrop?.Invoke(dialog) ?? false;
    public static void ShowHelp(Window owner, string title) => (showHelp ?? throw new InvalidOperationException("Dialog help has not been configured."))(owner, title);
}
