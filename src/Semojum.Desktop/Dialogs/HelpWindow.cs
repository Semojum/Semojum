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
        new("결과 편집", "텍스트 고치기", "원문을 고치면 입력이 멈춘 뒤 로컬 엔진이 점역합니다. Enter는 같은 블록 안의 줄바꿈입니다. Ctrl+Enter는 쪽바꿈을 넣습니다. 새 블록은 편집 메뉴에서 추가합니다. 경계에서 Backspace·Delete·Insert를 누르면 인접 블록이 합쳐집니다. Ctrl+Z·Ctrl+Y는 쪽별 최대 100단계 기록을 사용합니다."),
        new("결과 편집", "점자 직접 고치기", "점자 패널에서 6점 입력을 켜고 설정한 키를 함께 누른 뒤 모두 놓으면 한 칸이 입력됩니다. 키는 1·2·3·4·5·6점 순서로 설정합니다. 직접 고친 점자는 별도 색으로 표시합니다. 다시 점역할 때 유지·덮어쓰기·취소를 고를 수 있습니다. 텍스트는 점자 직접 수정으로 바뀌지 않습니다."),
        new("결과 편집", "여러 블록 선택", "Ctrl+A 한 번은 현재 블록, 두 번은 현재 쪽의 블록을 선택합니다. 편집 → 현재 쪽 블록 선택에서 여러 블록을 선택해 복사·삭제·이동·들여쓰기를 적용합니다. 편집 → 여러 줄·블록 구간 선택에서는 드래그나 Shift+클릭으로 블록의 일부부터 다음 블록까지 골라 복사·잘라내기·붙여넣기·삭제합니다. 부분 구간 창을 닫은 뒤 Ctrl+Z로 되돌릴 수 있습니다."),
        new("결과 편집", "대체 초안과 규정", "대체 초안 창에서 전달된 설명을 고르고 묵자와 점자를 확인한 뒤 넣기를 누릅니다. 최초 원문은 작업 파일에 남습니다. 점자 규정 창은 적용 규정과 서버 검색을 제공합니다. 검색 본문은 서버가 제공하는 발췌입니다."),
        new("메뉴별 기능", "화면과 창", "보기 메뉴에서 원본·텍스트·점자, 줄 번호, 줄번역을 조정합니다. 패널의 분리 단추는 독립 창을 만듭니다. 분리 창을 닫으면 본창으로 돌아오며 같은 문서와 되돌리기 기록을 공유합니다. 원본은 패널 폭에 맞춰 표시합니다."),
        new("메뉴별 기능", "저장과 내보내기", "자동 저장은 하지 않습니다. Ctrl+S는 .smj 작업 파일과 연결된 서버 작업을 저장합니다. 서버 저장이 실패해도 로컬 파일은 남습니다. .smj에는 원본·텍스트·점자·메모·옵션이 들어 있습니다. 내보내기에서 전체·원본 쪽·점자 면 범위를 고릅니다. 점자 TXT는 선택한 면의 점자를 판면과 번호까지 그대로 담습니다."),
        new("메뉴별 기능", "판면과 꼬리말", "판면에서 칸 수·줄 수·시작 번호를 정합니다. 삽입한 머리말은 현재 면 맨 위부터 이후 면에 적용합니다. 빈 머리말은 해제합니다. 수동 점자 때문에 위치를 연결할 수 없다는 안내가 나오면 해당 블록을 재점역하거나 블록 시작에 삽입하세요. 꼬리말 관리에서 기본 내용과 전체 정렬, 인쇄 면 번호의 시작·끝 범위를 정합니다. 끝 면을 비우면 끝까지, 내용을 비우면 그 범위의 꼬리말을 숨깁니다. 꼬리말은 페이지행이 있는 면에 출력합니다. 범위는 서버에서 복원되지 않으므로 .smj를 보관하세요. 모눈은 편집 좌표이며 최종 면 배치는 점자 면 미리보기에서 확인합니다."),
        new("메뉴별 기능", "설정과 6점키", "옵션 → 기본 점역 옵션에서 계정 기본값을 바꾸고, 양식에서 설정을 저장·선택·삭제합니다. 문서의 현재 옵션과 계정 기본값은 별개입니다. 6점키 설정은 1·2·3·4·5·6점 순서로 서로 다른 영문 키를 입력합니다. 모양 설정에서는 색상·글자·커서·고대비를 미리 확인하고 적용합니다."),
        new("마이페이지", "사용량과 작업 이력", "정보 → 마이페이지에서 내 사용량, 기관 잔여 크레딧, 일별 사용량과 작업 이력을 확인합니다. 작업 이력에서 선택한 작업을 다시 열 수 있습니다. 새 문서와 TXT는 차감되지 않습니다."),
        new("문제 해결", "저장·점역 실패", "점역 오류가 나면 원문을 보존한 상태로 문제 블록을 확인하세요. 엔진 응답 지연 시 점자 입력만 잠기며 원문 편집과 로컬 저장은 가능합니다. 서버 저장 미완료 표시는 완료로 처리하지 않습니다. 현재 신규 점자 저장에는 서버 제약이 있으므로 .smj 파일을 보관하세요."),
        new("문제 해결", "문의하기", "정보 → 게시판에서 질문을 작성합니다. 비밀글은 작성자와 운영자만 열 수 있습니다. 첨부는 5개, 각각 10 MB까지이며 현재 작업 정보는 체크한 경우에만 전송합니다. 문의 이메일: contact-semojum@gmail.com"),
        new("단축키", "단축키 목록", "Ctrl+N 새 문서 / Ctrl+O 열기 / Ctrl+S 저장 / Ctrl+W 문서 닫기\nCtrl+F 찾기 / F3 다음 찾기 / Ctrl+H 바꾸기 / Ctrl+G 이동\nCtrl+Z 되돌리기 / Ctrl+Y 다시 실행 / Ctrl+Enter 쪽바꿈\nAlt+G 기본 점역 옵션 / Alt+K 6점키 설정 / Alt+Y 편집 판면 / Alt+P 출력 설정 / Alt+S 문단 모양 / F4 기호\nCtrl+0–9 텍스트 상용구 / Alt+0–9 점자 상용구 / F1 도움말")
    ];
    public HelpWindow(string context)
    {
        Title = "세모점 도움말"; Width = 900; Height = 520; MinWidth = 600; MinHeight = 330; Background = Brushes.White;
        var root = new DockPanel(); Content = root;
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(8) }; DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var tabs = new TabControl { Width = 260 }; DockPanel.SetDock(tabs, Dock.Left); root.Children.Add(tabs);
        var reader = new FlowDocumentScrollViewer { IsToolBarVisible = false }; root.Children.Add(reader);
        var history = new List<Topic>(); var index = -1;
        void Render(Topic topic)
        {
            var doc = new FlowDocument { FontFamily = new FontFamily("Malgun Gothic"), FontSize = 14, PagePadding = new(24) };
            doc.Blocks.Add(new Paragraph(new Run(topic.Title)) { FontSize = 22, FontWeight = FontWeights.Bold }); doc.Blocks.Add(new Paragraph(new Run(topic.Body))); reader.Document = doc;
        }
        void Visit(Topic topic) { if (index >= 0 && history[index] == topic) return; history.RemoveRange(index + 1, history.Count - index - 1); history.Add(topic); index++; Render(topic); }
        toolbar.Children.Add(CompactDialog.Button("◀", () => { if (index > 0) Render(history[--index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("▶", () => { if (index + 1 < history.Count) Render(history[++index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("인쇄", () => { var print = new PrintDialog(); if (print.ShowDialog() == true) print.PrintDocument(((IDocumentPaginatorSource)reader.Document).DocumentPaginator, "세모점 도움말"); return Task.CompletedTask; }));
        foreach (var mode in new[] { "내용", "색인", "검색" })
        {
            var panel = new DockPanel(); var list = new ListBox { DisplayMemberPath = "Title" };
            if (mode == "검색")
            {
                var input = new TextBox { Margin = new(8), MinHeight = 28 }; DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input);
                input.TextChanged += (_, _) => list.ItemsSource = Topics.Where(t => (t.Title + t.Body).Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            }
            list.ItemsSource = mode == "색인" ? Topics.OrderBy(t => t.Title).ToArray() : Topics;
            list.SelectionChanged += (_, _) => { if (list.SelectedItem is Topic topic) Visit(topic); };
            panel.Children.Add(list); tabs.Items.Add(new TabItem { Header = mode, Content = panel });
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
    }
}
