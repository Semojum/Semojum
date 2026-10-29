using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SharpVectors.Converters;
using WpfMath.Controls;
using WpfMath.Parsers;

namespace Semojum.Desktop.Controls;

/// <summary>Read-only rendering of the selected formula; editing remains in the formula dialog.</summary>
public sealed class MathPreviewDock : Border
{
    private readonly FormulaControl formula = new() { FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock error = new() { Text = "수식을 표시할 수 없습니다. LaTeX 원문을 확인해 주세요.", Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBox latex = new() { IsReadOnly = true, Height = 32, MinHeight = 32, Padding = new(11, 5, 11, 5), VerticalContentAlignment = VerticalAlignment.Center, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button edit = new() { Content = "편집", Width = 44, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), ToolTip = "수식 편집기로 열기" };
    public event Action? EditRequested;
    public event Action? CloseRequested;
    public MathPreviewDock()
    {
        Height = 212; Background = Brushes.White; BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 209)); BorderThickness = new(1);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new(28) }); root.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) }); Child = root;
        var heading = new Border { Background = new SolidColorBrush(Color.FromRgb(243, 243, 243)), BorderBrush = new SolidColorBrush(Color.FromRgb(234, 234, 234)), BorderThickness = new(0, 0, 0, 1) };
        root.Children.Add(heading);
        var header = new DockPanel { Margin = new(11, 0, 11, 0) }; heading.Child = header;
        var close = new Button { Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0), ToolTip = "수식 미리보기 닫기", Style = (Style)FindResource("PanelButton") };
        close.Content = new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/math-dock-close.svg"), Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false };
        System.Windows.Automation.AutomationProperties.SetName(close, "수식 미리보기 닫기"); close.Click += (_, _) => CloseRequested?.Invoke();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        edit.Style = (Style)FindResource("PanelButton"); System.Windows.Automation.AutomationProperties.SetName(edit, "수식 편집기로 열기"); edit.Click += (_, _) => EditRequested?.Invoke();
        DockPanel.SetDock(edit, Dock.Right); header.Children.Add(edit);
        header.Children.Add(new TextBlock { Text = "수식 · LaTeX", FontFamily = new FontFamily("Malgun Gothic"), FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var body = new StackPanel { Margin = new(15, 10, 15, 12) };
        var visual = new Grid { Height = 72 }; visual.Children.Add(formula); visual.Children.Add(error); body.Children.Add(visual);
        body.Children.Add(new TextBlock { Text = "LaTeX 원문", Foreground = new SolidColorBrush(Color.FromRgb(95, 95, 95)), FontSize = 12, Margin = new(0, 10, 0, 2) });
        System.Windows.Automation.AutomationProperties.SetName(latex, "선택한 수식 LaTeX 원문"); body.Children.Add(latex);
        var scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
    }
    public void ShowFormula(string value, bool canEdit)
    {
        latex.Text = value; edit.IsEnabled = canEdit; error.Text = "수식을 표시할 수 없습니다. LaTeX 원문을 확인해 주세요.";
        try
        {
            WpfTeXFormulaParser.Instance.Parse(value);
            formula.Formula = value; formula.Visibility = Visibility.Visible; error.Visibility = Visibility.Collapsed;
        }
        catch (Exception)
        {
            formula.Formula = ""; formula.Visibility = Visibility.Collapsed; error.Visibility = Visibility.Visible;
        }
    }
    public void ShowInvalidMarkup()
    {
        latex.Text = ""; edit.IsEnabled = false; formula.Formula = ""; formula.Visibility = Visibility.Collapsed;
        error.Text = "수식 태그가 중첩되었거나 닫히지 않았습니다. 원문을 확인해 주세요.";
        error.Visibility = Visibility.Visible;
    }
}
