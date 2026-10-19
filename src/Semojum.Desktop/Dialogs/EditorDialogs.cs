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
        var root = new DockPanel(); Content = root;
        var header = new Border { Background = Brushes.White, Padding = new Thickness(24, 18, 24, 18), BorderBrush = new SolidColorBrush(Color.FromRgb(207, 213, 220)), BorderThickness = new Thickness(0, 0, 0, 1), Child = new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold } };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 12, 24, 20) };
        var ok = new Button { Content = "확인", MinWidth = 92, IsDefault = true, Style = (Style)FindResource("PrimaryButton") };
        ok.Click += (_, _) => { try { if (Apply()) DialogResult = true; } catch (ArgumentException ex) { Error.Text = ex.Message; } };
        var cancel = new Button { Content = "취소", MinWidth = 92, IsCancel = true, Margin = new Thickness(10, 0, 0, 0) };
        footer.Children.Add(ok); footer.Children.Add(cancel); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        root.Children.Add(Fields);
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

public sealed class LayoutDialog : EditorDialog
{
    private readonly TextBox columns, lines, start, sourceStart, cover;
    private readonly ComboBox numbers, align;
    private readonly CheckBox sourceNumber, brailleNumber, changeLine;
    public PageLayout Result { get; private set; }
    public LayoutDialog(PageLayout layout) : base("편집용 점자문서 페이지설정")
    {
        Result = layout;
        lines = new() { Text = layout.Lines.ToString() }; columns = new() { Text = layout.Columns.ToString() }; start = new() { Text = layout.StartPage.ToString() };
        numbers = Choices(layout.PageNumbers);
        sourceStart = new() { Text = layout.SourcePageStart.ToString() }; cover = new() { Text = layout.CoverPages.ToString() };
        align = new() { ItemsSource = new[] { "가운데", "오른쪽" }, SelectedIndex = layout.FooterAlign == "right" ? 1 : 0 };
        sourceNumber = new() { Content = "원본 쪽 번호", IsChecked = layout.ShowSourcePageNumber };
        brailleNumber = new() { Content = "점자 면 번호", IsChecked = layout.ShowBraillePageNumber };
        changeLine = new() { Content = "묵자 쪽 전환선", IsChecked = layout.ShowChangeLine };
        Row("페이지당 줄 수", lines); Row("줄당 칸 수", columns); Row("점자 시작 면", start); Row("원본 시작 쪽", sourceStart);
        Row("표지 면 수", cover); Row("페이지행 출력", numbers); Row("꼬리말 정렬", align);
        Fields.Children.Add(sourceNumber); Fields.Children.Add(brailleNumber); Fields.Children.Add(changeLine);
        Note("BRF 내보내기에 적용합니다. 원시 점자와 직접 수정한 내용은 작업 파일에 보존됩니다."); Fields.Children.Add(Error);
    }
    private static ComboBox Choices(string selected) => new() { ItemsSource = new[] { "홀수 페이지", "모든 페이지", "출력 안 함" }, SelectedItem = selected };
    protected override bool Apply()
    {
        if (!int.TryParse(columns.Text, out var c) || !int.TryParse(lines.Text, out var l) || !int.TryParse(start.Text, out var s)
            || !int.TryParse(sourceStart.Text, out var original) || !int.TryParse(cover.Text, out var covers)) throw new ArgumentException("숫자로 입력해 주세요.");
        var value = Result with { Columns = c, Lines = l, StartPage = s, PageNumbers = numbers.SelectedItem?.ToString() ?? "출력 안 함",
            SourcePageStart = original, CoverPages = covers, FooterAlign = align.SelectedIndex == 1 ? "right" : "center",
            ShowSourcePageNumber = sourceNumber.IsChecked == true, ShowBraillePageNumber = brailleNumber.IsChecked == true, ShowChangeLine = changeLine.IsChecked == true };
        value.Validate(); Result = value; return true;
    }
}

