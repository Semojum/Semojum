using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class BoardDialog : CompactDialog
{
    public BoardDialog(BoardClient client, string workInfo, bool initiallyMine = false) : base("게시판 - 세모점", 700, 420, editor: true, editorActions: true)
    {
        Background = Brushes.White;
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, RowHeight = 32, Style = (Style)FindResource("BoardGrid") };
        foreach (var (label, name, width) in new[] { ("번호", "Number", 48d), ("제목", "DisplayTitle", 340d), ("글쓴이", "Author", 90d), ("작성일", "CreatedAt", 90d), ("조회", "ViewCount", 132d) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(name), Width = width });
        var root = new DockPanel(); Body(root);
        var count = new TextBlock { Height = 24, Margin = new(16, 0, 44, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(count, Dock.Top); root.Children.Add(count); root.Children.Add(grid);
        var fields = Choices(["제목+내용", "제목", "내용", "글쓴이"], "제목+내용"); fields.Width = 110;
        var query = new TextBox { Width = 180, Height = 24, MinHeight = 24, MaxLength = 100, Margin = new(8, 0, 0, 0), Padding = new(7, 0, 7, 0) };
        System.Windows.Automation.AutomationProperties.SetName(query, "게시판 검색어");
        Actions.HorizontalAlignment = HorizontalAlignment.Left; Actions.Margin = new(16, 0, 44, 24); ((Border)Actions.Parent).BorderThickness = new(0);
        Actions.Children.Add(fields); Actions.Children.Add(query);
        int page = 1, total = 0; bool mine = initiallyMine;
        async Task LoadAsync()
        {
            var field = fields.SelectedIndex switch { 1 => "TITLE", 2 => "CONTENT", 3 => "AUTHOR", _ => "TITLE_CONTENT" };
            var data = await client.ListAsync(page, field, query.Text, mine, Lifetime.Token);
            total = data.TotalCount; grid.ItemsSource = data.Notices.Concat(data.Items).ToArray();
            count.Text = $"전체 {total}건 · 10건씩 · {page}/{Math.Max(1, (total + 9) / 10)}쪽";
        }
        async Task OpenAsync()
        {
            if (grid.SelectedItem is not BoardRow row) return;
            if (row.NoticeId is { } notice)
            { var n = await client.NoticeAsync(notice, Lifetime.Token); MessageBox.Show(this, n.GetProperty("body").GetString(), n.GetProperty("title").GetString()); }
            else new PostDialog(client, await client.ReadAsync(row.PostId, Lifetime.Token), workInfo) { Owner = this }.ShowDialog();
            await LoadAsync();
        }
        grid.MouseDoubleClick += async (_, _) => await RunAsync(OpenAsync);
        Action("검색", async () => { page = 1; await LoadAsync(); });
        Actions.Children.Add(new Border { Width = 72 });
        var mineButton = Action("내 글 보기", async () => { mine = !mine; page = 1; await LoadAsync(); }); mineButton.Width = 94; mineButton.Margin = new(0);
        var write = Action("글쓰기", async () => { var d = new PostEditorDialog(client, null, workInfo) { Owner = this }; if (d.ShowDialog() == true) { page = 1; await LoadAsync(); } }, true); write.Margin = new(16, 0, 0, 0);
        // Paging and notifications remain accessible when the window is resized.
        var navigation = new StackPanel { Orientation = Orientation.Horizontal }; var pageMenu = new ContextMenu(); pageMenu.Items.Add(new MenuItem { Header = navigation, StaysOpenOnClick = true }); grid.ContextMenu = pageMenu;
        count.ToolTip = "이전·다음 쪽: 목록에서 마우스 오른쪽 단추 또는 PageUp·PageDown";
        navigation.Children.Add(Button("이전", async () => await RunAsync(async () => { if (page > 1) { page--; await LoadAsync(); } })));
        navigation.Children.Add(Button("다음", async () => await RunAsync(async () => { if (page * 10 < total) { page++; await LoadAsync(); } })));
        grid.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await RunAsync(OpenAsync); }
            else if (e.Key is System.Windows.Input.Key.PageDown or System.Windows.Input.Key.PageUp)
            { e.Handled = true; await RunAsync(async () => { if (e.Key == System.Windows.Input.Key.PageDown && page * 10 < total) page++; else if (e.Key == System.Windows.Input.Key.PageUp && page > 1) page--; await LoadAsync(); }); }
        };
        OnLoad(LoadAsync);
    }
}

