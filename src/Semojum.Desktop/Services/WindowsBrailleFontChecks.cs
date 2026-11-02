using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using Semojum.Core;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

/// <summary>Braille cells render from the bundled Nuridot Braille, while other text in braille controls keeps Segoe UI Symbol.</summary>
internal static class WindowsBrailleFontChecks
{
    private static readonly string SixDots = string.Concat(Enumerable.Range(0x2800, 64).Select(c => (char)c));
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var bundled = new GlyphTypeface(new Uri(BundledFonts.DirectoryUri, "NuridotBraille.ttf"));
        check(HasSixDots(bundled) && SixDots.All(c => Math.Abs(bundled.AdvanceWidths[bundled.CharacterToGlyphMap[c]] - 740d / 1024) < 1e-9),
            "The bundled Nuridot Braille font holds all 64 six-dot cells at one 740/1024 em advance");
        check(ReferenceEquals(Application.Current.FindResource("BrailleFont"), BundledFonts.Braille), "The BrailleFont resource is the bundled braille family");
        check(Faces(BundledFonts.Braille, SixDots) is [var six] && HasSixDots(six), "Every six-dot cell resolves to a Nuridot Braille glyph");
        check(Faces(BundledFonts.Braille, "⡀⣀") is [var eight] && HasSixDots(eight) && "⡀⣀".All(c => eight.CharacterToGlyphMap.ContainsKey(c)), "8-dot cells also resolve to Nuridot Braille");
        check(Faces(BundledFonts.Braille, "123<!>") is [var marks] && IsSegoeSymbol(marks), "Digits and layout markers keep Segoe UI Symbol instead of the braille font");