public sealed class SixDotDialog : EditorDialog
{
    private readonly TextBox keys;
    private readonly CheckBox space, enter;
    public string Keys { get; private set; }
    public bool Space => space.IsChecked == true;
    public bool Enter => enter.IsChecked == true;
    public SixDotDialog(string current, bool allowSpace, bool allowEnter) : base("6점키 설정")
    {
        Keys = current; keys = new() { Text = current, CharacterCasing = CharacterCasing.Upper };
        Row("1 · 2 · 3 · 4 · 5 · 6점", keys);
        space = new() { Content = "Space로 빈칸", IsChecked = allowSpace }; enter = new() { Content = "Enter로 줄바꿈", IsChecked = allowEnter };
        Fields.Children.Add(space); Fields.Children.Add(enter);
        Note("기본 키: F D S J K L\n함께 누른 키를 모두 놓으면 점자 한 칸이 입력됩니다. 실제 키보드의 동시 입력 지원 여부에 따라 동작이 달라질 수 있습니다."); Fields.Children.Add(Error);
    }
    protected override bool Apply()
    {
        var value = string.Concat(keys.Text.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        if (value.Length != 6 || value.Any(c => !char.IsAsciiLetter(c)) || value.Distinct().Count() != 6) throw new ArgumentException("서로 다른 영문 키 6개를 입력해 주세요.");
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

public sealed class MathDialog : EditorDialog
{
    private readonly TextBox input;
    private readonly FormulaControl preview;
    public string Latex => input.Text;
    public MathDialog() : base("수식 편집기", 820)
    {
        var symbols = new WrapPanel();
        foreach (var (label, value) in new[] { ("분수", @"\frac{a}{b}"), ("√", @"\sqrt{x}"), ("x²", @"x^{2}"), ("Σ", @"\sum_{i=1}^{n} i"), ("π", @"\pi"), ("≤", @"\leq") })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 8) };
            button.Click += (_, _) => { input!.SelectedText = value; input.Focus(); };
            symbols.Children.Add(button);
        }
        Fields.Children.Add(symbols);
        preview = new() { Formula = @"\frac{a+b}{2}=\sqrt{c}", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Fields.Children.Add(new Border { Background = Brushes.White, BorderBrush = (Brush)FindResource("Line"), BorderThickness = new Thickness(1), Height = 140, Child = preview, Padding = new Thickness(20) });
        input = new() { Text = @"\frac{a+b}{2}=\sqrt{c}", FontFamily = new FontFamily("Consolas"), AcceptsReturn = true, Height = 80, Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
        input.TextChanged += (_, _) => { preview.Formula = input.Text; };
        Fields.Children.Add(input);
        Note("LaTeX 미리보기입니다. 넣으면 원문에 수식 태그가 삽입됩니다. 점역 결과를 확인해 주세요.");
    }
    protected override bool Apply() => !string.IsNullOrWhiteSpace(input.Text);
}

public sealed class FindDialog : Window
{
    public FindDialog(SearchPanel target, string initialQuery, bool showReplacement,
        Func<string, Task<string>> findNext, Func<string, string, bool, Task<string>> replaceMatches)
    {
        Title = target == SearchPanel.Text ? "문서 텍스트 찾기·바꾸기" : "문서 점자 찾기·바꾸기";
        Width = 540; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        var query = new TextBox { Text = initialQuery, Margin = new Thickness(0, 6, 0, 14) };
        var replacement = new TextBox { Margin = new Thickness(0, 6, 0, 14) };
        var result = new TextBlock { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(new TextBlock { Text = "범위: 문서의 모든 " + (target == SearchPanel.Text ? "텍스트" : "점자") + " 블록 · 대소문자 구분 없이 찾기", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "찾을 내용", Margin = new Thickness(0, 14, 0, 0) }); panel.Children.Add(query);
        panel.Children.Add(new TextBlock { Text = "바꿀 내용" }); panel.Children.Add(replacement);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var busy = false;
        async Task RunAsync(Func<Task<string>> action)
        {
            if (busy) return;
            busy = true; panel.IsEnabled = false;
            try { result.Text = await action(); }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { panel.IsEnabled = true; busy = false; }
        }
        foreach (var (label, action) in new (string, Func<Task<string>>)[]
        {
            ("다음 찾기", () => findNext(query.Text)),
            ("바꾸기", () => replaceMatches(query.Text, replacement.Text, false)),
            ("모두 바꾸기", () => replaceMatches(query.Text, replacement.Text, true))
        })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0) };
            button.Click += async (_, _) => await RunAsync(action); actions.Children.Add(button);
        }
        panel.Children.Add(actions); panel.Children.Add(result);
        PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.F3) { e.Handled = true; await RunAsync(() => findNext(query.Text)); }
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

public sealed class ManualEditDialog : Window
{
    public ManualEditDecision Decision { get; private set; } = ManualEditDecision.Cancel;
    public ManualEditDialog()
    {
        Title = "직접 수정한 점자"; Width = 580; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(26) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "직접 고친 점자를 지우고 다시 점역하시겠습니까?", FontSize = 17, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "유지하면 직접 고친 점자가 남습니다. 덮어쓰면 현재 원문으로 다시 점역합니다.", Margin = new Thickness(0, 14, 0, 24), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        foreach (var (label, decision) in new[] { ("유지", ManualEditDecision.Keep), ("덮어쓰기", ManualEditDecision.Overwrite), ("취소", ManualEditDecision.Cancel) })
        {
            var b = new Button { Content = label, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            b.Click += (_, _) => { Decision = decision; DialogResult = decision != ManualEditDecision.Cancel; };
            buttons.Children.Add(b);
        }
    }
}
