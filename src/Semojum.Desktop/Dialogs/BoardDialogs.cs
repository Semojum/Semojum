using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Automation;
using Microsoft.Win32;
using Semojum.Core.Integration;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;

internal static class BoardDesign
{
    internal static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
    internal static SvgViewbox Icon(string name) => new() { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/board-{name}.svg"), Width = 16, Height = 16, Stretch = Stretch.None };
    internal static DockPanel Shell(Window window, DockPanel root, string? loginId, out StackPanel sidebar)
    {
        if (window.Content is Grid frame && frame.Children[0] is Border { Child: DockPanel heading })
        {
            if (heading.Children.OfType<StackPanel>().FirstOrDefault() is { } buttons)
            {
                buttons.Margin = new(0);
                foreach (var (button, asset) in buttons.Children.OfType<Button>().Zip(new[] { "caption-min", "caption-max", "caption-close" })) { button.Width = 46; button.Height = 32; button.Content = Icon(asset); }
            }
            if (heading.Children.OfType<Border>().FirstOrDefault() is { } logo)
            { logo.Width = 16; logo.CornerRadius = new(4); logo.Background = Brush("#005FB8"); logo.Margin = new(12, 0, 8, 0); if (logo.Child is TextBlock label) { label.Text = "S"; label.FontFamily = new FontFamily("Segoe UI Symbol"); } }
        }
        var shell = new DockPanel { Background = Brush("#F4F6F9") }; root.Children.Add(shell);
        var menu = new Menu { Height = 32, Background = Brush("#F3F3F3"), Padding = new(4, 0, 0, 0) };
        var file = new MenuItem { Header = "파일(_F)", Width = 64 }; var close = new MenuItem { Header = "닫기" }; close.Click += (_, _) => window.Close(); file.Items.Add(close); menu.Items.Add(file);
        var view = new MenuItem { Header = "보기(_V)", Width = 64 }; menu.Items.Add(view);
        var information = new MenuItem { Header = "정보(_A)", Width = 64 }; var help = new MenuItem { Header = "도움말" }; help.Click += (_, _) => new HelpWindow("게시판") { Owner = window }.Show(); information.Items.Add(help); menu.Items.Add(information);
        DockPanel.SetDock(menu, Dock.Top); shell.Children.Add(menu);
        var status = new Border { Height = 28, Background = Brush("#F3F3F3"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 1, 0, 0),
            Child = new TextBlock { Text = loginId ?? "", FontSize = 12, Foreground = Brush("#5F5F5F"), Margin = new(12, 0, 24, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
        DockPanel.SetDock(status, Dock.Bottom); shell.Children.Add(status);
        sidebar = new StackPanel { Margin = new(0, 14, 0, 0) };
        var navigation = new Border { Width = 220, Background = Brush("#F9F9F9"), BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 0, 1, 0), Child = sidebar };
        DockPanel.SetDock(navigation, Dock.Left); shell.Children.Add(navigation);
        var content = new DockPanel { Margin = new(24, 20, 24, 20) }; shell.Children.Add(content); return content;
    }
    internal static Button Navigation(string label, bool selected, Action? action = null)
    {
        var button = new Button { Content = label, Height = 32, MinHeight = 0, BorderThickness = new(0), Background = Brush(selected ? "#EFF6FC" : "#F9F9F9"), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new(16, 0, 0, 0), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        var surface = new FrameworkElementFactory(typeof(Border)); surface.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background)) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); content.SetValue(FrameworkElement.MarginProperty, new Thickness(16, 0, 0, 0)); surface.AppendChild(content);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = surface };
        SetNavigationSelected(button, selected);
        if (action is null) { button.IsEnabled = false; button.ToolTip = "현재 서버에서 이 목록을 제공하지 않습니다."; ToolTipService.SetShowOnDisabled(button, true); } else button.Click += (_, _) => action(); return button;
    }
    internal static void SetNavigationSelected(Button button, bool selected)
    {
        button.Background = Brush(selected ? "#EFF6FC" : "#F9F9F9");
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        AutomationProperties.SetItemStatus(button, selected ? "선택됨" : "선택 안 됨");
    }
    internal static Grid InputWithHint(TextBox input, string hintText, string name, string hintId, Thickness margin, bool multiline = false)
    {
        var host = new Grid { Margin = margin };
        input.Margin = new Thickness(0);
        AutomationProperties.SetName(input, name);
        host.Children.Add(input);
        var hint = new TextBlock { Text = hintText, FontSize = 12, Foreground = Brush("#8A8A8A"), IsHitTestVisible = false,
            Margin = multiline ? new Thickness(8, 8, 8, 0) : new Thickness(8, 0, 8, 0),
            VerticalAlignment = multiline ? VerticalAlignment.Top : VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(hint, hintId);
        host.Children.Add(hint);
        void UpdateHint() => hint.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        input.TextChanged += (_, _) => UpdateHint();
        UpdateHint();
        return host;
    }
    internal static Border Card(UIElement child, Thickness padding) => new() { Child = child, Padding = padding, Background = Brushes.White, BorderBrush = Brush("#EAEAEA"), BorderThickness = new(1), CornerRadius = new(8) };
    internal static Style CheckStyle()
    {
        var style = new Style(typeof(CheckBox), Application.Current.FindResource(typeof(CheckBox)) as Style);
        var panel = new FrameworkElementFactory(typeof(StackPanel)); panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var square = new FrameworkElementFactory(typeof(Border), "Square"); square.SetValue(FrameworkElement.WidthProperty, 20d); square.SetValue(FrameworkElement.HeightProperty, 20d); square.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); square.SetValue(Border.BorderThicknessProperty, new Thickness(1)); square.SetValue(Border.BorderBrushProperty, Brush("#D1D1D1")); square.SetValue(Border.BackgroundProperty, Brush("#FBFBFB"));
        var icon = new FrameworkElementFactory(typeof(SvgViewbox), "Tick"); icon.SetValue(SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/board-check.svg")); icon.SetValue(FrameworkElement.WidthProperty, 16d); icon.SetValue(FrameworkElement.HeightProperty, 16d); icon.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); square.AppendChild(icon); panel.AppendChild(square);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); panel.AppendChild(content);
        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = panel }; var selected = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#005FB8"), "Square")); selected.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#005FB8"), "Square")); selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Tick")); template.Triggers.Add(selected);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Square")); template.Triggers.Add(focus);
        style.Setters.Add(new Setter(Control.TemplateProperty, template)); return style;
    }
}

