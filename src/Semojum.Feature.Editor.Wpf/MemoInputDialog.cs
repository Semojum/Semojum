using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class MemoInputDialog : CompactDialog
{
    internal TextBox Input { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 2000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top, Height = 60, Padding = new(7), Margin = new(0, 8, 0, 6) };
    public string Value => Input.Text.Trim();
    public MemoInputDialog(string? author = null) : base("메모", 300, 150, popover: true)
    {
        MinHeight = 150; MinWidth = 300;
        MemoTextInputAppearance.Attach(this, Input);
        var panel = new StackPanel { Margin = new(11) }; Body(panel);
        panel.Children.Add(MemoAuthor.Header(author)); panel.Children.Add(Input);
        System.Windows.Automation.AutomationProperties.SetName(Input, "메모 내용");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var save = Action("저장", () => { if (Value.Length == 0) throw new ArgumentException("메모를 입력해 주세요."); DialogResult = true; }, true);
        InputBindings.Add(new KeyBinding(ApplicationCommands.Save, Key.Enter, ModifierKeys.Control) { CommandTarget = this });
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, e) => { save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; }));
        var cancel = Action("취소", () => DialogResult = false);
        foreach (var button in new[] { save, cancel })
        { Actions.Children.Remove(button); button.MinWidth = button.Width = 56; button.Height = 32; buttons.Children.Add(button); }
        Loaded += (_, _) => Input.Focus();
    }

    internal void ApplyInputAppearance(bool highContrast)
    {
        MemoTextInputAppearance.Apply(Input, highContrast);
    }
}

internal static class MemoTextInputAppearance
{
    internal static void Attach(Window owner, TextBox input)
    {
        Apply(input, SystemParameters.HighContrast);
        System.ComponentModel.PropertyChangedEventHandler systemChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast))
                owner.Dispatcher.BeginInvoke(new Action(() => Apply(input, SystemParameters.HighContrast)));
        };
        SystemParameters.StaticPropertyChanged += systemChanged;
        owner.Closed += (_, _) => SystemParameters.StaticPropertyChanged -= systemChanged;
    }

    internal static void Apply(TextBox input, bool highContrast) => input.Template = InputTemplate(highContrast);

    private static ControlTemplate InputTemplate(bool highContrast)
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        surface.SetValue(Border.BorderBrushProperty, EditorModalDesign.Color("#D1D1D1"));
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(UIElement.FocusableProperty, false);
        host.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(Control.Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        host.SetBinding(ScrollViewer.VerticalScrollBarVisibilityProperty, new Binding { Path = new PropertyPath("(0)", ScrollViewer.VerticalScrollBarVisibilityProperty), RelativeSource = RelativeSource.TemplatedParent });
        host.SetBinding(ScrollViewer.HorizontalScrollBarVisibilityProperty, new Binding { Path = new PropertyPath("(0)", ScrollViewer.HorizontalScrollBarVisibilityProperty), RelativeSource = RelativeSource.TemplatedParent });
        surface.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = surface };
        foreach (var enabled in new[] { true, false })
        {
            var colors = new Trigger { Property = UIElement.IsEnabledProperty, Value = enabled };
            colors.Setters.Add(new Setter(Control.BackgroundProperty, highContrast ? new DynamicResourceExtension(SystemColors.WindowBrushKey) : Brushes.White));
            if (highContrast)
            {
                colors.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(SystemColors.WindowTextBrushKey)));
                colors.Setters.Add(new Setter(TextBox.SelectionBrushProperty, new DynamicResourceExtension(SystemColors.HighlightBrushKey)));
            }
            template.Triggers.Add(colors);
        }
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, highContrast ? SystemColors.HighlightBrush : EditorModalDesign.Color("#005FB8"), "Surface"));
        template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        if (highContrast) disabled.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(SystemColors.GrayTextBrushKey)));
        else disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5));
        template.Triggers.Add(disabled);
        return template;
    }
}

internal static class MemoAuthor
{
    internal static Grid Avatar(string? author, bool reply = false)
    {
        var avatar = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Top };
        avatar.Children.Add(new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Feature.Editor.Wpf;component/Assets/Figma/{(reply ? "memo-reply-author" : "memo-author")}.svg"), Width = 20, Height = 20, Stretch = Stretch.None });
        avatar.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(author) ? "?" : System.Globalization.StringInfo.GetNextTextElement(author.Trim()), Foreground = Brushes.White, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return avatar;
    }
    internal static DockPanel Header(string? author)
    {
        var row = new DockPanel { Height = 20 }; var avatar = Avatar(author); avatar.Margin = new(0, 0, 8, 0); row.Children.Add(avatar);
        row.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(author) ? "작성자 미상" : author, FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
