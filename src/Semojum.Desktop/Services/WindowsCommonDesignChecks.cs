using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Desktop.Controls;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsCommonDesignChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new Window { Width = 540, Height = 520, Title = "공통 컨트롤 검사" };
        var layout = new StackPanel { Margin = new(24) }; window.Content = layout;
        var action = new Button { Content = "보통 단추", Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
        var primary = new Button { Content = "강조 단추", Width = 120, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.FindResource("PrimaryButton") };
        var explicitButton = new Button { Content = "24", Width = 80, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var input = new TextBox { Text = "한글 입력", Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        var multiline = new TextBox { Text = "한글\n둘째 줄", AcceptsReturn = true, MinHeight = 64, Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        var borderless = new TextBox { BorderThickness = new(0), Background = Brushes.Transparent, Width = 250, HorizontalAlignment = HorizontalAlignment.Left };
        var combo = new ComboBox { Width = 250, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "첫 번째", "두 번째" }, SelectedIndex = 0 };
        var compactCombo = new ComboBox { Width = 100, Height = 24, MinHeight = 24, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = new[] { "하나" }, SelectedIndex = 0 };
        var choice = new CheckBox { Content = "점자 미리보기", IsChecked = true, Margin = new(0) };
        var radio = new RadioButton { Content = "개조식", IsChecked = true };
        foreach (var control in new FrameworkElement[] { action, primary, explicitButton, input, multiline, borderless, combo, compactCombo, choice, radio })
        { control.Margin = new Thickness(0, 0, 0, 8); layout.Children.Add(control); }
        AutomationProperties.SetName(input, "공통 입력"); AutomationProperties.SetName(combo, "공통 선택");
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var buttonSurface = action.Template.FindName("Surface", action) as Border;
            check(action.ActualHeight == 32 && buttonSurface?.CornerRadius.TopLeft == 4 && explicitButton.ActualHeight == 24,
                "Default buttons use the 32-pixel rounded design while explicit 24-pixel buttons stay compact");
            check(primary.Background is SolidColorBrush primaryBrush && primaryBrush.Color == Color.FromRgb(0, 95, 184), "Primary action keeps the Figma blue");
            check(action.Template.FindName("FocusOuter", action) is Border { BorderThickness.Left: 2 } && action.Template.FindName("FocusInner", action) is Border { BorderThickness.Left: 1 },
                "Button template retains the visible Figma keyboard-focus rings");
            check(input.ActualHeight == 32 && multiline.ActualHeight >= 64 && input.Template.FindName("PART_ContentHost", input) is ScrollViewer,
                "Single-line fields use 32 pixels while multiline text retains its size and editing host");
            input.Select(input.Text.Length, 0); input.SelectedText = " 확인";
            check(input.Text == "한글 입력 확인" && AutomationProperties.GetName(input) == "공통 입력", "Text editing and the accessible field name are preserved");
            check((borderless.Template.FindName("Underline", borderless) as FrameworkElement)?.Visibility == Visibility.Collapsed,
                "Explicitly borderless inputs do not gain a duplicate underline");
            combo.IsDropDownOpen = true; combo.SelectedIndex = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(combo.ActualHeight == 32 && compactCombo.ActualHeight == 24 && combo.SelectedIndex == 1 && combo.IsDropDownOpen,
                "Combo boxes retain selection and popup behavior, including explicitly compact controls");
            check(Descendants(combo).OfType<SvgViewbox>().Any(i => i.Source?.ToString().EndsWith("/common-combo-chevron.svg", StringComparison.Ordinal) == true && i.Drawings is { Bounds.IsEmpty: false }),
                "Default combo uses the original Figma chevron SVG");
            combo.IsDropDownOpen = false;
            var checkSurface = choice.Template.FindName("CheckSurface", choice) as Border;
            var checkIcon = choice.Template.FindName("CheckIcon", choice) as SvgViewbox;
            check(checkSurface is { CornerRadius.TopLeft: 4 } && checkIcon is { IsVisible: true, Drawings.Bounds.IsEmpty: false }, "Checked state uses the 20-pixel Figma box and original check icon");
            choice.IsChecked = false;
            check(checkIcon?.Visibility == Visibility.Collapsed && checkSurface?.Background is SolidColorBrush uncheckedBrush && uncheckedBrush.Color == Color.FromRgb(251, 251, 251), "Unchecked state remains distinct");
            check(radio.Template.FindName("RadioOn", radio) is SvgViewbox { IsVisible: true, Drawings.Bounds.IsEmpty: false } && radio.Template.FindName("RadioDot", radio) is SvgViewbox { IsVisible: true }, "Radio selection uses the supplied Figma SVGs");
            var glyphs = GlyphFiles(window.FontFamily, "가나다 ABC 123").ToArray();
            var installed = Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, "Malgun Gothic", StringComparison.OrdinalIgnoreCase));
            check(installed ? glyphs.Any(f => Path.GetFileName(f).StartsWith("malgun", StringComparison.OrdinalIgnoreCase)) : glyphs.Any(f => f.Contains("NotoSansKR", StringComparison.OrdinalIgnoreCase)),
                "General UI renders in Malgun Gothic when installed and otherwise uses a bundled Korean font");
            var missingSystemFont = new FontFamily("Semojum Missing Font Test, " + BundledFonts.Interface.Source);
            var fallbackGlyphs = GlyphFiles(missingSystemFont, "한글").ToArray();
            check(fallbackGlyphs.Any(f => f.Contains("NotoSansKR", StringComparison.OrdinalIgnoreCase)),
                "When the system UI font is unavailable, Korean glyphs fall back to the bundled font");
            check(BundledFonts.Interface.Source.Contains("Noto Sans KR", StringComparison.Ordinal) && !BundledFonts.Interface.Source.Contains("Malgun", StringComparison.OrdinalIgnoreCase),
                "Document InterfaceFont remains the existing bundled family");
            var lineHeight = new InterfaceLineHeightConverter();
            check(Equals(lineHeight.Convert(18d, typeof(double), null!, CultureInfo.InvariantCulture), 24d) && Equals(lineHeight.Convert(22d, typeof(double), null!, CultureInfo.InvariantCulture), 28d),
                "General text line heights follow the Figma 18/24 and 22/28 ramp");
            if (Environment.GetEnvironmentVariable("COMMON_CONTROLS_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
        }
        finally { window.Close(); }

        var editor = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 960, Height = 600 }; editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            check(editor.SourcePanel.ActualWidth >= 300 && editor.Braille.ActualWidth >= 300 && editor.TextStylePicker.ActualHeight == 24,
                "The 960-pixel editor retains usable panels and its explicitly compact toolbar");
            var menuLabels = editor.ApplicationMenu.Items.OfType<MenuItem>().ToArray();
            check(menuLabels.All(item => TextWidth(item.Header?.ToString()?.Replace("_", "", StringComparison.Ordinal) ?? "", item.FontFamily, item.FontSize, item.FontWeight) <= item.ActualWidth - 8),
                "All six main-menu labels fit their actual 960-pixel controls with the UI font");
            var documentGlyphs = GlyphFiles(editor.PageEditor.Editor.FontFamily, "한글 ABC").ToArray();
            check(documentGlyphs.Any(f => f.Contains("NotoSansKR", StringComparison.OrdinalIgnoreCase)) && documentGlyphs.Any(f => f.Contains("Inter-Regular", StringComparison.OrdinalIgnoreCase)) &&
                editor.Braille.GridView.FontSize == 22,
                "The source document renders with its bundled fonts and the braille grid retains its 22-pixel geometry");
            check(Descendants(editor).OfType<SvgViewbox>().Any(i => i.Source?.ToString().EndsWith("/common-rulebook.svg", StringComparison.Ordinal) == true && i.Drawings is { Bounds.IsEmpty: false }),
                "Rule search renders its original Figma icon");
            var separators = Descendants(editor.EditorToolbar).OfType<Border>().Where(b => b.Width == 1 && b.Height == 18 && b.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(234, 234, 234)).ToArray();
            var searchFrame = Descendants(editor.EditorToolbar).OfType<Border>().Single(b => b.Width == 240 && b.Height == 32 && b.Child is not null && Descendants(b).Contains(editor.RuleSearch));
            var hint = Descendants(searchFrame).OfType<TextBlock>().Single(t => t.Text == "점역 규정 검색...");
            check(editor.EditorToolbar.Background is SolidColorBrush toolbarBrush && toolbarBrush.Color == Color.FromRgb(243, 243, 243) && separators.Length == 4,
                "The main toolbar uses the Figma background and four 18px separators");
            check(searchFrame.BorderBrush is SolidColorBrush searchStroke && searchStroke.Color == Color.FromRgb(209, 209, 209) && searchFrame.CornerRadius == new CornerRadius(4) &&
                  hint.Foreground is SolidColorBrush hintInk && hintInk.Color == Color.FromRgb(138, 138, 138) && hint.IsVisible && !hint.IsHitTestVisible,
                "The rule-search field uses the Figma outline and a non-intercepting hint");
            editor.RuleSearch.Text = "점자"; await Dispatcher.Yield(DispatcherPriority.DataBind);
            check(hint.Visibility == Visibility.Collapsed && editor.RuleSearch.Text == "점자", "Typing in rule search hides only the hint");
            editor.RuleSearch.Text = "";
            if (Environment.GetEnvironmentVariable("COMMON_EDITOR_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(editor, capture);
            editor.Width = 480; editor.Height = 360; editor.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var toolbar = (ScrollViewer)editor.FindName("EditorToolbarScroll");
            var formatting = (ScrollViewer)editor.FindName("FormatToolbarScroll");
            var save = Descendants(editor.EditorToolbar).OfType<Button>().Single(b => Equals(b.Tag, "save"));
            var lastFormat = Descendants(formatting).OfType<Button>().Single(b => Equals(b.Tag, "lineNumbers"));
            toolbar.ScrollToLeftEnd(); formatting.ScrollToRightEnd(); editor.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(toolbar.ScrollableWidth > 0 && formatting.ScrollableWidth > 0 && InViewport(toolbar, save) && InViewport(formatting, lastFormat),
                "At 480 pixels the full toolbar buttons remain inside the viewport when scrollbars are visible");
        }
        finally { await editor.ReleaseResourcesAsync(); editor.PrepareSmokeShutdown(); editor.Close(); }
    }
    private static IEnumerable<string> GlyphFiles(FontFamily family, string text)
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
            drawing.DrawText(new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
                new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, Brushes.Black, 1), new Point());
        return Drawings(visual.Drawing).OfType<GlyphRunDrawing>().Select(g => g.GlyphRun.GlyphTypeface.FontUri.LocalPath);
    }
    private static double TextWidth(string text, FontFamily family, double size, FontWeight weight)
        => new FormattedText(text, CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
    private static bool InViewport(ScrollViewer scroll, FrameworkElement element)
    {
        var presenter = Descendants(scroll).OfType<ScrollContentPresenter>().FirstOrDefault(p => ReferenceEquals(p.TemplatedParent, scroll));
        if (presenter is null || !element.IsVisible) return false;
        var bounds = element.TransformToAncestor(presenter).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= presenter.ActualWidth + 1 && bounds.Bottom <= presenter.ActualHeight + 1;
    }
    private static IEnumerable<Drawing> Drawings(Drawing drawing)
    {
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
