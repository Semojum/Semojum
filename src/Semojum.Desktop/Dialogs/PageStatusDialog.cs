using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class PageStatusDialog : Window
{
    private sealed record PageRow(int Number, string State, int Blocks)
    {
        public string Page => $"{Number}쪽";
        public string Label => State switch
        {
            "COMPLETED" => "변환 완료", "NEEDS_REVIEW" => "검수 필요", "FAILED" => "변환 실패", "BLOCKED" => "처리 불가",
            "PENDING" => "대기 중", "PROCESSING" => "변환 중", "CANCELLED" or "CANCELED" => "취소됨", _ => State
        };
        public string Content => Blocks > 0 ? $"{Blocks}개 블록" : "편집 내용 없음";
    }
    public int? SelectedPage { get; private set; }
    public PageStatusDialog(DocumentSession document)
    {
        Title = "쪽별 변환 상태"; Width = 600; Height = 460; MinWidth = 460; MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var note = new TextBlock { Text = "AI 재변환은 제공하지 않습니다. 실패하거나 처리할 수 없는 쪽은 목록에서 확인할 수 있습니다.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        var list = new ListView(); var columns = new GridView(); list.View = columns;
        foreach (var (label, path, width) in new[] { ("원본 쪽", "Page", 90d), ("상태", "Label", 150d), ("편집 내용", "Content", 180d) })
            columns.Columns.Add(new GridViewColumn { Header = label, DisplayMemberBinding = new System.Windows.Data.Binding(path), Width = width });
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var refresh = new Button { Content = "새로 고침", Margin = new Thickness(0, 0, 8, 0) };
        var go = new Button { Content = "선택한 쪽으로 이동", IsEnabled = false, Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "닫기" };
        void Refresh()
        {
            var selected = (list.SelectedItem as PageRow)?.Number;
            var rows = document.PageStatuses.OrderBy(p => p.Key)
                .Select(p => new PageRow(p.Key, p.Value, document.Blocks.Count(b => b.Page == p.Key && b.ServerId is not null))).ToArray();
            list.ItemsSource = rows; list.SelectedItem = rows.FirstOrDefault(r => r.Number == selected) ?? rows.FirstOrDefault();
            if (rows.Length == 0) note.Text = "서버 변환 상태가 없는 로컬 문서입니다.";
        }
        list.SelectionChanged += (_, _) => go.IsEnabled = list.SelectedItem is PageRow { Blocks: > 0 };
        go.Click += (_, _) => { if (list.SelectedItem is PageRow { Blocks: > 0 } row) { SelectedPage = row.Number; DialogResult = true; } };
        refresh.Click += (_, _) => Refresh(); close.Click += (_, _) => Close();
        footer.Children.Add(refresh); footer.Children.Add(go); footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(list); Refresh();
    }
}
