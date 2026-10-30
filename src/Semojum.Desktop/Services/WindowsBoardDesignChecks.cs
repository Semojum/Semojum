using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsBoardDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        await api.LoginAsync("fixture", "fixture"); var client = new BoardClient(api);
        var board = new BoardDialog(client, "검수");
        check(board.Width == 1440 && board.Height == 1000, "Board requests the final Figma geometry before work-area fitting");
        board.Show();
        try
        {
            await WaitAsync(() => Descendants(board).OfType<DataGrid>().Any(g => g.Items.Count == 1));
            CheckWorkAreaFit(board, "Board", check);
            var allNav = Descendants(board).OfType<Button>().Single(b => Equals(b.Content, "전체 글"));
            var mineNav = Descendants(board).OfType<Button>().Single(b => Equals(b.Content, "내 글"));
            check(allNav.FontWeight == FontWeights.SemiBold && mineNav.FontWeight == FontWeights.Normal
                && AutomationProperties.GetItemStatus(allNav) == "선택됨" && AutomationProperties.GetItemStatus(mineNav) == "선택 안 됨",
                "The successfully loaded all-posts view has the selected weight and accessible state");
            mineNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.RequestedModes.Contains(true) && mineNav.FontWeight == FontWeights.SemiBold);
            check(allNav.FontWeight == FontWeights.Normal && AutomationProperties.GetItemStatus(mineNav) == "선택됨"
                && Descendants(board).OfType<TextBlock>().Any(t => t.Text == "내 글"),
                "A successful my-posts switch updates the title and both selected styles");
            var loadedItems = Descendants(board).OfType<DataGrid>().Single().ItemsSource;
            fixture.FailMine = false;
            allNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Descendants(board).OfType<TextBlock>().Any(t => t.Text.Contains("fixture view failure")));
            check(mineNav.FontWeight == FontWeights.SemiBold && allNav.FontWeight == FontWeights.Normal
                && AutomationProperties.GetItemStatus(mineNav) == "선택됨"
                && ReferenceEquals(Descendants(board).OfType<DataGrid>().Single().ItemsSource, loadedItems)
                && Descendants(board).OfType<TextBlock>().Any(t => t.Text == "내 글"),
                "A failed all-posts switch preserves the loaded list, title and selected state");
            fixture.FailMine = null;
            allNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => allNav.FontWeight == FontWeights.SemiBold && AutomationProperties.GetItemStatus(allNav) == "선택됨");
            check(mineNav.FontWeight == FontWeights.Normal && Descendants(board).OfType<TextBlock>().Any(t => t.Text == "전체 글"),
                "Retrying the list switch commits the successfully loaded all-posts view");
            check(HasIcon(board, "board-caption-close.svg") && HasIcon(board, "board-chevron-down.svg"), "Board title bar and search use the final Figma SVGs");
            check(HasIcon(board, "board-chevron-left.svg") && HasIcon(board, "board-chevron-right.svg"), "Board pagination renders both Figma arrow icons");
            check(Descendants(board).OfType<Button>().Any(b => Equals(b.Content, "알림")), "Board retains access to notifications");
            check(Descendants(board).OfType<TextBlock>().Any(t => t.Text == "fixture"), "Board status bar displays the actual signed-in account");
            var pageInput = Descendants(board).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시판 쪽 번호");
            check(pageInput.Text == "1" && Descendants(board).OfType<TextBlock>().Any(t => t.Text == "/ 3"), "Board shows the current and total pages from the actual response");
            var second = Descendants(board).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "게시판 2쪽");
            second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.RequestedPages.Contains(2) && pageInput.Text == "2");
            check(Descendants(board).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "이전 쪽" && b.IsEnabled), "Selecting a page requests that page and enables the previous action");
            pageInput.Text = "3";
            pageInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(board)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            await WaitAsync(() => fixture.RequestedPages.Contains(3) && pageInput.Text == "3");
            var requests = fixture.RequestedPages.Count;
            pageInput.Text = "4";
            pageInput.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(board)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(fixture.RequestedPages.Count == requests && pageInput.Text == "3", "An out-of-range page cannot trigger a server request or replace the current page");
            fixture.FailPage = 2;
            var failedPage = Descendants(board).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "게시판 2쪽");
            failedPage.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.RequestedPages.Count > requests && Descendants(board).OfType<TextBlock>().Any(t => t.Text.Contains("fixture page failure")));
            check(pageInput.Text == "3" && Descendants(board).OfType<DataGrid>().Single().Items.Count == 1,
                "A failed page request preserves the last loaded page and reports the error");
            if (Environment.GetEnvironmentVariable("BOARD_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(board, capture);
            var footer = Descendants(board).OfType<ScrollViewer>().Single(s => AutomationProperties.GetName(s) == "게시판 검색 및 쪽 이동");
            board.Width = 960; board.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var inputRect = pageInput.TransformToAncestor(footer).TransformBounds(new Rect(0, 0, pageInput.ActualWidth, pageInput.ActualHeight));
            check(inputRect.Left >= 0 && inputRect.Right <= footer.ViewportWidth + 1, "Board page input remains visible at 960px width");
            board.Width = 480; board.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            footer.ScrollToRightEnd(); board.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            inputRect = pageInput.TransformToAncestor(footer).TransformBounds(new Rect(0, 0, pageInput.ActualWidth, pageInput.ActualHeight));
            check(footer.ScrollableWidth > 0 && inputRect.Left >= 0 && inputRect.Right <= footer.ViewportWidth + 1, "Narrow board footer scrolls to keep page input accessible");
        }
        finally { board.Close(); }
        var editor = new PostEditorDialog(client, null, "검수");
        check(editor.Width == 1440 && editor.Height == 1000, "Post editor requests the final Figma geometry before work-area fitting");
        editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckWorkAreaFit(editor, "Post editor", check);
            var titleInput = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 제목");
            var contentInput = Descendants(editor).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 본문");
            var titleHint = Descendants(editor).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "BoardTitlePlaceholder");
            var bodyHint = Descendants(editor).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "BoardContentPlaceholder");
            check(titleInput.Text == "" && contentInput.Text == "" && titleHint.IsVisible && bodyHint.IsVisible
                && !titleHint.IsHitTestVisible && !bodyHint.IsHitTestVisible && titleHint.FontSize == 12 && bodyHint.FontSize == 12
                && titleHint.Foreground is SolidColorBrush { Color: var hc } && hc == Color.FromRgb(138, 138, 138),
                "Empty post inputs show non-intercepting Figma hints without changing submitted values");
            titleInput.Focus(); titleInput.Text = "실제 제목"; contentInput.Text = "실제 본문";
            check(titleHint.Visibility == Visibility.Collapsed && bodyHint.Visibility == Visibility.Collapsed,
                "Entering post text hides both hints");
            titleInput.Clear(); contentInput.Clear();
            check(titleHint.Visibility == Visibility.Visible && bodyHint.Visibility == Visibility.Visible,
                "Clearing the inputs restores both hints");
            var beforeSave = fixture.SaveRequests;
            Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "등록")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(fixture.SaveRequests == beforeSave, "Placeholder text cannot make an empty post valid or reach the server");
            check(Descendants(editor).OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "게시글 분류" && !c.IsEnabled
                && AutomationProperties.GetHelpText(c).Contains("제공하지 않습니다")), "Unsupported post classification remains visible with an explicit unavailable state");
            check(Descendants(editor).OfType<TextBox>().Any(t => t.AcceptsReturn && t.Height == 220), "Post editor offers the final Figma multiline field");
            check(Descendants(editor).OfType<CheckBox>().Any(c => Equals(c.Content, "현재 작업 정보 첨부") && c.IsChecked == true), "New post retains optional current-work attachment");
            check(HasIcon(editor, "board-check.svg"), "The checked work-information option renders the Figma check icon");
            var workOption = Descendants(editor).OfType<CheckBox>().Single(c => Equals(c.Content, "현재 작업 정보 첨부"));
            var checkContainer = (FrameworkElement)VisualTreeHelper.GetParent(workOption);
            var checkRect = workOption.TransformToAncestor(checkContainer).TransformBounds(new Rect(0, 0, workOption.ActualWidth, workOption.ActualHeight));
            check(workOption.ActualHeight >= 20 && checkRect.Top >= 0 && checkRect.Bottom <= checkContainer.ActualHeight, "The work-information checkbox fits completely within its row");
            check(Descendants(editor).OfType<Button>().Where(b => Equals(b.Content, "취소") || Equals(b.Content, "등록")).Select(b => b.Content).SequenceEqual(new object[] { "취소", "등록" }), "Cancel precedes registration in the post editor");
            var choose = Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "파일 고르기"));
            check(choose.Width == 110 && choose.Height == 32 && choose.IsEnabled, "Post editor retains an enabled 110x32 Figma file picker");
            if (Environment.GetEnvironmentVariable("BOARD_EDITOR_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(editor, capture);
            editor.Width = 480; editor.Height = 400; editor.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editScroll = Descendants(editor).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            editScroll.ScrollToBottom(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            var chooseRect = choose.TransformToAncestor(editScroll).TransformBounds(new Rect(0, 0, choose.ActualWidth, choose.ActualHeight));
            check(editScroll.ScrollableHeight > 0 && chooseRect.Top >= -1 && chooseRect.Bottom <= editScroll.ViewportHeight + 2 && choose.IsEnabled,
                "The narrow post editor can scroll to its file picker");
        }
        finally { editor.Close(); }
        var editPost = new BoardPost(42, "기존 제목", "기존 본문", "작성자", true, false, false, "2026-10-21", "", 4, [], []);
        var editing = new PostEditorDialog(client, editPost, "검수"); editing.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var hints = Descendants(editing).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) is "BoardTitlePlaceholder" or "BoardContentPlaceholder").ToArray();
            check(hints.Length == 2 && hints.All(h => h.Visibility == Visibility.Collapsed)
                && Descendants(editing).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "게시글 제목" && t.Text == "기존 제목")
                && Descendants(editing).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "게시글 본문" && t.Text == "기존 본문"),
                "Editing existing post content never overlays placeholder text");
        }
        finally { editing.Close(); }
        var submitted = new PostEditorDialog(client, null, "검수");
        var timedOut = false;
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        saveTimer.Tick += (_, _) => { timedOut = true; saveTimer.Stop(); submitted.Close(); };
        submitted.Loaded += (_, _) => submitted.Dispatcher.BeginInvoke(new Action(() =>
        {
            Descendants(submitted).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 제목").Text = "서버에 보낼 제목";
            Descendants(submitted).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 본문").Text = "서버에 보낼 본문";
            Descendants(submitted).OfType<Button>().Single(b => Equals(b.Content, "등록")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        saveTimer.Start(); bool? saved;
        try { saved = submitted.ShowDialog(); } finally { saveTimer.Stop(); if (submitted.IsVisible) submitted.Close(); }
        check(!timedOut && saved == true && fixture.LastSaveBody.Contains("서버에 보낼 제목") && fixture.LastSaveBody.Contains("서버에 보낼 본문")
            && !fixture.LastSaveBody.Contains("한 줄로 무엇이 문제인지 적는다") && !fixture.LastSaveBody.Contains("12쪽 7줄에서"),
            "Submitting a post sends only the entered values, never the placeholder hints");
        var post = new BoardPost(42, "문의 제목", "서버의 본문", "작성자", true, false, false, "2026-10-21", "", 4, [new(3, "첨부.pdf", 1024, "https://example.invalid/attachment.pdf")], [new(1, "운영자 답변 내용", false, "2026-10-22", "")]);
        var detail = new PostDialog(client, post, "검수");
        check(detail.Width == 1440 && detail.Height == 1000, "Post detail requests the final Figma geometry before work-area fitting");
        detail.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckWorkAreaFit(detail, "Post detail", check);
            check(Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "운영자 답변 내용"), "Post detail retains the actual operator answer");
            check(HasIcon(detail, "board-export.svg"), "Post attachments render the Figma download icon");
            var attachment = Descendants(detail).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "첨부.pdf 다운로드");
            check(attachment.ActualHeight == 32 && Math.Abs(attachment.ActualWidth - 268) < 2 && Equals(attachment.ToolTip, "첨부.pdf"),
                "Post attachment uses the Figma 268x32 surface and retains its filename for accessibility");
            var detailScroll = Descendants(detail).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            var attachmentLeft = attachment.TransformToAncestor(detailScroll).Transform(new Point()).X;
            check(attachmentLeft >= -1 && attachmentLeft <= 2, $"Post attachment aligns with the left edge of the actual content (left={attachmentLeft:F1})");
            var answer = Descendants(detail).OfType<Border>().Single(b => b.Child is StackPanel p && Descendants(p).OfType<TextBlock>().Any(t => t.Text == "운영자 답변 내용"));
            check(answer.CornerRadius == new CornerRadius(6) && answer.Background is SolidColorBrush { Color: var c } && c == Color.FromRgb(249, 249, 249),
                "The actual operator answer uses the Figma rounded neutral card");
            if (Environment.GetEnvironmentVariable("BOARD_DETAIL_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(detail, capture);
            detail.Width = 480; detail.Height = 400; detail.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var postScroll = Descendants(detail).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            var attachmentRect = attachment.TransformToAncestor(postScroll).TransformBounds(new Rect(0, 0, attachment.ActualWidth, attachment.ActualHeight));
            check(attachmentRect.Left >= -1 && attachmentRect.Right <= postScroll.ViewportWidth + 2 && attachment.IsEnabled,
                "The attachment download stays within a 480px detail window and remains enabled");
        }
        finally { detail.Close(); }
        var longName = new string('가', 160) + ".pdf";
        var longPost = post with { Attachments = [new(4, longName, 1024, "https://example.invalid/long.pdf")] };
        var longDetail = new PostDialog(client, longPost, "검수"); longDetail.Show();
        try
        {
            foreach (var width in new[] { 1440d, 480d })
            {
                longDetail.Width = width; longDetail.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var button = Descendants(longDetail).OfType<Button>().Single(b => AutomationProperties.GetName(b) == longName + " 다운로드");
                var text = Descendants(button).OfType<TextBlock>().Single(t => t.Text.StartsWith(longName, StringComparison.Ordinal));
                var icon = Descendants(button).OfType<SvgViewbox>().Single(i => i.Source?.ToString().EndsWith("board-export.svg", StringComparison.Ordinal) == true);
                var textBounds = text.TransformToAncestor(button).TransformBounds(new Rect(0, 0, text.ActualWidth, text.ActualHeight));
                var iconBounds = icon.TransformToAncestor(button).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                check(text.TextTrimming == TextTrimming.CharacterEllipsis && textBounds.Left >= 0 && textBounds.Right <= button.ActualWidth + 1
                    && iconBounds.Left >= 0 && iconBounds.Right <= button.ActualWidth + 1 && Equals(button.ToolTip, longName) && button.IsEnabled,
                    $"A long attachment name and icon stay inside the {width:0}px window button while retaining the full accessible filename");
            }
        }
        finally { longDetail.Close(); }
        var alerts = new NotificationsDialog(client, "검수"); alerts.Show();
        try
        {
            await WaitAsync(() => Descendants(alerts).OfType<TextBlock>().Any(t => t.Text.Contains("문의 제목", StringComparison.Ordinal)));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(alerts.Width == 400 && alerts.Height == 278 && HasIcon(alerts, "board-notification-close.svg") && HasIcon(alerts, "board-note.svg"), "Notifications match Figma geometry and original icons");
            if (Environment.GetEnvironmentVariable("BOARD_ALERT_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(alerts, capture);
        }
        finally { alerts.Close(); }
    }
    private static void CheckWorkAreaFit(Window window, string name, Action<bool, string> check)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor) || !GetWindowRect(handle, out var actual))
            throw new InvalidOperationException($"Cannot inspect {name} monitor work area and window bounds.");
        var scale = Math.Max(96, GetDpiForWindow(handle)) / 96d;
        var workWidth = monitor.Work.Right - monitor.Work.Left;
        var workHeight = monitor.Work.Bottom - monitor.Work.Top;
        var expectedWidth = Math.Min(1440, Math.Max(300, workWidth / scale));
        var expectedHeight = Math.Min(1000, Math.Max(200, workHeight / scale));
        check(Math.Abs(window.Width - expectedWidth) < 1 && Math.Abs(window.Height - expectedHeight) < 1 &&
              actual.Left >= monitor.Work.Left - 2 && actual.Top >= monitor.Work.Top - 2 &&
              actual.Right <= monitor.Work.Right + 2 && actual.Bottom <= monitor.Work.Bottom + 2,
              $"{name} fits its monitor work area while preserving the largest available Figma size " +
              $"(window={window.Width:F1}x{window.Height:F1}, work={workWidth}x{workHeight}px, scale={scale:F2}, " +
              $"bounds={actual.Left},{actual.Top},{actual.Right},{actual.Bottom})");
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    private static bool HasIcon(DependencyObject root, string asset) => Descendants(root).OfType<SvgViewbox>().Any(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true && i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static async Task WaitAsync(Func<bool> predicate)
    { var until = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20); if (!predicate()) throw new InvalidOperationException("Board fixture did not load"); }
    private sealed class Fixture : HttpMessageHandler
    {
        internal List<int> RequestedPages { get; } = [];
        internal List<bool> RequestedModes { get; } = [];
        internal int? FailPage { get; set; }
        internal bool? FailMine { get; set; }
        internal int SaveRequests { get; private set; }
        internal string LastSaveBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var page = 1;
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/board/posts")
            {
                SaveRequests++; LastSaveBody = await request.Content!.ReadAsStringAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"postId\":43}}", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/board/posts")
            {
                var value = request.RequestUri.Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("page=", StringComparison.Ordinal));
                if (value is not null && int.TryParse(value.AsSpan(5), out var parsed)) page = parsed;
                RequestedPages.Add(page);
                var mine = request.RequestUri.Query.Contains("mine=true", StringComparison.OrdinalIgnoreCase); RequestedModes.Add(mine);
                if (FailMine == mine)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"FIXTURE\",\"message\":\"fixture view failure\"}", Encoding.UTF8, "application/json") };
                if (FailPage == page)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"FIXTURE\",\"message\":\"fixture page failure\"}", Encoding.UTF8, "application/json") };
            }
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" },
                "/api/board/posts" => new { page, totalCount = 23, notices = Array.Empty<object>(), items = new[] { new { postId = 42, title = "문의 제목", answerCount = 1, author = "작성자", secret = false, createdAt = "2026-10-21", viewCount = 4 } } },
                "/api/board/notifications" => new { unreadCount = 1, items = new[] { new { notificationId = 1, postId = 42, title = "문의 제목", answerCount = 1, lastAnsweredAt = "2026-10-22", read = false } } },
                _ => new { }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
        }
    }
}
