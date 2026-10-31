using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SharpVectors.Converters;

namespace Semojum.Desktop.Controls;

/// <summary>Empty original panel file picker and drop target.</summary>
internal sealed class OriginalUploadView : UserControl
{
    internal Button UploadArea { get; } = new();
    private readonly Border limitWarning = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock limitTitle = new();
    internal event Action<string[]?>? UploadRequested;
    internal OriginalUploadView()
    {
        var root = new Grid { Background = Brushes.White };
        root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        Content = root;
        var region = UploadArea;
        region.Margin = new Thickness(16, 20, 16, 16); region.Height = 388;
        region.VerticalAlignment = VerticalAlignment.Top;
        region.Padding = new Thickness(0);
        region.BorderThickness = new Thickness(0);
        region.Background = Brushes.Transparent;
        region.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        region.VerticalContentAlignment = VerticalAlignment.Stretch;
        region.AllowDrop = true;
        region.ToolTip = "파일을 선택하거나 이 영역에 놓으세요";
        System.Windows.Automation.AutomationProperties.SetName(region, "원본 파일 선택 또는 놓기");
        var template = new ControlTemplate(typeof(Button));
        template.VisualTree = new FrameworkElementFactory(typeof(ContentPresenter));
        region.Template = template;
        var surface = new Grid();
        surface.Children.Add(new Border { Background = ColorBrush(0xF7, 0xF9, 0xFC), CornerRadius = new CornerRadius(10) });
        surface.Children.Add(new Rectangle { Stroke = ColorBrush(0x00, 0x5F, 0xB8), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 6, 4 }, RadiusX = 10, RadiusY = 10, IsHitTestVisible = false });
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 65, 0, 0), IsHitTestVisible = false };
        content.Children.Add(Icon("original-upload.svg", 32));
        content.Children.Add(new TextBlock { Text = "파일을 드래그하거나\n클릭해서 선택하세요", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = ColorBrush(0x1A, 0x1A, 0x1A), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 16, 0, 0), LineHeight = 22 });
        content.Children.Add(new TextBlock { Text = "업로드 전체 최대 100MB", FontSize = 12, Foreground = ColorBrush(0x5F, 0x5F, 0x5F), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
        content.Children.Add(new TextBlock { Text = "업로드 가능한 형식: PDF, PNG, JPG, JPEG,\nTXT, HWP, HWPX, SMJ", FontSize = 12, Foreground = ColorBrush(0x5F, 0x5F, 0x5F), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), LineHeight = 18 });
        content.Children.Add(new Border { Background = ColorBrush(0x5B, 0x8D, 0xEF), CornerRadius = new CornerRadius(6), Width = 140, Height = 40, Margin = new Thickness(0, 16, 0, 0), Child = new TextBlock { Text = "파일 선택", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        surface.Children.Add(content); region.Content = surface;
        var contents = new StackPanel(); contents.Children.Add(region);
        limitWarning.Margin = new Thickness(16, 0, 16, 20); limitWarning.Height = 52;
        limitWarning.Background = ColorBrush(0xFD, 0xE7, 0xE9); limitWarning.BorderBrush = ColorBrush(0xC4, 0x2B, 0x1C);
        limitWarning.BorderThickness = new Thickness(1); limitWarning.CornerRadius = new CornerRadius(6);
        var warningGrid = new Grid();
        warningGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        warningGrid.ColumnDefinitions.Add(new ColumnDefinition());
        var warningIcon = Icon("original-upload-warning.svg", 16); warningIcon.VerticalAlignment = VerticalAlignment.Top; warningIcon.Margin = new Thickness(8, 10, 0, 0);
        warningGrid.Children.Add(warningIcon);
        var warningText = new StackPanel { Margin = new Thickness(0, 5, 8, 0) };
        limitTitle.FontSize = 14; limitTitle.FontWeight = FontWeights.Bold; limitTitle.Foreground = ColorBrush(0xC4, 0x2B, 0x1C); limitTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        warningText.Children.Add(limitTitle);
        warningText.Children.Add(new TextBlock { Text = "업로드 전체 100MB를 초과했습니다", FontSize = 12, Foreground = ColorBrush(0xC4, 0x2B, 0x1C) });
        Grid.SetColumn(warningText, 1); warningGrid.Children.Add(warningText); limitWarning.Child = warningGrid;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(limitWarning, System.Windows.Automation.AutomationLiveSetting.Assertive);
        contents.Children.Add(limitWarning);
        root.Children.Add(new ScrollViewer { Content = contents, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DataContextChanged += (_, _) => ClearLimitWarning();
        region.Click += (_, _) => UploadRequested?.Invoke(null);
        region.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        region.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) UploadRequested?.Invoke(paths); e.Handled = true; };
        var footer = new Border { BorderBrush = ColorBrush(0xEA, 0xEA, 0xEA), BorderThickness = new Thickness(0, 1, 0, 0), Background = ColorBrush(0xFB, 0xFB, 0xFB) };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(Icon("original-upload-prev.svg", 16));
        bar.Children.Add(new Border { Width = 44, Height = 24, BorderBrush = ColorBrush(0xEA, 0xEA, 0xEA), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Background = Brushes.White, Margin = new Thickness(12, 0, 8, 0), Child = new TextBlock { Text = "—", HorizontalAlignment = HorizontalAlignment.Center } });
        bar.Children.Add(new TextBlock { Text = "/ —", Foreground = ColorBrush(0x5F, 0x5F, 0x5F), Margin = new Thickness(0, 0, 20, 0) });
        bar.Children.Add(Icon("original-upload-next.svg", 16)); footer.Child = bar;
        Grid.SetRow(footer, 1); root.Children.Add(footer);
    }
    internal void ShowLimitWarning(string[] paths, long totalBytes)
    {
        var name = paths.Length == 1 ? System.IO.Path.GetFileName(paths[0]) : $"파일 {paths.Length}개";
        limitTitle.Text = $"{name} ({Math.Ceiling(totalBytes / (1024d * 1024d)):0}MB)";
        limitWarning.Visibility = Visibility.Visible;
    }
    internal void ClearLimitWarning() => limitWarning.Visibility = Visibility.Collapsed;
    private static SolidColorBrush ColorBrush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
    private static SvgViewbox Icon(string file, double size) => new() { Source = new Uri($"pack://application:,,,/Semojum.Feature.Editor.Wpf;component/Assets/Figma/{file}"), Width = size, Height = size, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center };
}
