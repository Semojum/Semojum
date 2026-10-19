using System.Windows;
using System.Windows.Controls;
using Semojum.Core;

namespace Semojum.Desktop.Dialogs;
public sealed class OutputSettingsDialog : EditorDialog
{
    private readonly ComboBox paper, wrap, numbers;
    private readonly TextBox columns, lines, first;
    private readonly CheckBox duplex = new() { Content = "양면용 마지막 빈 면 맞춤", IsChecked = true };
    private readonly CheckBox remember = new() { Content = "이 계정의 출력 기본값으로 저장" };
    public OutputPreferences Result { get; private set; }
    public bool Remember => remember.IsChecked == true;
    public OutputSettingsDialog(PageLayout editing, OutputPreferences? saved) : base("문서 최적화 · 출력 설정", 620)
    {
        Result = saved ?? new(); var layout = Result.Layout ?? new();
        Note($"편집 판면: {editing.Columns}칸 × {editing.Lines}줄. 출력 파일에만 아래 설정을 적용합니다.");
        paper = new() { ItemsSource = new[] { "10×11인치 (32칸·26줄)", "사용자 지정" }, SelectedIndex = layout.Columns == 32 && layout.Lines == 26 ? 0 : 1 };
        columns = new() { Text = layout.Columns.ToString() }; lines = new() { Text = layout.Lines.ToString() }; first = new() { Text = layout.StartPage.ToString() };
        numbers = new() { ItemsSource = new[] { "홀수 페이지", "모든 페이지", "출력 안 함" }, SelectedItem = layout.PageNumbers };
        wrap = new() { ItemsSource = new[] { "글자(음절)별", "단어(어절)별" }, SelectedIndex = Result.LineBreakUnit == "word" ? 1 : 0 };
        duplex.IsChecked = Result.Duplex;
        paper.SelectionChanged += (_, _) => { if (paper.SelectedIndex == 0) { columns.Text = "32"; lines.Text = "26"; } };
        Row("용지", paper); Row("줄당 칸 수", columns); Row("면당 줄 수", lines); Row("시작 페이지", first); Row("페이지 번호 출력", numbers); Row("줄바꿈 방식", wrap);
        Fields.Children.Add(duplex); Fields.Children.Add(remember);
        Note("양면 인쇄의 용지 이송은 점자 프린터에서 설정합니다. 여기서는 필요한 마지막 빈 면을 조판합니다. 중간 형태 줄바꿈은 엔진에 정의되어 있지 않습니다."); Fields.Children.Add(Error);
    }
    protected override bool Apply()
    {
        if (!int.TryParse(columns.Text, out var c) || !int.TryParse(lines.Text, out var l) || !int.TryParse(first.Text, out var s)) throw new ArgumentException("판면 값은 정수로 입력해 주세요.");
        var layout = (Result.Layout ?? new()) with { Columns = c, Lines = l, StartPage = s, PageNumbers = numbers.SelectedItem?.ToString() ?? "홀수 페이지", PadToEven = duplex.IsChecked == true };
        layout.Validate(); Result = new(layout, wrap.SelectedIndex == 1 ? "word" : "syllable", duplex.IsChecked == true); return true;
    }
}
