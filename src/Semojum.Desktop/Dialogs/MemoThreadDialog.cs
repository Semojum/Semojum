using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;
public sealed class MemoThreadDialog : CompactDialog
{
    public MemoThreadDialog(EditorBlock block, string panel, int line, string lineText, Action<Action> edit) : base($"{(panel == "TEXT" ? "텍스트" : "점자")} · {block.Page}쪽 {line + 1}줄 메모", 620, 530)
    {
        var body = new DockPanel { Margin = new(16) }; Body(body);
        var context = new TextBlock { Text = lineText, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) }; DockPanel.SetDock(context, Dock.Top); body.Children.Add(context);
        var input = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 120, MaxLength = 2000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(input, Dock.Bottom); body.Children.Add(input);
        var list = new ListBox { Margin = new(0, 0, 0, 12), DisplayMemberPath = "Body" }; body.Children.Add(list);
        void Refresh() { list.ItemsSource = block.Memos.Where(m => m.Panel == panel && m.LineIndex == line).OrderBy(m => m.CreatedAt).ToArray(); Status.Text = $"해결 {block.Memos.Count(m => m.Panel == panel && m.LineIndex == line && m.Resolved)}개 · 해결 상태는 .smj에 보관됩니다."; }
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is LineMemo m) input.Text = m.Body; };
        Action("새 메모", () => { list.SelectedItem = null; input.Clear(); input.Focus(); });
        Action("추가·수정", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("메모를 입력해 주세요.");
            var selected = list.SelectedItem as LineMemo;
            edit(() => { if (selected is not null) block.Memos.Remove(selected); block.Memos.Add(new(selected?.LocalId ?? Guid.NewGuid().ToString("N"), selected?.ServerId, panel, line, lineText[..Math.Min(80, lineText.Length)], input.Text.Trim(), selected?.CreatedAt ?? DateTimeOffset.Now, selected?.Resolved ?? false)); });
            input.Clear(); Refresh();
        }, true);
        Action("해결·다시 열기", () => { if (list.SelectedItem is LineMemo m) { edit(() => block.Memos[block.Memos.IndexOf(m)] = m with { Resolved = !m.Resolved }); Refresh(); } });
        Action("삭제", () => { if (list.SelectedItem is LineMemo m) { edit(() => block.Memos.Remove(m)); Refresh(); input.Clear(); } });
        Action("닫기", Close); Refresh();
    }
}
public sealed class MemoListDialog : CompactDialog
{
    private sealed record Item(EditorBlock Block, LineMemo Memo) { public string Label => $"{Block.Page}쪽 · {Memo.Panel} {Memo.LineIndex + 1}줄 · {(Memo.Resolved ? "해결" : "미해결")} · {Memo.Body}"; }
    public NavigationLocation? Destination { get; private set; }
    public MemoListDialog(DocumentSession model) : base("메모 목록", 680, 440)
    {
        var list = new ListBox { Margin = new(16), DisplayMemberPath = "Label", ItemsSource = model.Blocks.SelectMany(b => b.Memos.Select(m => new Item(b, m))).ToArray() }; Body(list);
        void Open()
        {
            if (list.SelectedItem is not Item item) return;
            var panel = item.Memo.Panel == "TEXT" ? SearchPanel.Text : SearchPanel.Braille;
            var offsets = DocumentNavigation.LineOffsets(DocumentSearch.Content(item.Block, panel));
            if (item.Memo.LineIndex >= offsets.Length) throw new ArgumentException("편집으로 메모의 원래 줄이 사라졌습니다. 블록에서 메모 위치를 확인해 주세요.");
            Destination = new(item.Block, panel, offsets[item.Memo.LineIndex]); DialogResult = true;
        }
        Action("이동", Open, true); Action("닫기", Close);
    }
}
