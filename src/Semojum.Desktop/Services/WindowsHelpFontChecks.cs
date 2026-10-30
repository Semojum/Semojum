using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;
internal static class WindowsHelpFontChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var help = new HelpWindow("판면과 꼬리말") { Width = 600, Height = 400 }; help.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); help.UpdateLayout();
            var reader = Descendants(help).OfType<FlowDocumentScrollViewer>().Single();
            var body = new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text;
            check(reader.Document.FontFamily.Source == BundledFonts.UserInterface.Source, "Help article uses the UI font, not the editable document font");
            check(body.Contains("범위는 서버에서 복원되지 않으므로 .smj를 보관하세요.", StringComparison.Ordinal) &&
                body.Contains("모눈과 점자 면 미리보기는 같은 조판 결과를 사용합니다.", StringComparison.Ordinal), "Long help article retains its actual full text");
            var glyphs = Descendants(reader).OfType<Visual>().SelectMany(v => Drawings(VisualTreeHelper.GetDrawing(v))).OfType<GlyphRunDrawing>()
                .Select(g => g.GlyphRun.GlyphTypeface.FontUri.LocalPath).ToArray();
            var installed = Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, "Malgun Gothic", StringComparison.OrdinalIgnoreCase));
            check(installed ? glyphs.Any(f => Path.GetFileName(f).StartsWith("malgun", StringComparison.OrdinalIgnoreCase)) : glyphs.Any(f => f.Contains("NotoSansKR", StringComparison.OrdinalIgnoreCase)),
                "Rendered help article uses Malgun Gothic or the bundled Korean fallback");
            var scroll = Descendants(reader).OfType<ScrollViewer>().FirstOrDefault(s => s.ScrollableHeight > 0);
            check(scroll is not null && scroll.ExtentWidth <= scroll.ViewportWidth + 1, "The long help article wraps without horizontal overflow in a 600-pixel window");
            if (scroll is not null)
            {
                scroll.ScrollToEnd(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); help.UpdateLayout();
                check(scroll.VerticalOffset >= scroll.ScrollableHeight - 1, "The long help article scrolls to its end");
                var end = reader.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)?.GetCharacterRect(LogicalDirection.Backward) ?? Rect.Empty;
                check(!end.IsEmpty && end.Top < reader.ActualHeight && end.Bottom >= 0, "The final text remains visible at the bottom of the help reader");
            }
            if (Environment.GetEnvironmentVariable("HELP_FONT_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(help, capture);
        }
        finally { help.Close(); }
    }
    private static IEnumerable<Drawing> Drawings(Drawing? drawing)
    {
        if (drawing is null) yield break;
        yield return drawing;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var item in Drawings(child)) yield return item;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
