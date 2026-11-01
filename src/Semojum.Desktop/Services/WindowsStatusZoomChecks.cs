using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Semojum.Desktop.Services;

internal static class WindowsStatusZoomChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            window.UpdateLayout();
            var buttons = Children(window.StatusZoom).OfType<Button>().ToArray();
            var minus = buttons.Single(b => AutomationProperties.GetName(b) == "축소");
            var plus = buttons.Single(b => AutomationProperties.GetName(b) == "확대");
            check(window.StatusBar.ActualHeight == 28 && window.ZoomSlider.Value == 50 && window.ZoomPercent.Text == "100%" && window.ZoomSlider.ActualWidth == 106,
                "Status zoom starts at the Figma 100-percent midpoint with a 106-pixel track");
            await CheckStatusCellsAsync(window, check);
            plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.SourceEditor.FontSize == 16 && Math.Abs(window.Braille.GridView.FontSize - 16 * 22.0 / 14) < 0.001 && window.ZoomPercent.Text == "114%",
                "Zoom-in updates the source, braille grid, and displayed percentage together");
            minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(window.SourceEditor.FontSize == 14 && window.ZoomSlider.Value == 50 && window.ZoomPercent.Text == "100%",
                "Zoom-out returns to the original size and synchronizes the slider");
            window.ZoomSlider.Value = 75;
            check(window.SourceEditor.FontSize == 23 && window.ZoomPercent.Text == "164%", "Moving the slider changes the actual editor size");
            window.ZoomPicker.SelectedItem = "125%";
            check(window.SourceEditor.FontSize == 17.5 && Math.Abs(window.ZoomSlider.Value - (50 + 3.5 * 50 / 18)) < 0.001,
                "Existing zoom selection remains synchronized with the status slider");
            window.ZoomSlider.Value = 0;
            check(window.SourceEditor.FontSize == 10, "Slider retains the existing minimum font size");
            window.ZoomSlider.Value = 100;
            check(window.SourceEditor.FontSize == 32, "Slider retains the existing maximum font size");
            check(window.ZoomSlider.Focusable && minus.Focusable && plus.Focusable && AutomationProperties.GetName(window.ZoomSlider) == "편집기 확대 비율",
                "Zoom slider and both actions are keyboard focusable and named for assistive technology");
            window.MinWidth = 480; // as WindowBounds.Fit allows on a monitor narrower than the 1280-pixel minimum
            foreach (var width in new[] { 1280.0, 480.0 })
            {
                window.Width = width;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var position = window.StatusZoom.TranslatePoint(new Point(), window);
                check(window.StatusZoom.IsVisible && window.ZoomSlider.ActualWidth == 106 && position.X >= 0 && position.X + window.StatusZoom.ActualWidth <= window.ActualWidth,
                    $"Status zoom remains visible and reachable at {width:0}px window width");
            }
            if (Environment.GetEnvironmentVariable("STATUS_ZOOM_CAPTURE") is { Length: > 0 } capture)
                await WindowsSmoke.RenderElementAsync(window.StatusBar, capture);
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    // Figma 263:7467: 「입력 한글 │ 쪽 12 / 128 │ 줄 7 · 칸 18 / 32 │ 옵션 2024 · 2급 · 32×25」 with 1×14 #EAEAEA dividers.
    private static async Task CheckStatusCellsAsync(MainWindow window, Action<bool, string> check)
    {
        var parts = ((StackPanel)window.StatusInput.Parent).Children.OfType<FrameworkElement>().ToArray();
        var texts = parts.OfType<TextBlock>().ToArray(); var dividers = parts.OfType<Border>().ToArray();
        double Left(FrameworkElement element) => element.TranslatePoint(new Point(), window.StatusBar).X;
        static bool Near(IEnumerable<double> actual, double[] expected) => actual.Count() == expected.Length && actual.Zip(expected).All(p => Math.Abs(p.First - p.Second) < 0.75);
        check(texts.SequenceEqual(new[] { window.StatusInput, window.StatusPage, window.CaretStatus, window.StatusOptions })
            && Near(texts.Select(Left), [20, 100, 197, 332]) && Near(dividers.Select(Left), [87, 184, 319, 493])
            && dividers.All(d => Math.Abs(d.ActualWidth - 1) < 0.5 && Math.Abs(d.ActualHeight - 14) < 0.5 && d.Background is SolidColorBrush { Color: var line } && line == Color.FromRgb(234, 234, 234))
            && texts.All(t => t.FontSize == 12 && t.Foreground is SolidColorBrush { Color: var ink } && ink == Color.FromRgb(95, 95, 95)),
            $"The status bar has the four Figma cells and 1x14 dividers (text {string.Join(",", texts.Select(t => Left(t).ToString("0.#")))}; dividers {string.Join(",", dividers.Select(d => Left(d).ToString("0.#")))})");
        check(Regex.IsMatch(window.CaretStatus.Text, @"^줄 \d+ · 칸 \d+ / \d+$") && window.StatusPage.Text.StartsWith("쪽 ", StringComparison.Ordinal)
            && window.StatusOptions.Text.StartsWith("옵션 2024 · ", StringComparison.Ordinal),
            $"The status bar merges line and cell into one cell and keeps the page and options cells (actual {window.StatusPage.Text} | {window.CaretStatus.Text} | {window.StatusOptions.Text})");

        // The first cell is the keyboard's 한/영 state rather than the focused panel.
        var korean = new CultureInfo("ko-KR");
        check(MainWindow.InputStatusText(korean, ImeConversionModeValues.Native | ImeConversionModeValues.FullShape) == "입력 한글"
            && MainWindow.InputStatusText(korean, ImeConversionModeValues.Alphanumeric) == "입력 영문"
            && MainWindow.InputStatusText(new CultureInfo("en-US"), ImeConversionModeValues.Native) == "입력 영문",
            "Hangul conversion on a Korean keyboard reads 입력 한글 and every other input reads 입력 영문");
        var languages = InputLanguageManager.Current;
        var previousLanguage = languages.CurrentInputLanguage; var previousMode = InputMethod.Current.ImeConversionMode;
        var installed = languages.AvailableInputLanguages?.OfType<CultureInfo>().FirstOrDefault(c => c.TwoLetterISOLanguageName == "ko");
        try
        {
            window.PageEditor.Editor.Focus();
            if (installed is null)
            {
                check(window.StatusInput.Text == "입력 영문", $"Without a Korean keyboard the status bar reports English input (actual {window.StatusInput.Text})");
                return;
            }
            languages.CurrentInputLanguage = installed;
            InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Native;
            await WaitUntilAsync(() => window.StatusInput.Text == "입력 한글");
            check(window.StatusInput.Text == "입력 한글", $"Switching the Korean keyboard to Hangul updates the status bar (actual {window.StatusInput.Text}, mode {InputMethod.Current.ImeConversionMode})");
            InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Alphanumeric;
            await WaitUntilAsync(() => window.StatusInput.Text == "입력 영문");
            check(window.StatusInput.Text == "입력 영문", $"Switching back to English with 한/영 updates the status bar (actual {window.StatusInput.Text})");
            InputMethod.Current.ImeConversionMode = ImeConversionModeValues.Native;
            await WaitUntilAsync(() => window.StatusInput.Text == "입력 한글");
            InputMethod.SetIsInputMethodEnabled(window.RuleSearch, false);
            check(window.RuleSearch.Focus(), "The rule search box takes keyboard focus");
            await WaitUntilAsync(() => window.StatusInput.Text == "입력 영문");
            check(window.StatusInput.Text == "입력 영문", $"Moving focus to a control without an IME reports English input (actual {window.StatusInput.Text})");
            window.PageEditor.Editor.Focus();
            await WaitUntilAsync(() => window.StatusInput.Text == "입력 한글");
            check(window.StatusInput.Text == "입력 한글", $"Returning focus to the text editor reports Hangul input again (actual {window.StatusInput.Text})");
        }
        finally
        {
            window.RuleSearch.ClearValue(InputMethod.IsInputMethodEnabledProperty);
            if (previousLanguage is not null) languages.CurrentInputLanguage = previousLanguage;
            InputMethod.Current.ImeConversionMode = previousMode;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(25);
    }

    private static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
