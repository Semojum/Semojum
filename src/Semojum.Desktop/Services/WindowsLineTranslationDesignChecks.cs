using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Desktop.Controls;
using Semojum.Core;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsLineTranslationDesignChecks
{
    internal static void Run(Window owner, Action<bool, string> check)
    {
        var pane = new BraillePane { Width = 820, Height = 420 }; pane.SetLinePreview(7, "⠼⠉⠀⠦⠊⠴"); pane.SetLinePreviewVisible(true);
        var closed = false; pane.PreviewCloseRequested += (_, _) => { closed = true; pane.SetLinePreviewVisible(false); };
        var window = new Window { Width = 820, Height = 420, WindowStyle = WindowStyle.None, Content = pane, Owner = owner }; window.Show();
        try
        {
            window.UpdateLayout(); var strip = pane.PreviewStrip;
            var close = Children(strip).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "줄번역 숨기기");
            var icon = close.Content as SvgViewbox;
            check(strip.ActualHeight == 90 && strip.Cells.Text == "⠼⠉⠀⠦⠊⠴" && strip.LineLabel.Text == "줄번역", "Braille panel line translation displays the supplied cells at the Figma height");
            check(icon is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false } && icon.Source?.ToString().EndsWith("line-preview-close.svg", StringComparison.Ordinal) == true && icon.ActualWidth > 0 && icon.ActualWidth <= 16 && icon.ActualHeight > 0 && icon.ActualHeight <= 16, "Line translation uses the exact Figma close SVG in a 16px slot");
            check(pane.GridView.IsVisible, "Braille grid remains visible while line translation is open");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(closed && strip.Visibility == Visibility.Collapsed && pane.GridView.IsVisible, "Closing line translation leaves the braille editor available");
        }
        finally { window.Close(); pane.Dispose(); }

        var global = new LineTranslation(); global.SetPreview(3, "⠁⠃");
        check(global.Height == 64 && global.Cells.Text == "⠁⠃", "The shared editor strip retains its existing 64px height and supplied translation");

        var editor = new MainWindow(smoke: true, testWithoutAccount: true); editor.Show();
        try
        {
            editor.ApplyAppearance(new());
            check(editor.Braille.GridView.CurrentLineBrush is SolidColorBrush { Color: var initial } && initial == Color.FromRgb(242, 246, 251), "Default braille current line uses the final Figma color");
            bool TextLineIs(Color expected) => editor.SourceEditor.TextArea.TextView.CurrentLineBackground is SolidColorBrush source && source.Color == expected
                && editor.PageEditor.Editor.TextArea.TextView.CurrentLineBackground is SolidColorBrush page && page.Color == expected;
            check(TextLineIs(Color.FromRgb(242, 246, 251)), "New text and source editors use the final Figma current-line color");
            editor.ApplyAppearance(new EditorAppearance(CurrentLineColor: "#123456"), explicitAppearance: true);
            check(TextLineIs(Color.FromRgb(18, 52, 86)), "An unsaved text current-line color is previewed");
            editor.ApplyAppearance(new());
            check(TextLineIs(Color.FromRgb(242, 246, 251)), "Canceling the preview restores the new default text current-line color");
            var stored = new EditorAppearance(CurrentLineColor: "#FFF5A3");
            typeof(MainWindow).GetField("accountPreferences", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(editor, new AccountPreferences(Appearance: stored));
            editor.ApplyAppearance(stored);
            check(editor.Braille.GridView.CurrentLineBrush is SolidColorBrush { Color: var saved } && saved == Color.FromRgb(255, 245, 163), "A saved yellow current-line color is preserved");
            check(TextLineIs(Color.FromRgb(255, 245, 163)), "Saved yellow current-line colors remain on text and source editors");
            var custom = stored with { CurrentLineColor = "#123456" }; editor.ApplyAppearance(custom, explicitAppearance: true);
            check(TextLineIs(Color.FromRgb(18, 52, 86)), "A saved user's text current-line color can be previewed");
            editor.ApplyAppearance(custom with { HighContrast = true }, explicitAppearance: true);
            check(TextLineIs(Color.FromRgb(32, 32, 32)), "High contrast retains its current-line color");
            editor.ApplyAppearance(custom, explicitAppearance: true);
            check(editor.Braille.GridView.CurrentLineBrush is SolidColorBrush { Color: var customColor } && customColor == Color.FromRgb(18, 52, 86), "A user preview color is applied to the braille grid");
            editor.ToggleDetached();
            check(editor.DetachedPane?.GridView.CurrentLineBrush is SolidColorBrush { Color: var detached } && detached == Color.FromRgb(18, 52, 86), "A detached braille panel retains the current-line color");
            editor.ToggleDetached();
        }
        finally { editor.PrepareSmokeShutdown(); editor.Close(); }
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Children(child)) yield return nested; }
    }
}
