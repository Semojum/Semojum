using System.Windows;
using System.Windows.Controls;
using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Dialogs;

public sealed class OptionsEditor : StackPanel
{
    private readonly ComboBox korean, english, rule, mixed, spaces, wrap, choices;
    private Dictionary<string, VisualDescription> visual = [];
    public OptionsEditor()
    {
        korean = Pair("한글", "정자(1급)", "약자(2급)"); english = Pair("영문", "정자(1급)", "약자(2급)");
        rule = Pair("점역 규정", "한글 규정", "영어 규정"); mixed = Pair("한영 혼용 규정", "한국어", "영어");
        spaces = Pair("연속 공백", "그대로", "하나로"); wrap = Pair("줄바꿈 단위", "음절", "어절");
        choices = Pair("선택지 배치", "한 줄에 하나씩", "짧으면 합쳐 적기");
        var button = CompactDialog.Button("설명 유형 설정…", () =>
        {
            var d = new VisualDescriptionDialog(visual) { Owner = Window.GetWindow(this) };
            if (d.ShowDialog() == true) visual = d.Result;
            return Task.CompletedTask;
        });
        CompactDialog.Row(this, "시각 자료", button);
        Children.Add(new TextBlock { Text = "영어 문서·한영 혼용 규정·연속 공백 설정은 보존되지만 현재 로컬 재점역에는 적용되지 않습니다.",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, FontSize = 12, Margin = new(0, 8, 0, 4) });
        Set(new());
    }
    private ComboBox Pair(string label, params string[] labels)
    { var box = CompactDialog.Choices(labels); CompactDialog.Row(this, label, box); return box; }
    public void Set(BrailleOptions value)
    {
        korean.SelectedIndex = value.KoreanGrade1 ? 0 : 1; english.SelectedIndex = value.EnglishGrade1 ? 0 : 1;
        rule.SelectedIndex = value.EnglishDocument ? 1 : 0; mixed.SelectedIndex = value.MixedRule == "english" ? 1 : 0;
        spaces.SelectedIndex = value.ConsecutiveSpace == "keep" ? 0 : 1; wrap.SelectedIndex = value.LineBreakUnit == "word" ? 1 : 0;
        choices.SelectedIndex = value.ChoiceLayout == "onePerLine" ? 0 : 1;
        visual = new(value.VisualDescriptions ?? VisualDescriptions.Defaults());
    }
    public BrailleOptions Value => new(korean.SelectedIndex == 0, english.SelectedIndex == 0, rule.SelectedIndex == 1,
        wrap.SelectedIndex == 1 ? "word" : "syllable", mixed.SelectedIndex == 1 ? "english" : "korean",
        spaces.SelectedIndex == 0 ? "keep" : "single", choices.SelectedIndex == 0 ? "onePerLine" : "merge", new(visual));
}

