using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class OptionsEditor : StackPanel
{
    private readonly SegmentedChoice korean, rule, mixed, spaces, wrap, choices;
    private readonly bool grouped;
    private StackPanel? currentGroup;
    private bool englishGrade1;
    private Dictionary<string, VisualDescription> visual = [];
    public OptionsEditor(bool grouped = false)
    {
        this.grouped = grouped;
        if (grouped) BeginGroup("글과 규정");
        korean = Pair("한글", "정자(1급)", "약자(2급)");
        rule = Pair("점역 규정", "한글 규정", "영어 규정"); mixed = Pair("한영 혼용 규정", "한국어", "영어");
        if (grouped) BeginGroup("줄과 빈칸");
        spaces = Pair("연속 공백", "그대로", "하나로"); wrap = Pair("줄바꿈 단위", "음절", "어절");
        choices = Pair("선택지 배치", "한 줄에 하나씩", "짧으면 합쳐 적기");
        rule.ToolTip = mixed.ToolTip = spaces.ToolTip = "이 설정은 저장되며 서버 변환에 사용됩니다. 현재 로컬 재점역에는 적용되지 않습니다.";
        Set(new());
    }
    private void BeginGroup(string title)
    {
        Children.Add(new TextBlock { Text = title, FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 4) });
        currentGroup = new StackPanel(); var card = EditorModalDesign.Card(currentGroup, new Thickness(0)); card.Padding = new Thickness(16, 12, 16, 8); Children.Add(card);
    }
    private SegmentedChoice Pair(string label, params string[] labels)
    {
        var box = new SegmentedChoice(labels);
        if (grouped) { FooterDesign.StyleAlignment(box); box.Width = 264; AutomationProperties.SetName(box, label); FooterDesign.Row(currentGroup!, label, box); }
        else CompactDialog.SettingRow(this, label, box);
        return box;
    }
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
    public VisualDescriptionDialog(Dictionary<string, VisualDescription> current) : base("시각 자료 설명 유형", 700, 518)
    {
        Result = new(current); var rows = new StackPanel { Margin = new(16, 16, 16, 0) }; Body(rows);
        var checkStyle = new Style(typeof(CheckBox));
        var checkRow = new FrameworkElementFactory(typeof(DockPanel)); var square = new FrameworkElementFactory(typeof(Border), "Square");
        square.SetValue(FrameworkElement.WidthProperty, 20d); square.SetValue(FrameworkElement.HeightProperty, 20d); square.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); square.SetValue(Border.BorderThicknessProperty, new Thickness(1)); square.SetValue(Border.BorderBrushProperty, EditorModalDesign.Color("#D1D1D1")); square.SetValue(Border.BackgroundProperty, EditorModalDesign.Color("#FBFBFB")); square.SetValue(DockPanel.DockProperty, Dock.Left);
        var tick = new FrameworkElementFactory(typeof(SharpVectors.Converters.SvgViewbox), "Tick"); tick.SetValue(FrameworkElement.WidthProperty, 16d); tick.SetValue(FrameworkElement.HeightProperty, 16d); tick.SetValue(System.Windows.Controls.Viewbox.StretchProperty, Stretch.None); tick.SetValue(SharpVectors.Converters.SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/visual-check.svg")); tick.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); square.AppendChild(tick); checkRow.AppendChild(square);
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0)); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); checkRow.AppendChild(content);
        var checkTemplate = new ControlTemplate(typeof(CheckBox)) { VisualTree = checkRow }; var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; on.Setters.Add(new Setter(Border.BackgroundProperty, EditorModalDesign.Color("#005FB8"), "Square")); on.Setters.Add(new Setter(Border.BorderBrushProperty, EditorModalDesign.Color("#005FB8"), "Square")); on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Tick")); checkTemplate.Triggers.Add(on);
        var focused = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focused.Setters.Add(new Setter(Border.BorderBrushProperty, EditorModalDesign.Color("#005FB8"), "Square")); checkTemplate.Triggers.Add(focused); checkStyle.Setters.Add(new Setter(Control.TemplateProperty, checkTemplate));
        var controls = new Dictionary<string, (CheckBox[] Checks, ComboBox Default)>();
        foreach (var (type, label) in new[] { ("figure", "그림"), ("table", "표"), ("chart", "차트"), ("diagram", "도표"), ("cartoon", "만화") })
        {
            var value = current.GetValueOrDefault(type) ?? VisualDescriptions.Defaults()[type];
            var row = new Grid { Height = 66 };
            row.ColumnDefinitions.Add(new() { Width = new(70) }); row.ColumnDefinitions.Add(new() { Width = new(396) }); row.ColumnDefinitions.Add(new() { Width = new(36) }); row.ColumnDefinitions.Add(new() { Width = new(150) });
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
            var checks = VisualDescriptions.Choices[type].Select(c => new CheckBox { Content = c, IsChecked = value.Options.Contains(c), Height = 20, Margin = new(0), Style = checkStyle }).ToArray();
            foreach (var check in checks) AutomationProperties.SetName(check, label + " " + check.Content);
            var choices = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Rows = 2, Height = 52, VerticalAlignment = VerticalAlignment.Top };
            foreach (var c in checks) choices.Children.Add(c); Grid.SetColumn(choices, 1); row.Children.Add(choices);
            var select = Choices(value.Options, value.DefaultOption);
            select.Width = 150; select.Height = select.MinHeight = 24; select.Padding = new Thickness(11, 0, 25, 0); select.Background = EditorModalDesign.Color("#FBFBFB"); select.BorderBrush = EditorModalDesign.Color("#EAEAEA"); AutomationProperties.SetName(select, label + " 기본 설명");
            var defaultPanel = new StackPanel(); defaultPanel.Children.Add(new TextBlock { Text = "기본", Height = 18, Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
            var selectFrame = new Grid { Width = 150, Height = 24, Clip = new RectangleGeometry(new Rect(0, 0, 150, 24), 4, 4) }; selectFrame.Children.Add(select);
            selectFrame.Children.Add(new Border { Width = 24, Margin = new Thickness(0, 1, 1, 1), HorizontalAlignment = HorizontalAlignment.Right, Background = EditorModalDesign.Color("#FBFBFB"), IsHitTestVisible = false, Child = EditorModalDesign.Icon("visual-chevron") });
            selectFrame.Children.Add(new Border { BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false }); defaultPanel.Children.Add(selectFrame);
            Grid.SetColumn(defaultPanel, 3); row.Children.Add(defaultPanel);
            foreach (var check in checks) check.Click += (_, _) =>
            {
                var old = select.SelectedItem;
                select.ItemsSource = checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray();
                select.SelectedItem = old; if (select.SelectedIndex < 0) select.SelectedIndex = 0;
            };
            controls[type] = (checks, select);
            rows.Children.Add(new Border { BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new(0, 0, 0, type == "cartoon" ? 0 : 1), Margin = new(0, 0, 0, type == "cartoon" ? 0 : 12), Child = row });
        }
        var restore = Action("기본값 복원", () =>
        {
            foreach (var (type, fields) in controls)
            {
                var value = VisualDescriptions.Defaults()[type];
                foreach (var check in fields.Checks) check.IsChecked = value.Options.Contains(check.Content.ToString()!);
                fields.Default.ItemsSource = value.Options; fields.Default.SelectedItem = value.DefaultOption;
            }
        });
        Action("취소", () => DialogResult = false);
        Action("확인", () =>
        {
            Result = controls.ToDictionary(p => p.Key, p => new VisualDescription(p.Value.Checks.Where(c => c.IsChecked == true).Select(c => c.Content.ToString()!).ToArray(), p.Value.Default.SelectedItem?.ToString() ?? ""));
            VisualDescriptions.Validate(Result); DialogResult = true;
        }, true);
        EditorModalDesign.Frame(this, Root, Actions); restore.Width = 110;
        if (Root.Children.OfType<Border>().FirstOrDefault() is { } footer) { footer.BorderThickness = new Thickness(0, 1, 0, 0); footer.BorderBrush = EditorModalDesign.Color("#EAEAEA"); }
    }
}

