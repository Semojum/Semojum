using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;

namespace Semojum.Desktop.Services;

internal static class WindowsQa1110TableChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        // Structure controls must work without invoking translation.
        await using var sidecar = new BrailleSidecar(new("unused-structure-test-engine", []));
        var dialog = new TableDialog(TaggedTable.Write([["가", "나"], ["다", "라"]]), new(Type: "table"), new(), new(sidecar));
        dialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var grid = Descendants(dialog).OfType<DataGrid>().Single();
            var table = ((DataView)grid.ItemsSource).Table!;
            Button? Find(string name) => Descendants(dialog).OfType<Button>().FirstOrDefault(b => Equals(b.Content, name));
            async Task Click(string name)
            {
                var button = Find(name);
                check(button is not null, "QA1110: table provides " + name);
                button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            }
            await Click("열 추가");
            check(table.Columns.Count == 3 && grid.Columns.Count == 3, $"QA1110: added column appears immediately (model={table.Columns.Count}, view={grid.Columns.Count})");
            // Reordered columns must delete the selected data field, not its visual index.
            grid.Columns[0].DisplayIndex = grid.Columns.Count - 1;
            grid.CurrentCell = new DataGridCellInfo(table.DefaultView[0], grid.Columns[0]);
            await Click("선택 열 삭제");
            check(table.Columns.Count == 2 && grid.Columns.Count == 2 && (string)table.Rows[0][0] == "나",
                "QA1110: deleting reordered column removes its matching data field and refreshes grid");
            await Click("행 추가");
            check(table.Rows.Count == 3 && grid.Items.OfType<DataRowView>().Count() == 3, "QA1110: added row appears immediately");
            grid.SelectedItem = table.DefaultView[1];
            await Click("선택 행 삭제");
            check(table.Rows.Count == 2 && grid.Items.OfType<DataRowView>().Count() == 2, "QA1110: deleted row disappears immediately");
        }
        finally { dialog.Close(); }
        var window = new MainWindow(smoke: true, testWithoutAccount: true); window.Show();
        try
        {
            var block = window.Session.ActiveBlock!;
            block.Memos.Add(new("qa-reply", null, "TEXT", 0, "본문", "메모", DateTimeOffset.Now));
            var memo = new MemoThreadDialog(block, "TEXT", 0, "본문", window.EditStructure, window.Session) { Owner = window };
            memo.Show();
            try
            {
                memo.ReplyInput.Text = "한글 회신 ABC"; memo.ReplyInput.Focus();
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); memo.UpdateLayout();
                var input = memo.ReplyInput;
                var scroll = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
                var glyph = input.GetRectFromCharacterIndex(0);
                check(!glyph.IsEmpty && glyph.Height > 10 && glyph.Top >= 0 && glyph.Bottom <= input.ActualHeight,
                    $"QA1110: reply glyph fits input (glyph={glyph}, inputHeight={input.ActualHeight})");
                check(scroll.ViewportHeight >= glyph.Height && scroll.ComputedVerticalScrollBarVisibility != Visibility.Visible,
                    $"QA1110: one-line reply has readable viewport and no stray scrollbar (viewport={scroll.ViewportHeight}, glyph={glyph.Height}, scrollbar={scroll.ComputedVerticalScrollBarVisibility})");
                await WindowsSmoke.RenderElementAsync(memo, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa1110-memo.png"));
            }
            finally { memo.Close(); }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }

    internal static async Task RunPreviewAsync(Action<bool, string> check)
    {
        await using var sidecar = EngineRuntime.Create();
        var dialog = new TableDialog(TaggedTable.Write([["가", "나"], ["다", "라"]]), new(Type: "table"), new(), new(sidecar));
        dialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = Descendants(dialog).OfType<DataGrid>().Single();
            var view = (DataView)grid.ItemsSource;
            void Click(string name) => Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, name))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("열 추가"); Click("행 추가");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            view[2][2] = "추가한 칸";
            var expected = TaggedTable.Write(view.Table!.Rows.Cast<DataRow>().Select(row => row.ItemArray.Select(v => v?.ToString() ?? "")));
            Click("점자 미리보기");
            for (var i = 0; dialog.Translation is null && i < 300; i++) await Task.Delay(50);
            check(dialog.SourceText == expected && dialog.Translation is { Cells.Length: > 0 } && dialog.Translation.SourceText == expected,
                "QA1110: preview translates the edited 3x3 table, including the new cell");
            var preview = Descendants(dialog).OfType<TextBox>().Single(t => t.IsReadOnly);
            check(preview.Text == dialog.Translation?.Cells, "QA1110: displayed braille preview matches edited table translation");
            Click("행 추가");
            check(dialog.Translation is null && preview.Text == "", "QA1110: structure changes invalidate the old braille preview");
        }
        finally { dialog.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
