using System.Data;
using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;
public sealed class TableDialog : CompactDialog
{
    public string SourceText { get; private set; } = "";
    public BrailleTranslation? Translation { get; private set; }
    public TableDialog(string source, BlockMetadata metadata, BrailleOptions options, LocalBrailleEngine engine) : base("표 구조 편집", 760, 620)
    {
        var cells = TaggedTable.Read(source); var table = new DataTable();
        for (var i = 0; i < cells.Max(r => r.Length); i++) table.Columns.Add($"열 {i + 1}");
        foreach (var row in cells) table.Rows.Add(row.Cast<object>().ToArray());
        var root = new DockPanel { Margin = new(16) }; Body(root);
        var grid = new DataGrid { ItemsSource = table.DefaultView, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false, MinHeight = 180, Height = 260 };
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = (System.Windows.Media.FontFamily)FindResource("BrailleFont"), FontSize = 22, Margin = new(0, 12, 0, 0) };
        bool Commit() => grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);
        void RefreshColumns()
        {
            // WPF does not regenerate DataView columns when its schema changes.
            grid.ItemsSource = null;
            grid.Columns.Clear();
            grid.ItemsSource = table.DefaultView;
        }
        var toolbar = new WrapPanel { Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        void StructureButton(string label, System.Action change)
        {
            toolbar.Children.Add(Button(label, () =>
            {
                try { if (Commit()) { change(); Translation = null; preview.Clear(); } }
                catch (Exception ex) { Status.Text = ex.Message; }
                return Task.CompletedTask;
            }));
        }
        StructureButton("열 추가", () =>
        {
            if (table.Columns.Count >= 100) return;
            var number = 1;
            while (table.Columns.Contains("열 " + number)) number++;
            table.Columns.Add("열 " + number);
            RefreshColumns();
        });
        StructureButton("선택 열 삭제", () =>
        {
            if (grid.CurrentColumn is not { } column || table.Columns.Count <= 1) return;
            // DisplayIndex changes when the user reorders columns; the binding field does not.
            var name = column.SortMemberPath;
            if (!table.Columns.Contains(name)) return;
            table.Columns.Remove(name);
            RefreshColumns();
        });
        StructureButton("행 추가", () =>
        {
            if (table.Rows.Count >= 100) return;
            var row = table.NewRow(); table.Rows.Add(row);
            grid.SelectedItem = table.DefaultView[table.Rows.IndexOf(row)];
            grid.ScrollIntoView(grid.SelectedItem);
        });
        StructureButton("선택 행 삭제", () =>
        {
            if (grid.SelectedItem is DataRowView row && table.Rows.Count > 1) table.Rows.Remove(row.Row);
        });
        DockPanel.SetDock(grid, Dock.Top); root.Children.Add(grid);
        root.Children.Add(preview);
        async Task Translate()
        {
            if (!Commit()) throw new InvalidOperationException("편집 중인 셀 내용을 확인해 주세요.");
            SourceText = TaggedTable.Write(table.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted).Select(r => r.ItemArray.Select(v => v?.ToString() ?? "")));
            Translation = await engine.TranslateElementAsync(SourceText, metadata with { Type = "table", TableSource = SourceText }, options, Lifetime.Token);
            Translation = Translation with { SourceText = SourceText }; preview.Text = Translation.Cells;
        }
        Action("점자 미리보기", Translate); Action("적용", async () => { await Translate(); DialogResult = true; }, true); Action("취소", () => DialogResult = false);
    }
}
