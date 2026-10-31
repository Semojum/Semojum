using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Semojum.Desktop.Controls;
using Semojum.Desktop.Dialogs;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsIndividualControlStatesChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var button = new Button { Content = "확인" }; PublicDialogDesign.StyleAction(button);
        var primary = new Button { Content = "확인" }; PublicDialogDesign.StyleAction(primary, true);
        var nested = new TextBox { Text = "업로드" };
        var stretch = new Button { Width = 130, Height = 32, Content = nested, Template = FigmaDialogElements.SurfaceTemplate(4, true) };
        var input = new TextBox { Text = "첫 줄\n두 번째 줄", AcceptsReturn = true, Height = 60, Width = 180 };
        EditorModalDesign.Input(input);
        var plain = new TextBox { Text = "경계 없음", BorderThickness = new Thickness(0) }; EditorModalDesign.Input(plain); plain.BorderThickness = new Thickness(0);
        var genericBlue = new Button { Content = "다시 시작", Background = (Brush)new BrushConverter().ConvertFromString("#005FB8")!, BorderBrush = Brushes.Red };
        var genericIcon = new Button { Content = "×", Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        var genericCustom = new Button { Content = "사용자 색", Background = Brushes.Purple };
        var genericPrimary = new Button { Content = "강조", Background = (Brush)new BrushConverter().ConvertFromString("#005FB8")!, BorderBrush = (Brush)new BrushConverter().ConvertFromString("#005FB8")!, Foreground = Brushes.White };
        var genericDestructive = new Button { Content = "삭제", Background = (Brush)new BrushConverter().ConvertFromString("#C42B1C")!, BorderBrush = (Brush)new BrushConverter().ConvertFromString("#C42B1C")!, Foreground = Brushes.White };
        var genericNeutral = new Button { Content = "취소" };
        var defaultInput = new TextBox { Text = "기본" };
        var customInput = new TextBox { Text = "사용자 색", Background = Brushes.AliceBlue };
        var panel = new StackPanel(); foreach (var element in new FrameworkElement[] { button, primary, stretch, input, plain, genericBlue, genericIcon, genericCustom, genericPrimary, genericDestructive, genericNeutral, defaultInput, customInput }) panel.Children.Add(element);
        var window = new Window { Width = 300, Height = 580, Content = panel, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(button.Width == 88 && button.Height == 32 && primary.Width == 88 && primary.Height == 32, "Dialog button dimensions are preserved");
            check(button.Template.FindName("Surface", button) is Border && button.Template.FindName("FocusOuter", button) is Border outer && outer.BorderThickness == new Thickness(2) && outer.Visibility == Visibility.Collapsed, "Button has an initially hidden two-pixel keyboard focus ring");
            check(button.Template.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusedProperty && Equals(t.Value, true)), "Focus ring is driven by keyboard focus");
            var clicks = 0; button.Click += (_, _) => clicks++; button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            check(clicks == 1, "The existing button click route is preserved");
            check(stretch.Template.FindName("Surface", stretch) is Border { Child: ContentPresenter { HorizontalAlignment: HorizontalAlignment.Stretch } } && nested.Text == "업로드", "Stretch content and embedded input are preserved");
            var converter = new FigmaButtonStateBrush();
            string State(string color, bool hover, bool pressed, bool enabled, bool border = false)
            {
                var brush = (Brush)new BrushConverter().ConvertFromString(color)!;
                return ((SolidColorBrush)converter.Convert(new object[] { brush, brush, hover, pressed, enabled }, typeof(Brush), border, CultureInfo.InvariantCulture)).Color.ToString();
            }
            check(State("#005FB8", true, false, true) == "#FF1A6FC0" && State("#005FB8", true, true, true) == "#FF004A91" && State("#005FB8", false, false, false) == "#FFBFBFBF", "Primary hover, press and disabled match the design");
            check(State("#C42B1C", true, false, true, true) == "#FFCF3B2C" && State("#C42B1C", true, true, true) == "#FFA82318" && State("#C42B1C", false, false, false) == "#FFE3BDB9", "Destructive hover, press and disabled match the design");
            check(State("#FBFBFB", true, false, true) == "#FFF5F5F5" && State("#FBFBFB", true, true, true) == "#FFF0F0F0", "Neutral hover and press match the design");
            check(State("#EFF6FC", true, true, true) == "#FFEFF6FC", "Custom backgrounds are preserved");
            check(State("#00000000", true, false, true) == "#FFF0F0F0" && State("#00000000", true, true, true) == "#FFEAEAEA" && State("#00000000", false, false, true) == "#00000000", "Borderless buttons use the Figma hover and press backgrounds");
            var foreground = new FigmaButtonForegroundBrush(); var inputBackground = new FigmaInputFocusBrush();
            var foregroundResult = (SolidColorBrush)foreground.Convert(new object[] { Brushes.Black, (Brush)new BrushConverter().ConvertFromString("#FBFBFB")!, false }, typeof(Brush), null!, CultureInfo.InvariantCulture);
            check(foregroundResult.Color.ToString() == "#FF9F9F9F", "Disabled neutral button uses the design's gray text");
            var defaultBg = (Brush)new BrushConverter().ConvertFromString("#FBFBFB")!;
            check(Equals(inputBackground.Convert(new object[] { defaultBg, true }, typeof(Brush), null!, CultureInfo.InvariantCulture), Brushes.White) && ReferenceEquals(inputBackground.Convert(new object[] { Brushes.AliceBlue, true }, typeof(Brush), null!, CultureInfo.InvariantCulture), Brushes.AliceBlue), "Focused default input turns white while custom backgrounds remain unchanged");
            check(input.Template.FindName("PART_ContentHost", input) is ScrollViewer && input.Text.Contains("두 번째 줄"), "Multiline editable input retains its WPF content host and text");
            check(input.Template.FindName("Underline", input) is Border { Height: 1 } && input.Template.FindName("FocusUnderline", input) is Border { Height: 2, Visibility: Visibility.Collapsed }, "Input has the design-system resting and focus underlines");
            check(input.Template.Triggers.OfType<Trigger>().Any(t => t.Property == UIElement.IsKeyboardFocusWithinProperty && Equals(t.Value, true)), "Input focus underline is driven by keyboard focus");
            var blueSurface = (Border)genericBlue.Template.FindName("Surface", genericBlue);
            var iconSurface = (Border)genericIcon.Template.FindName("Surface", genericIcon);
            var customSurface = (Border)genericCustom.Template.FindName("Surface", genericCustom);
            check(BindingOperations.GetMultiBindingExpression(blueSurface, Border.BackgroundProperty) is not null && ((SolidColorBrush)blueSurface.Background).Color.ToString() == "#FF005FB8", "The actual blue button surface binds its locally set color");
            check(((SolidColorBrush)iconSurface.Background).Color.A == 0 && Equals(customSurface.Background, Brushes.Purple), "The actual transparent and custom-color button surfaces preserve their backgrounds");
            check(State("#005FB8", true, false, true) == "#FF1A6FC0" && State("#00000000", true, true, true) == "#FFEAEAEA", "The actual surface converter supplies blue hover and transparent press colors");
            var redBorder = converter.Convert(new object[] { genericBlue.Background, Brushes.Red, true, false, true }, typeof(Brush), "genericBorder", CultureInfo.InvariantCulture);
            check(ReferenceEquals(redBorder, Brushes.Red), "A separately specified border color is preserved on hover");
            genericBlue.IsEnabled = false; window.UpdateLayout();
            check(blueSurface.Opacity == 1 && ((SolidColorBrush)blueSurface.Background).Color.ToString() == "#FFBFBFBF" && Equals(blueSurface.BorderBrush, Brushes.Red), "The actual disabled blue button uses the Figma fill and retains its separately specified border");
            genericPrimary.IsEnabled = genericDestructive.IsEnabled = genericNeutral.IsEnabled = genericIcon.IsEnabled = genericCustom.IsEnabled = false;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var primarySurface = (Border)genericPrimary.Template.FindName("Surface", genericPrimary);
            var destructiveSurface = (Border)genericDestructive.Template.FindName("Surface", genericDestructive);
            var neutralSurface = (Border)genericNeutral.Template.FindName("Surface", genericNeutral);
            static ContentPresenter Content(Border surface) => (ContentPresenter)surface.Child;
            static string ColorOf(Brush brush) => ((SolidColorBrush)brush).Color.ToString();
            check(primarySurface.Opacity == 1 && ColorOf(primarySurface.Background) == "#FFBFBFBF" && ColorOf(primarySurface.BorderBrush) == "#FFBFBFBF" && ColorOf(System.Windows.Documents.TextElement.GetForeground(Content(primarySurface))) == "#FFFFFFFF", "Disabled generic primary renders the Figma gray fill and white text");
            check(destructiveSurface.Opacity == 1 && ColorOf(destructiveSurface.Background) == "#FFE3BDB9" && ColorOf(destructiveSurface.BorderBrush) == "#FFE3BDB9" && ColorOf(System.Windows.Documents.TextElement.GetForeground(Content(destructiveSurface))) == "#FFFFFFFF", "Disabled generic destructive button renders the Figma muted red and white text");
            check(neutralSurface.Opacity == 1 && ColorOf(neutralSurface.Background) == "#FFF5F5F5" && ColorOf(neutralSurface.BorderBrush) == "#FFEAEAEA" && ColorOf(System.Windows.Documents.TextElement.GetForeground(Content(neutralSurface))) == "#FF9F9F9F", "Disabled generic neutral button renders the Figma fill, border and text");
            check(Pixel(primarySurface, 8, 8) == Color.FromRgb(191, 191, 191) && Pixel(destructiveSurface, 8, 8) == Color.FromRgb(227, 189, 185), "Actual disabled button surface pixels match the Figma primary and destructive fills");
            check(iconSurface.Opacity == 1 && Content(iconSurface).Opacity == .4 && customSurface.Opacity == .4 && Equals(customSurface.Background, Brushes.Purple), "Disabled transparent icons and custom-colored buttons keep their previous opacity behavior");
            var inputSurface = (Border)defaultInput.Template.FindName("InputSurface", defaultInput);
            var customInputSurface = (Border)customInput.Template.FindName("InputSurface", customInput);
            check(ColorOf(inputSurface.Background) == "#FFFBFBFB", "Unfocused generic input has the Figma default background");
            window.Activate(); defaultInput.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(defaultInput.IsKeyboardFocusWithin && ColorOf(inputSurface.Background) == "#FFFFFFFF" && ((Border)defaultInput.Template.FindName("FocusUnderline", defaultInput)).Visibility == Visibility.Visible,  $"Focused generic input turns white and shows its underline: focus={defaultInput.IsKeyboardFocusWithin}, background={ColorOf(inputSurface.Background)}, underline={((Border)defaultInput.Template.FindName("FocusUnderline", defaultInput)).Visibility}");
            customInput.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(customInput.IsKeyboardFocusWithin && Equals(customInputSurface.Background, Brushes.AliceBlue),  $"Focused generic input preserves an explicit custom background: focus={customInput.IsKeyboardFocusWithin}, background={customInputSurface.Background}");
            FigmaSystemContrast.SetIsHighContrast(genericPrimary, true); FigmaSystemContrast.SetIsHighContrast(genericNeutral, true); FigmaSystemContrast.SetIsHighContrast(defaultInput, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(Equals(primarySurface.Background, SystemColors.ControlBrush) && Equals(primarySurface.BorderBrush, SystemColors.GrayTextBrush) && Equals(System.Windows.Documents.TextElement.GetForeground(Content(primarySurface)), SystemColors.GrayTextBrush), "Generic buttons use system disabled colors in simulated Windows high contrast");
            check(Equals(inputSurface.Background, SystemColors.WindowBrush) && Equals(inputSurface.BorderBrush, SystemColors.WindowTextBrush) && Equals(defaultInput.Foreground, SystemColors.WindowTextBrush) && Equals(defaultInput.SelectionBrush, SystemColors.HighlightBrush), "Generic input uses system colors in simulated Windows high contrast");
            FigmaSystemContrast.SetIsHighContrast(genericPrimary, false); FigmaSystemContrast.SetIsHighContrast(genericNeutral, false); FigmaSystemContrast.SetIsHighContrast(defaultInput, false);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(ColorOf(primarySurface.Background) == "#FFBFBFBF" && ColorOf(neutralSurface.Background) == "#FFF5F5F5", "Generic buttons restore their Figma disabled colors after simulated contrast changes");
            check(plain.Template.FindName("Underline", plain) is Border { Visibility: Visibility.Collapsed } && plain.Template.FindName("FocusUnderline", plain) is Border { Visibility: Visibility.Collapsed }, "Borderless inputs remain borderless");
            input.Text += "!"; check(input.Text.EndsWith("!", StringComparison.Ordinal), "Input text remains writable");
            if (Environment.GetEnvironmentVariable("INDIVIDUAL_STATE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
        }
        finally { window.Close(); }
        // The actual panel keeps a light Figma header under transparent text and SVG buttons.
        var pane = new BraillePane { Height = 140 };
        var customWhite = new Button { Content = "흰 글자", Foreground = Brushes.White, Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        var darkBackground = new Border { Background = Brushes.Black, Child = customWhite };
        var contrastPanel = new StackPanel(); contrastPanel.Children.Add(pane); contrastPanel.Children.Add(darkBackground);
        var contrastWindow = new Window { Width = 480, Height = 260, Content = contrastPanel, ShowInTaskbar = false };
        contrastWindow.Show();
        try
        {
            contrastWindow.Activate(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); contrastWindow.UpdateLayout();
            var detach = Descendants(pane).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "점자 창 분리 또는 합치기");
            var close = Descendants(pane).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "점자 패널 숨기기");
            var header = (Border)pane.FindName("Header");
            var detachSurface = (Border)detach.Template.FindName("Surface", detach);
            var closeSurface = (Border)close.Template.FindName("Surface", close);
            var whiteSurface = (Border)customWhite.Template.FindName("Surface", customWhite);
            var detachText = (ContentPresenter)detachSurface.Child;
            var whiteText = (ContentPresenter)whiteSurface.Child;
            var before = System.Windows.Documents.TextElement.GetForeground(detachText);
            check(((SolidColorBrush)header.Background).Color == Color.FromRgb(249, 249, 249) && ((SolidColorBrush)before).Color == Color.FromRgb(26, 26, 26), "Actual braille panel detach text contrasts with its light Figma header");
            check(close.Content is SharpVectors.Converters.SvgViewbox && OpaquePixels(closeSurface) > 0, "Actual braille close SVG is visible on the light header");
            FigmaSystemContrast.SetIsHighContrast(detach, true); FigmaSystemContrast.SetIsHighContrast(close, true); FigmaSystemContrast.SetIsHighContrast(customWhite, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); contrastWindow.UpdateLayout();
            check(Equals(System.Windows.Documents.TextElement.GetForeground(detachText), before) && ((SolidColorBrush)detachSurface.Background).Color.A == 0, "High contrast preserves transparent detach text and its actual light header");
            check(Equals(System.Windows.Documents.TextElement.GetForeground(whiteText), Brushes.White) && ((SolidColorBrush)whiteSurface.Background).Color.A == 0 && Equals(darkBackground.Background, Brushes.Black), "High contrast preserves explicit white foreground on a dark parent");
            check(((SolidColorBrush)closeSurface.Background).Color.A == 0 && OpaquePixels(closeSurface) > 0, "High contrast leaves the actual fixed-color SVG visible on the light header");
            var transparentState = new FigmaButtonStateBrush();
            var hover = (SolidColorBrush)transparentState.Convert(new object[] { Brushes.Transparent, Brushes.Transparent, true, false, true, true }, typeof(Brush), "generic", CultureInfo.InvariantCulture);
            var pressed = (SolidColorBrush)transparentState.Convert(new object[] { Brushes.Transparent, Brushes.Transparent, true, true, true, true }, typeof(Brush), "generic", CultureInfo.InvariantCulture);
            check(hover.Color == Color.FromRgb(240, 240, 240) && pressed.Color == Color.FromRgb(234, 234, 234), "Transparent high-contrast buttons keep their existing hover and pressed surfaces");
            detach.Focus(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); contrastWindow.UpdateLayout();
            var focusOuter = (Border)detach.Template.FindName("FocusOuter", detach);
            var focusInner = (Border)detach.Template.FindName("FocusInner", detach);
            check(detach.IsKeyboardFocused && focusOuter.Visibility == Visibility.Visible && focusInner.Visibility == Visibility.Visible && Equals(focusOuter.BorderBrush, SystemColors.WindowTextBrush) && Equals(focusInner.BorderBrush, SystemColors.WindowBrush) &&
                Math.Max(Contrast(((SolidColorBrush)focusOuter.BorderBrush).Color, ((SolidColorBrush)header.Background).Color), Contrast(((SolidColorBrush)focusInner.BorderBrush).Color, ((SolidColorBrush)header.Background).Color)) >= 3,
                "High-contrast keyboard focus remains visible against the actual light header");
            FigmaSystemContrast.SetIsHighContrast(detach, false); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(Equals(System.Windows.Documents.TextElement.GetForeground(detachText), before), "Leaving high contrast restores the normal transparent text color");
        }
        finally { contrastWindow.Close(); }
        var forms = new[] { new BrailleForm("user:new", "새 양식", false, null, new(), new()), new BrailleForm("user:sample", "내 양식", false, null, new(), new()) };
        var picker = new FormPickerDialog(forms, null, canEdit: true); picker.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); picker.UpdateLayout();
            var edits = Descendants(picker).OfType<Button>().Where(b => b.Content is SharpVectors.Converters.SvgViewbox && System.Windows.Automation.AutomationProperties.GetName(b).EndsWith(" 편집", StringComparison.Ordinal)).ToArray();
            var disabled = edits.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "새 양식 편집");
            var enabled = edits.Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "내 양식 편집");
            static double ContentOpacity(Button b) => ((ContentPresenter)((Border)b.Template.FindName("Surface", b)).Child).Opacity;
            check(!disabled.IsEnabled && ContentOpacity(disabled) == .4, "The actual non-editable form's SVG button is visibly disabled");
            check(enabled.IsEnabled && ContentOpacity(enabled) == 1, "The actual editable form's SVG button retains full opacity");
        }
        finally { picker.Close(); }
        bool? RunEnter(bool edit)
        {
            var dialog = new FormPickerDialog(forms, forms[1], canEdit: true);
            Exception? failure = null; KeyEventArgs? key = null;
            dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    dialog.UpdateLayout();
                    var target = edit ? Descendants(dialog).OfType<Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "내 양식 편집") : (UIElement)dialog.ChoicesList;
                    key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    target.RaiseEvent(key);
                    if (dialog.IsVisible) dialog.Close();
                }
                catch (Exception ex) { failure = ex; dialog.Close(); }
            }));
            var result = dialog.ShowDialog();
            if (failure is not null) throw failure;
            check(key?.Handled == true && (edit ? dialog.EditRequest == forms[1] : dialog.EditRequest is null && dialog.Selected == forms[1]), edit ? "Enter on a form edit button invokes edit rather than selecting" : "Enter on the list selects the current form");
            return result;
        }
        check(RunEnter(true) == false, "The routed edit key closes with an edit request");
        check(RunEnter(false) == true, "The routed list key closes with the selected form");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static int OpaquePixels(FrameworkElement element)
    {
        var width = (int)Math.Ceiling(element.ActualWidth); var height = (int)Math.Ceiling(element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var count = 0; for (var index = 3; index < pixels.Length; index += 4) if (pixels[index] > 0) count++;
        return count;
    }
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color)
        {
            static double Linear(byte value) { var s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
            return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
        }
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static Color Pixel(FrameworkElement element, int x, int y)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }
}
