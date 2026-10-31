using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Automation;
using Semojum.Feature.Shell.Wpf;

namespace Semojum.Desktop.Dialogs;

public sealed class HelpWindow : Window
{
    private static SharpVectors.Converters.SvgViewbox HelpIcon(string name, double width = 16, double height = 16) => EditorModalDesign.Icon(name, width, height, "Semojum.Feature.Shell.Wpf");
    private sealed record Topic(string Group, string Title, string Body);
    private static readonly Topic[] Topics =
    [
        new("시작하기", "로그인과 새 문서", "로그인 후 Ctrl+N으로 새 문서를 만듭니다. 새 문서와 TXT 열기는 AI와 크레딧을 사용하지 않습니다. 여러 문서는 위쪽 탭으로 전환하고 Ctrl+W로 닫습니다."),
        new("문서 변환", "파일 업로드", "PDF·HWP·HWPX·이미지를 업로드할 수 있습니다. 전체 크기는 100 MB까지입니다. 여러 이미지는 순서를 확인하고 올리세요. 점역 양식과 옵션을 고른 후 변환을 시작합니다. 실패한 쪽은 쪽별 변환 상태에서 확인합니다."),
        new("결과 편집", "텍스트 고치기", "원문을 고치면 입력이 멈춘 뒤 로컬 엔진이 점역합니다. Enter는 같은 블록 안의 줄바꿈입니다. Ctrl+Enter는 쪽바꿈을 넣습니다. 다른 쪽은 이동 창에서 선택합니다. 경계에서 Backspace·Delete·Insert를 누르면 인접 블록이 합쳐집니다. Ctrl+Z·Ctrl+Y는 쪽별 최대 100단계 기록을 사용합니다."),
        new("결과 편집", "점자 직접 고치기", "점자 패널에서 6점 입력을 켜고 설정한 키를 함께 누른 뒤 모두 놓으면 한 칸이 입력됩니다. 키는 1·2·3·4·5·6점 순서로 설정합니다. 직접 고친 점자는 별도 색으로 표시합니다. 다시 점역할 때 유지·덮어쓰기·취소를 고를 수 있습니다. 텍스트는 점자 직접 수정으로 바뀌지 않습니다."),
        new("결과 편집", "검토 표시 읽는 법", "문서 여백의 표시는 확인이 필요한 점역 결과나 해결되지 않은 메모를 나타냅니다. 표시를 선택하면 해당 내용을 확인할 수 있습니다. 확인할 근거가 없는 위치에는 표시하지 않습니다."),
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
        Title = "누리닷 도움말 - 누리닷"; Width = 1100; Height = 700; MinWidth = 600; MinHeight = 320; Background = Brushes.White;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel(); Content = root;
        Services.WindowFrame.Apply(this, editor: true);
        if (Content is Grid frame && frame.Children[0] is Border caption && caption.Child is DockPanel heading && heading.Children.OfType<StackPanel>().FirstOrDefault() is { } captionButtons)
        {
            captionButtons.Margin = new Thickness(0);
            var assets = new[] { "help-min", "help-max", "help-close" };
            foreach (var (button, asset) in captionButtons.Children.OfType<Button>().Zip(assets))
            { button.Width = 46; button.Height = 32; button.Content = HelpIcon(asset); button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0); button.Template = EditorModalDesign.ButtonTemplate(typeof(Button)); }
        }
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(12, 4, 0, 4) };
        var toolbarLayout = new DockPanel { LastChildFill = false };
        var toolbarFrame = new Border { Height = 40, Background = EditorModalDesign.Color("#F9F9F9"), BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new(0, 0, 0, 1), Child = toolbarLayout };
        DockPanel.SetDock(toolbarFrame, Dock.Top); root.Children.Add(toolbarFrame);
        var search = new TextBox { Width = 300, Height = 32, MinHeight = 0, Padding = new Thickness(31, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center }; EditorModalDesign.Input(search); AutomationProperties.SetName(search, "도움말 검색");
        var searchFrame = new Grid { Width = 300, Height = 32, Margin = new Thickness(0, 4, 20, 4) }; searchFrame.Children.Add(search);
        var searchIcon = HelpIcon("help-search"); searchIcon.HorizontalAlignment = HorizontalAlignment.Left; searchIcon.Margin = new Thickness(7, 0, 0, 0); searchIcon.IsHitTestVisible = false; searchFrame.Children.Add(searchIcon);
        var placeholder = new TextBlock { Text = "도움말 검색", Foreground = EditorModalDesign.Color("#8A8A8A"), Margin = new Thickness(31, 0, 0, 0), IsHitTestVisible = false };
        searchFrame.Children.Add(placeholder); search.TextChanged += (_, _) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        DockPanel.SetDock(searchFrame, Dock.Right); toolbarLayout.Children.Add(searchFrame); toolbarLayout.Children.Add(toolbar);
        var tabs = new TabControl { Width = 300, Background = EditorModalDesign.Color("#F9F9F9") };
        var tabsPanel = new FrameworkElementFactory(typeof(DockPanel));
        var tabStrip = new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.TabPanel)); tabStrip.SetValue(FrameworkElement.HeightProperty, 32d); tabStrip.SetValue(Panel.IsItemsHostProperty, true); tabStrip.SetValue(DockPanel.DockProperty, Dock.Top); tabsPanel.AppendChild(tabStrip);
        var tabContent = new FrameworkElementFactory(typeof(ContentPresenter)); tabContent.SetValue(ContentPresenter.ContentSourceProperty, "SelectedContent"); tabsPanel.AppendChild(tabContent);
        tabs.Template = new ControlTemplate(typeof(TabControl)) { VisualTree = tabsPanel };
        var tabStyle = new Style(typeof(TabItem)); tabStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, 100d)); tabStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 32d)); tabStyle.Setters.Add(new Setter(Control.BackgroundProperty, EditorModalDesign.Color("#F9F9F9"))); tabStyle.Setters.Add(new Setter(Control.ForegroundProperty, EditorModalDesign.Color("#5F5F5F")));
        var tabSurface = new FrameworkElementFactory(typeof(Border), "Surface"); tabSurface.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background)) { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent }); tabSurface.SetValue(Border.BorderBrushProperty, EditorModalDesign.Color("#EAEAEA")); tabSurface.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 0, 1));
        var tabGrid = new FrameworkElementFactory(typeof(Grid)); var tabLine = new FrameworkElementFactory(typeof(Border), "Line"); tabLine.SetValue(FrameworkElement.HeightProperty, 2d); tabLine.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top); tabLine.SetValue(Border.BackgroundProperty, EditorModalDesign.Color("#005FB8")); tabLine.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); tabGrid.AppendChild(tabLine);
        var tabLabel = new FrameworkElementFactory(typeof(ContentPresenter)); tabLabel.SetValue(ContentPresenter.ContentSourceProperty, "Header"); tabLabel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); tabLabel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); tabGrid.AppendChild(tabLabel); tabSurface.AppendChild(tabGrid);
        var tabTemplate = new ControlTemplate(typeof(TabItem)) { VisualTree = tabSurface }; var tabSelected = new Trigger { Property = System.Windows.Controls.Primitives.Selector.IsSelectedProperty, Value = true }; tabSelected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Line")); tabSelected.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White)); tabSelected.Setters.Add(new Setter(Control.ForegroundProperty, EditorModalDesign.Color("#1A1A1A"))); tabSelected.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold)); tabTemplate.Triggers.Add(tabSelected); tabStyle.Setters.Add(new Setter(Control.TemplateProperty, tabTemplate)); tabs.ItemContainerStyle = tabStyle;
        DockPanel.SetDock(tabs, Dock.Left); root.Children.Add(tabs);
        var reader = new FlowDocumentScrollViewer { IsToolBarVisible = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(reader);
        var history = new List<Topic>(); var index = -1;
        var topicNodes = new Dictionary<string, TreeViewItem>();
        var treeNavigationChanged = false;
        void Render(Topic topic)
        {
            var doc = new FlowDocument { FontFamily = (FontFamily)FindResource("UiFont"), FontSize = 14, TextAlignment = TextAlignment.Left, PagePadding = new(40, 22, 40, 20) };
            doc.Blocks.Add(new Paragraph(new Run(topic.Group + "  ›  " + topic.Title)) { Foreground = EditorModalDesign.Color("#8A8A8A"), FontSize = 12, Margin = new(0, 0, 0, 6) });
            doc.Blocks.Add(new Paragraph(new Run(topic.Title)) { FontSize = 28, FontWeight = FontWeights.Bold, LineHeight = 36, Margin = new(0, 0, 0, 20) });
            if (topic.Title == "점자 직접 고치기")
            {
                doc.Blocks.Add(new Paragraph(new Run("점자 패널에서 칸을 직접 쳐서 고치는 방법입니다.")) { FontSize = 18, LineHeight = 26, Margin = new Thickness(0, 0, 0, 14) });
                doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = EditorModalDesign.Color("#EAEAEA") }) { Margin = new Thickness(0, 0, 0, 18) });
                doc.Blocks.Add(new Paragraph(new Run("차례대로 하기")) { FontSize = 14, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) });
                var steps = new[] { "점자 패널에서 고칠 칸을 선택합니다.", "6점 입력을 켜고 설정한 키를 함께 누른 뒤 모두 놓습니다.", "직접 고친 점자는 별도 색으로 표시합니다.", "되돌리려면 Ctrl+Z를 누릅니다." };
                for (var i = 0; i < steps.Length; i++)
                {
                    var badge = new Grid { Width = 20, Height = 20 }; badge.Children.Add(HelpIcon("help-step-badge", 20, 20)); badge.Children.Add(new TextBlock { Text = (i + 1).ToString(), Foreground = EditorModalDesign.Color("#005FB8"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                    var paragraph = new Paragraph { LineHeight = 24, Margin = new Thickness(0, 0, 0, 4) }; paragraph.Inlines.Add(new InlineUIContainer(badge)); paragraph.Inlines.Add(new Run("  " + steps[i])); doc.Blocks.Add(paragraph);
                }
                var note = new StackPanel(); var noteHeading = new StackPanel { Orientation = Orientation.Horizontal }; noteHeading.Children.Add(HelpIcon("help-info")); noteHeading.Children.Add(new TextBlock { Text = "알아 두기", FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0) }); note.Children.Add(noteHeading);
                note.Children.Add(new TextBlock { Text = "점자를 직접 고쳐도 텍스트 패널은 그대로 둡니다. 다시 점역할 때 유지 여부를 선택합니다.", TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 16, Margin = new Thickness(0, 8, 0, 0) });
                var noteAccent = new Border { BorderBrush = EditorModalDesign.Color("#005FB8"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12), Child = note };
                var noteCard = new Border { Background = EditorModalDesign.Color("#F9F9F9"), BorderBrush = EditorModalDesign.Color("#EAEAEA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = noteAccent };
                doc.Blocks.Add(new BlockUIContainer(noteCard) { Margin = new Thickness(0, 16, 0, 20) });
                doc.Blocks.Add(new Paragraph(new Run("함께 보기")) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
                foreach (var relatedTitle in new[] { "검토 표시 읽는 법", "설정과 6점키", "단축키 목록" })
                {
                    var related = Topics.First(t => t.Title == relatedTitle); var link = new Hyperlink(new Run(relatedTitle)) { Foreground = EditorModalDesign.Color("#005FB8") }; link.Click += (_, _) => Visit(related);
                    var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 4) }; paragraph.Inlines.Add(new InlineUIContainer(HelpIcon("help-related"))); paragraph.Inlines.Add(link); doc.Blocks.Add(paragraph);
                }
            }
            else doc.Blocks.Add(new Paragraph(new Run(topic.Body)) { LineHeight = 22, Margin = new(0) });
            if (topic.Title == "추가 도구")
            {
                var command = runCommand ?? (Owner is IHelpCommandSource source ? source.ExecuteHelpCommandAsync : null);
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
            var feedback = new StackPanel();
            feedback.Children.Add(new Border { Height = 1, Background = EditorModalDesign.Color("#EAEAEA"), Margin = new Thickness(0, 20, 0, 12) });
            var feedbackRow = new StackPanel { Orientation = Orientation.Horizontal };
            feedbackRow.Children.Add(new TextBlock { Text = "이 도움말이 도움이 되었나요?", Foreground = EditorModalDesign.Color("#8A8A8A"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) });
            foreach (var answer in new[] { "예", "아니요" })
            {
                var button = new Button { Content = answer, Width = 60, Height = 32, MinWidth = 0, MinHeight = 0, Margin = new Thickness(0, 0, 8, 0), IsEnabled = false,
                    ToolTip = "의견 보내기 기능을 사용할 수 없습니다." };
                PublicDialogDesign.StyleAction(button); button.Width = 60;
                ToolTipService.SetShowOnDisabled(button, true); AutomationProperties.SetName(button, "도움말 의견 " + answer);
                AutomationProperties.SetHelpText(button, "의견 보내기 기능을 사용할 수 없습니다."); feedbackRow.Children.Add(button);
            }
            feedback.Children.Add(feedbackRow); doc.Blocks.Add(new BlockUIContainer(feedback));
            reader.Document = doc;
        }
        void Visit(Topic topic) { if (index >= 0 && history[index] == topic) return; history.RemoveRange(index + 1, history.Count - index - 1); history.Add(topic); index++; Render(topic); }
        toolbar.Children.Add(CompactDialog.Button("이전", () => { if (index > 0) Render(history[--index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("다음", () => { if (index + 1 < history.Count) Render(history[++index]); return Task.CompletedTask; }));
        toolbar.Children.Add(CompactDialog.Button("인쇄", () => { var print = new PrintDialog(); if (print.ShowDialog() == true) print.PrintDocument(((IDocumentPaginatorSource)reader.Document).DocumentPaginator, "누리닷 도움말"); return Task.CompletedTask; }));
        var toolbarNames = new[] { "이전", "다음", "인쇄" }; var toolbarAssets = new[] { "help-prev", "help-next", "help-print" };
        for (var i = 0; i < toolbar.Children.Count; i++)
        {
            var button = (Button)toolbar.Children[i]; button.MinWidth = button.Width = 32; button.Height = 32; button.Margin = i == 2 ? new Thickness(8, 0, 0, 0) : new Thickness(0); button.Padding = new Thickness(0); button.Style = (Style)FindResource("ToolButton"); button.Content = HelpIcon(toolbarAssets[i]); button.ToolTip = toolbarNames[i]; AutomationProperties.SetName(button, toolbarNames[i]);
        }
        foreach (var mode in new[] { "내용", "색인", "검색" })
        {
            if (mode == "내용")
            {
                var tree = new TreeView { BorderThickness = new(0), Padding = new(0), Margin = new(0, 2, 0, 0), Background = Brushes.Transparent, FontSize = 12, FontWeight = FontWeights.Normal };
                ScrollViewer.SetVerticalScrollBarVisibility(tree, ScrollBarVisibility.Hidden);
                var itemStyle = new Style(typeof(TreeViewItem)); var stack = new FrameworkElementFactory(typeof(StackPanel));
                var surface = new FrameworkElementFactory(typeof(Border), "TopicSurface"); surface.SetValue(FrameworkElement.HeightProperty, 32d); surface.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                var row = new FrameworkElementFactory(typeof(DockPanel)); var icon = new FrameworkElementFactory(typeof(SharpVectors.Converters.SvgViewbox), "Arrow"); icon.SetValue(FrameworkElement.WidthProperty, 16d); icon.SetValue(FrameworkElement.HeightProperty, 16d); icon.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 4, 0)); icon.SetValue(System.Windows.Controls.Viewbox.StretchProperty, Stretch.None); icon.SetValue(SharpVectors.Converters.SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Feature.Shell.Wpf;component/Assets/Figma/help-tree-right.svg")); icon.SetValue(UIElement.VisibilityProperty, Visibility.Hidden); icon.SetValue(DockPanel.DockProperty, Dock.Left); row.AppendChild(icon);
                var header = new FrameworkElementFactory(typeof(ContentPresenter), "TopicHeader"); header.SetValue(ContentPresenter.ContentSourceProperty, "Header"); header.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); row.AppendChild(header); surface.AppendChild(row); stack.AppendChild(surface);
                var children = new FrameworkElementFactory(typeof(ItemsPresenter), "TopicChildren"); children.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed); stack.AppendChild(children);
                var itemTemplate = new ControlTemplate(typeof(TreeViewItem)) { VisualTree = stack };
                var hasItems = new Trigger { Property = ItemsControl.HasItemsProperty, Value = true }; hasItems.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Arrow")); itemTemplate.Triggers.Add(hasItems);
                var leaf = new Trigger { Property = ItemsControl.HasItemsProperty, Value = false }; leaf.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(20, 0, 0, 0), "TopicHeader")); itemTemplate.Triggers.Add(leaf);
                var expanded = new Trigger { Property = TreeViewItem.IsExpandedProperty, Value = true }; expanded.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "TopicChildren")); expanded.Setters.Add(new Setter(SharpVectors.Converters.SvgViewbox.SourceProperty, new Uri("pack://application:,,,/Semojum.Feature.Shell.Wpf;component/Assets/Figma/help-tree-down.svg"), "Arrow")); itemTemplate.Triggers.Add(expanded);
                var chosen = new Trigger { Property = TreeViewItem.IsSelectedProperty, Value = true }; chosen.Setters.Add(new Setter(Border.BackgroundProperty, EditorModalDesign.Color("#EFF6FC"), "TopicSurface")); chosen.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold)); itemTemplate.Triggers.Add(chosen);
                itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate)); tree.Resources[typeof(TreeViewItem)] = itemStyle;
                TreeViewItem? defaultBranch = null;
                foreach (var group in Topics.GroupBy(t => t.Group))
                {
                    var branch = new TreeViewItem { Header = group.Key, IsExpanded = group.Key == "결과 편집", IsSelected = group.Key == "결과 편집" };
                    if (group.Key == "결과 편집") defaultBranch = branch;
                    var items = group.Key == "결과 편집" ? group.OrderBy(t => t.Title == "3패널 보기" ? 0 : t.Title == "텍스트 고치기" ? 1 : 2) : group.AsEnumerable();
                    branch.PreviewMouseLeftButtonDown += (_, e) =>
                    {
                        if (e.GetPosition(branch).Y >= 32) return;
                        branch.IsExpanded = !branch.IsExpanded; branch.IsSelected = true; branch.Focus(); e.Handled = true;
                    };
                    foreach (var topic in items) { var item = new TreeViewItem { Header = topic.Title, Tag = topic }; topicNodes[topic.Title] = item; branch.Items.Add(item); }
                    tree.Items.Add(branch);
                }
                tree.SelectedItemChanged += (_, e) =>
                {
                    // The preselected default branch can be realized after this handler is attached.
                    if (e.OldValue is not null || !ReferenceEquals(e.NewValue, defaultBranch)) treeNavigationChanged = true;
                    if (tree.SelectedItem is TreeViewItem { Tag: Topic topic }) Visit(topic);
                };
                tabs.Items.Add(new TabItem { Header = "내용", Content = tree, Tag = "separator" }); continue;
            }
            var panel = new DockPanel(); var list = new ListBox { DisplayMemberPath = "Title" };
            if (mode == "검색")
            {
                var input = new TextBox { Margin = new(8), MinHeight = 28 }; DockPanel.SetDock(input, Dock.Top); panel.Children.Add(input);
                input.TextChanged += (_, _) => list.ItemsSource = Topics.Where(t => (t.Title + t.Body).Contains(input.Text, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                search.TextChanged += (_, _) => { input.Text = search.Text; if (search.Text.Length > 0) tabs.SelectedIndex = 2; };
                input.TextChanged += (_, _) => { if (search.Text != input.Text) search.Text = input.Text; };
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
        if (topicNodes.TryGetValue(matched.Title, out var selectedTopic))
            Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // A real navigation choice can arrive before this deferred initial selection.
                if (treeNavigationChanged || index < 0 || history[index] != matched) return;
                if (selectedTopic.Parent is TreeViewItem parent) parent.IsExpanded = true;
                selectedTopic.IsSelected = true;
            });
        Loaded += (_, _) => Services.WindowBounds.Fit(this);
    }
}
