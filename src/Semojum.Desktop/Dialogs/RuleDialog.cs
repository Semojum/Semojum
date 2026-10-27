using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class RuleDialog : CompactDialog
{
    internal StackPanel RuleResults { get; } = new();
    public RuleDialog(SemojumApiClient api, RuleEvidence[] evidence, string selectedSource, string selectedBraille,
        string? initialQuery = null) : base("점자 규정 검색", 720, 700)
    {
        var root = new DockPanel { Margin = new(16) }; Body(root);
        if (initialQuery is null && (selectedSource.Length > 0 || selectedBraille.Length > 0))
        {
            var excerpt = new TextBlock { Text = $"묵자: {selectedSource}\n점자: {selectedBraille}", TextWrapping = TextWrapping.Wrap, MaxHeight = 64, Margin = new(0, 0, 0, 10) };
            DockPanel.SetDock(excerpt, Dock.Top); root.Children.Add(excerpt);
        }
        var filters = new StackPanel { Margin = new(0, 0, 20, 16) }; DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);
        var query = new TextBox { Text = initialQuery ?? "", MaxLength = 100, Height = 32, Padding = new(31, 0, 7, 0) };
        System.Windows.Automation.AutomationProperties.SetName(query, "규정 검색어");
        var document = Choices(["전체 문서", "한국 점자 규정", "점자 도서 제작 지침", "점자 자료 제작 지침"], "전체 문서");

        System.Windows.Automation.AutomationProperties.SetName(document, "규정 문서 필터");
        var results = RuleResults; root.Children.Add(new ScrollViewer { Content = results, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var resultCount = new TextBlock { Foreground = (Brush)FindResource("Muted"), Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }; Actions.Children.Add(resultCount);
        var page = 0; var totalPages = 1;
        Button previous = null!, next = null!;
        previous = Action("이전 쪽", async () => { if (page > 0) { page--; await SearchAsync(); } });
        next = Action("다음 쪽", async () => { if (page + 1 < totalPages) { page++; await SearchAsync(); } });
        void Display(IEnumerable<BrailleRule> rows)
        {
            results.Children.Clear(); Expander? expanded = null;
            foreach (var row in rows)
            {
                var header = new StackPanel();
                header.Children.Add(new TextBlock { Text = row.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 10) });
                header.Children.Add(new TextBlock { Text = row.Contents, MaxHeight = 36, LineHeight = 18, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });

                if (row.Supported == false) header.Children.Add(new TextBlock { Text = row.SupportLabel, Foreground = Brushes.DarkRed });
                var expandedContent = new StackPanel { Margin = new(9, 5, 9, 8) };
                expandedContent.Children.Add(new TextBlock { Text = row.HasFullText ? "조항 원문" : "발췌 · 전문 미제공", Foreground = (Brush)FindResource("Muted"), Margin = new(0, 0, 0, 4) });
                expandedContent.Children.Add(new TextBlock { Text = row.ExpandedText, TextWrapping = TextWrapping.Wrap });
                if (!row.HasFullText && row.RuleId.Length > 0)
                    expandedContent.Children.Add(Button("규정 전문 찾기", () => RunAsync(async () => { query.Text = row.RuleId; document.SelectedIndex = 0; page = 0; await SearchAsync(); })));
                var item = new Expander { Header = header, Content = new Border { Background = Brushes.White, BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(1), Child = expandedContent }, Tag = row.Source + " · " + row.Section, Margin = new(0, 0, 20, 8), Style = (Style)FindResource("RuleCard") };
                System.Windows.Automation.AutomationProperties.SetName(item, row.Section + " " + row.Name);
                item.Expanded += (_, _) => { if (expanded != item) { if (expanded is not null) expanded.IsExpanded = false; expanded = item; } };
                results.Children.Add(item);
            }
            if (results.Children.Count == 0) results.Children.Add(new TextBlock { Text = "검색 결과가 없습니다.", Margin = new(8) });
        }
        async Task SearchAsync()
        {
            var code = document.SelectedIndex switch { 1 => "MCST", 2 => "NLD", 3 => "NISE", _ => "" };
            var data = await new RuleClient(api).SearchAsync(query.Text, code, page, Lifetime.Token);
            totalPages = data.TotalPages;
            Display(data.Items);
            previous.IsEnabled = page > 0; next.IsEnabled = page + 1 < totalPages;
            resultCount.Text = $"{data.TotalElements}건 · {page + 1}/{Math.Max(1, totalPages)}쪽";
        }
        var search = Button("검색", () => RunAsync(async () => { page = 0; await SearchAsync(); })); DockPanel.SetDock(search, Dock.Right);
        var searchHost = new Grid(); searchHost.Children.Add(query);
        search.MinWidth = search.Width = 24; search.Margin = new(4, 0, 0, 0); search.HorizontalAlignment = HorizontalAlignment.Left;
        search.Content = new ContentControl { Content = FindResource("Icon.rulebook"), IsHitTestVisible = false }; search.Style = (Style)FindResource("ToolButton"); search.Padding = new(4);
        System.Windows.Automation.AutomationProperties.SetName(search, "규정 검색"); searchHost.Children.Add(search); filters.Children.Add(searchHost);
        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 16, 0, 0) }; filters.Children.Add(chips);
        var labels = new[] { "전체", "한국 점자 규정", "점자 도서 제작 지침", "점자 자료 제작 지침" };
        var widths = new[] { 44d, 106, 136, 136 };
        void PaintChips()
        {
            for (var i = 0; i < chips.Children.Count; i++)
            {
                var chip = (Button)chips.Children[i]; chip.Background = i == document.SelectedIndex ? new SolidColorBrush(Color.FromRgb(204, 228, 247)) : Brushes.White;
                chip.BorderBrush = new SolidColorBrush(i == document.SelectedIndex ? Color.FromRgb(0, 95, 184) : Color.FromRgb(181, 181, 181));
            }
        }
        for (var i = 0; i < labels.Length; i++)
        {
            var selection = i; var chip = Button(labels[i], () => { document.SelectedIndex = selection; PaintChips(); return Task.CompletedTask; });
            chip.MinWidth = chip.Width = widths[i]; chip.Margin = new(0, 0, 8, 0); chip.Padding = new(0); chips.Children.Add(chip);
        }
        PaintChips();
        query.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await RunAsync(async () => { page = 0; await SearchAsync(); }); } };
        document.SelectionChanged += async (_, _) => await RunAsync(async () => { page = 0; await SearchAsync(); });
        void AppliedRules()
        {
            Display(evidence.Select(r => new BrailleRule(r.RuleId, r.Source, r.Section, r.Title, r.Excerpt)));
            previous.IsEnabled = next.IsEnabled = false;
            Status.Text = evidence.Length == 0 ? "이 블록에 전달된 적용 규정이 없습니다." : "원문 수신 당시의 적용 규정입니다. 검색으로 현재 본문을 확인할 수 있습니다.";
        }
        if (initialQuery is null && evidence.Length > 0) AppliedRules(); else OnLoad(SearchAsync);
        Action("적용 규정", AppliedRules); Action("닫기", Close);
    }
}
