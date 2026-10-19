using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class AlternativeDialog : CompactDialog
{
    public int SelectedIndex { get; private set; }
    public string SourceText { get; private set; } = "";
    public BrailleTranslation? Translation { get; private set; }
    public AlternativeDialog(EditorBlock block, BrailleOptions settings, LocalBrailleEngine engine) : base("대체 초안", 660, 440)
    {
        var drafts = block.Metadata.Alternatives ?? [];
        var panel = new DockPanel { Margin = new(16) }; Body(panel);
        var list = new ListBox { ItemsSource = drafts.Select((d, i) => $"{i + 1}. {d.Label}").ToArray(), Height = 130 };
        DockPanel.SetDock(list, Dock.Top); panel.Children.Add(list);
        var reason = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) }; DockPanel.SetDock(reason, Dock.Top); panel.Children.Add(reason);
        var preview = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Background = new SolidColorBrush(Color.FromRgb(204, 228, 247)), MinHeight = 70 };
        DockPanel.SetDock(preview, Dock.Top); panel.Children.Add(preview);
        var cells = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 22, FontFamily = new("Segoe UI Symbol"), Margin = new(0, 8, 0, 0) }; panel.Children.Add(cells);
        list.SelectionChanged += async (_, _) => await RunAsync(async () =>
        {
            Translation = null;
            if (list.SelectedIndex < 0) return;
            SelectedIndex = list.SelectedIndex; var draft = drafts[SelectedIndex];
            SourceText = draft.TaggedText ?? draft.Text;
            preview.Text = SourceText; reason.Text = draft.Reason ?? ""; cells.Text = "점역 중…";
            Translation = draft.TaggedText is null && draft.LegacyBraille is not null ? BrailleTranslation.Plain(draft.LegacyBraille) :
                await engine.TranslateElementAsync(SourceText, block.Metadata with { Type = SourceText.Contains("<!표>") ? "table" : "caption" }, settings, Lifetime.Token);
            Translation = Translation with { SourceText = SourceText };
            cells.Text = Translation.Cells;
        });
        Action("넣기", () => { if (Translation is not null) DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
        Loaded += (_, _) => list.SelectedIndex = Math.Clamp(block.Metadata.SelectedIndex ?? 0, 0, Math.Max(0, drafts.Length - 1));
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
