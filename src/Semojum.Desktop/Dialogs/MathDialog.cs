using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using WpfMath.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed class MathDialog : CompactDialog
{
    internal TextBox Input { get; } = new() { FontFamily = new("Consolas"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal TextBox BrailleInput { get; } = new() { FontFamily = new("Segoe UI Symbol"), FontSize = 22, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    internal WrapPanel SymbolButtons { get; } = new();
    public string Latex => Input.Text.Trim();
    public BrailleTranslation? Translation { get; private set; }
    public bool Edited => Translation is not null && BrailleInput.Text != Translation.Cells;
    private readonly Func<string, CancellationToken, Task<BrailleTranslation>> translate;
    private string? translatedFor;
    private int revision;
    public MathDialog(Func<string, CancellationToken, Task<BrailleTranslation>> translate, string? latex = null) : base("수식 편집기", 980, 462)
    {
        this.translate = translate;
        Input.FontFamily = (FontFamily)FindResource("MonoFont");
        var body = new DockPanel { Margin = new(8, 8, 8, 0) }; Body(body);
        var toolbox = new Border { Height = 76, Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)), BorderBrush = new SolidColorBrush(Color.FromRgb(181, 181, 181)), BorderThickness = new(1), Margin = new(0, 0, 0, 8) };
        DockPanel.SetDock(toolbox, Dock.Top); body.Children.Add(toolbox);
        SymbolButtons.Width = 432; SymbolButtons.HorizontalAlignment = HorizontalAlignment.Left; SymbolButtons.Margin = new(3);
        toolbox.Child = SymbolButtons;
        var symbols = new[] {
            ("⅟", "분수", @"\frac{a}{b}"), ("√", "제곱근", @"\sqrt{x}"), ("x²", "거듭제곱", @"x^{2}"), ("x₂", "아래 첨자", @"x_{2}"),
            ("∑", "합", @"\sum_{i=1}^{n} i"), ("∏", "곱", @"\prod_{i=1}^{n} i"), ("∫", "적분", @"\int_{a}^{b} x dx"), ("lim", "극한", @"\lim_{x\to 0} x"),
            ("( )", "소괄호", @"\left(x\right)"), ("[ ]", "대괄호", @"\left[x\right]"), ("{", "중괄호", @"\left\{x\right\}"), ("≣", "행렬", @"\begin{pmatrix}a&b\\c&d\end{pmatrix}"),
            ("±", "플러스마이너스", @"\pm"), ("≤", "작거나 같음", @"\leq"), ("∈", "원소", @"\in"), ("→", "오른쪽 화살표", @"\to"),
            ("α", "알파", @"\alpha"), ("sin", "사인", @"\sin x"), ("∠", "각", @"\angle"), ("v⃗", "벡터", @"\vec{v}"),
            ("⋯", "줄임표", @"\cdots"), ("㎡", "제곱미터", @"\mathrm{m}^{2}"), ("H₂O", "화학식", @"\mathrm{H}_{2}\mathrm{O}") };
        void Insert(string value) { Input.SelectedText = value; Input.Focus(); }
        foreach (var (label, description, value) in symbols)
        {
            var button = new Button { Content = label, Width = 34, Height = 32, Margin = new(0, 0, 2, 4), Padding = new(0), ToolTip = description };
            if (description == "벡터") button.FontFamily = new("Cambria Math");
            System.Windows.Automation.AutomationProperties.SetName(button, description);
            button.Click += (_, _) =>
            {
                foreach (Button other in SymbolButtons.Children) { other.Background = Brushes.White; other.BorderBrush = new SolidColorBrush(Color.FromRgb(181, 181, 181)); }
                button.Background = new SolidColorBrush(Color.FromRgb(204, 228, 247)); button.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 95, 184)); Insert(value);
            };
            SymbolButtons.Children.Add(button);
        }
        var extra = new ContextMenu();
        foreach (var (label, value) in new[] { ("로그", @"\log_{2} x"), ("cos", @"\cos x"), ("tan", @"\tan x"), ("π", @"\pi"), ("θ", @"\theta"), ("∞", @"\infty"), ("×", @"\times"), ("÷", @"\div"), ("≥", @"\geq"), ("≠", @"\neq") })
        { var item = new MenuItem { Header = label }; item.Click += (_, _) => Insert(value); extra.Items.Add(item); }
        toolbox.ContextMenu = extra; toolbox.ToolTip = "다른 수식 기호: 마우스 오른쪽 단추";
        var panes = new Grid(); panes.ColumnDefinitions.Add(new() { Width = new(628) }); panes.ColumnDefinitions.Add(new() { Width = new(8) }); panes.ColumnDefinitions.Add(new() { Width = new(300) }); panes.ColumnDefinitions.Add(new());
        body.Children.Add(panes); var left = new StackPanel(); panes.Children.Add(left);
        var preview = new FormulaControl { FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var formula = new Grid(); formula.Children.Add(preview);
        formula.Children.Add(new TextBlock { Text = "묵자 수식", Foreground = (Brush)FindResource("Muted"), Margin = new(7, 3, 0, 0), VerticalAlignment = VerticalAlignment.Top });
        left.Children.Add(new Border { Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(181, 181, 181)), BorderThickness = new(1), Height = 160, Child = formula });
        Input.Height = 92; Input.Padding = new(7, 20, 7, 7); Input.FontSize = 12; Input.VerticalContentAlignment = VerticalAlignment.Top;
        var latexHost = new Grid { Margin = new(0, 8, 0, 0) }; latexHost.Children.Add(Input);
        latexHost.Children.Add(new TextBlock { Text = "LaTeX", Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 16, 0), IsHitTestVisible = false });
        left.Children.Add(latexHost);
        BrailleInput.Height = 252; BrailleInput.Padding = new(6, 20, 6, 6); BrailleInput.VerticalContentAlignment = VerticalAlignment.Top; BrailleInput.BorderThickness = new(2); BrailleInput.BorderBrush = new SolidColorBrush(Color.FromRgb(0, 95, 184));
        var brailleHost = new Grid { VerticalAlignment = VerticalAlignment.Top }; brailleHost.Children.Add(BrailleInput);
        brailleHost.Children.Add(new TextBlock { Text = "점자 수식", Foreground = (Brush)FindResource("Muted"), Margin = new(8, 4, 0, 0), VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        Grid.SetColumn(brailleHost, 2); panes.Children.Add(brailleHost);
        System.Windows.Automation.AutomationProperties.SetName(Input, "LaTeX 수식");
        System.Windows.Automation.AutomationProperties.SetName(BrailleInput, "점자 수식");
        Input.Text = latex ?? @"\frac{a+b}{2}=\sqrt{c}";
        if (Input.Text.StartsWith(@"\frac", StringComparison.Ordinal) && SymbolButtons.Children[0] is Button fraction)
        { fraction.Background = new SolidColorBrush(Color.FromRgb(204, 228, 247)); fraction.BorderBrush = (Brush)FindResource("Accent"); }
        Input.TextChanged += async (_, _) =>
        {
            try { preview.Formula = Input.Text; } catch { /* The engine reports invalid expressions below. */ }
            await RefreshAsync(true);
        };
        Action("넣기", async () =>
        {
            if (Latex.Length == 0) throw new ArgumentException("수식을 입력해 주세요.");
            if (translatedFor != Latex) await RefreshAsync();
            if (Translation is null || translatedFor != Latex) throw new ArgumentException("점역 결과를 확인해 주세요.");
            if (!DocumentSearch.IsBrailleText(BrailleInput.Text) || string.IsNullOrWhiteSpace(BrailleInput.Text)) throw new ArgumentException("점자 수식에는 점자와 공백만 입력할 수 있습니다.");
            DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
        Loaded += async (_, _) => { preview.Formula = Input.Text; await RefreshAsync(); };
    }
    internal async Task RefreshAsync(bool delay = false)
    {
        var generation = ++revision; var latex = Latex; Translation = null; translatedFor = null;
        BrailleInput.IsReadOnly = true;
        try
        {
            if (delay) await Task.Delay(300, Lifetime.Token);
            if (generation != revision || latex.Length == 0) return;
            Status.Text = "점역 중…";
            var result = await translate(latex, Lifetime.Token);
            if (generation != revision || Lifetime.IsCancellationRequested) return;
            Translation = result; translatedFor = latex; BrailleInput.Text = result.Cells;
            Status.Text = result.NeedsReview ? "점역 결과에 검토할 문자가 있습니다." : "";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (generation == revision) { Status.Text = ex.Message; BrailleInput.Clear(); } }
        finally { if (generation == revision) BrailleInput.IsReadOnly = false; }
    }
}
