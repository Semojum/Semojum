using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;
public sealed class MemoThreadDialog : CompactDialog
{
    internal ListBox MemoItems { get; } = new() { Margin = new(0, 0, 0, 12) };
    internal CheckBox ShowResolved { get; } = new() { Content = "해결한 메모 포함", Margin = new(0, 0, 0, 10) };
    internal Button ResolveButton { get; }
    public NavigationLocation? Destination { get; private set; }
    public MemoThreadDialog(EditorBlock block, string panel, int line, string lineText, Action<Action> edit,
        DocumentSession? model = null, bool showResolved = false) : base($"{(panel == "TEXT" ? "텍스트" : "점자")} · {block.Page}쪽 {line + 1}줄 메모", 720, 560)
    {
        var body = new DockPanel { Margin = new(16) }; Body(body);
        var context = new TextBlock { Text = lineText, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(context, Dock.Top); body.Children.Add(context);
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, MaxLength = 2000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(input, Dock.Bottom); body.Children.Add(input);
        var replyLabel = new TextBlock { Text = "회신…", Margin = new(0, 0, 0, 4) }; DockPanel.SetDock(replyLabel, Dock.Bottom); body.Children.Add(replyLabel);
        System.Windows.Automation.AutomationProperties.SetName(input, "메모 회신");
        var toolbar = new DockPanel(); DockPanel.SetDock(toolbar, Dock.Top); body.Children.Add(toolbar);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(navigation, Dock.Right); toolbar.Children.Add(navigation);
        void Move(int delta)
        {
            if (model is null) return;
            var anchors = model.Blocks.SelectMany(b => b.Memos.Where(m => ShowResolved.IsChecked == true || !m.Resolved)
                .Select(m => (Block: b, m.Panel, m.LineIndex))).Distinct()
                .OrderBy(a => a.Block.Page).ThenBy(a => model.Blocks.IndexOf(a.Block)).ThenBy(a => a.Panel).ThenBy(a => a.LineIndex).ToList();
            var index = anchors.FindIndex(a => a.Block == block && a.Panel == panel && a.LineIndex == line);
            var next = index < 0 ? (delta > 0 ? 0 : anchors.Count - 1) : index + delta;
            if (next < 0 || next >= anchors.Count) { Status.Text = delta > 0 ? "마지막 메모입니다." : "첫 메모입니다."; return; }
            var a = anchors[next]; var targetPanel = a.Panel == "TEXT" ? SearchPanel.Text : SearchPanel.Braille;
            var offsets = DocumentNavigation.LineOffsets(DocumentSearch.Content(a.Block, targetPanel));
            if (a.LineIndex >= offsets.Length) { Status.Text = "원래 줄이 삭제된 메모입니다. 메모 모아보기에서 확인해 주세요."; return; }
            Destination = new(a.Block, targetPanel, offsets[a.LineIndex]); DialogResult = true;
        }
        navigation.Children.Add(Button("‹ 이전 메모", () => { Move(-1); return Task.CompletedTask; }));
        navigation.Children.Add(Button("다음 메모 ›", () => { Move(1); return Task.CompletedTask; }));
        ShowResolved.IsChecked = showResolved; toolbar.Children.Add(ShowResolved);
        var list = MemoItems; body.Children.Add(list);
        var bubble = new FrameworkElementFactory(typeof(Border)); bubble.SetValue(Border.BackgroundProperty, Brushes.White);
        bubble.SetValue(Border.BorderBrushProperty, Brushes.LightGray); bubble.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        bubble.SetValue(Border.CornerRadiusProperty, new CornerRadius(8)); bubble.SetValue(Border.PaddingProperty, new Thickness(12)); bubble.SetValue(Border.MarginProperty, new Thickness(2, 4, 2, 4));
        var stack = new FrameworkElementFactory(typeof(StackPanel)); bubble.AppendChild(stack);
        var date = new FrameworkElementFactory(typeof(TextBlock)); date.SetBinding(TextBlock.TextProperty, new Binding("CreatedAt") { StringFormat = "{0:yyyy-MM-dd HH:mm}" });
        date.SetValue(TextBlock.ForegroundProperty, Brushes.Gray); stack.AppendChild(date);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetBinding(TextBlock.TextProperty, new Binding("Body"));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); stack.AppendChild(text);
        list.ItemTemplate = new DataTemplate { VisualTree = bubble };
        void Refresh()
        {
            var selected = (list.SelectedItem as LineMemo)?.LocalId;
            list.ItemsSource = block.Memos.Where(m => m.Panel == panel && m.LineIndex == line && (ShowResolved.IsChecked == true || !m.Resolved)).OrderBy(m => m.CreatedAt).ToArray();
            list.SelectedItem = list.Items.Cast<LineMemo>().FirstOrDefault(m => m.LocalId == selected);
            Status.Text = $"해결 {block.Memos.Count(m => m.Panel == panel && m.LineIndex == line && m.Resolved)}개 · 해결 상태는 .smj에 보관됩니다.";
        }
        ShowResolved.Checked += (_, _) => Refresh(); ShowResolved.Unchecked += (_, _) => Refresh();
        Action("회신 추가", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("메모를 입력해 주세요.");
            edit(() => block.Memos.Add(new(Guid.NewGuid().ToString("N"), null, panel, line, lineText[..Math.Min(80, lineText.Length)], input.Text.Trim(), DateTimeOffset.Now)));
            input.Clear(); Refresh();
        }, true);
        Action("선택 수정", () =>
        {
            if (list.SelectedItem is not LineMemo m) return;
            var dialog = new TextValueDialog("메모 수정", "본문", m.Body) { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Value)) return;
            if (dialog.Value.Trim().Length > 2000) throw new ArgumentException("메모는 2,000자까지 입력할 수 있습니다.");
            edit(() => block.Memos[block.Memos.IndexOf(m)] = m with { Body = dialog.Value.Trim() }); Refresh();
        });
        ResolveButton = Action("해결·다시 열기", () => { if (list.SelectedItem is LineMemo m) { edit(() => block.Memos[block.Memos.IndexOf(m)] = m with { Resolved = !m.Resolved }); Refresh(); } });
        Action("삭제", () => { if (list.SelectedItem is LineMemo m) { edit(() => block.Memos.Remove(m)); Refresh(); input.Clear(); } });
        Action("닫기", Close); Refresh();
    }
}
public sealed class MemoListDialog : CompactDialog
{
    private sealed record Item(EditorBlock Block, LineMemo Memo) { public string Label => $"{Block.Page}쪽 · {Memo.Panel} {Memo.LineIndex + 1}줄 · {(Memo.Resolved ? "해결" : "미해결")} · {Memo.Body}"; }
    public NavigationLocation? Destination { get; private set; }
    public bool IncludeResolved { get; private set; }
    internal ListBox MemoItems { get; } = new() { Margin = new(0, 8, 0, 0), DisplayMemberPath = "Label" };
    internal CheckBox ShowResolved { get; } = new() { Content = "해결한 메모 포함" };
    public MemoListDialog(DocumentSession model) : base("메모 목록", 680, 440)
    {
        var body = new DockPanel { Margin = new(16) }; Body(body); DockPanel.SetDock(ShowResolved, Dock.Top); body.Children.Add(ShowResolved);
        var list = MemoItems; body.Children.Add(list);
        void Refresh() => list.ItemsSource = model.Blocks.OrderBy(b => b.Page).SelectMany(b => b.Memos
            .Where(m => ShowResolved.IsChecked == true || !m.Resolved).OrderBy(m => m.LineIndex).ThenBy(m => m.CreatedAt).Select(m => new Item(b, m))).ToArray();
        ShowResolved.Checked += (_, _) => Refresh(); ShowResolved.Unchecked += (_, _) => Refresh();
        void Open()
        {
            if (list.SelectedItem is not Item item) return;
            var panel = item.Memo.Panel == "TEXT" ? SearchPanel.Text : SearchPanel.Braille;
            var offsets = DocumentNavigation.LineOffsets(DocumentSearch.Content(item.Block, panel));
            if (item.Memo.LineIndex >= offsets.Length) throw new ArgumentException("편집으로 메모의 원래 줄이 사라졌습니다. 블록에서 메모 위치를 확인해 주세요.");
            IncludeResolved = ShowResolved.IsChecked == true;
            Destination = new(item.Block, panel, offsets[item.Memo.LineIndex]); DialogResult = true;
        }
        Action("이동", Open, true); Action("닫기", Close); Refresh();
    }
}