        var block = new EditorBlock { Id = 1, Source = "점자", Braille = SixDots };
        var face = new BrailleGridFace(1, [SixDots[..32].Select((c, i) => (GridCell?)new GridCell(block, i, c)).ToArray(), SixDots[32..].Select((c, i) => (GridCell?)new GridCell(block, 32 + i, c)).ToArray()]);
        var grid = new BrailleGridView { ItemsSource = new[] { face }, Blocks = [block] };
        var strip = new LineTranslation(); strip.SetPreview(1, SixDots);
        var host = new Window { Width = 760, Height = 260, ShowInTaskbar = false, Content = new DockPanel { Children = { strip, grid } } };
        DockPanel.SetDock(strip, Dock.Bottom); host.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); host.UpdateLayout();
            var cells = Runs(grid).Where(r => r.Characters?.Any(IsSixDot) == true).ToArray();
            check(DrawsBrailleFont(grid) && cells.Length == 64, "The braille grid draws all 64 six-dot cells from Nuridot Braille");
            var left = cells.Min(r => r.BaselineOrigin.X);
            check(cells.All(r =>
            {
                var ink = r.ComputeInkBoundingBox(); ink.Offset((Vector)r.BaselineOrigin); var column = (r.BaselineOrigin.X - left) / 16; var row = Math.Floor(r.BaselineOrigin.Y / 28);
                return Math.Abs(column - Math.Round(column)) < .01 && ink.Left >= r.BaselineOrigin.X && ink.Right <= r.BaselineOrigin.X + 16 && ink.Top >= row * 28 && ink.Bottom <= row * 28 + 28;
            }) && cells.Select(r => r.BaselineOrigin.X).Distinct().Count() == 32, "Each Nuridot Braille cell stays inside its 16 by 28 grid box, so cell, selection and review geometry is unchanged");
            check(DrawsBrailleFont(strip.Cells), "Line translation draws its cells from Nuridot Braille");
        }
        finally { host.Close(); }

        var pages = new[] { new[] { new string('⠿', 32), SixDots[..32], SixDots[32..] } };
        var preview = new BraillePreviewDialog(pages, "", new PageLayout(), "글꼴 검사") { ShowInTaskbar = false }; preview.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); preview.UpdateLayout();
            var editor = Descendants(preview).OfType<TextEditor>().Single(); var view = editor.TextArea.TextView; view.EnsureVisualLines();
            check(DrawsBrailleFont(view), "Braille page preview draws its cells from Nuridot Braille");
            var numbers = editor.TextArea.LeftMargins.OfType<LineNumberMargin>().Single();
            check(Runs(numbers).Length > 0 && Runs(numbers).All(r => IsSegoeSymbol(r.GlyphTypeface)), "Braille page preview line numbers stay digits rather than Braille ASCII cells");
            var ruler = view.BackgroundRenderers.OfType<BraillePreviewDialog.CellRuler>().Single();
            var end = view.GetVisualPosition(new TextViewPosition(1, 33), ICSharpCode.AvalonEdit.Rendering.VisualYPosition.LineTop).X;
            check(Math.Abs(ruler.Offset - end) < .5 && Math.Abs(end - 32 * 22 * 740d / 1024) < .5, "Braille page preview draws the 32-cell edge after the last Nuridot Braille cell");
        }
        finally { preview.Close(); }

        var markerBlock = new EditorBlock { Id = 2, Source = "쪽", Braille = SixDots[..8] + "\n<!쪽바꿈>\n⠿" };
        var range = new RangeSelectionDialog(() => [markerBlock], SearchPanel.Braille, (_, _) => Task.CompletedTask) { ShowInTaskbar = false }; range.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); range.UpdateLayout();
            var editor = Descendants(range).OfType<TextEditor>().Single(); editor.TextArea.TextView.EnsureVisualLines();
            check(DrawsBrailleFont(editor.TextArea.TextView), "Range selection draws braille cells from Nuridot Braille");
            check(Runs(editor.TextArea.TextView).Where(r => r.Characters?.Contains('<') == true).ToArray() is { Length: > 0 } marker && marker.All(r => IsSegoeSymbol(r.GlyphTypeface)),
                "Range selection keeps layout markers as readable text");
        }
        finally { range.Close(); }

        var math = new MathDialog((_, _) => Task.FromResult(BrailleTranslation.Plain("⠁")), "x", "⠿⠁") { ShowInTaskbar = false }; math.Show();
        try { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); math.UpdateLayout(); check(DrawsBrailleFont(math.BrailleInput), "Math braille input draws its cells from Nuridot Braille"); }
        finally { math.Close(); }

        var symbols = new SymbolDialog([new("→", "⠒⠕", "화살표")]) { ShowInTaskbar = false }; symbols.Show();
        try { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); symbols.UpdateLayout(); check(DrawsBrailleFont(symbols), "Special symbol list draws each symbol's cells from Nuridot Braille"); }
        finally { symbols.Close(); }

        var snippets = new SnippetDialog(new(BrailleSnippets: Enumerable.Repeat("⠿⠁", 10).ToArray())) { ShowInTaskbar = false }; snippets.Show();
        try
        {
            Descendants(snippets).OfType<TabControl>().Single().SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); snippets.UpdateLayout();
            check(DrawsBrailleFont(snippets), "Braille snippet fields draw their cells from Nuridot Braille");
        }
        finally { snippets.Close(); }

        var find = new FindDialog(SearchPanel.Braille, "⠿⠯", true, (_, _) => Task.FromResult(""), (_, _, _, _) => Task.FromResult("")) { ShowInTaskbar = false }; find.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); find.UpdateLayout();
            var query = Descendants(find).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "찾을 내용");
            check(DrawsBrailleFont(query), "Braille find input draws its cells from Nuridot Braille");
            Descendants(find).OfType<ToggleButton>().Single(t => AutomationProperties.GetName(t) == "점자 입력").IsChecked = false;
            check(query.ReadLocalValue(Control.FontFamilyProperty) == DependencyProperty.UnsetValue, "Text find input returns to the dialog font");
        }
        finally { find.Close(); }

        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { ShowInTaskbar = false }; window.Show();
        try
        {
            window.ApplyAppearance(new());
            check(ReferenceEquals(window.DraftBraillePreview.FontFamily, BundledFonts.Braille) && Faces(window.DraftBraillePreview.FontFamily, SixDots) is [var draft] && HasSixDots(draft),
                "The draft dock braille preview uses Nuridot Braille");
            check(Faces(window.Braille.Editor.FontFamily, SixDots) is [var spaced] && HasSixDots(spaced) && Faces(window.Braille.Editor.FontFamily, "123") is [var digits] && IsSegoeSymbol(digits),
                "Account line spacing keeps Nuridot Braille for braille cells and Segoe UI Symbol for other text");
        }
        finally { window.PrepareSmokeShutdown(); window.Close(); }
    }

    internal static bool DrawsBrailleFont(Visual root)
    {
        var runs = Runs(root).Where(r => r.Characters?.Any(IsSixDot) == true).ToArray();
        return runs.Length > 0 && runs.All(r => HasSixDots(r.GlyphTypeface));
    }
    private static bool IsSixDot(char c) => c is >= '⠀' and <= '⠿';
    private static bool HasSixDots(GlyphTypeface face) => Path.GetFileName(face.FontUri.LocalPath).Equals("NuridotBraille.ttf", StringComparison.OrdinalIgnoreCase)
        && face.FamilyNames.Values.Contains("Nuridot Braille") && SixDots.All(c => face.CharacterToGlyphMap.ContainsKey(c));
    private static bool IsSegoeSymbol(GlyphTypeface face) => Path.GetFileName(face.FontUri.LocalPath).Equals("seguisym.ttf", StringComparison.OrdinalIgnoreCase);
    private static GlyphTypeface[] Faces(FontFamily family, string text)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 22, Brushes.Black, 1), new Point());
        return Runs(visual).Select(r => r.GlyphTypeface).Distinct().ToArray();
    }
    private static GlyphRun[] Runs(Visual root) => Descendants(root).OfType<Visual>().SelectMany(v => Drawings(VisualTreeHelper.GetDrawing(v))).OfType<GlyphRunDrawing>().Select(d => d.GlyphRun).ToArray();
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
