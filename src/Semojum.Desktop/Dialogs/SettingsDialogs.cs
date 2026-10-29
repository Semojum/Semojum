using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop.Dialogs;

public sealed class OptionsEditor : StackPanel
{
    private readonly SegmentedChoice korean, rule, mixed, spaces, wrap, choices;
    private bool englishGrade1;
    private Dictionary<string, VisualDescription> visual = [];
    public OptionsEditor()
    {
        korean = Pair("한글", "정자(1급)", "약자(2급)");
        rule = Pair("점역 규정", "한글 규정", "영어 규정"); mixed = Pair("한영 혼용 규정", "한국어", "영어");
        spaces = Pair("연속 공백", "그대로", "하나로"); wrap = Pair("줄바꿈 단위", "음절", "어절");
        choices = Pair("선택지 배치", "한 줄에 하나씩", "짧으면 합쳐 적기");
        rule.ToolTip = mixed.ToolTip = spaces.ToolTip = "이 설정은 저장되며 서버 변환에 사용됩니다. 현재 로컬 재점역에는 적용되지 않습니다.";
        Set(new());
    }
    private SegmentedChoice Pair(string label, params string[] labels)
    { var box = new SegmentedChoice(labels); CompactDialog.SettingRow(this, label, box); return box; }
    public void Set(BrailleOptions value)
    {
        korean.SelectedIndex = value.KoreanGrade1 ? 0 : 1; englishGrade1 = value.EnglishGrade1;
        rule.SelectedIndex = value.EnglishDocument ? 1 : 0; mixed.SelectedIndex = value.MixedRule == "english" ? 1 : 0;
        spaces.SelectedIndex = value.ConsecutiveSpace == "keep" ? 0 : 1; wrap.SelectedIndex = value.LineBreakUnit == "word" ? 1 : 0;
        choices.SelectedIndex = value.ChoiceLayout == "onePerLine" ? 0 : 1;
        visual = new(value.VisualDescriptions ?? VisualDescriptions.Defaults());
    }
    public BrailleOptions Value => new(korean.SelectedIndex == 0, englishGrade1, rule.SelectedIndex == 1,
        wrap.SelectedIndex == 1 ? "word" : "syllable", mixed.SelectedIndex == 1 ? "english" : "korean",
        spaces.SelectedIndex == 0 ? "keep" : "single", choices.SelectedIndex == 0 ? "onePerLine" : "merge", new(visual));
}

public sealed class VisualDescriptionDialog : CompactDialog
{
    public Dictionary<string, VisualDescription> Result { get; private set; }
    public VisualDescriptionDialog(Dictionary<string, VisualDescription> current) : base("시각 자료 설명 유형", 620, 470)
    {
        Result = new(current); var rows = new StackPanel { Margin = new(16, 12, 16, 0) }; Body(rows);
        var controls = new Dictionary<string, (CheckBox[] Checks, ComboBox Default)>();
        foreach (var (type, label) in new[] { ("figure", "그림"), ("table", "표"), ("chart", "차트"), ("diagram", "도표"), ("cartoon", "만화") })
        {
            var value = current.GetValueOrDefault(type) ?? VisualDescriptions.Defaults()[type];
            var row = new Grid { Height = 70 };
            row.ColumnDefinitions.Add(new() { Width = new(70) }); row.ColumnDefinitions.Add(new() { Width = new(352) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 8, 0, 0) });
            var checks = VisualDescriptions.Choices[type].Select(c => new CheckBox { Content = c, IsChecked = value.Options.Contains(c), Height = 24, Margin = new(0), Style = (Style)FindResource("CompactChoiceCheck") }).ToArray();
            var choices = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Rows = 2, Height = 48, VerticalAlignment = VerticalAlignment.Top };
            foreach (var c in checks) choices.Children.Add(c); Grid.SetColumn(choices, 1); row.Children.Add(choices);
            var select = Choices(value.Options, value.DefaultOption);
            var defaultPanel = new StackPanel { Margin = new(0, 0, 16, 0) };
            defaultPanel.Children.Add(new TextBlock { Text = "기본", Height = 22, Foreground = (System.Windows.Media.Brush)FindResource("Muted") }); defaultPanel.Children.Add(select);
            Grid.SetColumn(defaultPanel, 2); row.Children.Add(defaultPanel);
            foreach (var check in checks) check.Click += (_, _) =>
            {
                var old = select.SelectedItem;
                select.ItemsSource = checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray();
                select.SelectedItem = old; if (select.SelectedIndex < 0) select.SelectedIndex = 0;
            };
            controls[type] = (checks, select);
            rows.Children.Add(new Border { BorderBrush = (System.Windows.Media.Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, type == "cartoon" ? 0 : 1), Margin = new(0, 0, 0, type == "cartoon" ? 0 : 7), Child = row });
        }
        Action("확인", () =>
        {
            Result = controls.ToDictionary(p => p.Key, p => new VisualDescription(p.Value.Checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray(), p.Value.Default.SelectedItem?.ToString() ?? ""));
            VisualDescriptions.Validate(Result); DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
        Action("기본값 복원", () =>
        {
            foreach (var (type, fields) in controls)
            {
                var value = VisualDescriptions.Defaults()[type];
                foreach (var check in fields.Checks) check.IsChecked = value.Options.Contains(check.Content.ToString()!);
                fields.Default.ItemsSource = value.Options; fields.Default.SelectedItem = value.DefaultOption;
            }
        }).Width = 99;
    }
}

