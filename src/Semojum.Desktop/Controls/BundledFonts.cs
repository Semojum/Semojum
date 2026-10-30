using System.IO;
using System.Windows.Media;

namespace Semojum.Desktop.Controls;

/// <summary>Absolute file references also work inside an anonymous WPF composite font's FamilyMaps.</summary>
public static class BundledFonts
{
    public static Uri DirectoryUri { get; } = new(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
    private static string Location => DirectoryUri.AbsoluteUri.Replace(",", "%2C", StringComparison.Ordinal);
    public static FontFamily Interface { get; } = new($"{Location}#Inter, {Location}#Noto Sans KR");
    // The desktop UI follows the Windows design; keep the bundled family as a fallback.
    // The separate Interface family continues to back the editable document and its settings.
    public static FontFamily UserInterface { get; } = new($"Malgun Gothic, {Location}#Inter, {Location}#Noto Sans KR");
    public static FontFamily Mono { get; } = new($"{Location}#Roboto Mono");
}
