using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using WpfMath.Controls;

namespace Semojum.Desktop.Dialogs;

public abstract class EditorDialog : Window
{
    protected readonly StackPanel Fields = new() { Margin = new Thickness(24, 14, 24, 20) };
    protected readonly TextBlock Error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    protected EditorDialog(string title, double width = 560)
    {
        Title = title; Width = width; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MaxHeight = SystemParameters.WorkArea.Height;
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F1) { new HelpWindow(Title) { Owner = this }.Show(); e.Handled = true; } };
        var root = new DockPanel(); Content = root;
        var header = new Border { Background = Brushes.White, Padding = new Thickness(24, 18, 24, 18), BorderBrush = new SolidColorBrush(Color.FromRgb(207, 213, 220)), BorderThickness = new Thickness(0, 0, 0, 1), Child = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold } };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 12, 24, 20) };
        var ok = new Button { Content = "확인", MinWidth = 92, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        ok.Click += (_, _) => { try { if (Apply()) DialogResult = true; } catch (ArgumentException ex) { Error.Text = ex.Message; } };
        var cancel = new Button { Content = "취소", MinWidth = 92, IsCancel = true, Margin = new Thickness(10, 0, 0, 0) };
        footer.Children.Add(ok); footer.Children.Add(cancel); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
    protected void Row(string label, FrameworkElement field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center });
        field.MinHeight = 32; row.Children.Add(field); Fields.Children.Add(row);
    }
    protected void Note(string text) => Fields.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 4), FontSize = 12 });
    protected abstract bool Apply();
}