public sealed class PostEditorDialog : CompactDialog
{
    private sealed record AttachmentChoice(string Name, long? ExistingId, string? LocalPath) { public override string ToString() => Name; }
    public BoardPost? SavedPost { get; private set; }
    public long SavedId { get; private set; }
    public PostEditorDialog(BoardClient client, BoardPost? post, string workInfo) : base(post is null ? "게시판 — 글쓰기 - 세모점" : "게시판 — 글 수정 - 세모점", 700, 420, editor: true, editorActions: true)
    {
        Background = Brushes.White; var panel = new StackPanel { Margin = new(20, 12, 20, 12) }; Body(panel);
        var title = new TextBox { Text = post?.Title ?? "", MaxLength = 100, Height = 32, Margin = new(0, 6, 0, 0) }; panel.Children.Add(new TextBlock { Text = "제목", Height = 16, Foreground = (Brush)FindResource("Muted") }); panel.Children.Add(title);
        var content = new TextBox { Text = post?.Content ?? "", MaxLength = 10000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 130, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 2, 0, 12) };
        panel.Children.Add(new TextBlock { Text = "본문", Height = 16, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 12, 0, 0) }); panel.Children.Add(content);
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 };
        var secret = new CheckBox { Content = "비밀글", IsChecked = post?.Secret == true, Width = 110, FontSize = 14 };
        var info = new CheckBox { Content = "현재 작업 정보 첨부", ToolTip = "원본·편집 내용은 포함되지 않습니다.", FontSize = 14, IsChecked = post is null }; flags.Children.Add(secret); flags.Children.Add(info); panel.Children.Add(flags);
        var attachments = new ObservableCollection<AttachmentChoice>((post?.Attachments ?? []).Select(a => new AttachmentChoice(a.FileName, a.AttachmentId, null)));
        var list = new ListBox { ItemsSource = attachments, Height = 28, Margin = new(0, 6, 0, 0), Visibility = attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed };
        attachments.CollectionChanged += (_, _) => list.Visibility = attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        panel.Children.Add(new TextBlock { Text = "첨부", Height = 16, Margin = new(0, 10, 0, 2), Foreground = (Brush)FindResource("Muted") });
        var fileActions = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(fileActions);
        fileActions.Children.Add(Button("파일 고르기", () =>
        {
            var picker = new OpenFileDialog { Multiselect = true, Filter = "문서·이미지|*.jpg;*.jpeg;*.png;*.gif;*.pdf;*.hwp;*.hwpx;*.txt;*.doc;*.docx;*.xls;*.xlsx;*.brf;*.zip" };
            if (picker.ShowDialog(this) == true)
            {
                if (attachments.Count + picker.FileNames.Length + (info.IsChecked == true ? 1 : 0) > 5) { Status.Text = "작업 정보 포함 최대 5개까지 첨부할 수 있습니다."; return Task.CompletedTask; }
                foreach (var path in picker.FileNames)
                { if (new FileInfo(path).Length > 10 * 1024 * 1024) { Status.Text = "한 파일은 10 MB까지입니다: " + Path.GetFileName(path); continue; } attachments.Add(new(Path.GetFileName(path), null, path)); }
            }
            return Task.CompletedTask;
        }));
        var choose = (Button)fileActions.Children[0]; choose.Margin = new(0); choose.Width = 99;
        fileActions.Children.Add(new TextBlock { Text = "최대 5개 · 10MB 제한", Margin = new(32, 0, 0, 0), Foreground = (Brush)FindResource("Muted") });
        panel.Children.Add(list);
        var attachmentMenu = new ContextMenu(); var remove = new MenuItem { Header = "선택 첨부 제거" }; attachmentMenu.Items.Add(remove); list.ContextMenu = attachmentMenu;
        remove.Click += (_, _) => { if (list.SelectedItem is AttachmentChoice a) attachments.Remove(a); };
        list.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Delete && list.SelectedItem is AttachmentChoice a) { attachments.Remove(a); e.Handled = true; } };
        var infoMenu = new ContextMenu(); var infoPreview = new MenuItem { Header = "첨부할 작업 정보 보기" }; infoMenu.Items.Add(infoPreview); info.ContextMenu = infoMenu;
        infoPreview.Click += (_, _) => MessageBox.Show(this, workInfo, "첨부할 작업 정보");
        Action(post is null ? "등록" : "저장", async () =>
        {
            var files = new List<UploadFile>();
            foreach (var a in attachments.Where(a => a.LocalPath is not null))
            { if (new FileInfo(a.LocalPath!).Length > 10 * 1024 * 1024) throw new InvalidDataException("첨부 파일이 10 MB를 초과했습니다."); files.Add(new(a.Name, await File.ReadAllBytesAsync(a.LocalPath!, Lifetime.Token))); }
            if (info.IsChecked == true) files.Add(new($"작업정보_{DateTime.Now:yyyy-MM-dd}.txt", Encoding.UTF8.GetBytes(workInfo)));
            var saved = await client.SaveAsync(post?.PostId, title.Text, content.Text, secret.IsChecked == true, attachments.Where(a => a.ExistingId.HasValue).Select(a => a.ExistingId!.Value).ToArray(), files.ToArray(), Lifetime.Token);
            SavedId = saved.Id; SavedPost = saved.Post; DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
    }
}

