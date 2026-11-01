using System.IO;
using System.Windows.Media;

namespace Semojum.Desktop.Controls;

/// <summary>Absolute file references also work inside an anonymous WPF composite font's FamilyMaps.</summary>
public static class BundledFonts
{
    public static Uri DirectoryUri { get; } = new(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts") + Path.DirectorySeparatorChar);
    private static string Location => DirectoryUri.AbsoluteUri.Replace(",", "%2C", StringComparison.Ordinal);
    // Figma 263:5636 sets UI and document text in Malgun Gothic, Mono figures in Segoe UI Symbol, and braille at 22/28 with 16-pixel cells.
    // Braille cells use the bundled SimBraille (740/1024 em, about 16 pixels at 22) instead of Figma's Segoe UI Symbol;
    // the bundled Inter and Noto Sans KR remain only as fallbacks for systems without those Windows fonts.
    public static FontFamily Interface { get; } = new($"Malgun Gothic, {Location}#Inter, {Location}#Noto Sans KR");
    public static FontFamily UserInterface { get; } = new($"Malgun Gothic, {Location}#Inter, {Location}#Noto Sans KR");
    public static FontFamily Mono { get; } = new($"Segoe UI Symbol, {Location}#Roboto Mono");
    public static FontFamily Braille { get; } = CreateBraille();
    /// <summary>SimBraille 1.13 also draws Braille ASCII over Latin letters and digits, so it maps only braille cells and blanks;
    /// the 8-dot cells it lacks and all other text, such as line numbers and layout markers, stay in Segoe UI Symbol.</summary>
    private static FontFamily CreateBraille()
    {
        var simBraille = new FontFamily($"{Location}#SimBraille");
        var family = new FontFamily { LineSpacing = simBraille.LineSpacing, Baseline = simBraille.Baseline };
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0020,00A0,2800-28FF", Target = $"{Location}#SimBraille, Segoe UI Symbol" });
        family.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Target = "Segoe UI Symbol" });
        return family;
    }
}
