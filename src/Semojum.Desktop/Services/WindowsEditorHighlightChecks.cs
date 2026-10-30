using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using Semojum.Core;
using Semojum.Desktop.Controls;
using ICSharpCode.AvalonEdit.Rendering;

namespace Semojum.Desktop.Services;

internal static class WindowsEditorHighlightChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var block = new EditorBlock { Id = 1, Page = 1, Braille = "⠁⠃" };
        var first = new GridCell(block, 0, '⠁'); var second = new GridCell(block, 1, '⠃');
        var face = new BrailleGridFace(1, [[first, second]]);
        var grid = new BrailleGridView { Width = 200, Height = 100, Blocks = [block], ItemsSource = new[] { face } };
        var window = new Window { Width = 220, Height = 140, Content = grid, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            grid.SetLinkedRanges([new BlockSpan(block, 0, 1)]);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var visual = Descendants(grid).OfType<FrameworkElement>().First(x => x.GetType().Name == "Face");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth), (int)Math.Ceiling(visual.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var selected = Pixel(bitmap, 55, 2); var neighbor = Pixel(bitmap, 72, 2);
            check(selected == Color.FromRgb(204, 228, 247) && neighbor == Colors.White,
                $"A source-linked braille cell uses Figma selection blue without coloring its neighbor (selected {selected}; neighbor {neighbor})");
            grid.ActiveBlock = block; grid.CaretOffset = 1; grid.InvalidateFaces();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 40, 2) == Color.FromRgb(0, 95, 184), "The current braille row renders the Figma 2px blue marker");
            grid.SetLinkedRanges([]); grid.Point(first, false); grid.Point(second, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 55, 2) == Color.FromRgb(204, 228, 247) && grid.Selection is [{ Start: 0, Length: 1 }],
                "Direct braille selection uses the Figma color without changing its selected cell range");
            grid.SelectionHighlightBrush = new SolidColorBrush(Color.FromRgb(18, 52, 86));
            grid.SelectionTextBrush = Brushes.White; grid.InvalidateFaces();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 55, 2) == Color.FromRgb(18, 52, 86) && HasColor(bitmap, 52, 0, 16, 28, Colors.White),
                "Custom dark selection retains a readable custom selected-text color");
            grid.HighContrast = true; grid.InvalidateFaces(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 40, 2) == Colors.White && Pixel(bitmap, 55, 2) == Colors.DarkBlue && HasColor(bitmap, 52, 0, 16, 28, Colors.White),
                "High contrast keeps the current-row marker and selected text visible");
            grid.HighContrast = false; grid.SelectionHighlightBrush = Brushes.Black;
            grid.CurrentLineBrush = Brushes.Black; grid.Point(first, false);
            grid.SetLinkedRanges([new BlockSpan(block, 0, 1)]);
            window.Activate(); grid.Focus(); grid.InvalidateFaces();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(grid.IsKeyboardFocusWithin && Pixel(bitmap, 52, 2) == Colors.White && Pixel(bitmap, 53, 2) == Colors.Black,
                $"Focused caret remains visible over a black user selection (focus={grid.IsKeyboardFocusWithin}, center={Pixel(bitmap, 52, 2)})");
            grid.SetLinkedRanges([]); grid.InvalidateFaces(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 52, 2) == Colors.White && Pixel(bitmap, 55, 2) == Colors.Black,
                "Focused caret remains visible over a black user current-line color");
            grid.HighContrast = true; grid.InvalidateFaces(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 52, 2) == Colors.White && Pixel(bitmap, 55, 2) == Colors.DarkBlue,
                "Focused caret remains visible on the high contrast current line");
            grid.HighContrast = false; grid.CurrentLineBrush = Brushes.White; grid.InvalidateFaces();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(Pixel(bitmap, 51, 2) == Colors.Black && Pixel(bitmap, 52, 2) == Colors.White && Pixel(bitmap, 54, 2) == Colors.White,
                "Focused caret retains a dark edge over a white current line");
            grid.HighContrast = true;
            Keyboard.ClearFocus(); grid.InvalidateFaces(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            bitmap = Render(visual);
            check(!grid.IsKeyboardFocusWithin && Pixel(bitmap, 52, 2) == Colors.DarkBlue,
                "Caret is hidden when the braille grid loses keyboard focus");
        }
        finally { window.Close(); }

        var main = new MainWindow(smoke: true, testWithoutAccount: true); main.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            main.ApplyAppearance(new EditorAppearance(SelectionColor: "#123456", SelectionTextColor: "#FFFFFF"), explicitAppearance: true);
            check(main.Braille.GridView.SelectionHighlightBrush is SolidColorBrush { Color: var back } && back == Color.FromRgb(18, 52, 86)
                && main.Braille.GridView.SelectionTextBrush is SolidColorBrush { Color: var fore } && fore == Colors.White,
                "Saved selection background and text colors reach the real braille panel");
            main.ToggleDetached();
            check(main.DetachedPane?.GridView.SelectionHighlightBrush is SolidColorBrush { Color: var detachedBack } && detachedBack == Color.FromRgb(18, 52, 86)
                && main.DetachedPane.GridView.SelectionTextBrush is SolidColorBrush { Color: var detachedFore } && detachedFore == Colors.White,
                "Detached braille panel preserves both user selection colors");
            main.ToggleDetached();
            var editor = main.PageEditor.Editor;
            var inset = editor.TextArea.LeftMargins.OfType<SourceTextInset>().Single();
            editor.CaretOffset = editor.Document.GetLineByNumber(1).Offset;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            int LineY() => (int)Math.Round(editor.TextArea.TextView.GetVisualPosition(editor.TextArea.Caret.Position, VisualYPosition.LineTop).Y - editor.TextArea.TextView.VerticalOffset + 2);
            Color SourcePixel(int lineY)
            {
                var p = inset.TransformToAncestor(main).Transform(new Point(0, lineY));
                return Pixel(Render(main), (int)Math.Round(p.X), (int)Math.Round(p.Y));
            }
            var firstY = LineY();
            if (Environment.GetEnvironmentVariable("HIGHLIGHT_SOURCE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(main, capture);
            check(SourcePixel(firstY) == Color.FromRgb(0, 95, 184), $"Current source row renders the Figma 2px blue marker (color {SourcePixel(firstY)})");
            editor.CaretOffset = editor.Document.GetLineByNumber(2).Offset;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var secondY = LineY();
            check(secondY != firstY && SourcePixel(secondY) == Color.FromRgb(0, 95, 184) && SourcePixel(firstY) != Color.FromRgb(0, 95, 184),
                "Source current-row marker follows the caret without changing an earlier row");
            main.ApplyAppearance(new EditorAppearance(HighContrast: true), explicitAppearance: true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            check(SourcePixel(LineY()) == Colors.White, "Source current-row marker remains visible in high contrast");
        }
        finally { await main.ReleaseResourcesAsync(); main.PrepareSmokeShutdown(); main.Close(); }
    }

    private static RenderTargetBitmap Render(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); return bitmap;
    }

    private static bool HasColor(BitmapSource bitmap, int x, int y, int width, int height, Color expected)
    {
        for (var row = y; row < y + height; row++)
            for (var col = x; col < x + width; col++)
                if (Pixel(bitmap, col, row) == expected) return true;
        return false;
    }

    private static Color Pixel(BitmapSource bitmap, int x, int y)
    {
        var data = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), data, 4, 0);
        return Color.FromArgb(data[3], data[2], data[1], data[0]);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
