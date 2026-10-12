using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
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
    private readonly TextBox columns, lines, start;
    private readonly ComboBox numbers, footer;
    public PageLayout Result { get; private set; }
    public LayoutDialog(PageLayout layout) : base("편집용 점자문서 페이지설정")
    {
        Result = layout;
        lines = new() { Text = layout.Lines.ToString() }; columns = new() { Text = layout.Columns.ToString() }; start = new() { Text = layout.StartPage.ToString() };
        numbers = Choices(layout.PageNumbers); footer = Choices(layout.Footer);
        Row("페이지당 줄 수", lines); Row("줄당 칸 수", columns); Row("시작 페이지 번호", start); Row("페이지 번호 출력", numbers); Row("꼬리말 출력", footer);
        Note("칸 수를 넘는 줄은 편집기에 표시합니다. 이 목업에서는 문서 내용을 자동으로 다시 배치하지 않습니다."); Fields.Children.Add(Error);
    }
    private static ComboBox Choices(string selected) => new() { ItemsSource = new[] { "홀수 페이지", "짝수 페이지", "모든 페이지", "출력 안 함" }, SelectedItem = selected };
    protected override bool Apply()
    {
        if (!int.TryParse(columns.Text, out var c) || !int.TryParse(lines.Text, out var l) || !int.TryParse(start.Text, out var s)) throw new ArgumentException("숫자로 입력해 주세요.");
        var value = new PageLayout(c, l, s, numbers.SelectedItem?.ToString() ?? "출력 안 함", footer.SelectedItem?.ToString() ?? "출력 안 함");
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
    protected override bool Apply() => true;
}

public sealed class TranslationOptionsDialog : EditorDialog
{
    private readonly CheckBox auto;
    public bool AutoTranslate => auto.IsChecked == true;
    public TranslationOptionsDialog(bool enabled) : base("기본 점역 옵션")
    {
        auto = new() { Content = "블록을 떠날 때 변경한 원문을 자동으로 점역", IsChecked = enabled };
        Fields.Children.Add(auto);
        Note("직접 수정한 점자는 항상 유지 여부를 확인합니다.\n목업 엔진은 원문 내용과 관계없이 블록별 고정 샘플을 반환합니다.");
    }
    protected override bool Apply() => true;
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
        Note("LaTeX 미리보기입니다. 넣으면 원문에 수식 태그가 삽입됩니다. 수식 점역은 연동 전입니다.");
    }
    protected override bool Apply() => !string.IsNullOrWhiteSpace(input.Text);
}

public sealed class FindDialog : Window
{
    public FindDialog(TextEditor editor)
    {
        Title = "현재 블록에서 찾아 바꾸기"; Width = 500; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        var query = new TextBox { Margin = new Thickness(0, 6, 0, 14) };
        var replacement = new TextBox { Margin = new Thickness(0, 6, 0, 14) };
        var result = new TextBlock { Margin = new Thickness(0, 14, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "찾을 내용" }); panel.Children.Add(query); panel.Children.Add(new TextBlock { Text = "바꿀 내용" }); panel.Children.Add(replacement);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var find = new Button { Content = "다음 찾기", Margin = new Thickness(0, 0, 8, 0) };
        find.Click += (_, _) =>
        {
            if (query.Text.Length == 0) { result.Text = "찾을 내용을 입력해 주세요."; return; }
            var index = editor.Text.IndexOf(query.Text, Math.Min(editor.SelectionStart + editor.SelectionLength, editor.Text.Length), StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = editor.Text.IndexOf(query.Text, StringComparison.OrdinalIgnoreCase);
            if (index < 0) { result.Text = "일치하는 내용이 없습니다."; return; }
            editor.Select(index, query.Text.Length); editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber); result.Text = "일치하는 내용을 선택했습니다.";
        };
        var replace = new Button { Content = "모두 바꾸기" };
        replace.Click += (_, _) =>
        {
            if (query.Text.Length == 0) { result.Text = "찾을 내용을 입력해 주세요."; return; }
            var text = editor.Text;
            var updated = text.Replace(query.Text, replacement.Text, StringComparison.OrdinalIgnoreCase);
            if (text == updated) { result.Text = "변경할 내용이 없습니다."; return; }
            editor.Document.Replace(0, editor.Document.TextLength, updated); result.Text = "현재 블록의 일치하는 내용을 바꿨습니다.";
        };
        actions.Children.Add(find); actions.Children.Add(replace); panel.Children.Add(actions); panel.Children.Add(result);
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
        panel.Children.Add(new TextBlock { Text = "유지하면 이 블록은 다시 점역되지 않습니다.\n목업에서는 덮어쓰기를 선택하면 샘플 점자로 돌아갑니다.", Margin = new Thickness(0, 14, 0, 24), TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        foreach (var (label, decision) in new[] { ("유지", ManualEditDecision.Keep), ("덮어쓰기", ManualEditDecision.Overwrite), ("취소", ManualEditDecision.Cancel) })
        {
            var b = new Button { Content = label, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0) };
            b.Click += (_, _) => { Decision = decision; DialogResult = decision != ManualEditDecision.Cancel; };
            buttons.Children.Add(b);
        }
    }
}