public sealed class LayoutDialog : CompactDialog
{
    private readonly TextBox columns, lines, start, sourceStart, cover, numberRanges;
    private readonly ComboBox numbers, align, footerPages;
    private readonly CheckBox sourceNumber, brailleNumber, changeLine;
    public PageLayout Result { get; private set; }
    public LayoutDialog(PageLayout layout) : base("편집용 점자문서 페이지설정", 480, 280)
    {
        Result = layout;
        lines = new() { Text = layout.Lines.ToString() }; columns = new() { Text = layout.Columns.ToString() }; start = new() { Text = layout.StartPage.ToString() };
        numbers = Choices(layout.PageNumbers);
        footerPages = Choices(layout.Footer);
        sourceStart = new() { Text = layout.SourcePageStart.ToString() }; cover = new() { Text = layout.CoverPages.ToString() };
        align = new() { ItemsSource = new[] { "가운데", "오른쪽", "왼쪽" }, SelectedIndex = layout.FooterAlign switch { "right" => 1, "left" => 2, _ => 0 } };
        sourceNumber = new() { Content = "원본 쪽 번호", IsChecked = layout.ShowSourcePageNumber };
        brailleNumber = new() { Content = "점자 면 번호", IsChecked = layout.ShowBraillePageNumber };
        changeLine = new() { Content = "묵자 쪽 전환선 표시", IsChecked = layout.ShowChangeLine };
        numberRanges = new() { Text = PageNumberRange.Format(layout.NumberRanges) };
        var fields = new StackPanel { Margin = new(16, 16, 16, 0) }; Body(fields);
        void Setting(string label, Control field, double width)
        {
            var row = new DockPanel { Height = 32, Margin = new(0, 0, 0, 3) };
            row.Children.Add(new TextBlock { Text = label, Width = 164, FontSize = 14 });
            field.Height = field.MinHeight = 24; field.Width = width; field.FontSize = 12; field.Padding = new(7, 0, 7, 0); field.VerticalAlignment = VerticalAlignment.Top;
            field.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(field);
            fields.Children.Add(new Border { BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        Setting("페이지당 줄 수", lines, 80); Setting("줄당 칸 수", columns, 80); Setting("점자 페이지 시작 번호", start, 80);
        Setting("점자 페이지 번호 출력", numbers, 240); Setting("꼬리말 출력", footerPages, 240);
        Action("확인", () => { if (Apply()) DialogResult = true; }, true); Action("취소", () => DialogResult = false);
    }
    private static ComboBox Choices(string selected) => new() { ItemsSource = new[] { "홀수 페이지", "짝수 페이지", "모든 페이지", "출력 안 함" }, SelectedItem = selected };
    private bool Apply()
    {
        if (!int.TryParse(columns.Text, out var c) || !int.TryParse(lines.Text, out var l) || !int.TryParse(start.Text, out var s)
            || !int.TryParse(sourceStart.Text, out var original) || !int.TryParse(cover.Text, out var covers)) throw new ArgumentException("숫자로 입력해 주세요.");
        var value = Result with { Columns = c, Lines = l, StartPage = s, NumberRanges = PageNumberRange.Parse(numberRanges.Text), PageNumbers = numbers.SelectedItem?.ToString() ?? "출력 안 함",
            SourcePageStart = original, CoverPages = covers, FooterAlign = align.SelectedIndex switch { 1 => "right", 2 => "left", _ => "center" },
            ShowSourcePageNumber = sourceNumber.IsChecked == true, ShowBraillePageNumber = brailleNumber.IsChecked == true, ShowChangeLine = changeLine.IsChecked == true, Footer = footerPages.SelectedItem?.ToString() ?? "출력 안 함" };
        value.Validate(); Result = value; return true;
    }
}

public sealed class SixDotDialog : CompactDialog
{
    internal TextBox[] KeyFields { get; }
    public string Keys { get; private set; }
    public bool Space { get; }
    public bool Enter { get; }
    public SixDotDialog(string current, bool allowSpace, bool allowEnter) : base("6점키 설정", 480, 200)
    {
        Keys = current; Space = allowSpace; Enter = allowEnter;
        var chars = string.Concat(current.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        KeyFields = Enumerable.Range(0, 6).Select(i => new TextBox { Text = i < chars.Length ? chars[i].ToString() : "", CharacterCasing = CharacterCasing.Upper, MaxLength = 1, Width = 34, Height = 24, Padding = new(0), Margin = new(0, 0, 6, 0), HorizontalContentAlignment = HorizontalAlignment.Center }).ToArray();
        var body = new StackPanel { Margin = new(16, 16, 16, 0) }; Body(body);
        string[] presets = ["WERUIO", "FDSJKL", "XCVNM,"];
        var presetIndex = Array.IndexOf(presets, chars);
        var mode = new Controls.SegmentedChoice("1단", "2단", "3단", "사용자") { SelectedIndex = presetIndex < 0 ? 3 : presetIndex };
        void Field(string label, FrameworkElement value)
        {
            var row = new DockPanel { Height = 40 };
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, Width = 134, Height = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
            value.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(value); body.Children.Add(row);
        }
        Field("입력 방식", mode);
        var keys = new StackPanel { Orientation = Orientation.Horizontal };
        var settingPreset = false;
        foreach (var (field, i) in KeyFields.Select((field, i) => (field, i)))
        {
            System.Windows.Automation.AutomationProperties.SetName(field, $"{i + 1}점 키"); keys.Children.Add(field);
            field.TextChanged += (_, _) => { if (!settingPreset) mode.SelectedIndex = 3; };
            field.GotKeyboardFocus += (_, _) => field.SelectAll();
            field.PreviewMouseLeftButtonDown += (_, e) => { field.Focus(); field.SelectAll(); e.Handled = true; };
            field.PreviewTextInput += (_, e) => { if (e.Text.Length != 1 || e.Text[0] < '!' || e.Text[0] > '~') e.Handled = true; };
            DataObject.AddPastingHandler(field, (_, e) =>
            {
                if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetData(DataFormats.UnicodeText) is not string text ||
                    text.Length != 1 || text[0] < '!' || text[0] > '~') e.CancelCommand();
            });
        }
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex is < 0 or > 2) return;
            settingPreset = true;
            try { for (var i = 0; i < 6; i++) KeyFields[i].Text = presets[mode.SelectedIndex][i].ToString(); }
            finally { settingPreset = false; }
        };
        Field("키 지정", keys);
        Action("확인", () => { if (Apply()) DialogResult = true; }, true); Action("취소", () => DialogResult = false);
    }
    private bool Apply()
    {
        var value = string.Concat(KeyFields.Select(k => k.Text.Trim())).ToUpperInvariant();
        new SixDotInput().Configure(value);
        Keys = string.Join(" ", value.ToCharArray()); return true;
    }
}

public sealed class MemoDialog : EditorDialog
{
    private readonly TextBox input;
    public string Result => input.Text;
    public MemoDialog(EditorBlock block) : base($"블록 {block.Id} · 메모")
    {
        input = new() { Text = block.Memo, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Fields.Children.Add(input); Note("메모는 현재 블록에 저장됩니다.");
    }
    public MemoDialog(string title, string text) : base(title)
    {
        input = new() { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Fields.Children.Add(input); Note("현재 패널의 블록 안 줄에 메모를 남깁니다. 내용을 비우면 메모가 삭제됩니다.");
    }
    protected override bool Apply() => true;
}

public sealed class DraftDialog : EditorDialog
{
    private readonly ComboBox choice;
    public int SelectedIndex => choice.SelectedIndex;
    public DraftDialog(BrailleDraft[] drafts) : base("표 점역 초안 선택", 700)
    {
        choice = new() { ItemsSource = drafts.Select(d => d.Label).ToArray(), SelectedIndex = 0 };
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 250, FontSize = 22, FontFamily = new FontFamily("Segoe UI Symbol"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = drafts[0].Cells };
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) preview.Text = drafts[choice.SelectedIndex].Cells; };
        Row("초안", choice); Fields.Children.Add(preview);
    }
    protected override bool Apply() => choice.SelectedIndex >= 0;
}

public sealed class TranslationOptionsDialog : CompactDialog
{
    private readonly CheckBox auto;
    private readonly OptionsEditor editor = new();
    public BrailleOptions Result { get; private set; }
    public bool AutoTranslate => auto.IsChecked == true;
    public TranslationOptionsDialog(bool enabled, BrailleOptions? options = null) : base("작업 점역 옵션", 620, 600)
    {
        Result = options ?? new(); editor.Set(Result);
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(editor);
        auto = new() { Content = "텍스트 입력이 멈추면 자동으로 점역", IsChecked = enabled, Margin = new(0, 12, 0, 12) };
        panel.Children.Add(auto);
        panel.Children.Add(new TextBlock { Text = "직접 수정한 점자는 유지 여부를 확인합니다. 저장은 Ctrl+S를 누를 때만 합니다.", TextWrapping = TextWrapping.Wrap });
        Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Action("확인", () => { Result = editor.Value; DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
        Action("기본값 복원", () => editor.Set(new()));
    }
}

public sealed class FindDialog : CompactDialog
{
    public FindDialog(SearchPanel target, string initialQuery, bool showReplacement,
        Func<string, SearchPanel, Task<string>> findNext, Func<string, string, SearchPanel, bool, Task<string>> replaceMatches) : base("찾아 바꾸기", 440, 260)
    {
        var panel = new StackPanel { Margin = new Thickness(16, 12, 16, 0) }; Body(panel);
        var query = new TextBox { Text = initialQuery };
        var replacement = new TextBox();
        var result = new TextBlock { Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") };
        var targetPicker = CompactDialog.Choices(["텍스트", "점자"], target == SearchPanel.Text ? "텍스트" : "점자");
        SearchPanel Target() => targetPicker.SelectedIndex == 1 ? SearchPanel.Braille : SearchPanel.Text;
        void Field(string label, Control control)
        {
            var row = new DockPanel { Height = 40 };
            row.Children.Add(new TextBlock { Text = label, Width = 120, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
            control.Height = 32; control.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(control); panel.Children.Add(row);
        }
        Field("찾을 내용", query); Field("바꿀 내용", replacement); Field("대상", targetPicker);
        var busy = false;
        async Task SearchAsync(Func<Task<string>> action)
        {
            if (busy) return;
            busy = true; panel.IsEnabled = false;
            try { result.Text = await action(); }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { panel.IsEnabled = true; busy = false; }
        }
        foreach (var (label, action) in new (string, Func<Task<string>>)[]
        {
            ("다음 찾기", () => findNext(query.Text, Target())),
            ("바꾸기", () => replaceMatches(query.Text, replacement.Text, Target(), false)),
            ("모두 바꾸기", () => replaceMatches(query.Text, replacement.Text, Target(), true))
        })
        {
            Action(label, async () => await SearchAsync(action), label == "다음 찾기").Width = label == "다음 찾기" ? 87 : label == "모두 바꾸기" ? 99 : 76;
        }
        Action("닫기", Close); panel.Children.Add(result);
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F3) { e.Handled = true; await SearchAsync(() => findNext(query.Text, Target())); }
            else if (e.Key == System.Windows.Input.Key.Escape && !busy) Close();
        };
        Closing += (_, e) => { if (busy) e.Cancel = true; };
        Loaded += (_, _) =>
        {
            if (showReplacement && query.Text.Length > 0) replacement.Focus();
            else { query.Focus(); query.SelectAll(); }
        };
    }
}

public sealed class ManualEditDialog : NoticeDialog
{
    public ManualEditDecision Decision => Choice switch { 0 => ManualEditDecision.Keep, 1 => ManualEditDecision.Overwrite, _ => ManualEditDecision.Cancel };
    public ManualEditDialog() : base("직접 고친 점자를 지우고 다시 점역하시겠습니까?",
        "유지하면 이 블록은 다시 점역되지 않습니다.", ["유지", "덮어쓰기", "취소"], width: 520, height: 190) { }
}
