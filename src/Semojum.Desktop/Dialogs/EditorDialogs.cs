using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Shell;
using System.Windows.Controls.Primitives;
using SharpVectors.Converters;
using Semojum.Core;
using WpfMath.Controls;

namespace Semojum.Desktop.Dialogs;

internal static class EditorModalDesign
{
    internal static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    internal static SvgViewbox Icon(string name, double width = 16, double height = 16) => new()
    {
        Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{name}.svg"),
        Width = width, Height = height, Stretch = Stretch.None
    };
    internal static ControlTemplate ButtonTemplate(Type type)
    {
        var surface = new FrameworkElementFactory(typeof(Border), "Surface");
        surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty })
            surface.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); surface.AppendChild(content);
        var template = new ControlTemplate(type) { VisualTree = surface };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#005FB8"), "Surface")); template.Triggers.Add(focus);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Surface")); template.Triggers.Add(disabled);
        return template;
    }
    internal static void Input(TextBox box)
    {
        box.BorderBrush = Color("#EAEAEA"); box.BorderThickness = new Thickness(1); box.FontSize = 12;
        var root = new FrameworkElementFactory(typeof(Grid));
        var surface = new FrameworkElementFactory(typeof(Border), "Surface"); surface.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty, Border.PaddingProperty })
            surface.SetBinding(property, new System.Windows.Data.Binding(property.Name) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        surface.SetBinding(Border.BackgroundProperty, Semojum.Desktop.Controls.FigmaInputFocusBrush.Binding());
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"); surface.AppendChild(host);
        root.AppendChild(surface);
        var underline = new FrameworkElementFactory(typeof(Border), "Underline"); underline.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); underline.SetValue(FrameworkElement.HeightProperty, 1d); underline.SetValue(Border.BackgroundProperty, Color("#D1D1D1")); underline.SetValue(UIElement.IsHitTestVisibleProperty, false); root.AppendChild(underline);
        var focusLine = new FrameworkElementFactory(typeof(Border), "FocusUnderline"); focusLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Bottom); focusLine.SetValue(FrameworkElement.HeightProperty, 2d); focusLine.SetValue(Border.BackgroundProperty, Color("#005FB8")); focusLine.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); focusLine.SetValue(UIElement.IsHitTestVisibleProperty, false); root.AppendChild(focusLine);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = root };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "FocusUnderline")); template.Triggers.Add(focus);
        var borderless = new Trigger { Property = Control.BorderThicknessProperty, Value = new Thickness(0) }; borderless.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "Underline")); borderless.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "FocusUnderline")); template.Triggers.Add(borderless);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5, "Surface")); template.Triggers.Add(disabled);
        box.Template = template;
    }
    internal static void Frame(Window window, DockPanel root, WrapPanel actions)
    {
        if (window.Content is Grid frame && frame.Children[0] is Border titleBar && titleBar.Child is DockPanel heading)
        {
            frame.RowDefinitions[0].Height = new GridLength(55);
            WindowChrome.GetWindowChrome(window).CaptionHeight = 55;
            titleBar.Background = Color("#F3F3F3"); titleBar.BorderThickness = new Thickness(0);
            var title = heading.Children.OfType<TextBlock>().Single();
            title.Margin = new Thickness(24, 0, 0, 0); title.FontSize = 16; title.FontWeight = FontWeights.Bold; title.VerticalAlignment = VerticalAlignment.Center;
            var buttons = heading.Children.OfType<StackPanel>().Single(); buttons.Margin = new Thickness(0, 0, 24, 0);
            var close = buttons.Children.OfType<Button>().Single(); close.Content = Icon("modal-close");
            close.Width = close.Height = 28; close.Background = Brushes.Transparent; close.BorderThickness = new Thickness(0); close.Template = ButtonTemplate(typeof(Button)); close.VerticalAlignment = VerticalAlignment.Center;
            foreach (var outline in frame.Children.OfType<Border>().Where(x => x != titleBar)) outline.Visibility = Visibility.Collapsed;
        }
        if (root.Children.OfType<Border>().FirstOrDefault() is { } footer) footer.BorderThickness = new Thickness(0);
        actions.Margin = new Thickness(24, 16, 24, 16);
        foreach (var button in actions.Children.OfType<Button>())
        {
            button.Height = button.MinHeight = 32; button.MinWidth = 0; button.Width = 88; button.FontSize = 12;
            button.Template = ButtonTemplate(typeof(Button)); button.BorderThickness = new Thickness(1);
            if (!button.IsDefault) button.BorderBrush = Color("#EAEAEA");
        }
    }
    internal static Border Card(UIElement child, Thickness margin) => new()
    { Background = Color("#F9F9F9"), CornerRadius = new CornerRadius(4), Padding = new Thickness(16), Margin = margin, Child = child };
}

