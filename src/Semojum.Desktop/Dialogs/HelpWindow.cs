using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Semojum.Desktop.Dialogs;

public sealed class HelpWindow : Window
{
    private sealed record Topic(string Group, string Title, string Body);
    private static readonly Topic[] Topics =
    [
        new("시작하기", "로그인과 새 문서", "로그인 후 Ctrl+N으로 새 문서를 만듭니다. 새 문서와 TXT 열기는 AI와 크레딧을 사용하지 않습니다. 여러 문서는 위쪽 탭으로 전환하고 Ctrl+W로 닫습니다."),
        new("문서 변환", "파일 업로드", "PDF·HWP·HWPX·이미지를 업로드할 수 있습니다. 전체 크기는 100 MB까지입니다. 여러 이미지는 순서를 확인하고 올리세요. 점역 양식과 옵션을 고른 후 변환을 시작합니다. 실패한 쪽은 쪽별 변환 상태에서 확인합니다."),
        new("결과 편집", "텍스트 고치기", "원문을 고치면 입력이 멈춘 뒤 로컬 엔진이 점역합니다. Enter는 같은 블록 안의 줄바꿈입니다. Ctrl+Enter는 쪽바꿈을 넣습니다. 다른 쪽은 이동 창에서 선택합니다. 경계에서 Backspace·Delete·Insert를 누르면 인접 블록이 합쳐집니다. Ctrl+Z·Ctrl+Y는 쪽별 최대 100단계 기록을 사용합니다."),
        new("결과 편집", "점자 직접 고치기", "점자 패널에서 6점 입력을 켜고 설정한 키를 함께 누른 뒤 모두 놓으면 한 칸이 입력됩니다. 키는 1·2·3·4·5·6점 순서로 설정합니다. 직접 고친 점자는 별도 색으로 표시합니다. 다시 점역할 때 유지·덮어쓰기·취소를 고를 수 있습니다. 텍스트는 점자 직접 수정으로 바뀌지 않습니다."),
        new("메뉴별 기능", "여러 블록 선택", "Ctrl+A 한 번은 현재 블록, 두 번은 현재 쪽의 블록을 선택합니다. 도움말의 추가 도구에서 현재 쪽 블록 선택과 여러 줄·블록 구간 선택을 엽니다. 드래그나 Shift+클릭으로 블록 사이의 구간을 선택하고, 복사·잘라내기·붙여넣기·삭제합니다. Ctrl+Z로 되돌릴 수 있습니다."),
        new("메뉴별 기능", "대체 초안과 규정", "대체 초안 창에서 전달된 설명을 고르고 묵자와 점자를 확인한 뒤 Enter나 닫기를 누르면 선택을 적용합니다. Esc는 취소합니다. 모든 초안과 최초 원문은 작업 파일에 남습니다. 점자 규정 창은 적용 규정과 서버 검색을 제공합니다. 검색 목록에는 요약을 표시하며 펼치면 서버가 제공한 조항 전문과 예시를 볼 수 있습니다. 전문이 없는 항목은 발췌로 표시하고, 검색 전용 조항에는 점역 미지원을 표시합니다."),
        new("결과 편집", "3패널 보기", "보기 메뉴에서 원본·텍스트·점자, 줄 번호, 줄번역을 조정합니다. 패널의 분리 단추는 독립 창을 만듭니다. 분리 창을 닫으면 본창으로 돌아오며 같은 문서와 되돌리기 기록을 공유합니다. 패널 모두 제자리로를 누르면 세 패널을 복원합니다."),
        new("메뉴별 기능", "추가 도구", "주 메뉴와 도구 모음에 표시하지 않는 기능입니다. 아래 항목을 선택하면 현재 문서에서 실행합니다. 6점 입력은 F6으로 켜고 끕니다."),
        new("메뉴별 기능", "저장과 내보내기", "자동 저장은 하지 않습니다. Ctrl+S는 .smj 작업 파일과 연결된 서버 작업을 저장합니다. 서버 저장이 실패해도 로컬 파일은 남습니다. .smj에는 원본·텍스트·점자·메모·옵션이 들어 있습니다. 내보내기에서 전체·원본 쪽·점자 면 범위를 고릅니다. 점자 TXT는 선택한 면의 점자를 판면과 번호까지 그대로 담습니다."),
        new("메뉴별 기능", "판면과 꼬리말", "판면에서 칸 수·줄 수·시작 번호·변경선을 정합니다. 페이지 번호는 전체·홀수·짝수·출력 안 함과 지정 범위를 함께 설정합니다. 삽입한 머리말은 현재 면 맨 위부터 이후 면에 적용합니다. 빈 머리말은 해제합니다. 수동 점자 때문에 위치를 연결할 수 없다는 안내가 나오면 해당 블록을 재점역하거나 블록 시작에 삽입하세요. 꼬리말 관리에서 기본 내용과 인쇄 면 번호의 시작·끝 범위, 범위별 정렬을 정합니다. 각 항목은 다음 시작 면 직전까지 이어집니다. 지우고 이어가기는 앞 꼬리말을 그 구간까지 연장하며, 빈 내용은 꼬리말을 숨깁니다. 꼬리말은 페이지 번호가 없는 면에도 설정대로 출력합니다. 범위는 서버에서 복원되지 않으므로 .smj를 보관하세요. 모눈과 점자 면 미리보기는 같은 조판 결과를 사용합니다."),
        new("메뉴별 기능", "모눈과 연속 편집", "모눈 본문을 직접 클릭해 점자를 입력하고 드래그나 Shift+클릭으로 같은 원본 쪽의 여러 블록을 선택합니다. 머리말·페이지 번호·변경선은 생성 영역이므로 설정에서 바꿉니다. 왼쪽 ⋮ 손잡이를 다른 블록 앞에 놓으면 순서를 바꿉니다. Enter는 줄바꿈, Ctrl+Enter는 쪽바꿈, Ctrl+A 한 번은 블록·두 번은 쪽 선택입니다. 원문은 기본 화면에서 같은 원본 쪽의 블록을 이어서 보여 줍니다. 각 쪽의 실행 취소 이력을 메인·분리 창이 공유하며 표시상 줄바꿈은 저장 문자열에 추가되지 않습니다."),
        new("메뉴별 기능", "설정과 6점키", "옵션 → 기본 점역 옵션에서 계정 기본값을 바꾸고, 양식에서 설정을 저장·선택·삭제합니다. 문서의 현재 옵션과 계정 기본값은 별개입니다. 6점키 설정은 1·2·3·4·5·6점 순서로 서로 다른 영문 키를 입력합니다. "),
        new("마이페이지", "사용량과 작업 이력", "정보 → 마이페이지에서 내 사용량, 기관 잔여 쪽수, 일별 사용량과 작업 이력을 확인합니다. 작업 이력에서 선택한 작업을 다시 열 수 있습니다. 새 문서와 TXT는 차감되지 않습니다."),
        new("문제 해결", "저장·점역 실패", "점역 오류가 나면 원문을 보존한 상태로 문제 블록을 확인하세요. 엔진 응답 지연 시 점자 입력만 잠기며 원문 편집과 로컬 저장은 가능합니다. 서버 저장 미완료 표시는 완료로 처리하지 않습니다. 새 블록과 첫 점자도 서버에 저장합니다. 부분 실패 시 실패한 쪽만 다시 저장할 수 있습니다. 받은 작업 파일을 저장하면 내 계정의 사본으로 이어집니다. .smj 파일을 보관하세요."),
        new("문제 해결", "문의하기", "정보 → 게시판에서 질문을 작성합니다. 비밀글은 작성자와 운영자만 열 수 있습니다. 첨부는 5개, 각각 10 MB까지이며 현재 작업 정보는 체크한 경우에만 전송합니다. 문의 이메일: contact@semo-jum.com"),
        new("단축키", "단축키 목록", "Ctrl+N 새 문서 / Ctrl+O 열기 / Ctrl+S 저장 / Ctrl+W 문서 닫기\nCtrl+F 찾기 / F3 다음 찾기 / Ctrl+H 바꾸기 / Ctrl+G 이동\nCtrl+Z 되돌리기 / Ctrl+Y 다시 실행 / Ctrl+Enter 쪽바꿈\nAlt+G 기본 점역 옵션 / Alt+K 6점키 설정 / Alt+Y 편집 판면 / Alt+P 출력 설정 / Alt+S 문단 모양 / F4 기호\nCtrl+0–9 텍스트 상용구 / Alt+0–9 점자 상용구 / F1 도움말")
    ];
    public HelpWindow(string context, Func<string, Task>? runCommand = null)
    {
        Title = "세모점 도움말 - 세모점"; Width = 900; Height = 320; MinWidth = 600; MinHeight = 320; Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel(); Content = root;
        Services.WindowFrame.Apply(this, editor: true);
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8, 3, 0, 4) };
        var toolbarFrame = new Border { Height = 32, Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)), BorderBrush = (Brush)FindResource("Line"), BorderThickness = new(0, 0, 0, 1), Child = toolbar };
        DockPanel.SetDock(toolbarFrame, Dock.Top); root.Children.Add(toolbarFrame);
        var tabs = new TabControl { Width = 260, Style = (Style)FindResource("HelpTabs") }; DockPanel.SetDock(tabs, Dock.Left); root.Children.Add(tabs);
        var reader = new FlowDocumentScrollViewer { IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(reader);
        var history = new List<Topic>(); var index = -1;
        void Render(Topic topic)
        {
            var doc = new FlowDocument { FontFamily = (FontFamily)FindResource("InterfaceFont"), FontSize = 14, TextAlignment = TextAlignment.Left, PagePadding = new(24, 22, 24, 20) };
            doc.Blocks.Add(new Paragraph(new Run(topic.Title)) { FontSize = 20, FontWeight = FontWeights.SemiBold, LineHeight = 28, Margin = new(0, 0, 0, 11) });
            doc.Blocks.Add(new Paragraph(new Run(topic.Body)) { LineHeight = 22, Margin = new(0) });
            if (topic.Title == "추가 도구")
            {
                var command = runCommand ?? (Owner is MainWindow main ? main.ExecuteAsync : null);
                foreach (var (label, action) in new[] {
                    ("6점 입력 켜기 / 끄기 (F6)", "sixDotMode"), ("특수 기호 (F4)", "symbol"), ("현재 쪽 블록 선택", "selectBlocks"), ("여러 줄·블록 구간 선택", "selectRange"),
                    ("쪽별 변환 상태", "pageStatus"), ("메모 목록", "memos"), ("현재 줄 메모", "memo"), ("대체 초안", "drafts"), ("표 구조 편집", "table"), ("점자 규정", "rules"),
                    ("묵자쪽번호 삽입", "tag:묵자쪽번호"), ("점자면번호 삽입", "tag:점자면번호"), ("문서 닫기 (Ctrl+W)", "closeDocument"), ("알림", "notifications"),
                    ("로그인", "login"), ("로그아웃", "logout"), ("버전 확인", "version"), ("작업 폴더 선택", "workFolder"), ("마지막 작업 폴더", "lastFolder"),
                    ("서버 작업 열기", "serverOpen"), ("출력 설정 (Alt+P)", "optimize"), ("점자 면 미리보기·인쇄", "composition"), ("문단 모양 (Alt+S)", "appearance"),
                    ("편집창 색상 (Alt+C)", "colors"), ("상용구", "snippets"), ("작업 점역 옵션", "translationOptions"), ("문서 전체 재점역", "translateAll") })
                {
                    var link = new Hyperlink(new Run(label)) { IsEnabled = command is not null };
                    link.Click += async (_, _) => { if (command is not null) await command(action); };
                    doc.Blocks.Add(new Paragraph(link) { Margin = new(0, 8, 0, 0) });
                }
            }
            reader.Document = doc;
        }
        void Visit(Topic topic) { if (index >= 0 && history[index] == topic) return; history.RemoveRange(index + 1, history.Count - index - 1); history.Add(topic); index++; Render(topic); }
        toolbar.Children.Add(CompactDialog.Button("◀", () => { if (index > 0) Render(history[--index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("▶", () => { if (index + 1 < history.Count) Render(history[++index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("인쇄", () => { var print = new PrintDialog(); if (print.ShowDialog() == true) print.PrintDocument(((IDocumentPaginatorSource)reader.Document).DocumentPaginator, "세모점 도움말"); return Task.CompletedTask; }));
        foreach (Button button in toolbar.Children) { button.MinWidth = button.Width = 24; button.Height = 24; button.Margin = new(0); button.Padding = new(0); button.Style = (Style)FindResource("ToolButton"); }
        var printButton = (Button)toolbar.Children[2]; printButton.Content = new ContentControl { Content = FindResource("Icon.print"), IsHitTestVisible = false }; printButton.ToolTip = "인쇄"; printButton.Margin = new(10, 0, 0, 0);
        foreach (var mode in new[] { "내용", "색인", "검색" })
        {
            if (mode == "내용")
            {
                var tree = new TreeView { BorderThickness = new(0), Padding = new(0), Margin = new(0, 2, 0, 0), Background = Brushes.Transparent, FontSize = 12, FontWeight = FontWeights.Normal };
                ScrollViewer.SetVerticalScrollBarVisibility(tree, ScrollBarVisibility.Hidden);
                tree.Resources[typeof(TreeViewItem)] = FindResource("HelpTreeItem");
                foreach (var group in Topics.GroupBy(t => t.Group))
                {
                    var branch = new TreeViewItem { Header = group.Key, IsExpanded = group.Key == "결과 편집", IsSelected = group.Key == "결과 편집" };
                    var items = group.Key == "결과 편집" ? group.OrderBy(t => t.Title == "3패널 보기" ? 0 : t.Title == "텍스트 고치기" ? 1 : 2) : group.AsEnumerable();
                    branch.PreviewMouseLeftButtonDown += (_, e) =>
                    {
                        if (e.GetPosition(branch).Y >= 24) return;
                        branch.IsExpanded = !branch.IsExpanded; branch.IsSelected = true; branch.Focus(); e.Handled = true;
                    };
                    foreach (var topic in items) branch.Items.Add(new TreeViewItem { Header = topic.Title, Tag = topic });
                    tree.Items.Add(branch);
                }
                tree.SelectedItemChanged += (_, _) => { if (tree.SelectedItem is TreeViewItem { Tag: Topic topic }) Visit(topic); };
                tabs.Items.Add(new TabItem { Header = "내용", Content = tree, Tag = "separator" }); continue;
            }
            var panel = new DockPanel(); var list = new ListBox { DisplayMemberPath = "Title" };
            if (mode == "검색")
            {
                var input = new TextBox { Margin = new(8), MinHeight = 28 }; DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input);
                input.TextChanged += (_, _) => list.ItemsSource = Topics.Where(t => (t.Title + t.Body).Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            }
            list.ItemsSource = mode == "색인" ? Topics.OrderBy(t => t.Title).ToArray() : Topics;
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is Topic topic) Visit(topic); };
            panel.Children.Add(list); tabs.Items.Add(new TabItem { Header = mode, Content = panel, Tag = mode == "색인" ? "separator" : null });
        }
        var matched = Topics.FirstOrDefault(t => t.Title.Contains(context));
        if (matched is null)
        {
            var title = context switch
            {
                var s when s.Contains("꼬리말") || s.Contains("판면") || s.Contains("페이지설정") || s.Contains("출력") || s.Contains("조판") => "판면과 꼬리말",
                var s when s.Contains("선택") || s.Contains("구간") => "여러 블록 선택",
                var s when s.Contains("저장") || s.Contains("내보내기") => "저장과 내보내기",
                var s when s.Contains("점역") || s.Contains("설정") || s.Contains("양식") || s.Contains("모양") => "설정과 6점키",
                var s when s.Contains("메모") || s.Contains("규정") || s.Contains("초안") => "대체 초안과 규정",
                var s when s.Contains("게시판") || s.Contains("문의") => "문의하기",
                _ => "로그인과 새 문서"
            };
            matched = Topics.First(t => t.Title == title);
        }
        Visit(matched);
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
    }
}
