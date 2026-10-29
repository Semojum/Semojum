using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Automation;
using Semojum.Core;
using SharpVectors.Converters;

namespace Semojum.Desktop.Dialogs;
public sealed class MemoThreadDialog : CompactDialog
{
    internal ListBox MemoItems { get; } = new() { BorderThickness = new(0), Background = Brushes.White, Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
    internal CheckBox ShowResolved { get; } = new() { Content = "해결한 메모 포함" };
    internal Button ResolveButton { get; }
    internal Button ReplyButton { get; }
    internal TextBox ReplyInput { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 32, MaxLength = 2000, Padding = new(11, 7, 11, 7), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    public NavigationLocation? Destination { get; private set; }
    public MemoThreadDialog(EditorBlock block, string panel, int line, string lineText, Action<Action> edit,
        DocumentSession? model = null, bool showResolved = false, string? author = null) : base("메모", 360, 232, popover: true)
    {
        var body = new DockPanel { Margin = new(11, 7, 11, 11) }; Body(body);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Height = 24 };
        DockPanel.SetDock(toolbar, Dock.Top); body.Children.Add(toolbar);
        var input = ReplyInput;
        var replyHost = new Grid { Margin = new(0, 14, 0, 0), Height = 32 };
        replyHost.ColumnDefinitions.Add(new()); replyHost.ColumnDefinitions.Add(new() { Width = new(40) }); replyHost.Children.Add(input);
        var placeholder = new TextBlock { Text = "회신...", Foreground = Brushes.Gray, Margin = new(12, 7, 0, 0), IsHitTestVisible = false };
        replyHost.Children.Add(placeholder); input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DockPanel.SetDock(replyHost, Dock.Bottom); body.Children.Add(replyHost);
        System.Windows.Automation.AutomationProperties.SetName(input, "메모 회신");
        input.ToolTip = "Enter: 회신 추가 · Shift+Enter: 줄 바꿈";
        var list = MemoItems; ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); body.Children.Add(list);
        var conversation = new FrameworkElementFactory(typeof(MemoConversationView));
        conversation.SetValue(MemoConversationView.DeleteActionProperty, (Action<LineMemo, int?>)Delete);
        conversation.SetValue(MemoConversationView.ResolveActionProperty, (Action<LineMemo>)Resolve);
        if (model is not null) conversation.SetBinding(MemoConversationView.CanEditProperty, new Binding(nameof(DocumentSession.IsReadOnly)) { Source = model, Converter = InverseReadOnly.Instance });
        list.ItemTemplate = new DataTemplate { VisualTree = conversation };
        var container = new Style(typeof(ListBoxItem));
        container.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        container.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        var host = new FrameworkElementFactory(typeof(ContentPresenter)); host.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        container.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = host }));
        list.ItemContainerStyle = container;
        void Refresh()
        {
            var selected = (list.SelectedItem as LineMemo)?.LocalId;
            list.ItemsSource = block.Memos.Where(m => m.Panel == panel && m.LineIndex == line && (ShowResolved.IsChecked == true || !m.Resolved)).OrderBy(m => m.CreatedAt).ToArray();
            list.SelectedItem = list.Items.Cast<LineMemo>().FirstOrDefault(m => m.LocalId == selected) ?? list.Items.Cast<LineMemo>().FirstOrDefault();
            list.ToolTip = $"{lineText}\n해결 {block.Memos.Count(m => m.Panel == panel && m.LineIndex == line && m.Resolved)}개 · 우클릭: 메모 관리";
        }
        void Delete(LineMemo shown, int? replyIndex)
        {
            if (model is null || model.IsReadOnly || !model.Blocks.Contains(block)) return;
            var index = block.Memos.IndexOf(shown);
            if (index < 0 || !ReferenceEquals(block.Memos[index], shown)) { Status.Text = "메모가 변경되었습니다. 목록을 다시 확인해 주세요."; Refresh(); return; }
            if (replyIndex is { } reply)
            {
                if (shown.Replies is null || reply < 0 || reply >= shown.Replies.Length) return;
                edit(() => block.Memos[index] = shown with { Replies = shown.Replies.Where((_, i) => i != reply).ToArray() });
            }
            else edit(() => block.Memos.Remove(shown));
            Refresh();
        }
        void Resolve(LineMemo shown)
        {
            if (model is null || model.IsReadOnly || !model.Blocks.Contains(block)) return;
            var index = block.Memos.IndexOf(shown);
            if (index < 0 || !ReferenceEquals(block.Memos[index], shown)) { Status.Text = "메모가 변경되었습니다. 목록을 다시 확인해 주세요."; Refresh(); return; }
            edit(() => block.Memos[index] = shown with { Resolved = !shown.Resolved, ResolvedAt = shown.Resolved ? null : DateTimeOffset.Now });
            if (!shown.Resolved) Close(); else Refresh();
        }
        Button Tool(string glyph, string label, Action action)
        {
            var button = Action(glyph, action); Actions.Children.Remove(button);
            button.IsDefault = false; button.MinWidth = button.Width = 24; button.Height = 24; button.Padding = new(0); button.Margin = new(2, 0, 0, 0); button.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(button, label); toolbar.Children.Add(button); return button;
        }
        void Move(int delta)
        {
            if (model is null) return;
            var anchors = model.Blocks.SelectMany(b => b.Memos.Where(m => ShowResolved.IsChecked == true || !m.Resolved)
                .Select(m => (Block: b, m.Panel, m.LineIndex))).Distinct()
                .OrderBy(a => a.Block.Page).ThenBy(a => model.Blocks.IndexOf(a.Block)).ThenBy(a => a.Panel).ThenBy(a => a.LineIndex).ToList();
            var index = anchors.FindIndex(a => a.Block == block && a.Panel == panel && a.LineIndex == line);
            var next = index < 0 ? (delta > 0 ? 0 : anchors.Count - 1) : index + delta;
            if (next < 0 || next >= anchors.Count) { Status.Text = delta > 0 ? "마지막 메모입니다." : "첫 메모입니다."; return; }
            var a = anchors[next]; var targetPanel = a.Panel == "TEXT" ? SearchPanel.Text : SearchPanel.Braille;
            var offsets = DocumentNavigation.LineOffsets(DocumentSearch.Content(a.Block, targetPanel));
            if (a.LineIndex >= offsets.Length) { Status.Text = "원래 줄이 삭제된 메모입니다. 메모 목록에서 확인해 주세요."; return; }
            Destination = new(a.Block, targetPanel, offsets[a.LineIndex]); DialogResult = true;
        }
        ResolveButton = Tool("✓", "해결·다시 열기", () =>
        {
            if (list.SelectedItem is not LineMemo m) return;
            Resolve(m);
        });
        if (model is null) ResolveButton.IsEnabled = false;
        else ResolveButton.SetBinding(IsEnabledProperty, new Binding(nameof(DocumentSession.IsReadOnly)) { Source = model, Converter = InverseReadOnly.Instance });
        Tool("‹", "이전 메모", () => Move(-1)); Tool("›", "다음 메모", () => Move(1));
        ShowResolved.IsChecked = showResolved;
        ShowResolved.Checked += (_, _) => Refresh(); ShowResolved.Unchecked += (_, _) => Refresh();
        ReplyButton = Action("회신 추가", () =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) throw new ArgumentException("회신을 입력해 주세요.");
            if (list.SelectedItem is not LineMemo memo) throw new ArgumentException("회신할 메모를 선택하거나 새 메모를 추가해 주세요.");
            edit(() => block.Memos[block.Memos.IndexOf(memo)] = memo with { Replies = [.. memo.Replies ?? [], new(author, input.Text.Trim(), DateTimeOffset.Now)] });
            input.Clear(); Refresh();
        });
        Actions.Children.Remove(ReplyButton); ReplyButton.IsDefault = false;
        ReplyButton.Width = ReplyButton.Height = 32; ReplyButton.MinWidth = ReplyButton.MinHeight = 0;
        ReplyButton.Margin = new(8, 0, 0, 0); ReplyButton.Padding = new(0); ReplyButton.ToolTip = "회신 추가";
        ReplyButton.Background = ReplyButton.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 95, 184));
        ReplyButton.Content = MemoConversationView.DesignIcon("memo-send.svg");
        AutomationProperties.SetName(ReplyButton, "회신 추가"); Grid.SetColumn(ReplyButton, 1); replyHost.Children.Add(ReplyButton);
        input.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter && !System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
            { ReplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); e.Handled = true; }
        };
        var menu = new ContextMenu(); body.ContextMenu = menu;
        menu.Items.Add(new MenuItem { Header = ShowResolved, StaysOpenOnClick = true });
        void MenuAction(string label, Action callback)
        {
            var item = new MenuItem { Header = label }; item.Click += (_, _) => { try { callback(); } catch (Exception ex) { Status.Text = ex.Message; } }; menu.Items.Add(item);
        }
        MenuAction("회신 추가", () => ReplyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
        MenuAction("새 메모", () =>
        {
            var dialog = new MemoInputDialog(author) { Owner = this };
            if (dialog.ShowDialog() != true) return;
            var memo = new LineMemo(Guid.NewGuid().ToString("N"), null, panel, line, lineText[..Math.Min(80, lineText.Length)], dialog.Value, DateTimeOffset.Now, Author: author);
            edit(() => block.Memos.Add(memo)); Refresh(); list.SelectedItem = memo;
        });
        MenuAction("선택 수정", () =>
        {
            if (list.SelectedItem is not LineMemo m) return;
            var dialog = new MemoInputDialog(m.Author) { Owner = this }; dialog.Input.Text = m.Body;
            if (dialog.ShowDialog() != true) return;
            edit(() => block.Memos[block.Memos.IndexOf(m)] = m with { Body = dialog.Value }); Refresh();
        });
        MenuAction("닫기", Close); Refresh();
    }
}