public sealed class BoardDialog : CompactDialog
{
    public BoardDialog(BoardClient client, string workInfo, bool initiallyMine = false) : base("게시판 - 세모점", 1440, 1000, editor: true, editorActions: true)
    {
        Background = Brushes.White;
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, SelectionMode = DataGridSelectionMode.Single, RowHeight = 32, Style = (Style)FindResource("BoardGrid") };
        foreach (var (label, name, width) in new[] { ("번호", "Number", 70d), ("제목", "DisplayTitle", 570d), ("글쓴이", "Author", 120d), ("작성일", "CreatedAt", 100d), ("조회", "ViewCount", 80d) })
            grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(name), Width = width });
        var previousFooter = (Border)Actions.Parent; previousFooter.Child = null; previousFooter.Visibility = Visibility.Collapsed;
        var root = BoardDesign.Shell(this, Root, client.CurrentLoginId, out var sidebar);
        var heading = new DockPanel { Height = 40 }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var headerButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(headerButtons, Dock.Right); heading.Children.Add(headerButtons);
        var count = new TextBlock { Margin = new(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Muted") };
        var title = new StackPanel { Orientation = Orientation.Horizontal }; title.Children.Add(new TextBlock { Text = initiallyMine ? "내 글" : "전체 글", FontSize = 20, FontWeight = FontWeights.SemiBold }); title.Children.Add(count); heading.Children.Add(title);
        Actions.HorizontalAlignment = HorizontalAlignment.Left; Actions.Margin = new(0, 12, 0, 0); DockPanel.SetDock(Actions, Dock.Bottom); root.Children.Add(Actions);
        root.Children.Add(BoardDesign.Card(grid, new(19, 15, 19, 15)));
        var fields = Choices(["제목+내용", "제목", "내용", "글쓴이"], "제목+내용"); fields.Width = 120; fields.Height = fields.MinHeight = 32; fields.Padding = new(8, 0, 25, 0);
        var query = new TextBox { Width = 220, Height = 32, MinHeight = 32, MaxLength = 100, Margin = new(8, 0, 0, 0), Padding = new(7, 0, 7, 0) };
        System.Windows.Automation.AutomationProperties.SetName(query, "게시판 검색어");
        var fieldsView = new Grid { Width = 120, Height = 32 }; fieldsView.Children.Add(fields); fieldsView.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = BoardDesign.Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var chevron = BoardDesign.Icon("chevron-down"); chevron.HorizontalAlignment = HorizontalAlignment.Right; chevron.Margin = new(0, 0, 5, 0); chevron.IsHitTestVisible = false; fieldsView.Children.Add(chevron);
        Actions.Children.Add(fieldsView); Actions.Children.Add(query);
        int page = 1, total = 0; bool mine = initiallyMine, loadingList = false;
        var pageInput = new TextBox { Width = 44, Height = 24, MinHeight = 0, Padding = new(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(pageInput, "게시판 쪽 번호");
        var pageTotal = new TextBlock { Foreground = BoardDesign.Brush("#5F5F5F"), Margin = new(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        var numberButtons = new StackPanel { Orientation = Orientation.Horizontal };
        Button? previous = null, next = null;
        int TotalPages() => Math.Max(1, Math.Max(0, total) / 10 + (total > 0 && total % 10 != 0 ? 1 : 0));
        // A delayed list request must not trap the user in the dialog. Other
        // operations keep the shared busy guard to protect pending edits.
        Closing += (_, e) => { if (loadingList) { e.Cancel = false; Lifetime.Cancel(); } };
        async Task LoadAsync()
        {
            loadingList = true;
            try
            {
                var field = fields.SelectedIndex switch { 1 => "TITLE", 2 => "CONTENT", 3 => "AUTHOR", _ => "TITLE_CONTENT" };
                var data = await client.ListAsync(page, field, query.Text, mine, Lifetime.Token);
                if (Lifetime.IsCancellationRequested) return;
                total = data.TotalCount; grid.ItemsSource = data.Notices.Concat(data.Items).ToArray();
                count.Text = $"{total}건";
                ((TextBlock)title.Children[0]).Text = mine ? "내 글" : "전체 글";
                if (sidebar.Children.Count >= 3) { BoardDesign.SetNavigationSelected((Button)sidebar.Children[0], !mine); BoardDesign.SetNavigationSelected((Button)sidebar.Children[2], mine); }
                RenderPages();
            }
            finally { loadingList = false; }
        }
        async Task GoPageAsync(int target)
        {
            if (target < 1 || target > TotalPages()) { Status.Text = $"1부터 {TotalPages()}까지의 쪽 번호를 입력해 주세요."; RenderPages(); return; }
            var oldPage = page; page = target;
            try { await LoadAsync(); }
            catch { page = oldPage; RenderPages(); throw; }
        }
        async Task SwitchListAsync(bool targetMine)
        {
            var oldMine = mine; var oldPage = page;
            mine = targetMine; page = 1;
            try { await LoadAsync(); }
            catch { mine = oldMine; page = oldPage; RenderPages(); throw; }
        }
        void RenderPages()
        {
            pageInput.Text = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pageTotal.Text = "/ " + TotalPages().ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (previous is not null) previous.IsEnabled = page > 1;
            if (next is not null) next.IsEnabled = page < TotalPages();
            numberButtons.Children.Clear();
            var first = Math.Max(1, Math.Min(page - 1, Math.Max(1, TotalPages() - 2)));
            for (var n = first; n <= Math.Min(TotalPages(), first + 2); n++)
            {
                var target = n;
                var button = Button(n.ToString(System.Globalization.CultureInfo.InvariantCulture), async () => await RunAsync(() => GoPageAsync(target)));
                PublicDialogDesign.StyleAction(button, target == page);
                button.Width = 24; button.Height = 24; button.MinWidth = 0; button.MinHeight = 0; button.Padding = new(0); button.Margin = new(0, 0, 2, 0);
                System.Windows.Automation.AutomationProperties.SetName(button, $"게시판 {n}쪽");
                numberButtons.Children.Add(button);
            }
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
        Action("검색", async () => await GoPageAsync(1));
        var mineButton = Action("내 글 보기", async () => await SwitchListAsync(!mine)); mineButton.Width = 94; mineButton.Margin = new(0);
        var notifications = Action("알림", () => new NotificationsDialog(client, workInfo) { Owner = this }.ShowDialog());
        notifications.Margin = new(8, 0, 0, 0);
        var write = Action("글쓰기", async () => { var d = new PostEditorDialog(client, null, workInfo) { Owner = this }; if (d.ShowDialog() == true) await GoPageAsync(1); }, true); write.Margin = new(16, 0, 0, 0);
        foreach (var button in new[] { mineButton, notifications, write }) { Actions.Children.Remove(button); PublicDialogDesign.StyleAction(button, button == write); button.IsDefault = false; headerButtons.Children.Add(button); }
        sidebar.Children.Add(BoardDesign.Navigation("전체 글", !initiallyMine, async () => await RunAsync(() => SwitchListAsync(false))));
        sidebar.Children.Add(BoardDesign.Navigation("공지", false));
        sidebar.Children.Add(BoardDesign.Navigation("내 글", initiallyMine, async () => await RunAsync(() => SwitchListAsync(true))));
        sidebar.Children.Add(BoardDesign.Navigation("답변 받은 글", false));
        // Paging and notifications remain accessible when the window is resized.
        var navigation = new StackPanel { Orientation = Orientation.Horizontal }; var pageMenu = new ContextMenu(); pageMenu.Items.Add(new MenuItem { Header = navigation, StaysOpenOnClick = true }); grid.ContextMenu = pageMenu;
        count.ToolTip = "이전·다음 쪽: 목록에서 마우스 오른쪽 단추 또는 PageUp·PageDown";
        navigation.Children.Add(Button("이전", async () => await RunAsync(async () => { if (page > 1) await GoPageAsync(page - 1); })));
        navigation.Children.Add(Button("다음", async () => await RunAsync(async () => { if (page < TotalPages()) await GoPageAsync(page + 1); })));
        previous = Button("이전", async () => await RunAsync(() => GoPageAsync(page - 1)));
        previous.Content = BoardDesign.Icon("chevron-left"); previous.ToolTip = "이전 쪽"; System.Windows.Automation.AutomationProperties.SetName(previous, "이전 쪽"); PublicDialogDesign.StyleAction(previous); previous.Width = 24; previous.MinWidth = 0; previous.Height = 24; previous.MinHeight = 0; previous.Padding = new(0); previous.Margin = new(0, 0, 4, 0);
        next = Button("다음", async () => await RunAsync(() => GoPageAsync(page + 1)));
        next.Content = BoardDesign.Icon("chevron-right"); next.ToolTip = "다음 쪽"; System.Windows.Automation.AutomationProperties.SetName(next, "다음 쪽"); PublicDialogDesign.StyleAction(next); next.Width = 24; next.MinWidth = 0; next.Height = 24; next.MinHeight = 0; next.Padding = new(0); next.Margin = new(2, 0, 0, 0);
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        pager.Children.Add(previous); pager.Children.Add(pageInput); pager.Children.Add(pageTotal); pager.Children.Add(numberButtons); pager.Children.Add(next);
        // Keep page navigation centered in the remaining footer space; on small windows
        // the whole footer can scroll horizontally so neither search nor paging is lost.
        root.Children.Remove(Actions); Actions.Margin = new(0);
        var footer = new Grid { MinWidth = 650 };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.Children.Add(Actions); Grid.SetColumn(pager, 1); pager.HorizontalAlignment = HorizontalAlignment.Center; footer.Children.Add(pager);
        var footerScroll = new ScrollViewer { Content = footer, Margin = new(0, 12, 0, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        System.Windows.Automation.AutomationProperties.SetName(footerScroll, "게시판 검색 및 쪽 이동");
        DockPanel.SetDock(footerScroll, Dock.Bottom); root.Children.Insert(1, footerScroll);
        pageInput.KeyDown += async (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            e.Handled = true;
            if (!int.TryParse(pageInput.Text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var target))
            { Status.Text = "쪽 번호를 숫자로 입력해 주세요."; RenderPages(); return; }
            await RunAsync(() => GoPageAsync(target));
        };
        grid.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await RunAsync(OpenAsync); }
            else if (e.Key is System.Windows.Input.Key.PageDown or System.Windows.Input.Key.PageUp)
            { e.Handled = true; await RunAsync(async () => { if (e.Key == System.Windows.Input.Key.PageDown && page < TotalPages()) await GoPageAsync(page + 1); else if (e.Key == System.Windows.Input.Key.PageUp && page > 1) await GoPageAsync(page - 1); }); }
        };
        OnLoad(LoadAsync);
    }
}

public sealed class PostEditorDialog : CompactDialog
{
    private sealed record AttachmentChoice(string Name, long? ExistingId, string? LocalPath) { public override string ToString() => Name; }
    public BoardPost? SavedPost { get; private set; }
    public long SavedId { get; private set; }
    public PostEditorDialog(BoardClient client, BoardPost? post, string workInfo) : base(post is null ? "게시판 — 글쓰기 - 세모점" : "게시판 — 글 수정 - 세모점", 1440, 1000, editor: true, editorActions: true)
    {
        Background = Brushes.White; var oldFooter = (Border)Actions.Parent; oldFooter.Child = null; oldFooter.Visibility = Visibility.Collapsed;
        var root = BoardDesign.Shell(this, Root, client.CurrentLoginId, out var sidebar); sidebar.Children.Add(new TextBlock { Text = "전체 글", Height = 32, Background = BoardDesign.Brush("#EFF6FC"), FontWeight = FontWeights.SemiBold, Padding = new(16, 0, 0, 0) });
        var heading = new TextBlock { Text = post is null ? "글쓰기" : "글 수정", FontSize = 20, FontWeight = FontWeights.SemiBold, Height = 40 }; DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var card = new DockPanel(); Actions.HorizontalAlignment = HorizontalAlignment.Right; Actions.Margin = new(0, 16, 0, 0); DockPanel.SetDock(Actions, Dock.Bottom); card.Children.Add(Actions);
        var panel = new StackPanel(); card.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); root.Children.Add(BoardDesign.Card(card, new(19, 15, 19, 19)));
        panel.Children.Add(new TextBlock { Text = "분류", Height = 16, Foreground = (Brush)FindResource("Muted") });
        var category = Choices(["지원하지 않음"], "지원하지 않음"); category.Width = 220; category.Height = 32; category.MinHeight = 32; category.HorizontalAlignment = HorizontalAlignment.Left;
        category.Margin = new(0, 2, 0, 10); category.IsEnabled = false; category.ToolTip = "현재 서버는 게시글 분류를 제공하지 않습니다.";
        ToolTipService.SetShowOnDisabled(category, true); System.Windows.Automation.AutomationProperties.SetName(category, "게시글 분류");
        System.Windows.Automation.AutomationProperties.SetHelpText(category, "현재 서버는 게시글 분류를 제공하지 않습니다."); panel.Children.Add(category);
        var title = new TextBox { Text = post?.Title ?? "", MaxLength = 100, Height = 32 }; panel.Children.Add(new TextBlock { Text = "제목", Height = 16, Foreground = (Brush)FindResource("Muted") });
        panel.Children.Add(BoardDesign.InputWithHint(title, "한 줄로 무엇이 문제인지 적는다", "게시글 제목", "BoardTitlePlaceholder", new Thickness(0, 6, 0, 0)));
        var content = new TextBox { Text = post?.Content ?? "", MaxLength = 10000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 220, VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "본문", Height = 16, Foreground = (Brush)FindResource("Muted"), Margin = new(0, 12, 0, 0) });
        panel.Children.Add(BoardDesign.InputWithHint(content, "12쪽 7줄에서 괄호가 한 칸 밀려 나옵니다. 묵자에는 붙어 있는데…", "게시글 본문", "BoardContentPlaceholder", new Thickness(0, 2, 0, 12), multiline: true));
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Height = 24 };
        var checkStyle = BoardDesign.CheckStyle();
        var secret = new CheckBox { Content = "비밀글", IsChecked = post?.Secret == true, Width = 140, Height = 20, VerticalAlignment = VerticalAlignment.Center, Padding = new(0), FontSize = 14, Style = checkStyle };
        var info = new CheckBox { Content = "현재 작업 정보 첨부", ToolTip = "원본·편집 내용은 포함되지 않습니다.", Height = 20, VerticalAlignment = VerticalAlignment.Center, Padding = new(0), FontSize = 14, IsChecked = post is null, Style = checkStyle }; flags.Children.Add(secret); flags.Children.Add(info); panel.Children.Add(flags);
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
        var choose = (Button)fileActions.Children[0]; PublicDialogDesign.StyleAction(choose); choose.Margin = new(0); choose.Width = 110;
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
        var cancel = Action("취소", () => DialogResult = false); Actions.Children.Remove(cancel); Actions.Children.Insert(0, cancel);
        foreach (Button button in Actions.Children) { PublicDialogDesign.StyleAction(button, button.IsDefault); button.Width = 84; }
    }
}

public sealed class PostDialog : CompactDialog
{
    public PostDialog(BoardClient client, BoardPost post, string workInfo) : base("게시판 — " + post.Title + " - 세모점", 1440, 1000, editor: true)
    {
        Background = Brushes.White; var panel = new StackPanel();
        var oldFooter = (Border)Actions.Parent; oldFooter.Child = null; oldFooter.Visibility = Visibility.Collapsed;
        var root = BoardDesign.Shell(this, Root, client.CurrentLoginId, out var sidebar); sidebar.Children.Add(new TextBlock { Text = "전체 글", Height = 32, Background = BoardDesign.Brush("#EFF6FC"), FontWeight = FontWeights.SemiBold, Padding = new(16, 0, 0, 0) });
        Actions.Margin = new(0, 0, 0, 8); Actions.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(Actions, Dock.Top); root.Children.Add(Actions);
        root.Children.Add(BoardDesign.Card(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, new(19, 15, 19, 15)));
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
                download.MinWidth = 0; download.MaxWidth = 268; download.Height = download.MinHeight = 32; download.HorizontalAlignment = HorizontalAlignment.Stretch;
                download.Margin = new(0, 10, 0, 0); download.Padding = new(8, 0, 8, 0);
                download.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                var attachment = new Grid(); attachment.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); attachment.ColumnDefinitions.Add(new ColumnDefinition());
                var export = BoardDesign.Icon("export"); export.Margin = new(0, 0, 6, 0); attachment.Children.Add(export);
                var fileName = new TextBlock { Text = $"{a.FileName} ({a.Size / 1024d:0.#} KB)", FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(fileName, 1); attachment.Children.Add(fileName);
                download.Content = attachment; download.ToolTip = a.FileName; System.Windows.Automation.AutomationProperties.SetName(download, a.FileName + " 다운로드");
                var attachmentRow = new Grid(); attachmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 268 });
                attachmentRow.Children.Add(download); panel.Children.Add(attachmentRow);
            }
            foreach (var answer in post.Answers)
            {
                var reply = new StackPanel(); var byline = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 4, 4) };
                byline.Children.Add(new TextBlock { Text = "운영자 답변", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 10, 0) });
                byline.Children.Add(new TextBlock { Text = answer.AnsweredAt, FontSize = 12, Foreground = (Brush)FindResource("Muted") }); reply.Children.Add(byline);
                reply.Children.Add(new TextBlock { Text = answer.Content, FontSize = 12, LineHeight = 18, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(new Border { BorderThickness = new(3, 0, 0, 0), BorderBrush = (Brush)FindResource("Accent"), Background = new SolidColorBrush(Color.FromRgb(249, 249, 249)), CornerRadius = new(6), Margin = new(0, 16, 0, 0), Padding = new(9, 8, 9, 10), Child = reply });
            }
        }
        Render();
        Action("목록", Close);
        if (post.Mine)
        {
            Action("수정", () => { var d = new PostEditorDialog(client, post, workInfo) { Owner = this }; if (d.ShowDialog() == true && d.SavedPost is { } changed) { post = changed; Title = "게시판 — " + post.Title; Render(); } });
            Action("삭제", async () => { if (MessageBox.Show(this, "이 글을 삭제하시겠습니까?", "글 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return; await client.DeleteAsync(post.PostId, Lifetime.Token); DialogResult = true; });
        }
        foreach (Button button in Actions.Children) { PublicDialogDesign.StyleAction(button); button.Width = 56; }
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
    public NotificationsDialog(BoardClient client, string workInfo) : base("알림", 400, 278, popover: true)
    {
        var frame = PublicDialogDesign.Frame(this, "알림", "board-notification-close.svg", "BoardNotificationsClose", out var actions); Body(frame);
        var list = new ListBox { Margin = new(16, 38, 16, 12), BorderThickness = new(0), Padding = new(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        StripListPadding(list);
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)); list.ItemContainerStyle = itemStyle;
        list.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(NotificationView)) };
        var content = new Grid(); var empty = new TextBlock { Text = "새 알림이 없습니다.", HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        content.Children.Add(list); content.Children.Add(empty); Grid.SetRow(content, 1); frame.Children.Add(content);
        var loadingList = false;
        Closing += (_, e) => { if (loadingList) { e.Cancel = false; Lifetime.Cancel(); } };
        async Task LoadAsync()
        {
            loadingList = true;
            try
            {
                var n = await client.NotificationsAsync(Lifetime.Token);
                if (Lifetime.IsCancellationRequested) return;
                list.ItemsSource = n.Items; list.ToolTip = $"안 읽음 {n.UnreadCount}";
                empty.Visibility = n.Items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally { loadingList = false; }
        }
        var refresh = Button("새로고침", () => RunAsync(LoadAsync)); PublicDialogDesign.StyleAction(refresh); actions.Children.Add(refresh);
        var readAll = Button("모두 읽음", () => RunAsync(async () => { await client.ReadAllAsync(Lifetime.Token); await LoadAsync(); })); PublicDialogDesign.StyleAction(readAll); readAll.Width = 96; actions.Children.Add(readAll);
        var open = Button("열기", () => RunAsync(async () => { if (list.SelectedItem is BoardNotification n) { new PostDialog(client, await client.ReadAsync(n.PostId, Lifetime.Token), workInfo) { Owner = this }.ShowDialog(); await LoadAsync(); } }), true); PublicDialogDesign.StyleAction(open, true); actions.Children.Add(open);
        OnLoad(LoadAsync);
    }
}

public sealed class NotificationView : ContentControl
{
    public NotificationView() => DataContextChanged += (_, _) =>
    {
        if (DataContext is not BoardNotification item) return;
        var row = new DockPanel { Height = 42 };
        row.Children.Add(new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/board-note.svg"), Width = 16, Height = 16, Stretch = Stretch.None, Margin = new(0, 4, 8, 0), VerticalAlignment = VerticalAlignment.Top });
        var text = new StackPanel(); row.Children.Add(text);
        text.Children.Add(new TextBlock { Text = $"{item.PostId}. {item.Title}", FontWeight = item.Read ? FontWeights.Normal : FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = $"운영자 답변 {item.AnswerCount}건 · {item.LastAnsweredAt}", Foreground = (Brush)FindResource("Muted"), Margin = new(0, 3, 0, 0) });
        Content = new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Margin = new(0, 0, 0, 5), Child = row };
    };
}
