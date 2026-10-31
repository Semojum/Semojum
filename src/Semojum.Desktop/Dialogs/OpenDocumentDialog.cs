using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;
using static Semojum.Desktop.Dialogs.FigmaDialogElements;

namespace Semojum.Desktop.Dialogs;

public sealed class OpenDocumentDialog : CompactDialog
{
    internal ComboBox Forms { get; } = new();
    internal TextBox FileNames { get; } = new() { IsReadOnly = true, Text = "PDF · HWP · HWPX · TXT · JPG · PNG · SMJ" };
    private readonly ComboBox korean = Choices(["정자(1급)", "약자(2급)"], "약자(2급)"), rule = Choices(["한글 규정", "영어 규정"], "한글 규정"), mixed = Choices(["한국어", "영어"], "한국어");
    private readonly Button start;
    private BrailleOptions current;
    private PageLayout layout;
    private readonly bool serverSettingsAvailable;
    public string[] Paths { get; private set; } = [];
    public string? FormId => (Forms.SelectedItem as BrailleForm)?.FormId;
    public DocumentSession Settings => new() { Layout = layout, Options = current with { KoreanGrade1 = korean.SelectedIndex == 0, EnglishDocument = rule.SelectedIndex == 1, MixedRule = mixed.SelectedIndex == 1 ? "english" : "korean" } };
    public OpenDocumentDialog(BrailleForm[] forms, AccountDefaults defaults, bool serverSettingsAvailable = true) : base("점역 옵션", 560, 392, popover: true)
    {
        this.serverSettingsAvailable = serverSettingsAvailable;
        current = defaults.Options; layout = new(PageNumbers: defaults.PageNumberLine == "every" ? "모든 페이지" : "홀수 페이지");
        if (Content is Border frame) { frame.CornerRadius = new(8); frame.BorderBrush = Brush("#EAEAEA"); frame.Background = Brush("#F3F3F3"); }
        Root.Children.Remove(Status);
        var layoutRoot = new Grid(); layoutRoot.RowDefinitions.Add(new() { Height = new(55) });
        layoutRoot.RowDefinitions.Add(new() { Height = new(272) }); layoutRoot.RowDefinitions.Add(new() { Height = new(64) }); Body(layoutRoot);
        var header = new Grid { Margin = new(24, 0, 24, 0) };
        header.Children.Add(new TextBlock { Text = "점역 옵션", FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
        var close = new System.Windows.Controls.Button { Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new(0), Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right, Content = DesignIcon("open-options-close.svg", 16, 16), Template = SurfaceTemplate(0) };
        AutomationProperties.SetName(close, "닫기"); close.Click += (_, _) => Close(); header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (!close.IsMouseOver && e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
        layoutRoot.Children.Add(header);
        var body = new StackPanel { Margin = new(24, 16, 24, 0) }; Grid.SetRow(body, 1); layoutRoot.Children.Add(body);
        body.Children.Add(new TextBlock { Text = "파일", FontSize = 12, Foreground = Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
        var browse = Button("찾기…", () =>
        {
            var paths = SelectPaths(this);
            if (paths is not null) SetPaths(paths);
            return Task.CompletedTask;
        });
        FileNames.IsHitTestVisible = false; FileNames.Focusable = false; FileNames.BorderThickness = new(0); FileNames.Padding = new(11, 0, 11, 0);
        FileNames.VerticalContentAlignment = VerticalAlignment.Center; FileNames.Background = Brushes.Transparent;
        browse.Content = FileNames; browse.HorizontalContentAlignment = HorizontalAlignment.Stretch; browse.Padding = new(0); browse.Margin = new(0, 0, 0, 16);
        browse.Height = browse.MinHeight = 32; browse.Background = Brush("#FBFBFB"); browse.BorderBrush = Brush("#EAEAEA"); browse.Template = SurfaceTemplate(4, stretchContent: true); browse.ToolTip = "올릴 파일 선택";
        AutomationProperties.SetName(browse, "올릴 파일 선택"); body.Children.Add(browse);
        body.Children.Add(new TextBlock { Text = "점역 설정", FontSize = 12, Foreground = Brush("#5F5F5F"), Margin = new(0, 0, 0, 4) });
        var form = new Grid { Margin = new(16, 12, 16, 4) }; form.ColumnDefinitions.Add(new() { Width = new(134) }); form.ColumnDefinitions.Add(new());
        for (var i = 0; i < 4; i++) form.RowDefinitions.Add(new() { Height = new(36) });
        body.Children.Add(new Border { Height = 160, CornerRadius = new(6), Background = Brush("#F9F9F9"), Child = form });
        void Field(int row, string label, ComboBox field)
        {
            var labelText = new TextBlock { Text = label, FontSize = 14, Foreground = Brush("#1A1A1A"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(labelText, row); form.Children.Add(labelText);
            var slot = new Grid { Width = 260, Height = 32, HorizontalAlignment = HorizontalAlignment.Left };
            field.Height = field.MinHeight = 32; field.FontSize = 12; field.Padding = new(11, 0, 27, 0);
            field.VerticalContentAlignment = VerticalAlignment.Center; field.Background = Brush("#FBFBFB"); field.BorderBrush = Brush("#EAEAEA");
            AutomationProperties.SetName(field, label); slot.Children.Add(field);
            slot.Children.Add(new Border { Width = 22, Margin = new(0, 1, 1, 1), Background = Brush("#FBFBFB"), HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = false });
            var icon = DesignIcon("open-options-chevron.svg", 16, 16); icon.HorizontalAlignment = HorizontalAlignment.Right; icon.Margin = new(0, 0, 5, 0); icon.IsHitTestVisible = false; slot.Children.Add(icon);
            Grid.SetRow(slot, row); Grid.SetColumn(slot, 1); form.Children.Add(slot);
        }
        Forms.ItemsSource = forms; FormPickerDialog.Attach(Forms, this); Field(0, "점역 양식", Forms); Field(1, "한글", korean); Field(2, "점역 규정", rule); Field(3, "한영 혼용 규정", mixed);
        void SetOptions(BrailleOptions options) { current = options; korean.SelectedIndex = options.KoreanGrade1 ? 0 : 1; rule.SelectedIndex = options.EnglishDocument ? 1 : 0; mixed.SelectedIndex = options.MixedRule == "english" ? 1 : 0; }
        Forms.SelectionChanged += (_, _) => { if (Forms.SelectedItem is BrailleForm form) { SetOptions(form.Options); layout = form.Layout; } };
        Forms.SelectedItem = forms.FirstOrDefault(f => f.FormId == defaults.FormId) ?? forms.FirstOrDefault(f => f.FormId == "builtin:GENERAL_BOOK") ?? forms.FirstOrDefault(); SetOptions(defaults.Options);
        var footer = new Grid { Margin = new(24, 16, 24, 16) }; Grid.SetRow(footer, 2); layoutRoot.Children.Add(footer);
        Status.Margin = new(0); Status.MaxWidth = 285; Status.HorizontalAlignment = HorizontalAlignment.Left; Status.ToolTip = Status.Text; footer.Children.Add(Status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(actions);
        var cancel = Button("취소", () => { DialogResult = false; return Task.CompletedTask; });
        start = Button("변환 시작", () =>
        {
            try
            {
                if (Paths.Length == 0) throw new ArgumentException("파일을 선택해 주세요.");
                if (!IsLocal && Paths.Sum(p => new FileInfo(p).Length) > JobClient.MaximumUploadBytes)
                {
                    var notice = FileNotices.OversizedFile(); notice.Owner = this; notice.ShowDialog();
                    if (notice.Choice == 0) new HelpWindow("파일 열기") { Owner = this }.Show();
                    return Task.CompletedTask;
                }
                Settings.Layout.Validate(); DialogResult = true;
            }
            catch (Exception ex) { Status.Text = ex.Message; }
            return Task.CompletedTask;
        }, true);
        foreach (var button in new[] { cancel, start }) { button.Width = button == start ? 96 : 88; button.MinWidth = 0; button.Height = button.MinHeight = 32; button.Template = SurfaceTemplate(4); actions.Children.Add(button); }
        cancel.BorderBrush = Brush("#EAEAEA");
        if (!serverSettingsAvailable) Status.Text = "서버 설정을 불러오지 못했습니다. 저장된 .smj 작업은 열 수 있습니다. 새 변환은 연결을 확인한 뒤 다시 시도해 주세요.";
    }
    private bool IsLocal => Paths.Length == 1 && Path.GetExtension(Paths[0]).Equals(".smj", StringComparison.OrdinalIgnoreCase);
    private static readonly string[] Supported = [".pdf", ".hwp", ".hwpx", ".txt", ".jpg", ".jpeg", ".png", ".smj"];
    internal static string[]? SelectPaths(Window owner)
    {
        var picker = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.pdf;*.hwp;*.hwpx;*.txt;*.jpg;*.jpeg;*.png;*.smj|세모점 작업|*.smj|문서|*.pdf;*.hwp;*.hwpx;*.txt|이미지|*.jpg;*.jpeg;*.png" };
        while (picker.ShowDialog(owner) == true)
        {
            try { ValidatePaths(picker.FileNames); return picker.FileNames; }
            catch (ArgumentException ex)
            {
                if (picker.FileNames.All(p => Supported.Contains(Path.GetExtension(p).ToLowerInvariant())))
                { MessageBox.Show(owner, ex.Message, "파일 선택", MessageBoxButton.OK, MessageBoxImage.Information); continue; }
                var notice = FileNotices.UnsupportedFormat(); notice.Owner = owner; notice.ShowDialog();
                if (notice.Choice != 0) return null;
            }
        }
        return null;
    }

    private static void ValidatePaths(string[] paths)
    {
        var images = new[] { ".jpg", ".jpeg", ".png" };
        if (paths.Length == 0 || paths.Any(p => !Supported.Contains(Path.GetExtension(p).ToLowerInvariant()))) throw new ArgumentException("지원하는 문서 또는 이미지 파일을 선택해 주세요.");
        if (paths.Length > 1 && paths.Any(p => !images.Contains(Path.GetExtension(p).ToLowerInvariant()))) throw new ArgumentException("문서는 한 개씩, 이미지는 여러 개를 함께 선택할 수 있습니다.");
        if (paths.Length > 1) UploadFileReader.ValidateUniqueFileNames(paths);
    }
    internal void SetPaths(string[] paths)
    {
        ValidatePaths(paths);
        Paths = paths; FileNames.Text = string.Join("; ", paths.Select(Path.GetFileName));
        var local = IsLocal;
        start.Content = local ? "열기" : "변환 시작";
        start.IsEnabled = local || serverSettingsAvailable;
        Forms.IsEnabled = korean.IsEnabled = rule.IsEnabled = mixed.IsEnabled = !local && serverSettingsAvailable;
        start.ToolTip = local ? "작업 파일의 기존 설정을 그대로 엽니다." : "PDF·HWP·HWPX·이미지는 서버로 전송하며 성공한 쪽에 사용량이 차감됩니다. TXT는 로컬 점역합니다.";
        Status.Text = !serverSettingsAvailable && !local ? "서버 설정을 불러오지 못했습니다. 새 변환은 연결을 확인한 뒤 다시 시도해 주세요." : "";
    }
}
