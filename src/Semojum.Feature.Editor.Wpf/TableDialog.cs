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
        var grid = new DataGrid { ItemsSource = table.DefaultView, AutoGenerateColumns = true, CanUserAddRows = true, CanUserDeleteRows = true, MinHeight = 180, Height = 260 };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        toolbar.Children.Add(Button("열 추가", () => { if (table.Columns.Count >= 100) return Task.CompletedTask; table.Columns.Add("열 " + Guid.NewGuid().ToString("N")[..4]); return Task.CompletedTask; }));
        toolbar.Children.Add(Button("선택 열 삭제", () => { if (grid.CurrentColumn is { } c && table.Columns.Count > 1) table.Columns.RemoveAt(c.DisplayIndex); return Task.CompletedTask; }));
        toolbar.Children.Add(new TextBlock { Text = "마지막 빈 줄에서 행을 추가합니다. 행 머리 선택 후 Delete로 삭제합니다.", TextWrapping = TextWrapping.Wrap, Width = 400, Margin = new(8, 0, 0, 0) });
        DockPanel.SetDock(grid, Dock.Top); root.Children.Add(grid);
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new("Segoe UI Symbol"), FontSize = 22, Margin = new(0, 12, 0, 0) }; root.Children.Add(preview);
        async Task Translate()
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true);
            SourceText = TaggedTable.Write(table.Rows.Cast<DataRow>().Where(r => r.RowState != DataRowState.Deleted).Select(r => r.ItemArray.Select(v => v?.ToString() ?? "")));
            Translation = await engine.TranslateElementAsync(SourceText, metadata with { Type = "table", TableSource = SourceText }, options, Lifetime.Token);
            Translation = Translation with { SourceText = SourceText }; preview.Text = Translation.Cells;
        }
        Action("점자 미리보기", Translate); Action("적용", async () => { await Translate(); DialogResult = true; }, true); Action("취소", () => DialogResult = false);
    }
}
