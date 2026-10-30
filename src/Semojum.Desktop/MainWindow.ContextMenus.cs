using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core;
using SharpVectors.Converters;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private ContextMenu CreatePanelContextMenu(bool braille)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("FinalContextMenu") };
        var prefix = braille ? "braille-context-" : "text-context-";
        MenuItem Item(string label, string action, string? icon, string? shortcut = null)
        {
            var item = new MenuItem { Header = label, Tag = action, InputGestureText = shortcut ?? "", Style = (Style)FindResource("FinalContextItem") };
            if (icon is not null) item.Icon = new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{prefix}{icon}.svg"), Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false };
            item.Click += async (_, _) => await ExecuteAsync(action);
            return item;
        }
        void Line() => menu.Items.Add(new Separator { Style = (Style)FindResource("FinalContextSeparator") });
        MenuItem Group(string label, string icon, (string Label, string Tag)[] tags)
        {
            var group = new MenuItem { Header = label, Style = (Style)FindResource("FinalContextItem"), ToolTip = "원문에 해당 조판 태그를 넣습니다." };
            group.Icon = new SvgViewbox { Source = new Uri($"pack://application:,,,/Semojum.Mockup;component/Assets/Figma/{prefix}{icon}.svg"), Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false };
            foreach (var (name, tag) in tags) group.Items.Add(Item(name, "tag:" + tag, null));
            return group;
        }
        menu.Items.Add(Item("메모 추가", "memo", "memo", "Ctrl+M")); Line();
        if (!braille) menu.Items.Add(Item("잘라내기", "cut", "cut", "Ctrl+X"));
        menu.Items.Add(Item("복사", "copy", "copy", "Ctrl+C"));
        if (braille) menu.Items.Add(Item("잘라내기", "cut", null, "Ctrl+X"));
        menu.Items.Add(Item(braille ? "점자로 붙여넣기" : "붙여넣기", "paste", "paste", "Ctrl+V")); Line();
        menu.Items.Add(Item("대체 초안 보기", "drafts", "draft"));
        menu.Items.Add(Item("적용 규정 보기", "rules", "rule"));
        if (braille)
        {
            menu.Items.Add(Item("이 블록 다시 점역", "translate", "recon"));
            var restore = new MenuItem { Header = "직접 고친 점자 되돌리기", Style = (Style)FindResource("FinalContextItem"), Icon = new SvgViewbox { Source = new Uri("pack://application:,,,/Semojum.Mockup;component/Assets/Figma/braille-context-undo.svg"), Width = 16, Height = 16, Stretch = Stretch.None, IsHitTestVisible = false } };
            restore.Click += (_, _) => RestoreOriginalBraille();
            menu.Opened += (_, _) => { restore.IsEnabled = CanRestoreOriginalBraille(); restore.ToolTip = restore.IsEnabled ? "기록된 원래 점역 결과로 되돌립니다." : "현재 원문과 일치하는 원래 점역 결과가 없습니다."; };
            menu.Items.Add(restore); Line();
            menu.Items.Add(Group("점자 줄 넣기", "blank", [("상단 줄", "상단줄"), ("중단 줄", "중단줄"), ("하단 줄", "하단줄"), ("상단 굵은 줄", "상단굵은줄"), ("하단 굵은 줄", "하단굵은줄")]));
            menu.Items.Add(Group("표 테두리", "table", [("표 시작 테두리 선", "표시작"), ("표 중앙 테두리 선", "표중앙"), ("표 끝 테두리 선", "표끝"), ("표 구분선", "표구분선")]));
            menu.Items.Add(Item("현재 줄 지우기", "clearLine", null));
        }
        else
        {
            Line();
            menu.Items.Add(Group("정렬", "align", [("왼쪽", "왼쪽정렬"), ("가운데", "가운데정렬"), ("오른쪽", "오른쪽정렬")]));
            menu.Items.Add(Item("수식 입력창", "math", "math"));
        }
        return menu;
    }
    private bool CanRestoreOriginalBraille() => session?.IsReadOnly == false && session.ActiveBlock is { HasManualBraille: true, IsBrailleLocked: false, Translation: { SourceText: { } source } } block && source == block.Source;
    private void RestoreOriginalBraille()
    {
        if (!CanRestoreOriginalBraille() || session.ActiveBlock is not { Translation: { } result } block) return;
        EditStructure(() => { block.Braille = result.Cells; block.HasManualBraille = false; block.State = "원래 점역 결과 복원"; });
        translatedRevisions[block] = block.SourceRevision;
        session.Status = "원래 점역 결과를 복원했습니다. 실행 취소로 이전 점자 편집을 되돌릴 수 있습니다.";
    }
}
