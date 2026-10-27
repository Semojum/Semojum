using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

/// <summary>Per-page conversion state plus the server quality report (critical_errors / review_flags) with block links.</summary>
public sealed class PageStatusDialog : Window
{
    private sealed record PageRow(int Number, string State, int Blocks, int Issues, string Confidence)
    {
        public string Page => $"{Number}쪽";
        public string Label => State switch
        {
            "COMPLETED" => "변환 완료", "NEEDS_REVIEW" => "검수 필요", "FAILED" => "변환 실패", "BLOCKED" => "처리 불가",
            "PENDING" => "대기 중", "RUNNING" or "PROCESSING" => "변환 중", "CANCELLED" or "CANCELED" => "취소됨", _ => State
        };
        public string Content => Blocks > 0 ? $"{Blocks}개 블록" : "편집 내용 없음";
        public string Review => Issues > 0 ? $"{Issues}건" : "없음";
    }
    private sealed record IssueRow(QualityIssue Issue, EditorBlock? Block)
    {
        public string Severity => Issue.SeverityLabel;
        public string Type => Issue.Type;
        public string Message => Issue.Message.Length > 0 ? Issue.Message : "(설명 없음)";
        public string Target => Block is not null ? Block.Label : Issue.ElementId is null ? "쪽 전체" : "연결 없음 (삭제되었거나 알 수 없는 블록)";
        // The report describes the AI original; after a source edit the warning may no longer apply to the current text.
        public string Edited => Block is null ? "" : Block.Metadata.OriginalSource is { } original && original != Block.Source ? "원문 수정됨 · AI 원본 기준" : "현재 원문과 동일";
    }
    public int? SelectedPage { get; private set; }
    public int? SelectedBlockId { get; private set; }
    public PageStatusDialog(DocumentSession document)
    {
        Title = "쪽별 변환 상태"; Width = 760; Height = 600; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var note = new TextBlock { Text = "실패한 쪽은 변환 결과 안내창의 「실패한 쪽 다시 변환」으로 서버에 다시 요청할 수 있습니다(취소·크레딧 소진 쪽 제외). 서버가 보낸 검토 항목과 측정된 OCR 신뢰도(0~1, 0.7 미만은 낮음)를 확인할 수 있습니다. 값 없음은 AI가 신뢰도를 보내지 않은 경우입니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var list = new ListView { Height = 190 }; var columns = new GridView(); list.View = columns;
        foreach (var (label, path, width) in new[] { ("원본 쪽", "Page", 80d), ("상태", "Label", 120d), ("편집 내용", "Content", 140d), ("검토 항목", "Review", 100d), ("OCR 신뢰도", "Confidence", 150d) })
            columns.Columns.Add(new GridViewColumn { Header = label, DisplayMemberBinding = new Binding(path), Width = width });
        DockPanel.SetDock(list, Dock.Top); root.Children.Add(list);
        var issueHeader = new TextBlock { Text = "선택한 쪽의 서버 검토 항목", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) };
        DockPanel.SetDock(issueHeader, Dock.Top); root.Children.Add(issueHeader);
        var issues = new ListView(); var issueColumns = new GridView(); issues.View = issueColumns;
        foreach (var (label, path, width) in new[] { ("구분", "Severity", 60d), ("유형", "Type", 130d), ("내용", "Message", 240d), ("블록", "Target", 150d), ("원문", "Edited", 130d) })
            issueColumns.Columns.Add(new GridViewColumn { Header = label, DisplayMemberBinding = new Binding(path), Width = width });
        AutomationPropertiesHelper(list, "쪽별 변환 상태 목록"); AutomationPropertiesHelper(issues, "서버 검토 항목 목록");
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var refresh = new Button { Content = "새로 고침", Margin = new Thickness(0, 0, 8, 0) };
        var go = new Button { Content = "선택한 쪽으로 이동", IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var goIssue = new Button { Content = "항목의 블록으로 이동", IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "닫기" };
        void RefreshIssues()
        {
            var page = (list.SelectedItem as PageRow)?.Number;
            issues.ItemsSource = page is null ? System.Array.Empty<IssueRow>() : (document.QualityReports.GetValueOrDefault(page.Value) ?? [])
                .Select(i => new IssueRow(i, i.ElementId is null ? null : document.Blocks.FirstOrDefault(b => b.Page == page && b.ServerId == i.ElementId))).ToArray();
            issueHeader.Text = page is null ? "선택한 쪽의 서버 검토 항목" : issues.Items.Count == 0 ? $"{page}쪽 · 서버 검토 항목 없음" : $"{page}쪽 · 서버 검토 항목 {issues.Items.Count}건";
        }
        void Refresh()
        {
            var selected = (list.SelectedItem as PageRow)?.Number;
            var rows = document.PageStatuses.Keys.Concat(document.QualityReports.Keys).Distinct().OrderBy(p => p)
                .Select(p => new PageRow(p, document.PageStatuses.GetValueOrDefault(p) ?? "UNKNOWN",
                    document.Blocks.Count(b => b.Page == p && b.ServerId is not null), document.QualityReports.GetValueOrDefault(p)?.Length ?? 0, Confidence(document, p))).ToArray();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Number == selected) ?? rows.FirstOrDefault();
            if (rows.Length == 0) note.Text = "서버 변환 상태가 없는 로컬 문서입니다.";
            RefreshIssues();
        }
        list.SelectionChanged += (_, _) => { go.IsEnabled = list.SelectedItem is PageRow { Blocks: > 0 }; RefreshIssues(); };
        issues.SelectionChanged += (_, _) => goIssue.IsEnabled = issues.SelectedItem is IssueRow { Block: not null };
        go.Click += (_, _) => { if (list.SelectedItem is PageRow { Blocks: > 0 } row) { SelectedPage = row.Number; DialogResult = true; } };
        void GoIssue() { if (issues.SelectedItem is IssueRow { Block: { } block }) { SelectedPage = block.Page; SelectedBlockId = block.Id; DialogResult = true; } }
        goIssue.Click += (_, _) => GoIssue(); issues.MouseDoubleClick += (_, _) => GoIssue();
        refresh.Click += (_, _) => Refresh(); close.Click += (_, _) => Close();
        footer.Children.Add(refresh); footer.Children.Add(go); footer.Children.Add(goIssue); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(issues); Refresh();
    }
    // BE B-10: measured values only (text elements' ocr_confidence, page ocr_confidence_avg). "값 없음" is not the same as low.
    private static string Confidence(DocumentSession document, int page)
    {
        var values = document.Blocks.Where(b => b.Page == page && b.Metadata.OcrConfidence is not null).Select(b => b.Metadata.OcrConfidence!.Value).ToArray();
        var average = values.Length > 0 ? values.Average() : document.OcrConfidence.GetValueOrDefault(page);
        if (average is null) return "값 없음";
        var lowest = values.Length > 0 ? values.Min() : average.Value;
        return $"{average:0.00}" + (lowest < 0.7 ? $" · 낮음 {lowest:0.00}" : "");
    }
    private static void AutomationPropertiesHelper(UIElement element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
}