public sealed class VisualDescriptionDialog : CompactDialog
{
    public Dictionary<string, VisualDescription> Result { get; private set; }
    public VisualDescriptionDialog(Dictionary<string, VisualDescription> current) : base("시각 자료 설명 유형", 620, 520)
    {
        Result = new(current); var tabs = new TabControl { Margin = new(16) }; Body(tabs);
        var controls = new Dictionary<string, (CheckBox[] Checks, ComboBox Default)>();
        foreach (var (type, label) in new[] { ("figure", "그림"), ("table", "표"), ("chart", "차트"), ("diagram", "도표"), ("cartoon", "만화") })
        {
            var p = new StackPanel { Margin = new(16) }; var value = current.GetValueOrDefault(type) ?? VisualDescriptions.Defaults()[type];
            var checks = VisualDescriptions.Choices[type].Select(c => new CheckBox { Content = c, IsChecked = value.Options.Contains(c), Margin = new(0, 7, 0, 7) }).ToArray();
            foreach (var c in checks) p.Children.Add(c);
            var select = Choices(value.Options, value.DefaultOption);
            foreach (var check in checks) check.Click += (_, _) =>
            {
                var old = select.SelectedItem;
                select.ItemsSource = checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray();
                select.SelectedItem = old; if (select.SelectedIndex < 0) select.SelectedIndex = 0;
            };
            Row(p, "기본 설명", select); controls[type] = (checks, select); tabs.Items.Add(new TabItem { Header = label, Content = p });
        }
        Action("확인", () =>
        {
            Result = controls.ToDictionary(p => p.Key, p => new VisualDescription(p.Value.Checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray(), p.Value.Default.SelectedItem?.ToString() ?? ""));
            VisualDescriptions.Validate(Result); DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
    }
}

public sealed class DefaultsDialog : CompactDialog
{
    public DefaultsDialog(SettingsClient client) : base("기본 점역 옵션 설정", 620, 620)
    {
        var panel = new StackPanel { Margin = new(16, 8, 16, 8) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var options = new OptionsEditor(); panel.Children.Add(options);
        var forms = new ComboBox { MinHeight = 24 }; Row(panel, "점역 양식", forms);
        var numbers = Choices(["홀수 페이지", "모든 페이지"], "홀수 페이지"); Row(panel, "페이지행 면", numbers);
        forms.SelectionChanged += (_, _) => { if (forms.SelectedItem is BrailleForm f) options.Set(f.Options); };
        OnLoad(async () =>
        {
            var all = await client.FormsAsync(Lifetime.Token); var defaults = await client.DefaultsAsync(Lifetime.Token);
            forms.ItemsSource = all; forms.SelectedItem = all.FirstOrDefault(f => f.FormId == defaults.FormId);
            options.Set(defaults.Options); numbers.SelectedIndex = defaults.PageNumberLine == "every" ? 1 : 0;
        });
        Action("확인", async () =>
        {
            await client.SaveDefaultsAsync(new(options.Value, (forms.SelectedItem as BrailleForm)?.FormId, numbers.SelectedIndex == 1 ? "every" : "odd"), Lifetime.Token);
            DialogResult = true;
        }, true);
        Action("취소", () => DialogResult = false);
        Action("기본값 복원", () => { forms.SelectedItem = forms.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == "builtin:MIDHIGH_TEXTBOOK"); options.Set(new()); numbers.SelectedIndex = 0; });
    }
}

public sealed class FormDialog : CompactDialog
{
    public BrailleForm? Selected { get; private set; }
    public FormDialog(SettingsClient client, PageLayout currentLayout, BrailleOptions currentOptions, string? selectedId = null) : base("점역 양식", 460, 440)
    {
        var panel = new StackPanel { Margin = new(16, 10, 16, 10) }; Body(panel);
        panel.Children.Add(new TextBlock { Text = "양식", Margin = new(0, 0, 0, 6) });
        var picker = new ComboBox { MinHeight = 32 }; panel.Children.Add(picker);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 28, Margin = new(0, 12, 0, 12) }; panel.Children.Add(summary);
        var editbar = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(editbar);
        var edit = Button("수정…", async () => await RunAsync(() => EditAsync(false)));
        var delete = Button("삭제", async () => await RunAsync(async () =>
        {
            if (picker.SelectedItem is not BrailleForm f || f.Builtin) return;
            if (MessageBox.Show(this, $"‘{f.Name}’ 양식을 삭제하시겠습니까?", "양식 삭제", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await client.DeleteFormAsync(f, Lifetime.Token); await ReloadAsync(null);
        })); editbar.Children.Add(edit); editbar.Children.Add(delete);
        picker.SelectionChanged += (_, _) =>
        {
            var f = picker.SelectedItem as BrailleForm;
            edit.IsEnabled = delete.IsEnabled = f is { Builtin: false };
            summary.Text = f is null ? "양식이 없습니다." : $"이 양식이 정하는 값 (읽기 전용)\n지면       {f.Layout.Columns}칸 × {f.Layout.Lines}줄\n한글       {(f.Options.KoreanGrade1 ? "정자(1급)" : "약자(2급)")}\n줄바꿈    {(f.Options.LineBreakUnit == "word" ? "어절" : "음절")} 단위\n페이지행 {f.Layout.PageNumbers}\n꼬리말    {f.Layout.FooterAlign switch { "right" => "오른쪽", "left" => "왼쪽", _ => "가운데" }}";
        };
        async Task ReloadAsync(string? id)
        { picker.ItemsSource = await client.FormsAsync(Lifetime.Token); picker.SelectedItem = picker.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == id); if (picker.SelectedIndex < 0) picker.SelectedIndex = 0; }
        async Task EditAsync(bool create)
        {
            var selected = picker.SelectedItem as BrailleForm;
            var all = picker.Items.Cast<BrailleForm>().ToArray();
            var dialog = new FormEditDialog(client, all, create ? null : selected,
                create ? currentLayout : selected!.Layout, create ? currentOptions : selected!.Options, selected?.FormId) { Owner = this };
            if (dialog.ShowDialog() == true) await ReloadAsync(dialog.Saved!.FormId);
        }
        OnLoad(() => ReloadAsync(selectedId));
        Action("확인", () => { Selected = picker.SelectedItem as BrailleForm; if (Selected is not null) DialogResult = true; }, true);
        Action("취소", () => DialogResult = false);
        Action("새 양식 저장", () => EditAsync(true));
    }
}

public sealed class FormEditDialog : CompactDialog
{
    public BrailleForm? Saved { get; private set; }
    public FormEditDialog(SettingsClient client, BrailleForm[] forms, BrailleForm? current, PageLayout layout, BrailleOptions options, string? baseId) : base("사용자 양식 설정", 560, 650)
    {
        var panel = new StackPanel { Margin = new(16) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var name = new TextBox { Text = current?.Name ?? "", MaxLength = 100, MinHeight = 32 }; Row(panel, "양식 이름", name, 130);
        var basis = new ComboBox { ItemsSource = forms.Where(f => f.FormId != current?.FormId).ToArray(), MinHeight = 32 };
        basis.SelectedItem = forms.FirstOrDefault(f => f.FormId == (current?.BaseFormId ?? baseId)); Row(panel, "바탕 양식", basis, 130);
        var editor = new OptionsEditor(); editor.Set(options);
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateLayoutText() => summary.Text = $"{layout.Columns}칸 × {layout.Lines}줄 · {layout.PageNumbers} · 점자 시작 면 {layout.StartPage}";
        UpdateLayoutText(); panel.Children.Add(summary);
        panel.Children.Add(Button("페이지 설정…", () => { var d = new LayoutDialog(layout) { Owner = this }; if (d.ShowDialog() == true) { layout = d.Result; UpdateLayoutText(); } return Task.CompletedTask; }));
        panel.Children.Add(editor);
        Action("저장", async () => { Saved = await client.SaveFormAsync(current?.FormId, name.Text, (basis.SelectedItem as BrailleForm)?.FormId, layout, editor.Value, Lifetime.Token); DialogResult = true; }, true);
        panel.Children.Add(new TextBlock { Text = "양식은 서버에만 저장됩니다. 짝수 면 번호·번호 범위·꼬리말 출력 면·짝수 면 보정은 서버 양식에 없으므로 저장이 거부됩니다(값을 바꿔 저장하지 않습니다).",
            TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.DimGray, FontSize = 12, Margin = new(0, 8, 0, 4) });
        Action("취소", () => DialogResult = false);
        Action("바탕 양식 값으로", () => { if (basis.SelectedItem is BrailleForm f) { layout = f.Layout; editor.Set(f.Options); UpdateLayoutText(); } });
    }
}
