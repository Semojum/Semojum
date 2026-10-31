using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

/// <summary>A prepared update can be applied only after the user chooses to restart.</summary>
public sealed class UpdateReadyDialog : CompactDialog
{
    public Button RestartButton { get; }
    public Button DismissButton { get; }
    public UpdateReadyDialog(Func<Task<bool>> restart) : base("업데이트 준비 완료", 580, 80, popover: true)
    {
        var layout = new Grid();
        foreach (var width in new[] { 4d, 60d, -1d, 87d, 8d, 84d, 16d })
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(width) });
        layout.Children.Add(new Border { Background = Brush("#005FB8") });
        var icon = new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Feature.Shell.Wpf;component/Assets/Figma/update-ready-recon.svg"), Width = 28, Height = 28, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(icon, 1); layout.Children.Add(icon);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = "새 버전을 준비했습니다.", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Brush("#1A1A1A"), TextTrimming = TextTrimming.CharacterEllipsis });
        labels.Children.Add(new TextBlock { Text = "문서를 저장하고 다시 시작하면 업데이트됩니다.", FontSize = 12, Foreground = Brush("#5F5F5F"), TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(labels, 2); layout.Children.Add(labels);
        RestartButton = new Button { Content = "다시 시작", Width = 87, Height = 32, MinHeight = 32, Padding = new(0), Background = Brush("#005FB8"), BorderBrush = Brush("#005FB8"), Foreground = Brushes.White, IsDefault = true };
        DismissButton = new Button { Content = "취소", Width = 84, Height = 32, MinHeight = 32, Padding = new(0), IsCancel = true };
        Grid.SetColumn(RestartButton, 3); Grid.SetColumn(DismissButton, 5); layout.Children.Add(RestartButton); layout.Children.Add(DismissButton);
        DismissButton.Click += (_, _) => Close();
        RestartButton.Click += async (_, _) =>
        {
            RestartButton.IsEnabled = DismissButton.IsEnabled = false;
            try { if (await restart() && IsVisible) Close(); }
            catch (Exception ex) { Status.Text = "업데이트를 시작하지 못했습니다: " + ex.Message; }
            finally { RestartButton.IsEnabled = DismissButton.IsEnabled = true; }
        };
        Body(layout);
    }
    private static SolidColorBrush Brush(string value) => (SolidColorBrush)new BrushConverter().ConvertFromString(value)!;
}
