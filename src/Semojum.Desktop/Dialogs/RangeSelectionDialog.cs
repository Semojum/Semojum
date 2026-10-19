using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class RangeSelectionDialog : CompactDialog
{
    public RangeSelectionDialog(Func<EditorBlock[]> blocks, SearchPanel panel, Func<BlockSpan[], string, Task> replace)
        : base("여러 줄·블록 구간 선택", 850, 650)
    {
        var editor = new TextEditor { IsReadOnly = true, ShowLineNumbers = true, WordWrap = true,
            FontFamily = new(panel == SearchPanel.Text ? "Malgun Gothic" : "Segoe UI Symbol"), FontSize = panel == SearchPanel.Text ? 16 : 22, Padding = new(12) };
        editor.Text = BlockRange.Text(blocks(), panel);
        var hint = new TextBlock { Text = "드래그 또는 Shift+클릭으로 블록의 일부부터 다음 블록까지 선택하세요. 복사·잘라내기·붙여넣기·삭제를 적용하며, 창을 닫은 뒤 Ctrl+Z로 되돌릴 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new(16, 8, 16, 8) };
        DockPanel.SetDock(hint, Dock.Top); Root.Children.Add(hint); Body(editor);
        async Task Edit(string value)
        {
            var start = editor.SelectionStart; var spans = BlockRange.Select(blocks(), panel, start, editor.SelectionLength);
            await replace(spans, value); editor.Text = BlockRange.Text(blocks(), panel);
            editor.CaretOffset = Math.Clamp(start + value.Length, 0, editor.Document.TextLength); editor.Focus();
        }
        Task Copy() { if (editor.SelectionLength > 0) Clipboard.SetText(editor.SelectedText); return Task.CompletedTask; }
        async Task Cut() { if (editor.SelectionLength == 0) return; await Copy(); await Edit(""); }
        Task Paste() => Clipboard.ContainsText() ? Edit(Clipboard.GetText()) : Task.CompletedTask;
        Action("복사", Copy); Action("잘라내기", Cut); Action("붙여넣기", Paste);
        Action("삭제", () => editor.SelectionLength == 0 ? Task.CompletedTask : Edit("")); Action("닫기", Close, true);
        editor.PreviewKeyDown += async (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.C or Key.X or Key.V)
            { e.Handled = true; await RunAsync(e.Key == Key.C ? Copy : e.Key == Key.X ? Cut : Paste); }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Delete && editor.SelectionLength > 0)
            { e.Handled = true; await RunAsync(() => Edit("")); }
        };
    }
}
