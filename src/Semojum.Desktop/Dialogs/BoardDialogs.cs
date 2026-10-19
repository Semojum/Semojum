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
    public BoardDialog(BoardClient client, string workInfo) : base("게시판 — 세모점", 700, 460)
    {
        Background = Brushes.White;
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, RowHeight = 32 };
        foreach (var (label, name, width) in new[] { ("번호", "Number", 50d), ("제목", "DisplayTitle", 340d), ("글쓴이", "Author", 85d), ("작성일", "CreatedAt", 125d), ("조회", "ViewCount", 55d) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(name), Width = width });
        var root = new DockPanel(); Body(root);
        var count = new TextBlock { Margin = new(16, 6, 16, 6), HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(count, Dock.Top); root.Children.Add(count); root.Children.Add(grid);
        var fields = Choices(["제목+내용", "제목", "내용", "글쓴이"], "제목+내용"); fields.Width = 100;
        var query = new TextBox { Width = 130, Height = 24, MinHeight = 24, MaxLength = 100 };
        Actions.Children.Add(fields); Actions.Children.Add(query);
        int page = 1, total = 0; bool mine = false;
        async Task LoadAsync()
        {
            var field = fields.SelectedIndex switch { 1 => "TITLE", 2 => "CONTENT", 3 => "AUTHOR", _ => "TITLE_CONTENT" };
            var data = await client.ListAsync(page, field, query.Text, mine, Lifetime.Token);
            total = data.TotalCount; grid.ItemsSource = data.Notices.Concat(data.Items).ToArray();
            var notifications = await client.NotificationsAsync(Lifetime.Token);
            count.Text = $"전체 {total}건 · {page}/{Math.Max(1, (total + 9) / 10)}쪽 · 알림 {notifications.UnreadCount}";
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
        Action("내 글", async () => { mine = !mine; page = 1; await LoadAsync(); });
        Action("글쓰기", async () => { var d = new PostEditorDialog(client, null, workInfo) { Owner = this }; if (d.ShowDialog() == true) { page = 1; await LoadAsync(); } }, true);
        // Paging and notifications remain accessible when the window is resized.
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 4, 12, 4) }; DockPanel.SetDock(navigation, Dock.Bottom); root.Children.Insert(0, navigation);
        navigation.Children.Add(Button("이전", async () => await RunAsync(async () => { if (page > 1) { page--; await LoadAsync(); } })));
        navigation.Children.Add(Button("다음", async () => await RunAsync(async () => { if (page * 10 < total) { page++; await LoadAsync(); } })));
        navigation.Children.Add(Button("열기", async () => await RunAsync(OpenAsync)));
        navigation.Children.Add(Button("알림", async () => await RunAsync(async () => { new NotificationsDialog(client, workInfo) { Owner = this }.ShowDialog(); await LoadAsync(); })));
        OnLoad(LoadAsync);
    }
}

public sealed class PostEditorDialog : CompactDialog
{
    private sealed record AttachmentChoice(string Name, long? ExistingId, string? LocalPath) { public override string ToString() => Name; }
    public BoardPost? SavedPost { get; private set; }
    public long SavedId { get; private set; }
    public PostEditorDialog(BoardClient client, BoardPost? post, string workInfo) : base(post is null ? "게시판 — 글쓰기" : "게시판 — 글 수정", 700, 520)
    {
        Background = Brushes.White; var panel = new StackPanel { Margin = new(20, 12, 20, 12) }; Body(panel);
        var title = new TextBox { Text = post?.Title ?? "", MaxLength = 100, Height = 32 }; panel.Children.Add(new TextBlock { Text = "제목" }); panel.Children.Add(title);
        var content = new TextBox { Text = post?.Content ?? "", MaxLength = 10000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 6, 0, 8) };
        panel.Children.Add(new TextBlock { Text = "본문", Margin = new(0, 12, 0, 0) }); panel.Children.Add(content);
        var flags = new StackPanel { Orientation = Orientation.Horizontal };
        var secret = new CheckBox { Content = "비밀글", IsChecked = post?.Secret == true, Margin = new(0, 0, 20, 0) };
        var info = new CheckBox { Content = "현재 작업 정보 첨부 (원본·편집 내용 제외)", IsChecked = false }; flags.Children.Add(secret); flags.Children.Add(info); panel.Children.Add(flags);
        var attachments = new ObservableCollection<AttachmentChoice>((post?.Attachments ?? []).Select(a => new AttachmentChoice(a.FileName, a.AttachmentId, null)));
        var list = new ListBox { ItemsSource = attachments, Height = 65, Margin = new(0, 10, 0, 4) }; panel.Children.Add(list);
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
        fileActions.Children.Add(Button("선택 첨부 제거", () => { if (list.SelectedItem is AttachmentChoice a) attachments.Remove(a); return Task.CompletedTask; }));
        fileActions.Children.Add(Button("작업 정보 보기", () => { MessageBox.Show(this, workInfo, "첨부할 작업 정보"); return Task.CompletedTask; }));
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
    public PostDialog(BoardClient client, BoardPost post, string workInfo) : base("게시판 — " + post.Title, 700, 480)
    {
        Background = Brushes.White; var panel = new StackPanel { Margin = new(20) };
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Render()
        {
            panel.Children.Clear();
            panel.Children.Add(new TextBlock { Text = (post.Secret ? "비밀 · " : "") + post.Title, FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{post.Author} · {post.CreatedAt} · 조회 {post.ViewCount}", Margin = new(0, 10, 0, 12), Foreground = Brushes.DimGray });
            panel.Children.Add(new TextBlock { Text = post.Content, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            foreach (var a in post.Attachments)
                panel.Children.Add(Button($"↓ {a.FileName} ({a.Size / 1024d:0.#} KB)", async () => await RunAsync(() => DownloadAsync(a))));
            foreach (var answer in post.Answers)
                panel.Children.Add(new Border { BorderThickness = new(3, 0, 0, 0), BorderBrush = Brushes.DodgerBlue, Background = Brushes.WhiteSmoke, Margin = new(0, 16, 0, 0), Padding = new(10), Child = new TextBlock { Text = $"운영자 답변 · {answer.AnsweredAt}\n{answer.Content}", TextWrapping = TextWrapping.Wrap } });
        }
        Render();
        if (post.Mine)
        {
            Action("수정", () => { var d = new PostEditorDialog(client, post, workInfo) { Owner = this }; if (d.ShowDialog() == true && d.SavedPost is { } changed) { post = changed; Title = "게시판 — " + post.Title; Render(); } });
            Action("삭제", async () => { if (MessageBox.Show(this, "이 글을 삭제하시겠습니까?", "글 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await client.DeleteAsync(post.PostId, Lifetime.Token); DialogResult = true; });
        }
        Action("목록", () => Close());
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
    public NotificationsDialog(BoardClient client, string workInfo) : base("알림", 400, 330)
    {
        var list = new ListBox { Margin = new(16), DisplayMemberPath = "Display" }; Body(list);
        async Task LoadAsync() { var n = await client.NotificationsAsync(Lifetime.Token); list.ItemsSource = n.Items; Title = $"알림 · 안 읽음 {n.UnreadCount}"; }
        Action("열기", async () => { if (list.SelectedItem is BoardNotification n) { new PostDialog(client, await client.ReadAsync(n.PostId, Lifetime.Token), workInfo) { Owner = this }.ShowDialog(); await LoadAsync(); } }, true);
        Action("모두 읽음", async () => { await client.ReadAllAsync(Lifetime.Token); await LoadAsync(); });
        OnLoad(LoadAsync);
    }
}
