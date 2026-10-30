using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
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
        var stretch = new Button { Width = 130, Height = 32, Content = nested, Template = ExportRangeDialog.SurfaceTemplate(4, true) };
        var input = new TextBox { Text = "첫 줄\n두 번째 줄", AcceptsReturn = true, Height = 60, Width = 180 };
        EditorModalDesign.Input(input);
        var plain = new TextBox { Text = "경계 없음", BorderThickness = new Thickness(0) }; EditorModalDesign.Input(plain); plain.BorderThickness = new Thickness(0);
        var genericBlue = new Button { Content = "다시 시작", Background = (Brush)new BrushConverter().ConvertFromString("#005FB8")!, BorderBrush = Brushes.Red };
        var genericIcon = new Button { Content = "×", Background = Brushes.Transparent, BorderBrush = Brushes.Transparent };
        var genericCustom = new Button { Content = "사용자 색", Background = Brushes.Purple };
        var panel = new StackPanel(); foreach (var element in new FrameworkElement[] { button, primary, stretch, input, plain, genericBlue, genericIcon, genericCustom }) panel.Children.Add(element);
        var window = new Window { Width = 300, Height = 300, Content = panel, ShowInTaskbar = false };
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
            check(blueSurface.Opacity == .4 && ((SolidColorBrush)blueSurface.Background).Color.ToString() == "#FF005FB8", "The actual disabled blue button retains its color and disabled opacity");
            check(plain.Template.FindName("Underline", plain) is Border { Visibility: Visibility.Collapsed } && plain.Template.FindName("FocusUnderline", plain) is Border { Visibility: Visibility.Collapsed }, "Borderless inputs remain borderless");
            input.Text += "!"; check(input.Text.EndsWith("!", StringComparison.Ordinal), "Input text remains writable");
            if (Environment.GetEnvironmentVariable("INDIVIDUAL_STATE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(window, capture);
        }
        finally { window.Close(); }
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
}
