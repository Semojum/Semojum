using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class UsageDialog : CompactDialog
{
    private sealed record JobRow(string JobId, string FileName, string Status, string Pages, string Credits, string Finished);
    public string? JobId { get; private set; }
    public string? FileName { get; private set; }
    public UsageDialog(SemojumApiClient api) : base("마이페이지 — 세모점", 1024, 720)
    {
        Background = Brushes.White; var tabs = new TabControl { Margin = new(16) }; Body(tabs);
        var panel = new StackPanel { Margin = new(16) };
        tabs.Items.Add(new TabItem { Header = "사용량", Content = panel });
        var period = Choices(["이 달", "지난 3달", "올해"], "이 달"); Row(panel, "기간", period, 100);
        var summary = new TextBlock { FontSize = 22, Margin = new(0, 12, 0, 8), TextWrapping = TextWrapping.Wrap }; panel.Children.Add(summary);
        var usage = new TextBlock { Margin = new(0, 0, 0, 16) }; panel.Children.Add(usage);
        var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Height = 160 };
        panel.Children.Add(new ScrollViewer { Content = bars, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 185 });
        var sources = new TextBlock { Margin = new(0, 16, 0, 0), LineHeight = 28 }; panel.Children.Add(sources);
        var history = new DockPanel { Margin = new(16) };
        tabs.Items.Add(new TabItem { Header = "작업 이력", Content = history });
        var dates = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(dates, Dock.Top); history.Children.Add(dates);
        var from = new DatePicker { SelectedDate = DateTime.Today.AddDays(-29), Width = 150 }; var to = new DatePicker { SelectedDate = DateTime.Today, Width = 150 };
        dates.Children.Add(from); dates.Children.Add(new TextBlock { Text = " ~ " }); dates.Children.Add(to);
        var jobs = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, Margin = new(0, 12, 0, 0), RowHeight = 32 };
        foreach (var (label, property, width) in new[] { ("파일", "FileName", 300d), ("상태", "Status", 130d), ("쪽", "Pages", 100d), ("크레딧", "Credits", 85d), ("완료", "Finished", 190d) })
            jobs.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width });
        history.Children.Add(jobs);
        async Task LoadUsageAsync()
        {
            var code = period.SelectedIndex switch { 1 => "QUARTER", 2 => "YEAR", _ => "MONTH" };
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage/daily?period=" + code, cancellationToken: Lifetime.Token);
            summary.Text = $"남은 크레딧 {Value(data, "orgRemaining")} / 기관 할당 {Value(data, "orgAllocated")}";
            usage.Text = $"{Value(data, "from")} ~ {Value(data, "to")} · 내 사용량 {Value(data, "usedCredits")} 크레딧 ({Value(data, "usedPercent")}%)";
            bars.Children.Clear(); var daily = data.GetProperty("daily").EnumerateArray().ToArray(); var maximum = daily.Select(d => d.GetProperty("credits").GetDouble()).DefaultIfEmpty(1).Max();
            foreach (var day in daily)
            {
                var number = day.GetProperty("credits").GetDouble();
                bars.Children.Add(new Border { Background = number > 0 ? new SolidColorBrush(Color.FromRgb(0, 95, 184)) : Brushes.LightGray,
                    Width = Math.Clamp(800d / Math.Max(1, daily.Length), 5, 40) - 2, Height = Math.Max(2, 145 * number / Math.Max(1, maximum)), Margin = new(1, 0, 1, 0), VerticalAlignment = VerticalAlignment.Bottom,
                    ToolTip = $"{Value(day, "date")}: {number} 크레딧" });
            }
            sources.Text = "사용 내역\n" + string.Join("\n", data.GetProperty("bySource").EnumerateArray().Select(s =>
                $"{Value(s, "source") switch { "PDF_HWP" => "PDF · HWP", "IMAGE" => "이미지", _ => "TXT · 새 문서" }}   {Value(s, "credits")} 크레딧"));
        }
        async Task LoadJobsAsync()
        {
            if (from.SelectedDate is not { } start || to.SelectedDate is not { } end || start > end) throw new ArgumentException("조회 시작일과 종료일을 확인해 주세요.");
            var data = await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/users/usage/jobs?from={start:yyyy-MM-dd}&to={end:yyyy-MM-dd}", cancellationToken: Lifetime.Token);
            jobs.ItemsSource = data.GetProperty("items").EnumerateArray().Select(j => new JobRow(Value(j, "jobId"), Value(j, "fileName"),
                Value(j, "status") + (Value(j, "failedPages") is not ("—" or "0") ? " · 일부 실패" : ""),
                Value(j, "donePages") + "/" + Value(j, "totalPages"), Value(j, "credits"), Value(j, "finishedAt"))).ToArray();
        }
        period.SelectionChanged += async (_, _) => await RunAsync(LoadUsageAsync);
        dates.Children.Add(Button("조회", async () => await RunAsync(LoadJobsAsync)));
        void Open() { if (jobs.SelectedItem is JobRow j) { JobId = j.JobId; FileName = j.FileName; DialogResult = true; } }
        jobs.MouseDoubleClick += (_, _) => Open();
        Action("선택 작업 열기", Open, true); Action("닫기", () => Close());
        OnLoad(async () => { await LoadUsageAsync(); await LoadJobsAsync(); });
    }
    private static string Value(JsonElement j, string field) => j.TryGetProperty(field, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "—";
}