internal sealed class InverseReadOnly : IValueConverter
{
    internal static readonly InverseReadOnly Instance = new();
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is bool readOnly && !readOnly;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
}

public sealed class MemoConversationView : ContentControl
{
    public static readonly DependencyProperty DeleteActionProperty = DependencyProperty.Register(nameof(DeleteAction), typeof(Action<LineMemo, int?>), typeof(MemoConversationView));
    public Action<LineMemo, int?>? DeleteAction { get => (Action<LineMemo, int?>?)GetValue(DeleteActionProperty); set => SetValue(DeleteActionProperty, value); }
    public static readonly DependencyProperty ResolveActionProperty = DependencyProperty.Register(nameof(ResolveAction), typeof(Action<LineMemo>), typeof(MemoConversationView));
    public Action<LineMemo>? ResolveAction { get => (Action<LineMemo>?)GetValue(ResolveActionProperty); set => SetValue(ResolveActionProperty, value); }
    public static readonly DependencyProperty CanEditProperty = DependencyProperty.Register(nameof(CanEdit), typeof(bool), typeof(MemoConversationView), new PropertyMetadata(false, (d, _) => ((MemoConversationView)d).Refresh()));
    public bool CanEdit { get => (bool)GetValue(CanEditProperty); set => SetValue(CanEditProperty, value); }
    public MemoConversationView() => DataContextChanged += (_, _) => Refresh();
    internal static SvgViewbox DesignIcon(string name) => new() { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}"), Width = 16, Height = 16, Stretch = Stretch.None };
    private void Refresh()
    {
        if (DataContext is not LineMemo memo) return;
        var messages = new StackPanel();
        var actionStyle = new Style(typeof(Button), TryFindResource(typeof(Button)) as Style);
        actionStyle.Setters.Add(new Setter(OpacityProperty, 0.0));
        foreach (var property in new[] { nameof(IsMouseOver), nameof(IsKeyboardFocusWithin) })
        {
            var visible = new DataTrigger
            {
                Binding = new Binding(property) { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DockPanel), 1) },
                Value = true
            };
            visible.Setters.Add(new Setter(OpacityProperty, 1.0));
            actionStyle.Triggers.Add(visible);
        }
        var rowStyle = new Style(typeof(DockPanel));
        rowStyle.Setters.Add(new Setter(Panel.BackgroundProperty, Brushes.Transparent));
        foreach (var property in new[] { IsMouseOverProperty, IsKeyboardFocusWithinProperty })
        {
            var highlight = new Trigger { Property = property, Value = true };
            highlight.Setters.Add(new Setter(Panel.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 249, 249))));
            rowStyle.Triggers.Add(highlight);
        }
        void Add(string? author, string text, DateTimeOffset date, int? replyIndex)
        {
            var row = new DockPanel { MinHeight = 54, Style = rowStyle };
            var delete = new Button { Content = DesignIcon("memo-row-trash.svg"), Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0),
                Style = actionStyle, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 209)), IsEnabled = CanEdit, ToolTip = replyIndex is null ? "메모 삭제" : "답글 삭제" };
            AutomationProperties.SetName(delete, replyIndex is null ? "메모 삭제" : "답글 삭제");
            delete.Click += (_, _) => DeleteAction?.Invoke(memo, replyIndex); DockPanel.SetDock(delete, Dock.Right); row.Children.Add(delete);
            if (replyIndex is null)
            {
                var resolve = new Button { Content = DesignIcon("memo-row-resolve.svg"), Width = 24, Height = 24, MinWidth = 0, MinHeight = 0, Padding = new(0),
                    Style = actionStyle, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(209, 209, 209)), IsEnabled = CanEdit, ToolTip = memo.Resolved ? "해결 되돌리기" : "메모 해결" };
                AutomationProperties.SetName(resolve, memo.Resolved ? "해결 되돌리기" : "메모 해결");
                resolve.Click += (_, _) => ResolveAction?.Invoke(memo); DockPanel.SetDock(resolve, Dock.Right); row.Children.Add(resolve);
            }
            var lines = new StackPanel(); row.Children.Add(lines);
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Height = 20 };
            heading.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(author) ? "작성자 미상" : author, FontWeight = FontWeights.SemiBold });
            heading.Children.Add(new TextBlock { Text = DateTimeOffset.Now - date < TimeSpan.FromMinutes(1) ? "방금" : date.ToLocalTime().ToString("MM-dd HH:mm"), Foreground = new SolidColorBrush(Color.FromRgb(95, 100, 109)), Margin = new(8, 0, 0, 0), ToolTip = date.ToLocalTime().ToString("yyyy-MM-dd HH:mm") });
            lines.Children.Add(heading); lines.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, LineHeight = 16 }); messages.Children.Add(row);
        }
        Add(memo.Author, memo.Body, memo.CreatedAt, null);
        var replies = memo.Replies ?? [];
        for (var i = 0; i < replies.Length; i++) Add(replies[i].Author, replies[i].Body, replies[i].CreatedAt, i);
        Content = messages;
    }
}
public sealed class MemoListDialog : CompactDialog
{
    private sealed record Item(EditorBlock Block, LineMemo Memo) { public string Label => $"{Block.Page}쪽 · {Memo.Panel} {Memo.LineIndex + 1}줄 · {(Memo.Resolved ? "해결" : "미해결")} · {Memo.Body}"; }
    public NavigationLocation? Destination { get; private set; }
    public bool IncludeResolved { get; private set; }
    internal ListBox MemoItems { get; } = new() { Margin = new(0, 8, 0, 0), DisplayMemberPath = "Label" };
    internal CheckBox ShowResolved { get; } = new() { Content = "해결한 메모 포함" };
    public MemoListDialog(DocumentSession model) : base("메모 목록", 680, 440)
    {
        var body = new DockPanel { Margin = new(16) }; Body(body); DockPanel.SetDock(ShowResolved, Dock.Top); body.Children.Add(ShowResolved);
        var list = MemoItems; body.Children.Add(list);
        void Refresh() => list.ItemsSource = model.Blocks.OrderBy(b => b.Page).SelectMany(b => b.Memos
            .Where(m => ShowResolved.IsChecked == true || !m.Resolved).OrderBy(m => m.LineIndex).ThenBy(m => m.CreatedAt).Select(m => new Item(b, m))).ToArray();
        ShowResolved.Checked += (_, _) => Refresh(); ShowResolved.Unchecked += (_, _) => Refresh();
        void Open()
        {
            if (list.SelectedItem is not Item item) return;
            var panel = item.Memo.Panel == "TEXT" ? SearchPanel.Text : SearchPanel.Braille;
            var offsets = DocumentNavigation.LineOffsets(DocumentSearch.Content(item.Block, panel));
            if (item.Memo.LineIndex >= offsets.Length) throw new ArgumentException("편집으로 메모의 원래 줄이 사라졌습니다. 블록에서 메모 위치를 확인해 주세요.");
            IncludeResolved = ShowResolved.IsChecked == true;
            Destination = new(item.Block, panel, offsets[item.Memo.LineIndex]); DialogResult = true;
        }
        Action("이동", Open, true); Action("닫기", Close); Refresh();
    }
}
