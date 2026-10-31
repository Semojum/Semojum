using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class SaveAsDialog : CompactDialog
{
    private readonly TextBox name;
    private readonly ComboBox format;
    public string FileName { get; private set; } = "";
    public int Format => format.SelectedIndex;
    public SaveAsDialog(string title, string? currentPath) : base("다른 이름으로 저장", 520, 284, popover: true)
    {
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#EAEAEA"); frame.Background = Brush("#F3F3F3"); }
        Root.Children.Remove(Status);
        var layoutRoot = new Grid(); layoutRoot.RowDefinitions.Add(new() { Height = new(55) });
        layoutRoot.RowDefinitions.Add(new() { Height = new(164) }); layoutRoot.RowDefinitions.Add(new() { Height = new(64) }); Body(layoutRoot);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = "다른 이름으로 저장", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new System.Windows.Controls.Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = DesignIcon("save-as-close.svg", 16, 16), Template = SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (!close.IsMouseOver && e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        layoutRoot.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 16, 24, 0) }; Grid.SetRow(body, 1); layoutRoot.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "파일 이름", FontSize = 12, Foreground = Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
        name = new TextBox { Text = Path.GetFileNameWithoutExtension(title) };
        name.Height = name.MinHeight = 32; name.Padding = new(11, 0, 11, 0); name.VerticalContentAlignment = VerticalAlignment.Center;
        name.Background = Brushes.White; name.BorderBrush = Brush("#EAEAEA"); name.Margin = new(0, 0, 0, 16);
        name.Template = NameInputTemplate();
        AutomationProperties.SetName(name, "파일 이름"); body.Children.Add(name);
        body.Children.Add(new TextBlock { Text = "저장 설정", FontSize = 12, Foreground = Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
        format = Choices(["세모점 작업 파일 (*.smj)", "점자 파일 (*.brf)", "원문 텍스트 (*.txt)"]); format.SelectedIndex = 0;
        var form = new Grid { Margin = new(16, 10, 16, 10) }; form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        form.Children.Add(new TextBlock { Text = "파일 형식", FontSize = 14, Foreground = Brush("#1A1A1A"), VerticalAlignment = VerticalAlignment.Center });
        var slot = new Grid { Width = 260, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
        format.Height = format.MinHeight = 32; format.Padding = new(11, 0, 27, 0); format.VerticalContentAlignment = VerticalAlignment.Center;
        format.Background = Brush("#FBFBFB"); format.BorderBrush = Brush("#EAEAEA"); AutomationProperties.SetName(format, "파일 형식"); slot.Children.Add(format);
        slot.Children.Add(new Border { Width = 22, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var chevron = DesignIcon("save-as-chevron.svg", 16, 16); chevron.HorizontalAlignment = HorizontalAlignment.Right; chevron.Margin = new(0, 0, 5, 0); chevron.IsHitTestVisible = false; slot.Children.Add(chevron);
        Grid.SetColumn(slot, 1); form.Children.Add(slot); body.Children.Add(new Border { Height = 52, CornerRadius = new(6), Background = Brush("#F9F9F9"), Child = form });
        var footer = new Grid { Margin = new(24, 16, 24, 16) }; Grid.SetRow(footer, 2); layoutRoot.Children.Add(footer);
        Status.Margin = new(0); Status.MaxWidth = 265; Status.HorizontalAlignment = HorizontalAlignment.Left; footer.Children.Add(Status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; });
        var save = Button("저장", () =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("파일 이름을 입력해 주세요.");
                var picker = new SaveFileDialog { Title = "저장할 위치 선택", FileName = name.Text.Trim(), AddExtension = true, OverwritePrompt = true,
                    Filter = "세모점 작업 파일|*.smj|BRF 점자문서|*.brf|원문 텍스트|*.txt", FilterIndex = format.SelectedIndex + 1,
                    DefaultExt = format.SelectedIndex switch { 1 => ".brf", 2 => ".txt", _ => ".smj" } };
                if (currentPath is not null) picker.InitialDirectory = Path.GetDirectoryName(currentPath);
                if (picker.ShowDialog(this) == true) { FileName = picker.FileName; format.SelectedIndex = picker.FilterIndex - 1; DialogResult = true; }
            }
            catch (Exception ex) { Status.Text = ex.Message; }
            return Task.CompletedTask;
        }, true);
        foreach (var button in new[] { cancel, save }) { button.Width = 88; button.MinWidth = 0; button.Height = button.MinHeight = 32; button.Template = SurfaceTemplate(4); actions.Children.Add(button); }
        cancel.BorderBrush = Brush("#EAEAEA");
        Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
    }
    private static ControlTemplate NameInputTemplate()
    {
        var surface = new FrameworkElementFactory(typeof(Border));
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        surface.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Background)) { RelativeSource = RelativeSource.TemplatedParent });
        surface.SetBinding(Border.BorderBrushProperty, new Binding(nameof(BorderBrush)) { RelativeSource = RelativeSource.TemplatedParent });
        var grid = new FrameworkElementFactory(typeof(Grid));
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(host);
        var focusLine = new FrameworkElementFactory(typeof(Border), "FocusLine");
        focusLine.SetValue(FrameworkElement.HeightProperty, 2d);
        focusLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom);
        focusLine.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        grid.AppendChild(focusLine); surface.AppendChild(grid);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = surface };
        var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#005FB8"), "FocusLine"));
        template.Triggers.Add(focus);
        return template;
    }
}