public sealed class PostDialog : CompactDialog
{
    public PostDialog(BoardClient client, BoardPost post, string workInfo) : base("게시판 — " + post.Title + " - 세모점", 700, 420, editor: true)
    {
        Background = Brushes.White; var panel = new StackPanel { Margin = new(20, 12, 20, 12) };
        ((Border)Actions.Parent).Child = null; Actions.Margin = new(0, 0, 18, 0);
        var toolbar = new Border { Height = 24, Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = Actions };
        DockPanel.SetDock(toolbar, Dock.Top); Root.Children.Add(toolbar);
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Render()
        {
            panel.Children.Clear();
            var heading = new DockPanel { MinHeight = 28 };
            if (post.Secret) heading.Children.Add(new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(1), Width = 34, Height = 18, Margin = new(0, 0, 8, 0), Child = new TextBlock { Text = "비밀", Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center } });
            heading.Children.Add(new TextBlock { Text = post.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }); panel.Children.Add(heading);
            panel.Children.Add(new TextBlock { Text = $"{post.Author} · {post.CreatedAt} · 조회 {post.ViewCount}", Margin = new(0, 10, 0, 6), Foreground = (Brush)FindResource("Muted"), FontSize = 12 });
            panel.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line"), Margin = new(0, 0, 0, 13) });
            panel.Children.Add(new TextBlock { Text = post.Content, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 22 });
            foreach (var a in post.Attachments)
            {
                var download = Button("", async () => await RunAsync(() => DownloadAsync(a)));
                download.MinWidth = 230; download.Height = download.MinHeight = 24; download.HorizontalAlignment = HorizontalAlignment.Left;
                download.Margin = new(0, 10, 0, 0); download.Padding = new(8, 0, 8, 0);
                var attachment = new StackPanel { Orientation = Orientation.Horizontal };
                attachment.Children.Add(new ContentControl { Content = FindResource("Icon.export"), Width = 16, Height = 16, Margin = new(0, 0, 6, 0) });
                attachment.Children.Add(new TextBlock { Text = $"{a.FileName} ({a.Size / 1024d:0.#} KB)", FontSize = 12 });
                download.Content = attachment; System.Windows.Automation.AutomationProperties.SetName(download, a.FileName + " 다운로드"); panel.Children.Add(download);
            }
            foreach (var answer in post.Answers)
            {
                var reply = new StackPanel(); var byline = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 4, 4) };
                byline.Children.Add(new TextBlock { Text = "운영자 답변", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 10, 0) });
                byline.Children.Add(new TextBlock { Text = answer.AnsweredAt, FontSize = 12, Foreground = (Brush)FindResource("Muted") }); reply.Children.Add(byline);
                reply.Children.Add(new TextBlock { Text = answer.Content, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new Border { BorderThickness = new(3, 0, 0, 0), BorderBrush = (Brush)FindResource("Accent"), Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)), Margin = new(0, 16, 0, 0), Padding = new(9, 8, 9, 10), Child = reply });
            }
        }
        Render();
        Action("목록", Close);
        if (post.Mine)
        {
            Action("수정", () => { var d = new PostEditorDialog(client, post, workInfo) { Owner = this }; if (d.ShowDialog() == true && d.SavedPost is { } changed) { post = changed; Title = "게시판 — " + post.Title; Render(); } });
            Action("삭제", async () => { if (MessageBox.Show(this, "이 글을 삭제하시겠습니까?", "글 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await client.DeleteAsync(post.PostId, Lifetime.Token); DialogResult = true; });
        }
        foreach (Button button in Actions.Children) { button.MinWidth = button.Width = 52; button.Height = button.MinHeight = 20; button.Margin = new(6, 2, 0, 2); button.Padding = new(0); }
    }
    private async Task DownloadAsync(BoardAttachment attachment)
    {
        if (!Uri.TryCreate(attachment.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("첨부 주소가 올바르지 않습니다.");
        var picker = new SaveFileDialog { FileName = Path.GetFileName(attachment.FileName) };
        if (picker.ShowDialog(this) != true) return;
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, Lifetime.Token); response.EnsureSuccessStatusCode();
        var temp = picker.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await using var source = await response.Content.ReadAsStreamAsync(Lifetime.Token);
                var buffer = new byte[81920]; int n; long total = 0;
                while ((n = await source.ReadAsync(buffer, Lifetime.Token)) != 0)
                { total += n; if (total > 10 * 1024 * 1024) throw new InvalidDataException("첨부 크기 제한을 초과했습니다."); await target.WriteAsync(buffer.AsMemory(0, n), Lifetime.Token); }
            }
            File.Move(temp, picker.FileName, true); Status.Text = "첨부를 저장했습니다.";
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public sealed class NotificationsDialog : CompactDialog
{
    public NotificationsDialog(BoardClient client, string workInfo) : base("알림", 400, 230)
    {
        var list = new ListBox { Margin = new(16, 38, 16, 12), BorderThickness = new(0), Padding = new(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        StripListPadding(list);
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); list.ItemContainerStyle = itemStyle;
        list.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(NotificationView)) }; Body(list);
        async Task LoadAsync() { var n = await client.NotificationsAsync(Lifetime.Token); list.ItemsSource = n.Items; list.ToolTip = $"안 읽음 {n.UnreadCount}"; }
        Action("열기", async () => { if (list.SelectedItem is BoardNotification n) { new PostDialog(client, await client.ReadAsync(n.PostId, Lifetime.Token), workInfo) { Owner = this }.ShowDialog(); await LoadAsync(); } }, true);
        Action("모두 읽음", async () => { await client.ReadAllAsync(Lifetime.Token); await LoadAsync(); });
        OnLoad(LoadAsync);
    }
}

public sealed class NotificationView : ContentControl
{
    public NotificationView() => DataContextChanged += (_, _) =>
    {
        if (DataContext is not BoardNotification item) return;
        var row = new DockPanel { Height = 42 };
        row.Children.Add(new ContentControl { Content = FindResource("Icon.note"), Width = 16, Height = 16, Margin = new(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        var text = new StackPanel(); row.Children.Add(text);
        text.Children.Add(new TextBlock { Text = $"{item.PostId}. {item.Title}", FontWeight = item.Read ? FontWeights.Normal : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = $"운영자 답변 {item.AnswerCount}건 · {item.LastAnsweredAt}", Foreground = (Brush)FindResource("Muted"), Margin = new(0, 3, 0, 0) });
        Content = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Margin = new(0, 0, 0, 5), Child = row };
    };
}
