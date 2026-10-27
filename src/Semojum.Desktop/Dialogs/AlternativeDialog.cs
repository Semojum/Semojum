using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

public sealed class AlternativeDialog : CompactDialog
{
    public int SelectedIndex { get; private set; }
    public string SourceText { get; private set; } = "";
    public BrailleTranslation? Translation { get; private set; }
    internal List<RadioButton> DraftChoices { get; } = [];
    public AlternativeDialog(EditorBlock block, BrailleOptions settings, LocalBrailleEngine engine)
        : this(block, (source, metadata, token) => engine.TranslateElementAsync(source, metadata, settings, token)) { }

    internal AlternativeDialog(EditorBlock block, Func<string, BlockMetadata, CancellationToken, Task<BrailleTranslation>> translate)
        : base("대체 초안", 660, 270, popover: true)
    {
        var drafts = block.Metadata.Alternatives ?? [];
        var panel = new Grid(); Body(panel);
        foreach (var height in new[] { 27d, 144, 80 }) panel.RowDefinitions.Add(new() { Height = new(height) });
        panel.RowDefinitions.Add(new());
        var header = new DockPanel { Margin = new(11, 1, 11, 1) };
        var titleBar = new Border { Background = Brush("#FAFAFA"), BorderBrush = Brush("#D6D6D6"), BorderThickness = new(0, 0, 0, 1), Child = header };
        panel.Children.Add(titleBar);
        void Accept() { if (Translation is not null) DialogResult = true; }
        var close = Button("✕", () => { if (Translation is null) Close(); else Accept(); return Task.CompletedTask; });
        close.MinWidth = close.Width = close.Height = close.MinHeight = 24; close.Padding = new(0); close.Margin = new(8, 0, 0, 0);
        close.Background = close.BorderBrush = Brushes.Transparent; close.Foreground = Brush("#5F646D");
        close.ToolTip = "선택한 초안을 반영하고 닫기 (Enter) · 선택 취소 (Esc)";
        System.Windows.Automation.AutomationProperties.SetName(close, "선택한 초안 반영하고 닫기");
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var toggle = Button("", () => Task.CompletedTask);
        toggle.MinWidth = toggle.Width = toggle.Height = toggle.MinHeight = 24; toggle.Padding = new(3); toggle.Margin = new(0);
        toggle.BorderBrush = Brush("#005FB8"); toggle.Background = Brushes.White; toggle.Content = SvgIcon("draft-braille.svg", 16);
        toggle.ToolTip = "점자로 보기"; System.Windows.Automation.AutomationProperties.SetName(toggle, "점자로 보기");
        DockPanel.SetDock(toggle, Dock.Right); header.Children.Add(toggle);
        header.Children.Add(new TextBlock { Text = $"대체 초안 · 그림 설명 {drafts.Length}안", FontSize = 12, FontWeight = FontWeights.SemiBold });

        var choices = new StackPanel { Margin = new(15, 0, 15, 0) };
        var scroll = new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = 12, Padding = new(11, 9, 11, 9), BorderThickness = new(0), Background = Brush("#CCE4F7") };
        var previewHost = new Grid { Margin = new(15, 0, 15, 0) }; Grid.SetRow(previewHost, 2); panel.Children.Add(previewHost); previewHost.Children.Add(preview);
        TextBlock.SetLineHeight(preview, 20); TextBlock.SetLineStackingStrategy(preview, LineStackingStrategy.BlockLineHeight);
        var cells = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalContentAlignment = VerticalAlignment.Top,
            FontSize = 22, FontFamily = new("Segoe UI Symbol"), Padding = new(11, 5, 11, 5), BorderThickness = new(0), Background = preview.Background, Visibility = Visibility.Collapsed };
        previewHost.Children.Add(cells);
        var showBraille = false;
        toggle.Click += (_, _) =>
        {
            showBraille = !showBraille; preview.Visibility = showBraille ? Visibility.Collapsed : Visibility.Visible; cells.Visibility = showBraille ? Visibility.Visible : Visibility.Collapsed;
            toggle.ToolTip = showBraille ? "묵자로 보기" : "점자로 보기";
            System.Windows.Automation.AutomationProperties.SetName(toggle, toggle.ToolTip.ToString());
        };
        for (var i = 0; i < drafts.Length; i++)
        {
            var index = i; var draft = drafts[i];
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = SvgIcon("draft-radio.svg", 14); icon.Margin = new(0, 0, 8, 0); row.Children.Add(icon);
            var label = new TextBlock { Text = $"{(i < 20 ? char.ConvertFromUtf32(0x2460 + i) : (i + 1).ToString())} {draft.Label}", FontSize = 14 }; row.Children.Add(label);
            var radio = new RadioButton { Content = row, Height = 32, GroupName = "AlternativeDraft", ToolTip = draft.Reason,
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)FindResource("DraftChoice") };
            System.Windows.Automation.AutomationProperties.SetName(radio, draft.Label);
            radio.Checked += async (_, _) => await RunAsync(async () =>
            {
                SelectedIndex = index; Translation = null; SourceText = draft.TaggedText ?? draft.Text;
                preview.Text = SourceText; preview.ToolTip = draft.Reason; cells.Text = "점역 중…";
                Translation = draft.TaggedText is null && draft.LegacyBraille is not null ? BrailleTranslation.Plain(draft.LegacyBraille) :
                    await translate(SourceText, block.Metadata with { Type = SourceText.Contains("<!표>") ? "table" : "caption" }, Lifetime.Token);
                Translation = Translation with { SourceText = SourceText }; cells.Text = Translation.Cells;
            });
            radio.Checked += (_, _) => { icon.Source = Asset("draft-radio-selected.svg"); label.FontWeight = FontWeights.SemiBold; };
            radio.Unchecked += (_, _) => { icon.Source = Asset("draft-radio.svg"); label.FontWeight = FontWeights.Normal; };
            radio.MouseDoubleClick += (_, e) => { Accept(); e.Handled = true; };
            DraftChoices.Add(radio); choices.Children.Add(radio);
        }
        Loaded += (_, _) => { if (DraftChoices.Count > 0) DraftChoices[Math.Clamp(block.Metadata.SelectedIndex ?? 0, 0, DraftChoices.Count - 1)].IsChecked = true; };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Accept(); e.Handled = true; } };
    }
    private static Brush Brush(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static Uri Asset(string name) => new($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}");
    private static SvgViewbox SvgIcon(string name, double size) => new() { Source = Asset(name), Width = size, Height = size, Stretch = Stretch.None };
}
