using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;

public sealed class AppearanceDialog : CompactDialog
{
    public const string DefaultCurrentLineColor = "#F2F6FB";
    public const string DefaultLineNumberColor = "#8A8A8A";
    public EditorAppearance Result { get; private set; }
    private readonly Dictionary<string, TextBox> inputs = [];
    private readonly Dictionary<string, CheckBox> flags = [];
    public AppearanceDialog(EditorAppearance current, Action<EditorAppearance> preview, bool colors = false) : base("편집 화면 설정", 620, 650)
    {
        Result = current;
        var tabs = new TabControl { Margin = new(16) };
        StackPanel Page(string title) { var p = new StackPanel { Margin = new(12) }; tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return p; }
        void Text(Panel p, string key, string label, object value) { var box = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture), MinHeight = 24 }; inputs[key] = box; Row(p, label, box, 190); }
        void Flag(Panel p, string key, string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new(0, 6, 0, 6) }; flags[key] = box; p.Children.Add(box); }
        var shape = Page("문단 모양");
        Text(shape, "FontFamily", "글꼴", current.FontFamily); Text(shape, "TextSize", "텍스트 크기 (8–72)", current.TextSize);
        Text(shape, "BrailleSize", "점자 크기 (8–72)", current.BrailleSize); Text(shape, "LineSpacing", "줄 간격 (1–3)", current.LineSpacing);
        Text(shape, "CursorWidth", "커서 폭 (1–8)", current.CursorWidth); Text(shape, "OutlineWidth", "판면 윤곽선 두께 (0–8)", current.OutlineWidth);
        Text(shape, "RecentCount", "최근 파일 개수 (0–50)", current.RecentCount);
        Flag(shape, "Bold", "진하게", current.Bold); Flag(shape, "ShowLineNumbers", "줄 번호 보기", current.ShowLineNumbers);
        Flag(shape, "ShowFormattingMarks", "공백·탭·줄바꿈 부호 보기", current.ShowFormattingMarks); Flag(shape, "ShowToolbar", "도구 모음 보기", current.ShowToolbar);
        var palette = Page("편집창 색상");
        foreach (var (key, label, color) in new[] { ("TextColor", "글자", current.TextColor), ("BackgroundColor", "배경", current.BackgroundColor), ("SelectionTextColor", "선택 글자", current.SelectionTextColor), ("SelectionColor", "선택 배경", current.SelectionColor), ("BrailleColor", "점형 글자", current.BrailleColor), ("CurrentLineColor", "현재 줄 강조", current.CurrentLineColor), ("MarkerColor", "조판부호", current.MarkerColor), ("LineNumberColor", "줄 번호 글자", current.LineNumberColor), ("LineNumberBackground", "줄 번호 배경", current.LineNumberBackground), ("TagColor", "점역 코드", current.TagColor), ("OutlineColor", "판면 윤곽선", current.OutlineColor) }) Text(palette, key, label + " (#RRGGBB)", color);
        Flag(palette, "HighContrast", "고대비 테마 사용", current.HighContrast);
        palette.Children.Add(new TextBlock { Text = "점자 직접 수정 색 #B22222는 별도로 유지합니다.", TextWrapping = TextWrapping.Wrap });
        var speech = Page("점형 읽기"); Flag(speech, "SpeakDots", "커서 위치의 점형 번호를 음성으로 읽기", current.SpeakDots); Text(speech, "SpeechRate", "음성 속도 (-10–10)", current.SpeechRate);
        speech.Children.Add(new TextBlock { Text = "Windows에 설치된 음성을 사용합니다. 예: 1-2-5점. 음성이 없으면 Windows 음성 설정에서 설치해 주세요.", TextWrapping = TextWrapping.Wrap });
        Action("기본값", () => { Result = new(CurrentLineColor: DefaultCurrentLineColor, LineNumberColor: DefaultLineNumberColor); preview(Result); DialogResult = true; });
        Action("적용", () => { Result = Read(); preview(Result); });
        Action("확인", () => { Result = Read(); preview(Result); DialogResult = true; }, true);
        Action("취소", () => { preview(current); DialogResult = false; });
        Closing += (_, _) => { if (DialogResult != true) preview(current); };
        tabs.SelectedIndex = colors ? 1 : 0; Body(tabs);
    }
    private EditorAppearance Read()
    {
        string S(string key) => inputs[key].Text.Trim();
        double N(string key) => double.Parse(S(key), CultureInfo.InvariantCulture);
        int I(string key) => int.Parse(S(key), CultureInfo.InvariantCulture);
        bool B(string key) => flags[key].IsChecked == true;
        var result = new EditorAppearance(S("TextColor"), S("BackgroundColor"), S("CurrentLineColor"), S("SelectionColor"), S("FontFamily"), N("TextSize"), N("BrailleSize"), N("LineSpacing"), N("CursorWidth"), B("HighContrast"), B("SpeakDots"), I("SpeechRate"), S("SelectionTextColor"), S("BrailleColor"), S("MarkerColor"), S("LineNumberColor"), S("LineNumberBackground"), S("TagColor"), S("OutlineColor"), N("OutlineWidth"), B("Bold"), B("ShowLineNumbers"), B("ShowFormattingMarks"), I("RecentCount"), B("ShowToolbar"));
        AccountPreferenceStore.Validate(new(Appearance: result)); return result;
    }
}
