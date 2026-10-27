using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Semojum.Core;
using Semojum.Core.Integration;

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
    public OpenDocumentDialog(BrailleForm[] forms, AccountDefaults defaults, bool serverSettingsAvailable = true) : base("열기", 560, 300)
    {
        this.serverSettingsAvailable = serverSettingsAvailable;
        current = defaults.Options; layout = new(PageNumbers: defaults.PageNumberLine == "every" ? "모든 페이지" : "홀수 페이지");
        var body = new StackPanel { Margin = new(16, 12, 16, 0) }; Body(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var file = new DockPanel(); var browse = Button("찾기…", () =>
        {
            var picker = new OpenFileDialog { Multiselect = true, Filter = "지원 파일|*.pdf;*.hwp;*.hwpx;*.txt;*.jpg;*.jpeg;*.png;*.smj|세모점 작업|*.smj|문서|*.pdf;*.hwp;*.hwpx;*.txt|이미지|*.jpg;*.jpeg;*.png" };
            while (picker.ShowDialog(this) == true)
            {
                try { SetPaths(picker.FileNames); break; }
                catch (ArgumentException ex)
                {
                    if (picker.FileNames.All(p => Supported.Contains(Path.GetExtension(p).ToLowerInvariant()))) { Status.Text = ex.Message; break; }
                    var notice = FileNotices.UnsupportedFormat(); notice.Owner = this; notice.ShowDialog();
                    if (notice.Choice != 0) break;
                }
            }
            return Task.CompletedTask;
        });
        FileNames.IsHitTestVisible = false; FileNames.Focusable = false; FileNames.BorderThickness = new(0); FileNames.Padding = new(8, 0, 8, 0);
        browse.Content = FileNames; browse.HorizontalContentAlignment = HorizontalAlignment.Stretch; browse.Padding = new(0); browse.Margin = new(0); browse.ToolTip = "올릴 파일 선택";
        System.Windows.Automation.AutomationProperties.SetName(browse, "올릴 파일 선택");
        void Field(string label, Control field)
        {
            field.Height = 32; field.MinHeight = 32; field.FontSize = 12;
            var row = new DockPanel { Height = 40, LastChildFill = true };
            row.Children.Add(new TextBlock { Text = label, Width = 120, Foreground = (System.Windows.Media.Brush)FindResource("Muted"), VerticalAlignment = VerticalAlignment.Top, Margin = new(0, 4, 0, 0) });
            field.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(field); body.Children.Add(row);
        }
        Field("올릴 파일", browse);
        Forms.ItemsSource = forms; FormPickerDialog.Attach(Forms, this); Field("점역 양식", Forms); Field("한글", korean); Field("점역 규정", rule); Field("한영 혼용 규정", mixed);
        void SetOptions(BrailleOptions options) { current = options; korean.SelectedIndex = options.KoreanGrade1 ? 0 : 1; rule.SelectedIndex = options.EnglishDocument ? 1 : 0; mixed.SelectedIndex = options.MixedRule == "english" ? 1 : 0; }
        Forms.SelectionChanged += (_, _) => { if (Forms.SelectedItem is BrailleForm form) { SetOptions(form.Options); layout = form.Layout; } };
        Forms.SelectedItem = forms.FirstOrDefault(f => f.FormId == defaults.FormId) ?? forms.FirstOrDefault(f => f.FormId == "builtin:MIDHIGH_TEXTBOOK") ?? forms.FirstOrDefault(); SetOptions(defaults.Options);
        start = Action("변환 시작", () =>
        {
            if (Paths.Length == 0) throw new ArgumentException("파일을 선택해 주세요.");
            if (!IsLocal && Paths.Sum(p => new FileInfo(p).Length) > JobClient.MaximumUploadBytes)
            {
                var notice = FileNotices.OversizedFile(); notice.Owner = this; notice.ShowDialog();
                if (notice.Choice == 0) new HelpWindow("파일 열기") { Owner = this }.Show();
                return;
            }
            Settings.Layout.Validate(); DialogResult = true;
        }, true);
        start.Width = 87;
        Action("취소", () => DialogResult = false);
        if (!serverSettingsAvailable) Status.Text = "서버 설정을 불러오지 못했습니다. 저장된 .smj 작업은 열 수 있습니다. 새 변환은 연결을 확인한 뒤 다시 시도해 주세요.";
    }
    private bool IsLocal => Paths.Length == 1 && Path.GetExtension(Paths[0]).Equals(".smj", StringComparison.OrdinalIgnoreCase);
    private static readonly string[] Supported = [".pdf", ".hwp", ".hwpx", ".txt", ".jpg", ".jpeg", ".png", ".smj"];
    internal void SetPaths(string[] paths)
    {
        var images = new[] { ".jpg", ".jpeg", ".png" };
        if (paths.Length == 0 || paths.Any(p => !Supported.Contains(Path.GetExtension(p).ToLowerInvariant()))) throw new ArgumentException("지원하는 문서 또는 이미지 파일을 선택해 주세요.");
        if (paths.Length > 1 && paths.Any(p => !images.Contains(Path.GetExtension(p).ToLowerInvariant()))) throw new ArgumentException("문서는 한 개씩, 이미지는 여러 개를 함께 선택할 수 있습니다.");
        Paths = paths; FileNames.Text = string.Join("; ", paths.Select(Path.GetFileName));
        var local = IsLocal;
        start.Content = local ? "열기" : "변환 시작";
        start.IsEnabled = local || serverSettingsAvailable;
        Forms.IsEnabled = korean.IsEnabled = rule.IsEnabled = mixed.IsEnabled = !local && serverSettingsAvailable;
        start.ToolTip = local ? "작업 파일의 기존 설정을 그대로 엽니다." : "PDF·HWP·HWPX·이미지는 서버로 전송하며 성공한 쪽에 사용량이 차감됩니다. TXT는 로컬 점역합니다.";
        Status.Text = !serverSettingsAvailable && !local ? "서버 설정을 불러오지 못했습니다. 새 변환은 연결을 확인한 뒤 다시 시도해 주세요." : "";
    }
}