public sealed class DefaultsDialog : CompactDialog
{
    public DefaultsDialog(SettingsClient client) : base("기본 점역 옵션 설정", 560, 608)
    {
        var panel = new StackPanel { Margin = new(24, 0, 24, 0) }; Body(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var options = new OptionsEditor(grouped: true); panel.Children.Add(options);
        panel.Children.Add(new TextBlock { Text = "내보낼 꼴", FontSize = 12, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 16, 0, 4) });
        var output = new StackPanel(); var card = EditorModalDesign.Card(output, new Thickness(0)); card.Padding = new Thickness(16, 12, 16, 8); panel.Children.Add(card);
        FrameworkElement SelectField(ComboBox select, string label)
        {
            select.Width = 260; select.Height = select.MinHeight = 32; select.Padding = new Thickness(11, 0, 25, 0); select.Background = EditorModalDesign.Color("#FBFBFB"); select.BorderBrush = EditorModalDesign.Color("#EAEAEA"); AutomationProperties.SetName(select, label);
            var wrapper = new Grid { Width = 260, Height = 32, Clip = new RectangleGeometry(new Rect(0, 0, 260, 32), 4, 4) }; wrapper.Children.Add(select);
            wrapper.Children.Add(new Border { Width = 24, Margin = new Thickness(0, 1, 1, 1), HorizontalAlignment = HorizontalAlignment.Right, Background = EditorModalDesign.Color("#FBFBFB"), IsHitTestVisible = false, Child = EditorModalDesign.Icon("defaults-chevron") });
            wrapper.Children.Add(new Border { BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), IsHitTestVisible = false }); return wrapper;
        }
        FooterDesign.Row(output, "점역문서 형식", SelectField(Choices([".brf — 점자 파일"], ".brf — 점자 파일"), "점역문서 형식"));
        FooterDesign.Row(output, "교열문서 형식", SelectField(Choices([".txt — 텍스트 파일"], ".txt — 텍스트 파일"), "교열문서 형식"));
        var forms = new ComboBox { DisplayMemberPath = nameof(BrailleForm.Name) }; FooterDesign.Row(output, "점역 양식", SelectField(forms, "점역 양식"));
        var pageNumberLine = "odd";
        forms.SelectionChanged += (_, _) => { if (forms.SelectedItem is BrailleForm f) options.Set(f.Options); };
        OnLoad(async () =>
        {
            var all = await client.FormsAsync(Lifetime.Token); var defaults = await client.DefaultsAsync(Lifetime.Token);
            forms.ItemsSource = all; forms.SelectedItem = all.FirstOrDefault(f => f.FormId == defaults.FormId);
            options.Set(defaults.Options); pageNumberLine = defaults.PageNumberLine;
        });
        var restore = Action("기본값 복원", () => { forms.SelectedItem = forms.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == "builtin:GENERAL_BOOK"); options.Set(new()); pageNumberLine = "odd"; });
        Action("취소", () => DialogResult = false);
        Action("확인", async () =>
        {
            await client.SaveDefaultsAsync(new(options.Value, (forms.SelectedItem as BrailleForm)?.FormId, pageNumberLine), Lifetime.Token);
            DialogResult = true;
        }, true);
        EditorModalDesign.Frame(this, Root, Actions); restore.Width = 110;
        if (Root.Children.OfType<Border>().FirstOrDefault() is { } footer) { footer.BorderThickness = new Thickness(0, 1, 0, 0); footer.BorderBrush = EditorModalDesign.Color("#EAEAEA"); }
    }
}

