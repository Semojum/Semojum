using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class SymbolDialog : CompactDialog
{
    public BrailleSymbol? Selected { get; private set; }
    public SymbolDialog(BrailleSymbol[] symbols) : base("특수 기호 넣기", 560, 440)
    {
        var panel = new DockPanel { Margin = new(16) }; Body(panel);
        var group = Choices(new[] { "전체" }.Concat(symbols.Select(s => s.Group).Distinct()), "전체"); DockPanel.SetDock(group, Dock.Top); panel.Children.Add(group);
        var list = new ListBox { Margin = new(0, 8, 0, 0), FontSize = 18 }; panel.Children.Add(list);
        void Filter() { list.ItemsSource = symbols.Where(s => group.SelectedIndex == 0 || s.Group == group.SelectedItem.ToString()).Select(s => new ListBoxItem { Content = $"{s.Char}    {s.Cells}    {s.Group}", Tag = s }).ToArray(); }
        group.SelectionChanged += (_, _) => Filter(); Filter();
        void Accept() { if (list.SelectedItem is ListBoxItem { Tag: BrailleSymbol symbol }) { Selected = symbol; DialogResult = true; } }
        list.MouseDoubleClick += (_, _) => Accept(); Action("넣기", Accept, true); Action("취소", () => DialogResult = false);
    }
}

public sealed class SnippetDialog : CompactDialog
{
    public AccountPreferences Result { get; private set; }
    public SnippetDialog(AccountPreferences current) : base("상용구 설정", 620, 520)
    {
        Result = current; var tabs = new TabControl { Margin = new(16) }; Body(tabs);
        var inputs = new Dictionary<string, TextBox[]>();
        foreach (var (label, prefix, slots) in new[] { ("텍스트", "Ctrl", current.TextSnippets), ("점자", "Alt", current.BrailleSnippets) })
        {
            var panel = new StackPanel(); inputs[label] = Enumerable.Range(0, 10).Select(i => new TextBox { Text = slots?[i] ?? "", MaxLength = 10000, AcceptsReturn = true, MinHeight = 40, MaxHeight = 110, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }).ToArray();
            for (var i = 0; i < 10; i++) Row(panel, $"{prefix}+{i}", inputs[label][i], 85);
            tabs.Items.Add(new TabItem { Header = label, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }
        Action("저장", () =>
        {
            Result = current with { TextSnippets = inputs["텍스트"].Select(t => t.Text).ToArray(), BrailleSnippets = inputs["점자"].Select(t => t.Text).ToArray() };
            AccountPreferenceStore.Validate(Result); DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
    }
}
