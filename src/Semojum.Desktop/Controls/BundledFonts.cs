using System.IO;
using System.Windows.Media;

namespace Semojum.Desktop.Controls;

/// <summary>Absolute file references also work inside an anonymous WPF composite font's FamilyMaps.</summary>
public static class BundledFonts
{
    public static Uri DirectoryUri { get; } = new(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
    private static string Location => DirectoryUri.AbsoluteUri.Replace(",", "%2C", StringComparison.Ordinal);
    // Figma 263:5636 sets UI and document text in Malgun Gothic, and braille or Mono figures in Segoe UI Symbol.
    // The bundled families remain only as fallbacks for systems without those Windows fonts.
    public static FontFamily Interface { get; } = new($"Malgun Gothic, {Location}#Inter, {Location}#Noto Sans KR");
    public static FontFamily UserInterface { get; } = new($"Malgun Gothic, {Location}#Inter, {Location}#Noto Sans KR");
    public static FontFamily Mono { get; } = new($"Segoe UI Symbol, {Location}#Roboto Mono");
}