public sealed class FormDialog : CompactDialog
{
    public BrailleForm? Selected { get; private set; }
    public FormDialog(SettingsClient client, PageLayout currentLayout, BrailleOptions currentOptions, string? selectedId = null) : base("점역 양식", 460, 348, popover: true)
    {
        var frame = PublicDialogDesign.Frame(this, "점역 양식", "form-main-close.svg", "FormDialogClose", out var actions); Body(frame);
        var panel = new StackPanel { Margin = new(16, 12, 16, 0) }; Grid.SetRow(panel, 1); frame.Children.Add(panel);
        panel.Children.Add(new TextBlock { Text = "양식", Height = 18, Foreground = Brush("#5F5F5F") });
        var picker = new ComboBox { MinHeight = 32, Height = 32, DisplayMemberPath = nameof(BrailleForm.Name), ToolTip = "사용자 양식은 오른쪽 클릭으로 수정·삭제할 수 있습니다.", Padding = new(11, 0, 25, 0) };
        AutomationProperties.SetName(picker, "양식");
        var pickerField = new Grid { Height = 32, Margin = new(0, 0, 0, 16) }; pickerField.Children.Add(picker);
        pickerField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var pickerIcon = DesignIcon("form-main-chevron.svg", 16, 16); pickerIcon.HorizontalAlignment = HorizontalAlignment.Right; pickerIcon.Margin = new(0, 0, 5, 0); pickerIcon.IsHitTestVisible = false; pickerField.Children.Add(pickerIcon); panel.Children.Add(pickerField);
        FormPickerDialog.Attach(picker, this, async selected => { picker.SelectedItem = selected; await RunAsync(() => EditAsync(selected.Builtin)); },
            selected => RunAsync(() => DeleteAsync(selected)));
        var values = new Dictionary<string, TextBlock>();
        foreach (var label in new[] { "줄·칸", "한글", "줄바꿈", "페이지행" })
        {
            var row = new DockPanel { Height = 31 };
            row.Children.Add(new TextBlock { Text = label, Width = 94, FontSize = 14 });
            var value = new TextBlock { FontSize = 14, FontWeight = label == "한글" ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis }; values[label] = value; row.Children.Add(value);
            if (label == "페이지행") AutomationProperties.SetAutomationId(value, "FormPageRowSummary");
            panel.Children.Add(new System.Windows.Controls.Border { BorderBrush = (System.Windows.Media.Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        var edit = new MenuItem { Header = "수정…" }; edit.Click += async (_, _) => await RunAsync(() => EditAsync(false));
        var delete = new MenuItem { Header = "삭제…" }; delete.Click += async (_, _) =>
        { if (picker.SelectedItem is BrailleForm f) await RunAsync(() => DeleteAsync(f)); };
        picker.ContextMenu = new ContextMenu(); picker.ContextMenu.Items.Add(edit); picker.ContextMenu.Items.Add(delete);
        picker.SelectionChanged += (_, _) =>
        {
            var f = picker.SelectedItem as BrailleForm;
            edit.IsEnabled = delete.IsEnabled = f is { Builtin: false } && f.FormId != "user:new";
            values["줄·칸"].Text = f is null || f.FormId == "user:new" ? "사용자 양식을 설정해 주세요." : $"{f.Layout.Columns}칸 × {f.Layout.Lines}줄";
            values["한글"].Text = f is null ? "—" : f.Options.KoreanGrade1 ? "정자(1급)" : "약자(2급)";
            values["줄바꿈"].Text = f is null ? "—" : (f.Options.LineBreakUnit == "word" ? "어절" : "음절") + " 단위";
            var summary = f is null ? "—" : $"번호: {Policy(f.Layout.PageNumbers)} · 꼬리말 면: {Policy(f.Layout.Footer)}";
            values["페이지행"].Text = summary;
            values["페이지행"].ToolTip = summary;
            AutomationProperties.SetName(values["페이지행"], "페이지행 " + summary);
        };
        static string Policy(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
        async Task ReloadAsync(string? id)
        {
            var forms = await client.FormsAsync(Lifetime.Token);
            picker.ItemsSource = forms.Any(f => !f.Builtin) ? forms : [.. forms, new BrailleForm("user:new", "사용자 양식", false, null, currentLayout, currentOptions)];
            picker.SelectedItem = picker.Items.Cast<BrailleForm>().FirstOrDefault(f => f.FormId == id); if (picker.SelectedIndex < 0) picker.SelectedIndex = 0;
        }
        async Task DeleteAsync(BrailleForm form)
        {
            if (form.Builtin || form.FormId == "user:new") return;
            if (new DeleteFormDialog(form.Name) { Owner = this }.ShowDialog() != true) return;
            var retainedId = (picker.SelectedItem as BrailleForm)?.FormId;
            await client.DeleteFormAsync(form, Lifetime.Token);
            await ReloadAsync(retainedId == form.FormId ? null : retainedId);
        }
        async Task EditAsync(bool create)
        {
            var selected = picker.SelectedItem as BrailleForm;
            var all = picker.Items.Cast<BrailleForm>().Where(f => f.FormId != "user:new").ToArray();
            if (selected?.FormId == "user:new") selected = null;
            var dialog = new FormEditDialog(client, all, create ? null : selected,
                selected?.Layout ?? currentLayout, selected?.Options ?? currentOptions, selected?.FormId) { Owner = this };
            if (dialog.ShowDialog() == true) await ReloadAsync(dialog.Saved!.FormId);
            else if (dialog.Deleted) await ReloadAsync(null);
        }
        var loading = false;
        Loaded += async (_, _) =>
        {
            if (loading) return; loading = true;
            try { await ReloadAsync(selectedId); } catch (OperationCanceledException) { } catch (Exception ex) { Status.Text = ex.Message; }
            finally { loading = false; }
        };
        var settings = Button("사용자 양식 설정…", () => RunAsync(() => EditAsync(picker.SelectedItem is not BrailleForm { Builtin: false } f || f.FormId == "user:new")));
        PublicDialogDesign.StyleAction(settings); settings.Width = 169; actions.Children.Add(settings);
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; }); PublicDialogDesign.StyleAction(cancel); actions.Children.Add(cancel);
        var confirm = Button("확인", () => { Selected = picker.SelectedItem as BrailleForm; if (Selected is not null && Selected.FormId != "user:new") DialogResult = true; return Task.CompletedTask; }, true);
        PublicDialogDesign.StyleAction(confirm, true); actions.Children.Add(confirm);
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
    public bool Deleted { get; private set; }
    public FormEditDialog(SettingsClient client, BrailleForm[] forms, BrailleForm? current, PageLayout layout, BrailleOptions options, string? baseId) : base("사용자 양식 설정", 560, 568, popover: true)
    {
        var frame = PublicDialogDesign.Frame(this, "사용자 양식 설정", "form-edit-close.svg", "FormEditClose", out var actions); Body(frame);
        var panel = new StackPanel { Margin = new(16, 12, 16, 0) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); frame.Children.Add(scroll);
        void Label(string text, double top = 0) => panel.Children.Add(new TextBlock { Text = text, Height = 18, Margin = new(0, top, 0, 0), Foreground = Brush("#5F5F5F") });
        Label("양식 이름");
        var name = new TextBox { Text = current?.Name ?? "", MaxLength = 100, Height = 32, Padding = new(11, 0, 11, 0) }; AutomationProperties.SetName(name, "양식 이름"); panel.Children.Add(name);
        Label("바탕 양식", 10);
        var basis = new ComboBox { ItemsSource = forms.Where(f => f.FormId != current?.FormId).ToArray(), Height = 32, DisplayMemberPath = nameof(BrailleForm.Name), Padding = new(11, 0, 25, 0) };
        AutomationProperties.SetName(basis, "바탕 양식"); basis.SelectedItem = forms.FirstOrDefault(f => f.FormId == (current?.BaseFormId ?? baseId));
        var basisField = new Grid { Height = 32 }; basisField.Children.Add(basis);
        basisField.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
        var basisIcon = DesignIcon("form-edit-chevron.svg", 16, 16); basisIcon.HorizontalAlignment = HorizontalAlignment.Right; basisIcon.Margin = new(0, 0, 5, 0); basisIcon.IsHitTestVisible = false; basisField.Children.Add(basisIcon); panel.Children.Add(basisField);
        var rows = new StackPanel { Margin = new(0, 12, 0, 0) }; panel.Children.Add(rows);
        var lines = new TextBox(); var columns = new TextBox();
        void FormRow(string label, FrameworkElement control)
        {
            var row = new Grid { Height = 41 }; row.ColumnDefinitions.Add(new() { Width = new(154) }); row.ColumnDefinitions.Add(new());
            row.Children.Add(new TextBlock { Text = label, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            control.VerticalAlignment = VerticalAlignment.Center; control.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(control, 1); row.Children.Add(control);
            rows.Children.Add(new Border { BorderBrush = Brush("#EAEAEA"), BorderThickness = new(0, 0, 0, 1), Child = row });
        }
        void Number(string label, TextBox field, string unit)
        {
            var fields = new StackPanel { Orientation = Orientation.Horizontal };
            var number = new Grid { Width = 96, Height = 32 }; number.ColumnDefinitions.Add(new() { Width = new(72) }); number.ColumnDefinitions.Add(new() { Width = new(24) });
            field.Height = 32; field.Padding = new(11, 0, 2, 0); field.VerticalContentAlignment = VerticalAlignment.Center; AutomationProperties.SetName(field, label); number.Children.Add(field);
            var arrows = new Grid { Background = Brush("#F0F0F0") }; arrows.RowDefinitions.Add(new()); arrows.RowDefinitions.Add(new()); Grid.SetColumn(arrows, 1); number.Children.Add(arrows);
            var up = DesignIcon("form-spinner-up.svg", 9.60063, 5.19934); up.RenderTransformOrigin = new(.5, .5); up.RenderTransform = new ScaleTransform(1, -1);
            foreach (var (delta, icon, row) in new[] { (1, up, 0), (-1, DesignIcon("form-spinner-down.svg", 16, 16), 1) })
            {
                var button = new RepeatButton { Content = icon, BorderThickness = new(0), Background = Brush("#F0F0F0"), Padding = new(0), MinHeight = 0 };
                AutomationProperties.SetName(button, label + (delta > 0 ? " 증가" : " 감소"));
                button.Click += (_, _) => { if (int.TryParse(field.Text, out var value) && value + (long)delta is >= 1 and <= 100) field.Text = (value + delta).ToString(); };
                Grid.SetRow(button, row); arrows.Children.Add(button);
            }
            fields.Children.Add(number); fields.Children.Add(new TextBlock { Text = unit, Margin = new(8, 0, 0, 0), Foreground = Brush("#5F5F5F") }); FormRow(label, fields);
        }
        Number("페이지당 줄 수", lines, "줄"); Number("줄당 칸 수", columns, "칸");
        var korean = new SegmentedChoice("정자(1급)", "약자(2급)") { Width = 260, Height = 32, ItemContainerStyle = ScopeStyle() }; AutomationProperties.SetName(korean, "한글"); FormRow("한글", korean);
        var wrap = new SegmentedChoice("음절", "어절") { Width = 260, Height = 32, ItemContainerStyle = ScopeStyle() }; AutomationProperties.SetName(wrap, "줄바꿈 단위"); FormRow("줄바꿈 단위", wrap);
        FrameworkElement SelectField(ComboBox select, string label)
        {
            select.Width = 260; select.Height = select.MinHeight = 32; select.Padding = new(11, 0, 25, 0); AutomationProperties.SetName(select, label);
            var container = new Grid { Width = 260, Height = 32 }; container.Children.Add(select);
            container.Children.Add(new Border { Width = 24, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
            var icon = DesignIcon("form-edit-chevron.svg", 16, 16); icon.HorizontalAlignment = HorizontalAlignment.Right; icon.Margin = new(0, 0, 5, 0); icon.IsHitTestVisible = false; container.Children.Add(icon); return container;
        }
        var numbers = Choices(["홀수 페이지", "짝수 페이지", "모든 페이지", "출력 안 함"]); FormRow("쪽 번호 넣을 면", SelectField(numbers, "쪽 번호 넣을 면"));
        var footer = Choices(["왼쪽", "가운데", "오른쪽"]); FormRow("꼬리말 정렬", SelectField(footer, "꼬리말 정렬"));
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
        var extras = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 8, 0, 8) }; panel.Children.Add(extras);
        var saveAs = Button("다른 이름으로 저장", () => RunAsync(async () => { var prompt = new FormNameDialog(name.Text) { Owner = this }; if (prompt.ShowDialog() == true) await SaveAsync(null, prompt.Value); }));
        PublicDialogDesign.StyleAction(saveAs); saveAs.Width = 142; saveAs.Margin = new(0, 0, 8, 0); extras.Children.Add(saveAs);
        var applyBasis = Button("바탕 양식 값으로", () => { if (basis.SelectedItem is BrailleForm f) { layout = f.Layout; options = f.Options; SetFields(); } return Task.CompletedTask; });
        PublicDialogDesign.StyleAction(applyBasis); applyBasis.Width = 130; applyBasis.Margin = new(0); extras.Children.Add(applyBasis);
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; }); PublicDialogDesign.StyleAction(cancel); actions.Children.Add(cancel);
        var delete = Button("지우기", () => RunAsync(async () =>
        {
            if (current is null || current.Builtin) return;
            if (new DeleteFormDialog(current.Name) { Owner = this }.ShowDialog() != true) return;
            await client.DeleteFormAsync(current, Lifetime.Token); Deleted = true; DialogResult = false;
        }));
        PublicDialogDesign.StyleAction(delete); delete.Background = delete.BorderBrush = Brush("#C42B1C"); delete.Foreground = Brushes.White;
        delete.IsEnabled = current is { Builtin: false }; delete.ToolTip = delete.IsEnabled ? "사용자 양식 지우기" : "기본 양식은 지울 수 없습니다."; ToolTipService.SetShowOnDisabled(delete, true); actions.Children.Add(delete);
        var save = Button("저장", () => RunAsync(() => SaveAsync(current?.FormId, name.Text)), true); PublicDialogDesign.StyleAction(save, true); actions.Children.Add(save);
    }
}
