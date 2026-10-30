using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Semojum.Core;

namespace Semojum.Desktop.Controls;

/// <summary>Displays the progress belonging to one document tab.</summary>
internal sealed class TabConversionProgress : Grid, IDisposable
{
    private readonly DocumentSession model;
    private readonly Button trigger;
    private readonly Button cancel;
    private readonly Popup popup;
    private readonly Border card;
    private readonly TextBlock title;
    private readonly TextBlock counts;
    private readonly TextBlock estimate;
    private readonly Border fill;
    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool cancelPending;
    private bool suppressFocusOpen;

    public TabConversionProgress(DocumentSession model, Func<Task> cancelJob)
    {
        this.model = model;
        trigger = new Button { Content = "변환 중", FontSize = 11, Width = 48, Height = 20, MinHeight = 20,
            Padding = new(0), BorderThickness = new(0), Background = Brushes.Transparent,
            Foreground = (Brush)Application.Current.FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(trigger, "TabConversionProgress");
        title = new TextBlock { FontSize = 14, Height = 20, FontWeight = FontWeights.Bold, TextTrimming = TextTrimming.CharacterEllipsis };
        var track = new Grid { Height = 8, Margin = new(0, 8, 0, 8), ClipToBounds = true };
        track.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(234, 234, 234)), BorderThickness = new(1), CornerRadius = new(4) });
        fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new(4), Background = (Brush)Application.Current.FindResource("Accent") };
        track.Children.Add(fill);
        counts = new TextBlock { FontSize = 12, Height = 16, TextTrimming = TextTrimming.CharacterEllipsis };
        estimate = new TextBlock { FontSize = 12, Height = 16, Margin = new(0, 4, 0, 0), Foreground = (Brush)Application.Current.FindResource("Muted") };
        cancel = new Button { Content = "취소", Width = 76, Height = 32, MinHeight = 32, Padding = new(0),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var detail = new Grid { Height = 36 };
        detail.ColumnDefinitions.Add(new ColumnDefinition());
        detail.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
        var text = new StackPanel(); text.Children.Add(counts); text.Children.Add(estimate);
        detail.Children.Add(text); Grid.SetColumn(cancel, 1); detail.Children.Add(cancel);
        var content = new StackPanel(); content.Children.Add(title); content.Children.Add(track); content.Children.Add(detail);
        card = new Border { Width = 320, Height = 108, Padding = new(15, 11, 15, 0), CornerRadius = new(8),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 8, Direction = 270, Opacity = 0.14 },
            Background = new SolidColorBrush(Color.FromRgb(251, 251, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(234, 234, 234)), BorderThickness = new(1), Child = content };
        AutomationProperties.SetAutomationId(card, "TabProgressCard");
        System.Windows.Documents.TextElement.SetFontWeight(card, FontWeights.Normal);
        var shadowBase = new Border { CornerRadius = new(8), Background = card.Background, IsHitTestVisible = false,
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 2, ShadowDepth = 0, Opacity = 0.1 } };
        var popupLayers = new Grid { Width = 320, Height = 108, Margin = new(20) };
        popupLayers.Children.Add(shadowBase); popupLayers.Children.Add(card);
        popup = new Popup { Placement = PlacementMode.Custom, PlacementTarget = trigger, AllowsTransparency = true,
            StaysOpen = true, Child = popupLayers };
        popup.CustomPopupPlacementCallback = (size, target, _) =>
        {
            // Custom popup placement receives screen coordinates, including in mixed DPI contexts.
            var origin = trigger.PointToScreen(new Point());
            var extent = trigger.PointToScreen(new Point(100, 100));
            var scaleX = Math.Abs(extent.X - origin.X) / 100;
            var scaleY = Math.Abs(extent.Y - origin.Y) / 100;
            var x = -68 * scaleX; // 48px Figma alignment plus 20px reserved for the shadow.
            return [new(new Point(x, target.Height - 20 * scaleY), PopupPrimaryAxis.Vertical),
                    new(new Point(x, -size.Height - 20 * scaleY), PopupPrimaryAxis.Vertical)];
        };
        Children.Add(trigger); Children.Add(popup);
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); if (!trigger.IsMouseOver && !card.IsMouseOver && !trigger.IsKeyboardFocusWithin && !card.IsKeyboardFocusWithin) popup.IsOpen = false; };
        void Open() { closeTimer.Stop(); if (model.Progress is not null) popup.IsOpen = true; }
        void ScheduleClose() { closeTimer.Stop(); closeTimer.Start(); }
        trigger.MouseEnter += (_, _) => Open(); trigger.MouseLeave += (_, _) => ScheduleClose();
        trigger.GotKeyboardFocus += (_, _) => { if (!suppressFocusOpen) Open(); }; trigger.LostKeyboardFocus += (_, _) => ScheduleClose();
        trigger.Click += (_, e) => { e.Handled = true; Open(); };
        card.MouseEnter += (_, _) => closeTimer.Stop(); card.MouseLeave += (_, _) => ScheduleClose();
        card.LostKeyboardFocus += (_, _) => ScheduleClose();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { popup.IsOpen = false; e.Handled = true; } };
        card.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { suppressFocusOpen = true; popup.IsOpen = false; trigger.Focus(); suppressFocusOpen = false; e.Handled = true; } };
        cancel.Click += async (_, e) =>
        {
            e.Handled = true; cancelPending = true; Refresh();
            try { await cancelJob(); }
            finally { cancelPending = false; Refresh(); }
        };
        Refresh();
    }

    public void Refresh()
    {
        var progress = model.Progress;
        trigger.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        if (progress is null) { popup.IsOpen = false; return; }
        var total = Math.Max(0, progress.Total);
        var finished = Math.Clamp(progress.Finished, 0, total);
        var percent = total == 0 ? 0 : (int)Math.Round(100d * finished / total, MidpointRounding.AwayFromZero);
        title.Text = model.Title;
        counts.Text = $"{finished}쪽 / {total}쪽 처리 ({percent}%)" + (progress.Failed > 0 ? $" · 확인 필요 {progress.Failed}" : "");
        estimate.Text = progress.Estimate;
        fill.Width = 288d * percent / 100;
        cancel.IsEnabled = !cancelPending && model.JobId is not null;
        AutomationProperties.SetName(trigger, $"{model.Title} 변환 진행: {counts.Text}, {progress.Estimate}");
    }

    public void Dispose() { closeTimer.Stop(); popup.IsOpen = false; }
}