public abstract class EditorDialog : Window
{
    public new bool? ShowDialog() => Services.OwnedModal.Show(this, base.ShowDialog);
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
    public LayoutDialog(PageLayout layout) : base("편집용 점자문서 페이지설정", 520, 412)
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
        var fields = new StackPanel { Margin = new(24, 0, 24, 0) }; Body(fields);
        StackPanel Group(string label, Thickness margin)
        {
            fields.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 4) });
            var body = new StackPanel(); var card = EditorModalDesign.Card(body, margin); card.Padding = new Thickness(16, 12, 16, 8); fields.Children.Add(card); return body;
        }
        FrameworkElement Number(TextBox box)
        {
            var grid = new Grid { Width = 96, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(22) });
            box.Padding = new Thickness(8, 0, 0, 0); box.VerticalContentAlignment = VerticalAlignment.Center; box.MinHeight = 0;
            EditorModalDesign.Input(box);
            Grid.SetColumnSpan(box, 2); grid.Children.Add(box);
            var arrows = new StackPanel(); Grid.SetColumn(arrows, 1); grid.Children.Add(arrows);
            void Arrow(bool increase)
            {
                var icon = increase ? EditorModalDesign.Icon("layout-up", 9.60063, 5.19934) : EditorModalDesign.Icon("layout-down");
                if (increase) icon.RenderTransform = new ScaleTransform(1, -1); icon.RenderTransformOrigin = new Point(.5, .5);
                var button = new Button { Content = icon, Height = 16, MinHeight = 0, Padding = new Thickness(0), BorderThickness = new Thickness(0), Background = EditorModalDesign.Color("#F0F0F0"), Focusable = false };
                button.Template = EditorModalDesign.ButtonTemplate(typeof(Button));
                System.Windows.Automation.AutomationProperties.SetName(button, increase ? "값 늘리기" : "값 줄이기");
                button.Click += (_, _) => { if (int.TryParse(box.Text, out var n)) box.Text = (n + (increase ? 1 : -1)).ToString(); }; arrows.Children.Add(button);
            }
            Arrow(true); Arrow(false); return grid;
        }
        void Setting(StackPanel group, string label, FrameworkElement field, string? unit = null)
        {
            if (group.Children.Count > 0) ((FrameworkElement)group.Children[group.Children.Count - 1]).Margin = new Thickness(0, 0, 0, 4);
            var row = new DockPanel { Height = 32, LastChildFill = false };
            row.Children.Add(new TextBlock { Text = label, Width = 134, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            System.Windows.Automation.AutomationProperties.SetName(field, label); if (field is Grid grid && grid.Children.OfType<TextBox>().FirstOrDefault() is { } box) System.Windows.Automation.AutomationProperties.SetName(box, label);
            row.Children.Add(field); if (unit is not null) row.Children.Add(new TextBlock { Text = unit, FontSize = 12, Foreground = (Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) }); group.Children.Add(row);
        }
        var page = Group("면 크기", new Thickness(0));
        Setting(page, "페이지당 줄 수", Number(lines), "줄"); Setting(page, "줄당 칸 수", Number(columns), "칸");
        var foot = Group("쪽 번호와 꼬리말", new Thickness(0));
        Setting(foot, "시작 쪽 번호", Number(start));
        foreach (var combo in new[] { numbers, footerPages }) { combo.Width = 220; combo.Height = combo.MinHeight = 32; combo.HorizontalAlignment = HorizontalAlignment.Left; combo.Background = EditorModalDesign.Color("#FBFBFB"); combo.BorderBrush = EditorModalDesign.Color("#EAEAEA"); }
        FrameworkElement Combo(ComboBox combo, string label)
        {
            System.Windows.Automation.AutomationProperties.SetName(combo, label);
            var grid = new Grid { Width = 220, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Clip = new RectangleGeometry(new Rect(0, 0, 220, 32), 4, 4) }; grid.Children.Add(combo);
            var overlay = new Border { Width = 25, Margin = new Thickness(0, 1, 1, 1), HorizontalAlignment = HorizontalAlignment.Right, Background = EditorModalDesign.Color("#FBFBFB"), IsHitTestVisible = false, Child = EditorModalDesign.Icon("layout-combo-down") };
            grid.Children.Add(overlay); grid.Children.Add(new Border { BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false }); return grid;
        }
        Setting(foot, "쪽 번호 넣을 면", Combo(numbers, "쪽 번호 넣을 면")); Setting(foot, "꼬리말 넣을 면", Combo(footerPages, "꼬리말 넣을 면"));
        Action("취소", () => DialogResult = false); Action("확인", () => { if (Apply()) DialogResult = true; }, true);
        EditorModalDesign.Frame(this, Root, Actions);
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
    public SixDotDialog(string current, bool allowSpace, bool allowEnter) : base("6점키 설정", 520, 356)
    {
        Keys = current; Space = allowSpace; Enter = allowEnter;
        var chars = string.Concat(current.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        KeyFields = Enumerable.Range(0, 6).Select(i => new TextBox { Text = i < chars.Length ? chars[i].ToString() : "", CharacterCasing = CharacterCasing.Upper, MaxLength = 1, Width = 44, Height = 32, MinHeight = 0, Padding = new(0), FontSize = 14, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center }).ToArray();
        var body = new StackPanel { Margin = new(24, 0, 24, 0) }; Body(body);
        string[] presets = ["WERUIO", "FDSJKL", "XCVNM,"];
        var presetIndex = Array.IndexOf(presets, chars);
        var mode = new Controls.SegmentedChoice("1단", "2단", "3단", "사용자") { Width = 432, Height = 32, FontSize = 12, SelectedIndex = presetIndex < 0 ? 3 : presetIndex };
        var modeStyle = new Style(typeof(ListBoxItem));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(presenter);
        modeStyle.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        modeStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); modeStyle.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA"))); modeStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 4, 0)));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#EFF6FC"))); selected.Setters.Add(new Setter(Control.BorderBrushProperty, EditorModalDesign.Color("#005FB8"))); selected.Setters.Add(new Setter(Control.ForegroundProperty, EditorModalDesign.Color("#005FB8"))); modeStyle.Triggers.Add(selected); mode.ItemContainerStyle = modeStyle;
        body.Children.Add(new TextBlock { Text = "입력 방식", FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 4) });
        var modeCard = EditorModalDesign.Card(mode, new Thickness(0)); modeCard.Padding = new Thickness(16, 12, 16, 8); body.Children.Add(modeCard);
        body.Children.Add(new TextBlock { Text = "키 지정", FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 4) });
        var keys = new Canvas { Width = 432, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
        var leftLabel = new TextBlock { Text = "왼손  3 · 2 · 1", FontSize = 12, Foreground = EditorModalDesign.Color("#9F9F9F") }; keys.Children.Add(leftLabel); Canvas.SetLeft(leftLabel, 0); Canvas.SetTop(leftLabel, 0);
        var rightLabel = new TextBlock { Text = "오른손  4 · 5 · 6", FontSize = 12, Foreground = EditorModalDesign.Color("#9F9F9F") }; keys.Children.Add(rightLabel); Canvas.SetLeft(rightLabel, 230); Canvas.SetTop(rightLabel, 0);
        var hint = new TextBlock { Foreground = EditorModalDesign.Color("#9F9F9F"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var settingPreset = false;
        foreach (var (field, i) in KeyFields.Select((field, i) => (field, i)))
        {
            System.Windows.Automation.AutomationProperties.SetName(field, $"{i + 1}점 키"); keys.Children.Add(field);
            EditorModalDesign.Input(field); field.FontSize = 14;
            Canvas.SetLeft(field, i < 3 ? (2 - i) * 48 : 230 + (i - 3) * 48); Canvas.SetTop(field, 20);
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
            UpdateMode();
            if (mode.SelectedIndex is < 0 or > 2) return;
            settingPreset = true;
            try { for (var i = 0; i < 6; i++) KeyFields[i].Text = presets[mode.SelectedIndex][i].ToString(); }
            finally { settingPreset = false; }
        };
        void UpdateMode()
        {
            var custom = mode.SelectedIndex == 3;
            foreach (var field in KeyFields) { field.IsReadOnly = !custom; field.Background = EditorModalDesign.Color(custom ? "#FBFBFB" : "#F5F5F5"); field.Foreground = EditorModalDesign.Color(custom ? "#1A1A1A" : "#9F9F9F"); field.FontWeight = FontWeights.Bold; }
            hint.Text = custom ? "고칠 키를 선택한 뒤 원하는 키를 누릅니다." : "정해진 값이라 고칠 수 없다. 고치려면 ‘사용자’를 고른다.";
        }
        UpdateMode();
        var keyCard = EditorModalDesign.Card(keys, new Thickness(0)); keyCard.Padding = new Thickness(16, 12, 16, 8); body.Children.Add(keyCard); body.Children.Add(hint);
        Action("취소", () => DialogResult = false); Action("확인", () => { if (Apply()) DialogResult = true; }, true);
        EditorModalDesign.Frame(this, Root, Actions);
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
        Func<string, SearchPanel, Task<string>> findNext, Func<string, string, SearchPanel, bool, Task<string>> replaceMatches, string sixDotKeys = "F D S J K L") : base("찾아 바꾸기", 460, 288)
    {
        var panel = new StackPanel { Margin = new Thickness(24, 0, 24, 0) }; Body(panel);
        var query = new TextBox { Text = initialQuery };
        var replacement = new TextBox();
        var result = new TextBlock { Margin = new Thickness(0, 20, 0, 0), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") };
        var sixDot = new SixDotInput(); sixDot.Configure(sixDotKeys);
        var toggle = new ToggleButton { IsChecked = target == SearchPanel.Braille, Width = 32, Height = 32, MinHeight = 0, Padding = new Thickness(0), Background = EditorModalDesign.Color("#FBFBFB"), BorderBrush = EditorModalDesign.Color("#EAEAEA") };
        toggle.BorderThickness = new Thickness(1); toggle.Template = EditorModalDesign.ButtonTemplate(typeof(ToggleButton));
        System.Windows.Automation.AutomationProperties.SetName(toggle, "점자 입력");
        void UpdateToggle()
        {
            var on = toggle.IsChecked == true;
            toggle.Content = EditorModalDesign.Icon(on ? "find-braille-on" : "find-braille-off");
            toggle.BorderThickness = new Thickness(on ? 2 : 1);
            toggle.BorderBrush = EditorModalDesign.Color(on ? "#005FB8" : "#EAEAEA"); toggle.Background = EditorModalDesign.Color(on ? "#EFF6FC" : "#FBFBFB");
            toggle.ToolTip = on ? "점자 입력 켬" : "점자 입력 끔"; sixDot.Reset();
        }
        toggle.Checked += (_, _) => UpdateToggle(); toggle.Unchecked += (_, _) => UpdateToggle(); UpdateToggle();
        SearchPanel Target() => toggle.IsChecked == true ? SearchPanel.Braille : SearchPanel.Text;
        var form = new StackPanel();
        void Field(string label, TextBox control, bool showToggle)
        {
            var row = new DockPanel { Height = 32, Margin = new Thickness(0, 0, 0, showToggle ? 4 : 0), LastChildFill = false };
            row.Children.Add(new TextBlock { Text = label, Width = 134, Foreground = EditorModalDesign.Color("#1A1A1A"), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
            if (showToggle) { DockPanel.SetDock(toggle, Dock.Right); toggle.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(toggle); }
            control.Width = 198; control.Height = 32; control.MinHeight = 0; control.VerticalContentAlignment = VerticalAlignment.Center;
            EditorModalDesign.Input(control);
            System.Windows.Automation.AutomationProperties.SetName(control, label);
            if (!showToggle)
            {
                var input = new Grid { Width = 198, Height = 32 };
                input.Children.Add(control);
                var hint = new TextBlock { Text = "바꿀 말을 적는다", FontSize = 12, Foreground = EditorModalDesign.Color("#9F9F9F"),
                    Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                System.Windows.Automation.AutomationProperties.SetAutomationId(hint, "ReplacementPlaceholder");
                input.Children.Add(hint);
                control.TextChanged += (_, _) => hint.Visibility = control.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                row.Children.Add(input);
            }
            else row.Children.Add(control);
            form.Children.Add(row);
            control.PreviewKeyDown += (_, e) =>
            {
                if (toggle.IsChecked != true || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return;
                var key = Controls.SixDotKey.FromEvent(e); if (key is { } c && sixDot.Press(c)) e.Handled = true;
            };
            control.PreviewKeyUp += (_, e) =>
            {
                if (toggle.IsChecked != true) return;
                var key = Controls.SixDotKey.FromEvent(e);
                if (key is not { } c || !sixDot.IsMapped(c)) return;
                var cell = sixDot.Release(c); if (cell is not null) control.SelectedText = cell; e.Handled = true;
            };
            control.LostKeyboardFocus += (_, _) => sixDot.Reset();
        }
        Field("찾을 내용", query, true); Field("바꿀 내용", replacement, false);
        panel.Children.Add(new TextBlock { Text = "찾기 설정", Foreground = (Brush)FindResource("Muted"), FontSize = 12, Margin = new Thickness(0, 16, 0, 8) });
        var formCard = EditorModalDesign.Card(form, new Thickness(0)); formCard.Padding = new Thickness(16, 12, 16, 8); panel.Children.Add(formCard); panel.Children.Add(result);
        var busy = false;
        async Task SearchAsync(Func<Task<string>> action)
        {
            if (busy) return;
            busy = true; panel.IsEnabled = false;
            try { result.Text = await action(); }
            catch (Exception ex) { result.Text = ex.Message; }
            finally { panel.IsEnabled = true; busy = false; }
        }
        Action("닫기", Close);
        foreach (var (label, action) in new (string, Func<Task<string>>)[]
        {
            ("모두 바꾸기", () => replaceMatches(query.Text, replacement.Text, Target(), true)),
            ("바꾸기", () => replaceMatches(query.Text, replacement.Text, Target(), false)),
            ("다음 찾기", () => findNext(query.Text, Target()))
        })
        {
            Action(label, async () => await SearchAsync(action), label == "다음 찾기");
        }
        EditorModalDesign.Frame(this, Root, Actions);
        foreach (var button in Actions.Children.OfType<Button>()) button.Width = (string)button.Content == "모두 바꾸기" ? 110 : (string)button.Content == "다음 찾기" ? 96 : 88;
        ((Button)Actions.Children[0]).Margin = new Thickness(0);
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
        "유지하면 이 블록은 다시 점역되지 않습니다.", ["유지", "덮어쓰기", "취소"], width: 520, height: 206) { }
}
