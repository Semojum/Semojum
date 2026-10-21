using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class RuleDialog : CompactDialog
{
    private sealed record RuleRow(string RuleId, string Source, string Section, string Name, string Contents);
    public RuleDialog(SemojumApiClient api, RuleEvidence[] evidence, string selectedSource, string selectedBraille,
        string? initialQuery = null) : base("점자 규정 검색", 820, 620)
    {
        var root = new DockPanel { Margin = new(16) }; Body(root);
        if (initialQuery is null && (selectedSource.Length > 0 || selectedBraille.Length > 0))
        {
            var excerpt = new TextBlock { Text = $"묵자: {selectedSource}\n점자: {selectedBraille}", TextWrapping = TextWrapping.Wrap, MaxHeight = 64, Margin = new(0, 0, 0, 10) };
            DockPanel.SetDock(excerpt, Dock.Top); root.Children.Add(excerpt);
        }
        var filters = new DockPanel { Margin = new(0, 0, 0, 8) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        var query = new TextBox { Text = initialQuery ?? "", MaxLength = 100, MinHeight = 24 };
        System.Windows.Automation.AutomationProperties.SetName(query, "규정 검색어");
        var document = Choices(["전체 문서", "한국 점자 규정", "점자 도서 제작 지침", "점자 자료 제작 지침"], "전체 문서");
        document.Width = 180; document.Margin = new(8, 0, 0, 0); DockPanel.SetDock(document, Dock.Right);
        System.Windows.Automation.AutomationProperties.SetName(document, "규정 문서 필터");
        var results = new StackPanel(); root.Children.Add(new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var page = 0; var totalPages = 1;
        Button previous = null!, next = null!;
        previous = Action("이전 쪽", async () => { if (page > 0) { page--; await SearchAsync(); } });
        next = Action("다음 쪽", async () => { if (page + 1 < totalPages) { page++; await SearchAsync(); } });
        void Display(IEnumerable<RuleRow> rows)
        {
            results.Children.Clear(); Expander? expanded = null;
            foreach (var row in rows)
            {
                var header = new StackPanel();
                header.Children.Add(new TextBlock { Text = row.Section + " · " + row.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                header.Children.Add(new TextBlock { Text = row.Contents, MaxHeight = 36, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
                header.Children.Add(new TextBlock { Text = row.Source, Foreground = Brushes.Gray });
                var item = new Expander { Header = header, Content = new TextBlock { Text = row.Contents, TextWrapping = TextWrapping.Wrap, Margin = new(24, 12, 12, 12) }, Margin = new(0, 4, 0, 8), Background = Brushes.White };
                System.Windows.Automation.AutomationProperties.SetName(item, row.Section + " " + row.Name);
                item.Expanded += (_, _) => { if (expanded != item) { if (expanded is not null) expanded.IsExpanded = false; expanded = item; } };
                results.Children.Add(item);
            }
            if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "검색 결과가 없습니다.", Margin = new(8) });
        }
        async Task SearchAsync()
        {
            var path = $"/api/rules?page={page}&size=20";
            if (!string.IsNullOrWhiteSpace(query.Text)) path += "&q=" + Uri.EscapeDataString(query.Text.Trim());
            var code = document.SelectedIndex switch { 1 => "MCST", 2 => "NLD", 3 => "NISE", _ => "" };
            if (code.Length > 0) path += "&publisher=" + code;
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, path, cancellationToken: Lifetime.Token);
            totalPages = data.GetProperty("totalPages").GetInt32();
            string Field(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            Display(data.GetProperty("items").EnumerateArray().Select(row => new RuleRow(Field(row, "ruleId"), Field(row, "source"), Field(row, "section"), Field(row, "ruleName"), Field(row, "contents"))));
            previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < totalPages;
            Status.Text = $"{data.GetProperty("totalElements")}건 · {page + 1}/{Math.Max(1, totalPages)}쪽 · 현재 제공 본문은 발췌입니다. 항목을 펼쳐 확인할 수 있습니다.";
        }
        var search = Button("검색", () => RunAsync(async () => { page = 0; await SearchAsync(); })); DockPanel.SetDock(search, Dock.Right);
        filters.Children.Add(search); filters.Children.Add(document); filters.Children.Add(query);
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(async () => { page = 0; await SearchAsync(); }); } };
        document.SelectionChanged += async (_, _) => await RunAsync(async () => { page = 0; await SearchAsync(); });
        void AppliedRules()
        {
            Display(evidence.Select(r => new RuleRow(r.RuleId, r.Source, r.Section, r.Title, r.Excerpt)));
            previous.IsEnabled = next.IsEnabled = false;
            Status.Text = evidence.Length == 0 ? "이 블록에 전달된 적용 규정이 없습니다." : "원문 수신 당시의 적용 규정입니다. 검색으로 현재 본문을 확인할 수 있습니다.";
        }
        if (initialQuery is null && evidence.Length > 0) AppliedRules(); else OnLoad(SearchAsync);
        Action("적용 규정", AppliedRules); Action("닫기", Close);
    }
}
