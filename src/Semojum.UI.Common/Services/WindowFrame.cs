using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shell;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

/// <summary>Figma's 32 DIP title bar, with native Windows move/resize/system commands.</summary>
public static class WindowFrame
{
    public static void Apply(Window window, bool editor = false, bool mainEditor = false, bool help = false)
    {
        // Other editor-framed windows have their own designs; node 263:6661 is the main editor.
        mainEditor = editor && mainEditor;
        window.Style ??= (Style)window.FindResource(typeof(Window));
        var content = window.Content;
        window.Content = null;
        window.WindowStyle = WindowStyle.None;
        WindowBounds.UseMonitorWorkArea(window);
        window.UseLayoutRounding = true;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 32, GlassFrameThickness = new(0), CornerRadius = new(mainEditor ? 8 : 0),
            ResizeBorderThickness = new(window.ResizeMode == ResizeMode.NoResize ? 0 : 4),
            UseAeroCaptionButtons = false
        });
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = new(32) });
        root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        var heading = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, editor ? 0 : 4, editor ? 0 : 8, editor ? 0 : 4) };
        DockPanel.SetDock(buttons, Dock.Right); heading.Children.Add(buttons);
        // Every editor-framed window (main, detached braille, help, editor dialogs) uses the Figma 46×32 SVG caption buttons.
        Button Caption(string glyph, string name, Action action, string asset)
        {
            object icon = editor ? new SvgViewbox
            {
                Source = new Uri($"pack://application:,,,/Semojum.UI.Common;component/Assets/Figma/editor-caption-{asset}.svg"),
                Width = 16, Height = 16, Stretch = Stretch.None
            } : glyph;
            var button = new Button { Content = icon, Width = editor ? 46 : 24, Height = editor ? 32 : 20,
                MinHeight = 0, MinWidth = 0, Padding = new(0), FontSize = 12, FontFamily = (FontFamily)window.FindResource("MonoFont"),
                Style = (Style)window.FindResource(editor ? "ToolButton" : typeof(Button)),
                Margin = new(0), Background = editor ? Brushes.Transparent : Brushes.White,
                BorderBrush = editor ? Brushes.Transparent : (Brush)window.FindResource("Line"),
                Foreground = (Brush)window.FindResource("Muted"), ToolTip = name };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            WindowChrome.SetIsHitTestVisibleInChrome(button, true);
            button.Click += (_, _) => action(); buttons.Children.Add(button); return button;
        }
        if (editor)
        {
            Caption("—", "최소화", () => SystemCommands.MinimizeWindow(window), "min");
            Caption("□", "최대화 / 복원", () => { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); }, "max");
            // Figma: the main family shows a 16px rounded "S" (#005FB8); help shows an 18×16 square "ND" (#0F7B0F).
            var logo = new Border { Width = help ? 18 : 16, Height = 16,
                CornerRadius = help ? new(0) : new(4),
                Background = new SolidColorBrush(help ? Color.FromRgb(15, 123, 15) : Color.FromRgb(0, 95, 184)),
                Margin = new(12, 0, 8, 0),
                Child = new TextBlock { Text = help ? "ND" : "S", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 12,
                    Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center } };
            if (mainEditor) System.Windows.Automation.AutomationProperties.SetAutomationId(logo, "EditorCaptionLogo");
            DockPanel.SetDock(logo, Dock.Left); heading.Children.Add(logo);
        }
        Caption("✕", "닫기", () => SystemCommands.CloseWindow(window), "close");
        var title = new TextBlock { FontSize = 12, FontWeight = editor ? FontWeights.Normal : FontWeights.SemiBold,
            Margin = new(editor ? 0 : 12, 0, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        if (mainEditor) title.Foreground = new SolidColorBrush(Color.FromRgb(95, 95, 95));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window }); heading.Children.Add(title);
        // Final Figma rules editor-framed titles (help 263:10632) with #EAEAEA; compact dialogs keep a #D1D1D1 outline.
        root.Children.Add(new Border { Background = new SolidColorBrush(editor ? Color.FromRgb(243, 243, 243) : Color.FromRgb(251, 251, 251)),
            BorderBrush = new SolidColorBrush(editor ? Color.FromRgb(234, 234, 234) : Color.FromRgb(209, 209, 209)), BorderThickness = new(0, 0, 0, mainEditor ? 0 : 1), Child = heading });
        if (mainEditor)
        {
            // The close button opts into client hit testing. Reserve the outer resize
            // border so it cannot mask the native top-right resize target.
            var resizeEdge = new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Right, Background = Brushes.Transparent };
            System.Windows.Automation.AutomationProperties.SetAutomationId(resizeEdge, "EditorCaptionResizeEdge");
            var style = new Style(typeof(Border));
            var maximized = new DataTrigger { Binding = new Binding(nameof(Window.WindowState)) { Source = window }, Value = WindowState.Maximized };
            maximized.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            style.Triggers.Add(maximized); resizeEdge.Style = style;
            root.Children.Add(resizeEdge);
        }
        var presenter = new ContentPresenter { Content = content }; Grid.SetRow(presenter, 1); root.Children.Add(presenter);
        if (!editor)
        {
            var outline = new Border { BorderBrush = (Brush)window.FindResource("Muted"),
                BorderThickness = new(1, 0, 1, 0), Margin = new(0, 0, 0, 40), IsHitTestVisible = false };
            Grid.SetRow(outline, 1); root.Children.Add(outline);
        }
        window.Content = root;
    }
}