public sealed class DefaultsDialog : CompactDialog
{
    public DefaultsDialog(SettingsClient client) : base("기본 점역 옵션 설정", 620, 500)
    {
        var panel = new StackPanel { Margin = new(16, 7, 16, 0) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var options = new OptionsEditor(); panel.Children.Add(options);
        SettingRow(panel, "점역문서 형식", Choices([".brf — 점자 파일"], ".brf — 점자 파일"));
        SettingRow(panel, "교열문서 형식", Choices([".txt — 텍스트 파일"], ".txt — 텍스트 파일"));
        var forms = new ComboBox { DisplayMemberPath = nameof(BrailleForm.Name) }; SettingRow(panel, "점역 양식", forms);
        var pageNumberLine = "odd";
        forms.SelectionChanged += (_, _) => { if (forms.SelectedItem is BrailleForm f) options.Set(f.Options); };
        OnLoad(async () =>
        {
            var all = await client.FormsAsync(Lifetime.Token); var defaults = await client.DefaultsAsync(Lifetime.Token);
            forms.ItemsSource = all; forms.SelectedItem = all.FirstOrDefault(f => f.FormId == defaults.FormId);
            options.Set(defaults.Options); pageNumberLine = defaults.PageNumberLine;
        });
        Action("확인", async () =>
        {
            await client.SaveDefaultsAsync(new(options.Value, (forms.SelectedItem as BrailleForm)?.FormId, pageNumberLine), Lifetime.Token);
            DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
        Action("기본값 복원", () => { forms.SelectedItem = forms.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == "builtin:GENERAL_BOOK"); options.Set(new()); pageNumberLine = "odd"; }).Width = 99;
    }
}

public sealed class FormDialog : CompactDialog
{
    public BrailleForm? Selected { get; private set; }
    public FormDialog(SettingsClient client, PageLayout currentLayout, BrailleOptions currentOptions, string? selectedId = null) : base("점역 양식", 460, 340)
    {
        var panel = new StackPanel { Margin = new(16, 10, 16, 10) }; Body(panel);
        panel.Children.Add(new TextBlock { Text = "양식", Height = 20, Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        var picker = new ComboBox { MinHeight = 32, Height = 32, DisplayMemberPath = nameof(BrailleForm.Name), ToolTip = "사용자 양식은 오른쪽 클릭으로 수정·삭제할 수 있습니다." }; panel.Children.Add(picker);
        FormPickerDialog.Attach(picker, this);
        panel.Children.Add(new TextBlock { Text = "이 양식이 정하는 값 (읽기 전용)", Height = 20, Margin = new(0, 6, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        var values = new Dictionary<string, TextBlock>();
        foreach (var label in new[] { "지면", "한글", "줄바꿈", "페이지행", "분권" })
        {
            var row = new DockPanel { Height = 31 };
            row.Children.Add(new TextBlock { Text = label, Width = 94, FontSize = 14 });
            var value = new TextBlock { FontSize = 14, FontWeight = label == "한글" ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis }; values[label] = value; row.Children.Add(value);
            panel.Children.Add(new System.Windows.Controls.Border { BorderBrush = (System.Windows.Media.Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        var edit = new MenuItem { Header = "수정…" }; edit.Click += async (_, _) => await RunAsync(() => EditAsync(false));
        var delete = new MenuItem { Header = "삭제…" }; delete.Click += async (_, _) => await RunAsync(async () =>
        {
            if (picker.SelectedItem is not BrailleForm f || f.Builtin) return;
            if (new DeleteFormDialog(f.Name) { Owner = this }.ShowDialog() != true) return;
            await client.DeleteFormAsync(f, Lifetime.Token); await ReloadAsync(null);
        });
        picker.ContextMenu = new ContextMenu(); picker.ContextMenu.Items.Add(edit); picker.ContextMenu.Items.Add(delete);
        picker.SelectionChanged += (_, _) =>
        {
            var f = picker.SelectedItem as BrailleForm;
            edit.IsEnabled = delete.IsEnabled = f is { Builtin: false } && f.FormId != "user:new";
            values["지면"].Text = f is null || f.FormId == "user:new" ? "사용자 양식을 설정해 주세요." : $"{f.Layout.Columns}칸 × {f.Layout.Lines}줄";
            values["한글"].Text = f is null ? "—" : f.Options.KoreanGrade1 ? "정자(1급)" : "약자(2급)";
            values["줄바꿈"].Text = f is null ? "—" : (f.Options.LineBreakUnit == "word" ? "어절" : "음절") + " 단위";
            values["페이지행"].Text = f?.Layout.PageNumbers ?? "—";
            values["분권"].Text = "—"; values["분권"].ToolTip = "분권은 현재 양식 저장 항목에 포함되지 않습니다.";
        };
        async Task ReloadAsync(string? id)
        {
            var forms = await client.FormsAsync(Lifetime.Token);
            picker.ItemsSource = forms.Any(f => !f.Builtin) ? forms : [.. forms, new BrailleForm("user:new", "사용자 양식", false, null, currentLayout, currentOptions)];
            picker.SelectedItem = picker.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == id); if (picker.SelectedIndex < 0) picker.SelectedIndex = 0;
        }
        async Task EditAsync(bool create)
        {
            var selected = picker.SelectedItem as BrailleForm;
            var all = picker.Items.Cast<BrailleForm>().Where(f => f.FormId != "user:new").ToArray();
            if (selected?.FormId == "user:new") selected = null;
            var dialog = new FormEditDialog(client, all, create ? null : selected,
                selected?.Layout ?? currentLayout, selected?.Options ?? currentOptions, selected?.FormId) { Owner = this };
            if (dialog.ShowDialog() == true) await ReloadAsync(dialog.Saved!.FormId);
        }
        OnLoad(() => ReloadAsync(selectedId));
        Action("확인", () => { Selected = picker.SelectedItem as BrailleForm; if (Selected is not null && Selected.FormId != "user:new") DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
        Action("사용자 양식 설정…", () => EditAsync(picker.SelectedItem is not BrailleForm { Builtin: false } f || f.FormId == "user:new")).Width = 149;
    }
}

public sealed class FormNameDialog : CompactDialog
{
    internal TextBox NameInput { get; } = new() { MaxLength = 100 };
    public string Value => NameInput.Text.Trim();
    public FormNameDialog(string initial = "") : base("새 양식 저장", 360, 170)
    {
        MinHeight = 170; NameInput.Text = initial;
        var panel = new StackPanel { Margin = new(16, 10, 16, 0) };
        panel.Children.Add(new TextBlock { Text = "양식 이름", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Height = 20 });
        NameInput.Height = 32; NameInput.Padding = new(8, 0, 8, 0); panel.Children.Add(NameInput);
        panel.Children.Add(new TextBlock { Text = "지금 창에 뜬 값을 그대로 담습니다.", Foreground = (System.Windows.Media.Brush)FindResource("Muted"), Margin = new(0, 8, 0, 0) }); Body(panel);
        Action("저장", () => { if (Value.Length == 0) throw new ArgumentException("양식 이름을 입력해 주세요."); DialogResult = true; }, true);
        Action("취소", () => DialogResult = false); Loaded += (_, _) => NameInput.Focus();
    }
}

public sealed class FormEditDialog : CompactDialog
{
    public BrailleForm? Saved { get; private set; }
    public FormEditDialog(SettingsClient client, BrailleForm[] forms, BrailleForm? current, PageLayout layout, BrailleOptions options, string? baseId) : base("사용자 양식 설정", 560, 520)
    {
        var panel = new StackPanel { Margin = new(16, 10, 16, 0) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Label(string text, double top = 0) => panel.Children.Add(new TextBlock { Text = text, Height = 20, Margin = new(0, top, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        Label("양식 이름");
        var name = new TextBox { Text = current?.Name ?? "", MaxLength = 100, Height = 32, Padding = new(7, 0, 7, 0) }; panel.Children.Add(name);
        Label("바탕 양식", 8);
        var basis = new ComboBox { ItemsSource = forms.Where(f => f.FormId != current?.FormId).ToArray(), Height = 32, DisplayMemberPath = nameof(BrailleForm.Name) };
        basis.SelectedItem = forms.FirstOrDefault(f => f.FormId == (current?.BaseFormId ?? baseId)); panel.Children.Add(basis);
        var rows = new StackPanel { Margin = new(0, 5, 0, 0) }; panel.Children.Add(rows);
        var lines = new TextBox(); var columns = new TextBox();
        void Number(string label, TextBox field, string unit)
        {
            var fields = new StackPanel { Orientation = Orientation.Horizontal };
            field.Width = 96; field.Height = 24; field.Padding = new(7, 0, 7, 0); fields.Children.Add(field);
            fields.Children.Add(new TextBlock { Text = unit, Margin = new(10, 0, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
            SettingRow(rows, label, fields, 154, 260);
        }
        Number("페이지당 줄 수", lines, "줄"); Number("줄당 칸 수", columns, "칸");
        var korean = new SegmentedChoice("정자(1급)", "약자(2급)"); SettingRow(rows, "한글", korean, 154, 260);
        var wrap = new SegmentedChoice("음절", "어절"); SettingRow(rows, "줄바꿈 단위", wrap, 154, 260);
        var numbers = Choices(["홀수 페이지", "짝수 페이지", "모든 페이지", "출력 안 함"]); SettingRow(rows, "페이지 번호 출력", numbers, 154, 260);
        var footer = Choices(["왼쪽", "가운데", "오른쪽"]); SettingRow(rows, "꼬리말", footer, 154, 260);
        void SetFields()
        {
            lines.Text = layout.Lines.ToString(); columns.Text = layout.Columns.ToString(); korean.SelectedIndex = options.KoreanGrade1 ? 0 : 1;
            wrap.SelectedIndex = options.LineBreakUnit == "word" ? 1 : 0; numbers.SelectedItem = layout.PageNumbers;
            footer.SelectedIndex = layout.FooterAlign switch { "left" => 0, "right" => 2, _ => 1 };
        }
        SetFields();
        async Task SaveAsync(string? id, string title)
        {
            if (!int.TryParse(lines.Text, out var l) || !int.TryParse(columns.Text, out var c)) throw new ArgumentException("줄 수와 칸 수를 정수로 입력해 주세요.");
            var updatedLayout = layout with { Lines = l, Columns = c, PageNumbers = numbers.SelectedItem?.ToString() ?? layout.PageNumbers, FooterAlign = footer.SelectedIndex switch { 0 => "left", 2 => "right", _ => "center" } };
            var updatedOptions = options with { KoreanGrade1 = korean.SelectedIndex == 0, LineBreakUnit = wrap.SelectedIndex == 1 ? "word" : "syllable" };
            Saved = await client.SaveFormAsync(id, title, (basis.SelectedItem as BrailleForm)?.FormId, updatedLayout, updatedOptions, Lifetime.Token); DialogResult = true;
        }
        Action("저장", () => SaveAsync(current?.FormId, name.Text), true);
        Action("다른 이름으로 저장", async () => { var prompt = new FormNameDialog(name.Text) { Owner = this }; if (prompt.ShowDialog() == true) await SaveAsync(null, prompt.Value); }).Width = 142;
        Action("취소", () => DialogResult = false);
        Action("바탕 양식 값으로", () => { if (basis.SelectedItem is BrailleForm f) { layout = f.Layout; options = f.Options; SetFields(); } }).Width = 130;
    }
}
