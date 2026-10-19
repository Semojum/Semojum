using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class RuleDialog : CompactDialog
{
    private sealed record RuleRow(string RuleId, string Publisher, string Section, string Name, string Contents);
    public RuleDialog(SemojumApiClient api, RuleEvidence[] evidence, string selectedSource, string selectedBraille) : base("점자 규정", 760, 540)
    {
        var root = new DockPanel { Margin = new(16) }; Body(root);
        var excerpt = new TextBlock { Text = $"묵자: {selectedSource}\n점자: {selectedBraille}", TextWrapping = TextWrapping.Wrap, MaxHeight = 80, Margin = new(0, 0, 0, 10) }; DockPanel.SetDock(excerpt, Dock.Top); root.Children.Add(excerpt);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        var query = new TextBox { Width = 180, MaxLength = 100, MinHeight = 24 };
        var publisher = Choices(["전체 기관", "문화체육관광부", "국립장애인도서관", "국립특수교육원"], "전체 기관"); publisher.Width = 160;
        var part = Choices(["전체 편", "기본", "한글", "수학", "과학", "외국어"], "전체 편"); part.Width = 100;
        filters.Children.Add(query); filters.Children.Add(publisher); filters.Children.Add(part);
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, RowHeight = 32 };
        foreach (var (header, property, width) in new[] { ("규정", "Name", 120d), ("조항", "Section", 220d), ("발췌", "Contents", 340d) })
            grid.Columns.Add(new DataGridTextColumn { Header = header, Binding = new Binding(property), Width = width });
        root.Children.Add(grid);
        int page = 0, totalPages = 1;
        async Task SearchAsync()
        {
            var code = publisher.SelectedIndex switch { 1 => "MCST", 2 => "NLD", 3 => "NISE", _ => "" };
            var path = $"/api/rules?page={page}&size=20";
            if (!string.IsNullOrWhiteSpace(query.Text)) path += "&q=" + Uri.EscapeDataString(query.Text.Trim());
            if (code.Length > 0) path += "&publisher=" + code;
            if (publisher.SelectedIndex == 1 && part.SelectedIndex > 0) path += "&part=" + Uri.EscapeDataString(part.SelectedItem.ToString()!);
            var result = await api.SendAuthenticatedAsync(HttpMethod.Get, path, cancellationToken: Lifetime.Token);
            totalPages = result.GetProperty("totalPages").GetInt32();
            grid.ItemsSource = result.GetProperty("items").EnumerateArray().Select(r => new RuleRow(
                r.GetProperty("ruleId").GetString()!, r.GetProperty("publisher").GetString()!, r.GetProperty("section").GetString()!,
                r.GetProperty("ruleName").GetString()!, r.GetProperty("contents").GetString()!)).ToArray();
            Status.Text = $"{page + 1}/{Math.Max(1, totalPages)}쪽 · {result.GetProperty("totalElements")}건 · 서버 제공 본문은 첫 문장 발췌입니다.";
        }
        filters.Children.Add(Button("검색", async () => await RunAsync(async () => { page = 0; await SearchAsync(); })));
        grid.MouseDoubleClick += (_, _) => { if (grid.SelectedItem is RuleRow row) MessageBox.Show(this, $"{row.Publisher}\n{row.Section}\n{row.Contents}\n\n{row.RuleId}", row.Name); };
        if (evidence.Length > 0)
            grid.ItemsSource = evidence.Select(r => new RuleRow(r.RuleId, r.Source, r.Section, r.Title, r.Excerpt)).ToArray();
        else OnLoad(SearchAsync);
        Action("적용 규정", () => { grid.ItemsSource = evidence.Select(r => new RuleRow(r.RuleId, r.Source, r.Section, r.Title, r.Excerpt)).ToArray(); Status.Text = evidence.Length == 0 ? "이 블록에 전달된 적용 규정이 없습니다." : "원문 수신 당시의 적용 규정입니다."; });
        Action("선택 규정 검색", async () => { if (grid.SelectedItem is RuleRow row) { query.Text = row.RuleId; page = 0; await SearchAsync(); } });
        Action("이전", async () => { if (page > 0) { page--; await SearchAsync(); } });
        Action("다음", async () => { if (page + 1 < totalPages) { page++; await SearchAsync(); } });
        Action("닫기", () => Close());
    }
}
