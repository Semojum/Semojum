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
            var statusBar = Descendants(board).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "BoardStatusBar");
            check(statusBar.Child is TextBlock { Text: "fixture", HorizontalAlignment: HorizontalAlignment.Right }, "Board status bar shows the signed-in account on the right");
            check(!Descendants(board).OfType<Button>().Any(b => Equals(b.Content, "내 글 보기")) && Descendants(board).OfType<TextBlock>().Any(t => t.Text == "전체 글" && t.FontSize == 20 && t.FontWeight == FontWeights.Bold),
                "Board heading uses the final 20px bold title without a separate my-posts button");
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
        finally { board.Close(); fixture.FailPage = null; }
        fixture.IncludeNotice = true;
        var noticeBoard = new BoardDialog(client, "검수"); noticeBoard.Show();
        try
        {
            var noticeGrid = Descendants(noticeBoard).OfType<DataGrid>().Single();
            await WaitAsync(() => noticeGrid.Items.Count == 2);
            noticeBoard.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var noticeRow = noticeGrid.Items.Cast<BoardRow>().Single(row => row.NoticeId is not null);
            var postRow = noticeGrid.Items.Cast<BoardRow>().Single(row => row.NoticeId is null);
            var noticeNumber = (TextBlock)noticeGrid.Columns[0].GetCellContent(noticeRow);
            var noticeTitle = (TextBlock)noticeGrid.Columns[1].GetCellContent(noticeRow);
            var noticeAuthor = (TextBlock)noticeGrid.Columns[2].GetCellContent(noticeRow);
            var postNumber = (TextBlock)noticeGrid.Columns[0].GetCellContent(postRow);
            var postTitle = (TextBlock)noticeGrid.Columns[1].GetCellContent(postRow);
            check(noticeNumber.Text == "공지" && noticeNumber.FontSize == 14 && noticeNumber.FontWeight == FontWeights.Bold
                && noticeTitle.Text == "서버 점검 안내" && noticeTitle.FontSize == 14 && noticeTitle.FontWeight == FontWeights.Bold
                && noticeAuthor.FontWeight != FontWeights.Bold && postNumber.FontSize == 12 && postNumber.FontWeight == FontWeights.Normal
                && postTitle.FontSize == 12 && postTitle.FontWeight == FontWeights.Normal,
                "Board notice number and title use Figma bold typography without changing ordinary posts or notice authors");
            noticeGrid.SelectedItem = noticeRow; var noticeScrolls = false;
            var opened = await AnswerAsync(noticeBoard, 0, () => noticeGrid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(noticeBoard)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent }),
                notice => noticeScrolls = Descendants(notice).OfType<ScrollViewer>().Any(s => s.ScrollableHeight > 0));
            check(opened is not null && opened.MessageText == "서버 점검 안내" && opened.DetailText == Fixture.NoticeBody
                && opened.ActionButtons.Select(b => b.Content).SequenceEqual(new object[] { "확인" }) && Math.Abs(opened.Width - 520) <= 1 && Math.Abs(opened.Height - 316) <= 1 && noticeScrolls,
                "A board notice opens in the app message window with its title, a 「확인」 action and a scrolling long body");
            await WaitAsync(() => noticeGrid.IsEnabled && noticeGrid.Items.Count == 2);
            noticeGrid.SelectedItem = noticeGrid.Items.Cast<BoardRow>().Single(row => row.NoticeId is null);
            var reads = fixture.PostReads;
            noticeGrid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(noticeBoard)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await WaitAsync(() => fixture.PostReads > reads && Descendants(noticeBoard).OfType<PostView>().Any(v => v.IsVisible));
            noticeBoard.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckBodyOnly(noticeBoard, "A post opened from the list", check);
            check(Application.Current.Windows.OfType<Window>().Count(w => w.IsVisible && w.Owner == noticeBoard) == 0,
                "Opening a post from the list does not open another window over the board");
            check(Descendants(noticeBoard).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "서버의 본문"),
                "Opening a post from the list shows the post's content in the board body");
            var listed = fixture.RequestedPages.Count;
            Descendants(noticeBoard).OfType<Button>().Single(b => Equals(b.Content, "목록")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !Descendants(noticeBoard).OfType<PostView>().Any() && fixture.RequestedPages.Count > listed && noticeGrid.Items.Count == 2);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(noticeGrid.IsVisible && noticeBoard.Title == "게시판 - 누리닷" && (!noticeBoard.IsActive || noticeGrid.IsKeyboardFocusWithin),
                "「목록」 shows the refreshed list again in the same window and returns keyboard focus to it");
        }
        finally { noticeBoard.Close(); fixture.IncludeNotice = false; }
        fixture.IncludeNotice = true;
        var lists = new BoardDialog(client, "검수"); lists.Show();
        try
        {
            var listGrid = Descendants(lists).OfType<DataGrid>().Single();
            await WaitAsync(() => listGrid.IsEnabled && listGrid.Items.Count == 2);
            var noticeNav = Descendants(lists).OfType<Button>().Single(b => Equals(b.Content, "공지"));
            var answeredNav = Descendants(lists).OfType<Button>().Single(b => Equals(b.Content, "답변 받은 글"));
            check(noticeNav.IsEnabled && answeredNav.IsEnabled && noticeNav.ToolTip is null && answeredNav.ToolTip is null,
                "The 「공지」 and 「답변 받은 글」 sidebar lists are available without the old unavailable tooltip");
            var requested = fixture.RequestedPages.Count;
            noticeNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => listGrid.IsEnabled && AutomationProperties.GetItemStatus(noticeNav) == "선택됨");
            var noticeRows = listGrid.Items.Cast<BoardRow>().ToArray();
            check(noticeRows.Length == 1 && noticeRows[0] is { NoticeId: "notice-1", Number: "공지", Title: "서버 점검 안내" }
                && fixture.RequestedPages.Skip(requested).SequenceEqual([1]) && !fixture.RequestedModes[^1]
                && Descendants(lists).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "공지" && t.FontSize == 20) && Descendants(lists).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "1건"),
                "「공지」 shows only the notices of the first board list page, numbered 「공지」 under the 「공지」 heading");
            requested = fixture.RequestedPages.Count;
            answeredNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => listGrid.IsEnabled && AutomationProperties.GetItemStatus(answeredNav) == "선택됨");
            lists.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var answeredRows = listGrid.Items.Cast<BoardRow>().ToArray();
            check(answeredRows.Select(r => r.PostId).SequenceEqual(Enumerable.Range(111, 10).Reverse().Select(id => (long)id)) && answeredRows.All(r => r.NoticeId is null)
                && fixture.RequestedPages.Skip(requested).SequenceEqual([1]) && fixture.RequestedAnswered.Skip(requested).SequenceEqual([true]) && !fixture.RequestedModes[^1]
                && AutomationProperties.GetItemStatus(noticeNav) == "선택 안 됨"
                && Descendants(lists).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "답변 받은 글" && t.FontSize == 20) && Descendants(lists).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "13건")
                && Descendants(lists).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "/ 2"),
                "「답변 받은 글」 asks the server once for answered=true and shows its first page and total without notices");
            requested = fixture.RequestedPages.Count;
            Descendants(lists).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "게시판 2쪽").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => listGrid.IsEnabled && fixture.RequestedPages.Count > requested && listGrid.Items.Count == 3);
            check(fixture.RequestedPages.Skip(requested).SequenceEqual([2]) && fixture.RequestedAnswered[^1] && AutomationProperties.GetItemStatus(answeredNav) == "선택됨"
                && listGrid.Items.Cast<BoardRow>().Select(r => r.PostId).SequenceEqual([110L, 109L, 108L]),
                "The next 「답변 받은 글」 page is the server's answered=true page 2");
        }
        finally { lists.Close(); fixture.IncludeNotice = false; }
        var drafting = new BoardDialog(client, "검수"); drafting.Show();
        try
        {
            var draftGrid = Descendants(drafting).OfType<DataGrid>().Single();
            await WaitAsync(() => draftGrid.Items.Count == 1);
            Descendants(drafting).OfType<Button>().Single(b => Equals(b.Content, "글쓰기")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Descendants(drafting).OfType<PostEditorView>().Any(v => v.IsVisible));
            drafting.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckBodyOnly(drafting, "글쓰기 from the list", check);
            var draft = Descendants(drafting).OfType<PostEditorView>().Single();
            Descendants(draft).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 제목").Text = "쓰던 제목";
            var listed = fixture.RequestedPages.Count; var saves = fixture.SaveRequests;
            var mineNav = Descendants(drafting).OfType<Button>().Single(b => Equals(b.Content, "내 글"));
            var kept = await AnswerAsync(drafting, 0, () => mineNav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            check(kept is { MessageText: "작성 중인 글을 닫으시겠습니까?" } && kept.ActionButtons.Select(b => b.Content).SequenceEqual(new object[] { "계속 작성", "닫기" })
                && draft.IsVisible && fixture.RequestedPages.Count == listed,
                "Choosing another list while writing asks first, and 「계속 작성」 keeps the draft in the board body");
            var stayed = await AnswerAsync(drafting, 0, drafting.Close);
            check(stayed is not null && drafting.IsVisible && draft.IsVisible, "Closing the board while writing asks first, and 「계속 작성」 keeps the window and the draft");
            var cancel = Descendants(draft).OfType<Button>().Single(b => Equals(b.Content, "취소"));
            var discarded = await AnswerAsync(drafting, 1, () => cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await WaitAsync(() => !Descendants(drafting).OfType<PostEditorView>().Any());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(discarded is not null && draftGrid.IsVisible && draftGrid.Items.Count == 1 && !drafting.Changed && fixture.SaveRequests == saves
                && (!drafting.IsActive || draftGrid.IsKeyboardFocusWithin),
                "「취소」 then 「닫기」 discards the draft and shows the same list again with keyboard focus on it");
        }
        finally { CloseDiscarding(drafting); }
        var editor = BoardDialog.Writing(client, "검수");
        check(editor.Width == 1440 && editor.Height == 1000 && editor.Title == "게시판 - 누리닷", "Post editor opens as the board window with the final Figma geometry and title");
        editor.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckWorkAreaFit(editor, "Post editor", check);
            CheckBodyOnly(editor, "Post editor", check);
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
            var categoryBox = Descendants(editor).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "게시글 분류");
            check(categoryBox.IsEnabled && categoryBox.ToolTip is null && string.IsNullOrEmpty(AutomationProperties.GetHelpText(categoryBox))
                && categoryBox.Items.Cast<string>().SequenceEqual(["계정 · 발급", "점역 결과", "크레딧 · 결제", "사용법"]) && Equals(categoryBox.SelectedItem, "점역 결과"),
                "The post category is enabled with the four Figma categories and starts at 「점역 결과」");
            check(Descendants(editor).OfType<TextBox>().Any(t => t.AcceptsReturn && t.Height == 220), "Post editor offers the final Figma multiline field");
            check(Descendants(editor).OfType<CheckBox>().Any(c => Equals(c.Content, "현재 작업 정보 첨부") && c.IsChecked == true), "New post retains optional current-work attachment");
            check(HasIcon(editor, "board-check.svg"), "The checked work-information option renders the Figma check icon");
            var workOption = Descendants(editor).OfType<CheckBox>().Single(c => Equals(c.Content, "현재 작업 정보 첨부"));
            var checkContainer = (FrameworkElement)VisualTreeHelper.GetParent(workOption);
            var checkRect = workOption.TransformToAncestor(checkContainer).TransformBounds(new Rect(0, 0, workOption.ActualWidth, workOption.ActualHeight));
            check(workOption.ActualHeight >= 20 && checkRect.Top >= 0 && checkRect.Bottom <= checkContainer.ActualHeight, "The work-information checkbox fits completely within its row");
            var preview = (MenuItem)workOption.ContextMenu!.Items[0];
            var previewed = await AnswerAsync(editor, 0, () => preview.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)));
            check(previewed is { MessageText: "첨부할 작업 정보입니다.", DetailText: "검수" } && previewed.ActionButtons.Select(b => b.Content).SequenceEqual(new object[] { "확인" }),
                "The work-information preview opens in the app message window with the exact text that will be attached");
            check(Descendants(editor).OfType<Button>().Where(b => Equals(b.Content, "취소") || Equals(b.Content, "등록")).Select(b => b.Content).SequenceEqual(new object[] { "취소", "등록" }), "Cancel precedes registration in the post editor");
            var choose = Descendants(editor).OfType<Button>().Single(b => Equals(b.Content, "파일 고르기"));
            check(choose.Width == 110 && choose.Height == 32 && choose.IsEnabled, "Post editor retains an enabled 110x32 Figma file picker");
            if (Environment.GetEnvironmentVariable("BOARD_EDITOR_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(editor, capture);
            editor.Width = 480; editor.Height = 400; editor.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editScroll = Descendants(Descendants(editor).OfType<PostEditorView>().Single()).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            editScroll.ScrollToBottom(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); editor.UpdateLayout();
            var chooseRect = choose.TransformToAncestor(editScroll).TransformBounds(new Rect(0, 0, choose.ActualWidth, choose.ActualHeight));
            check(editScroll.ScrollableHeight > 0 && chooseRect.Top >= -1 && chooseRect.Bottom <= editScroll.ViewportHeight + 2 && choose.IsEnabled,
                "The narrow post editor can scroll to its file picker");
        }
        finally { CloseDiscarding(editor); }
        var editPost = new BoardPost(42, "기존 제목", "기존 본문", "작성자", true, false, false, "2026-10-21", "", 4, [], [], "BILLING");
        var editing = BoardDialog.Reading(client, editPost, "검수"); editing.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Descendants(editing).OfType<Button>().Single(b => Equals(b.Content, "수정")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Descendants(editing).OfType<PostEditorView>().Any(v => v.IsVisible));
            editing.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckBodyOnly(editing, "글 수정 from a post", check);
            var hints = Descendants(editing).OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) is "BoardTitlePlaceholder" or "BoardContentPlaceholder").ToArray();
            check(hints.Length == 2 && hints.All(h => h.Visibility == Visibility.Collapsed)
                && Descendants(editing).OfType<TextBlock>().Any(t => t.IsVisible && t.Text == "글 수정")
                && Descendants(editing).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "게시글 제목" && t.Text == "기존 제목")
                && Descendants(editing).OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "게시글 본문" && t.Text == "기존 본문"),
                "Editing existing post content replaces the post in the board body and never overlays placeholder text");
            check(Equals(Descendants(editing).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "게시글 분류").SelectedItem, "크레딧 · 결제"),
                "Editing a post shows its own category instead of the new-post default");
            Descendants(editing).OfType<Button>().Single(b => Equals(b.Content, "취소")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !Descendants(editing).OfType<PostEditorView>().Any());
            check(Descendants(editing).OfType<PostView>().Single().IsVisible && !Application.Current.Windows.OfType<NoticeDialog>().Any(d => d.Owner == editing && d.IsVisible) && !editing.Changed,
                "Cancelling an unchanged edit returns to the same post without asking");
        }
        finally { CloseDiscarding(editing); }
        // A post written before categories shows none; choosing one sends it with the edit (PUT), and leaving it unchosen would keep the server's.
        var recategorized = BoardDialog.Reading(client, editPost with { Category = null }, "검수"); recategorized.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Descendants(recategorized).OfType<Button>().Single(b => Equals(b.Content, "수정")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => Descendants(recategorized).OfType<PostEditorView>().Any(v => v.IsVisible));
            recategorized.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var legacyCategory = Descendants(recategorized).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "게시글 분류");
            check(legacyCategory.SelectedItem is null && legacyCategory.IsEnabled, "Editing a post without a category shows no category rather than inventing one");
            legacyCategory.SelectedItem = "사용법"; var saves = fixture.SaveRequests;
            Descendants(recategorized).OfType<Button>().Single(b => Equals(b.Content, "저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => fixture.SaveRequests > saves && !Descendants(recategorized).OfType<PostEditorView>().Any());
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(fixture.LastSaveMethod == HttpMethod.Put && fixture.LastSaveCategory == "USAGE" && recategorized.Changed && Descendants(recategorized).OfType<PostView>().Single().IsVisible,
                "Saving an edit sends the chosen category with PUT and returns to the post");
        }
        finally
        {
            // An unsaved choice would otherwise ask before closing; this post started without a category.
            foreach (var box in Descendants(recategorized).OfType<ComboBox>().Where(c => AutomationProperties.GetName(c) == "게시글 분류")) box.SelectedItem = null;
            CloseDiscarding(recategorized);
        }
        var submitted = BoardDialog.Writing(client, "검수"); submitted.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var listed = fixture.RequestedPages.Count;
            Descendants(submitted).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 제목").Text = "서버에 보낼 제목";
            Descendants(submitted).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "게시글 본문").Text = "서버에 보낼 본문";
            Descendants(submitted).OfType<Button>().Single(b => Equals(b.Content, "등록")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitAsync(() => !Descendants(submitted).OfType<PostEditorView>().Any() && fixture.RequestedPages.Count > listed);
            check(submitted.Changed && fixture.LastSaveBody.Contains("서버에 보낼 제목") && fixture.LastSaveBody.Contains("서버에 보낼 본문")
                && !fixture.LastSaveBody.Contains("한 줄로 무엇이 문제인지 적는다") && !fixture.LastSaveBody.Contains("12쪽 7줄에서"),
                "Submitting a post sends only the entered values, never the placeholder hints");
            check(fixture.LastSaveMethod == HttpMethod.Post && fixture.LastSaveCategory == "RESULT", "A new post sends the default 「점역 결과」 category as RESULT");
            check(submitted.IsVisible && fixture.RequestedPages[^1] == 1 && Descendants(submitted).OfType<DataGrid>().Single().IsVisible,
                "After 「등록」 the same board window shows page 1 of the list");
        }
        finally { CloseDiscarding(submitted); }
        var post = new BoardPost(42, "문의 제목", "서버의 본문", "작성자", true, false, false, "2026-10-21", "", 4, [new(3, "첨부.pdf", 1024, "https://example.invalid/attachment.pdf")], [new(1, "운영자 답변 내용", false, "2026-10-22", "")]);
        var detail = BoardDialog.Reading(client, post, "검수");
        check(detail.Width == 1440 && detail.Height == 1000 && detail.Title == "게시판 - 누리닷", "Post detail opens as the board window with the final Figma geometry and title");
        detail.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            CheckWorkAreaFit(detail, "Post detail", check);
            CheckBodyOnly(detail, "Post detail", check);
            var detailView = Descendants(detail).OfType<PostView>().Single();
            check(Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "작성자 · 2026-10-21 · 조회 4")
                && !Descendants(detail).OfType<TextBlock>().Any(t => t.Text.Contains("글쓴이와 운영자만 열람", StringComparison.Ordinal)),
                "Public post details do not claim that the post is restricted");
            check(Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "운영자 답변 내용"), "Post detail retains the actual operator answer");
            check(Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "운영자 답변" && t.FontSize == 14 && t.FontWeight == FontWeights.Bold)
                && Descendants(detail).OfType<TextBlock>().Any(t => t.Text == "첨부.pdf  1KB"), "Post detail uses the final answer heading and attachment size label");
            check(HasIcon(detail, "board-export.svg"), "Post attachments render the Figma download icon");
            var attachment = Descendants(detail).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "첨부.pdf 다운로드");
            check(attachment.ActualHeight == 32 && Math.Abs(attachment.ActualWidth - 268) < 2 && Equals(attachment.ToolTip, "첨부.pdf"),
                "Post attachment uses the Figma 268x32 surface and retains its filename for accessibility");
            var detailScroll = Descendants(detailView).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            var attachmentLeft = attachment.TransformToAncestor(detailScroll).Transform(new Point()).X;
            check(attachmentLeft >= -1 && attachmentLeft <= 2, $"Post attachment aligns with the left edge of the actual content (left={attachmentLeft:F1})");
            var answer = Descendants(detail).OfType<Border>().Single(b => b.Child is StackPanel p && Descendants(p).OfType<TextBlock>().Any(t => t.Text == "운영자 답변 내용"));
            check(answer.CornerRadius == new CornerRadius(6) && answer.Background is SolidColorBrush { Color: var c } && c == Color.FromRgb(249, 249, 249),
                "The actual operator answer uses the Figma rounded neutral card");
            if (Environment.GetEnvironmentVariable("BOARD_DETAIL_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(detail, capture);
            detail.Width = 480; detail.Height = 400; detail.UpdateLayout(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var postScroll = Descendants(detailView).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            var attachmentRect = attachment.TransformToAncestor(postScroll).TransformBounds(new Rect(0, 0, attachment.ActualWidth, attachment.ActualHeight));
            check(attachmentRect.Left >= -1 && attachmentRect.Right <= postScroll.ViewportWidth + 2 && attachment.IsEnabled,
                "The attachment download stays within a 480px detail window and remains enabled");
            var deleteRed = false;
            var declined = await AnswerAsync(detail, 1, () => Descendants(detail).OfType<Button>().Single(b => Equals(b.Content, "삭제")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
                notice => deleteRed = notice.ActionButtons[0].Background is SolidColorBrush { Color: var red } && red == Color.FromRgb(196, 43, 28));
            check(declined is { MessageText: "이 글을 삭제하시겠습니까?", DetailText: "문의 제목" } && declined.ActionButtons.Select(b => b.Content).SequenceEqual(new object[] { "삭제", "취소" })
                && deleteRed && fixture.DeleteRequests == 0 && detail.IsVisible && detailView.IsVisible,
                "Deleting a post asks in the app message window with a red 「삭제」, and 「취소」 keeps the post");
        }
        finally { detail.Close(); }
        var deleting = BoardDialog.Reading(client, post, "검수"); deleting.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var listed = fixture.RequestedPages.Count;
            var accepted = await AnswerAsync(deleting, 0, () => Descendants(deleting).OfType<Button>().Single(b => Equals(b.Content, "삭제")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
            await WaitAsync(() => !Descendants(deleting).OfType<PostView>().Any() && fixture.RequestedPages.Count > listed);
            check(accepted is not null && fixture.DeleteRequests == 1 && deleting.Changed && deleting.IsVisible && Descendants(deleting).OfType<DataGrid>().Single().IsVisible,
                "Choosing 「삭제」 in the message window deletes the post and shows the list in the same board window");
        }
        finally { deleting.Close(); }
        var secretDetail = BoardDialog.Reading(client, post with { Secret = true }, "검수"); secretDetail.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var secretByline = Descendants(secretDetail).OfType<TextBlock>().Single(t => t.Text.Contains("글쓴이와 운영자만 열람", StringComparison.Ordinal));
            check(secretByline.Text == "작성자 · 2026-10-21 · 조회 4 · 글쓴이와 운영자만 열람" && secretByline.FontSize == 12
                && secretByline.TextWrapping == TextWrapping.Wrap,
                "Secret post details display the Figma access explanation after the author, date and views, wrapping in narrow windows");
        }
        finally { secretDetail.Close(); }
        var longName = new string('가', 160) + ".pdf";
        var longPost = post with { Attachments = [new(4, longName, 1024, "https://example.invalid/long.pdf")] };
        var longDetail = BoardDialog.Reading(client, longPost, "검수"); longDetail.Show();
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
    // Figma D8 §8-2 · D8-2 §8-3: only the body changes; the board title, four sidebar items and status bar stay and the list is hidden.
    private static void CheckBodyOnly(BoardDialog board, string name, Action<bool, string> check)
    {
        var navigation = new[] { "전체 글", "공지", "내 글", "답변 받은 글" };
        check(board.Title == "게시판 - 누리닷" && navigation.All(label => Descendants(board).OfType<Button>().Count(b => Equals(b.Content, label) && b.IsVisible) == 1)
            && Descendants(board).OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "BoardStatusBar").IsVisible
            && !Descendants(board).OfType<DataGrid>().Single().IsVisible && Descendants(board).OfType<BoardBodyView>().Count(v => v.IsVisible) == 1,
            $"{name} replaces only the board body, keeping 「게시판 - 누리닷」, the four sidebar items and the status bar");
    }
    // A failed step must not leave a draft whose close confirmation would wait for an answer.
    private static void CloseDiscarding(BoardDialog board)
    {
        foreach (var view in Descendants(board).OfType<PostEditorView>().ToArray())
            foreach (var input in Descendants(view).OfType<TextBox>()) input.Clear();
        board.Close();
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    // Runs `open`, presses action `choice` of the app message window it opens over `owner`, and returns that window (null if none appeared).
    private static async Task<NoticeDialog?> AnswerAsync(Window owner, int choice, Action open, Action<NoticeDialog>? inspect = null)
    {
        NoticeDialog? shown = null; var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
        timer.Tick += (_, _) =>
        {
            if (Application.Current.Windows.OfType<NoticeDialog>().FirstOrDefault(d => d.Owner == owner && d.IsVisible) is not { } notice)
            { if (DateTime.UtcNow >= deadline) timer.Stop(); return; }
            timer.Stop(); notice.UpdateLayout(); inspect?.Invoke(notice); shown = notice;
            notice.ActionButtons[choice].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        timer.Start();
        try { open(); while (shown is null && timer.IsEnabled) await Task.Delay(20); }
        finally { timer.Stop(); }
        return shown;
    }
    private static bool HasIcon(DependencyObject root, string asset) => Descendants(root).OfType<SvgViewbox>().Any(i => i.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true && i.Width == 16 && i.Height == 16 && i.Drawings is { Bounds.IsEmpty: false });
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static async Task WaitAsync(Func<bool> predicate)
    { var until = DateTime.UtcNow.AddSeconds(5); while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20); if (!predicate()) throw new InvalidOperationException("Board fixture did not load"); }
    private sealed class Fixture : HttpMessageHandler
    {
        internal List<int> RequestedPages { get; } = [];
        internal List<bool> RequestedModes { get; } = [];
        internal List<bool> RequestedAnswered { get; } = [];
        internal int? FailPage { get; set; }
        internal bool? FailMine { get; set; }
        internal bool IncludeNotice { get; set; }
        internal int SaveRequests { get; private set; }
        internal int DeleteRequests { get; private set; }
        internal int PostReads { get; private set; }
        internal string LastSaveBody { get; private set; } = "";
        internal HttpMethod? LastSaveMethod { get; private set; }
        internal string? LastSaveCategory { get; private set; }
        internal static readonly string NoticeBody = string.Join("\n", Enumerable.Range(1, 16).Select(i => $"{i}. 점검 중에는 변환과 게시판을 사용할 수 없습니다."));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var page = 1; var mine = false; var searched = false; var answered = false;
            if (request.Method == HttpMethod.Delete) DeleteRequests++;
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/board/posts/42") PostReads++;
            // 등록 (POST) and 수정 (PUT) send multipart; the category part is absent when none was chosen.
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/api/board/posts" || request.Method == HttpMethod.Put && request.RequestUri!.AbsolutePath == "/api/board/posts/42")
            {
                SaveRequests++; LastSaveMethod = request.Method; LastSaveBody = await request.Content!.ReadAsStringAsync(token);
                LastSaveCategory = ((MultipartFormDataContent)request.Content).FirstOrDefault(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "category") is { } part ? await part.ReadAsStringAsync(token) : null;
                if (request.Method == HttpMethod.Post)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":{\"postId\":43}}", Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/api/board/posts")
            {
                var value = request.RequestUri.Query.TrimStart('?').Split('&').FirstOrDefault(v => v.StartsWith("page=", StringComparison.Ordinal));
                if (value is not null && int.TryParse(value.AsSpan(5), out var parsed)) page = parsed;
                RequestedPages.Add(page);
                mine = request.RequestUri.Query.Contains("mine=true", StringComparison.OrdinalIgnoreCase); RequestedModes.Add(mine);
                answered = request.RequestUri.Query.Contains("answered=true", StringComparison.OrdinalIgnoreCase); RequestedAnswered.Add(answered);
                searched = request.RequestUri.Query.TrimStart('?').Split('&').Any(v => v.StartsWith("keyword=", StringComparison.Ordinal) && v.Length > "keyword=".Length);
                if (FailMine == mine)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"FIXTURE\",\"message\":\"fixture view failure\"}", Encoding.UTF8, "application/json") };
                if (FailPage == page)
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"FIXTURE\",\"message\":\"fixture page failure\"}", Encoding.UTF8, "application/json") };
            }
            // Like the server, notices come only with the unsearched full list. With answered=true the server pages
            // 13 answered posts (120–111, 110–108; anyone's public posts) and sends no notices.
            var answeredPage = Enumerable.Range(0, Math.Clamp(13 - (page - 1) * 10, 0, 10)).Select(i => 120 - (page - 1) * 10 - i).ToArray();
            var items = answered
                ? answeredPage.Select(id => new { postId = id, title = $"답변 받은 문의 {id}", category = (string?)"RESULT", answerCount = 1, author = id % 2 == 0 ? "fixture" : "test***", secret = false, createdAt = "2026-10-21", viewCount = 4, status = "ANSWERED" }).ToArray()
                : new[] { new { postId = 42, title = "문의 제목", category = (string?)null, answerCount = 1, author = "작성자", secret = false, createdAt = "2026-10-21", viewCount = 4, status = "ANSWERED" } };
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/api/auth/login" => new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" },
                "/api/board/posts" => new { page, totalCount = answered ? 13 : 23, notices = IncludeNotice && !mine && !searched && !answered ? new object[] { new { postId = 0, title = "서버 점검 안내", answerCount = 0, author = "운영자", secret = false, createdAt = "2026-10-21", viewCount = 4, noticeId = "notice-1" } } : Array.Empty<object>(), items },
                "/api/board/notices/notice-1" => new { title = "서버 점검 안내", body = NoticeBody },
                "/api/board/posts/42" => new { postId = 42, title = "문의 제목", content = "서버의 본문", category = request.Method == HttpMethod.Put ? LastSaveCategory : "BILLING", author = "작성자", mine = true, secret = false, hidden = false, createdAt = "2026-10-21", updatedAt = "", viewCount = 4, attachments = Array.Empty<object>(), answers = Array.Empty<object>(), status = "WAITING" },
                "/api/board/notifications" => new { unreadCount = 1, items = new[] { new { notificationId = 1, postId = 42, title = "문의 제목", answerCount = 1, lastAnsweredAt = "2026-10-22", read = false } } },
                _ => new { }
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
        }
    }
}
