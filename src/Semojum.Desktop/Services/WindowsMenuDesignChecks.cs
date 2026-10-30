using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsMenuDesignChecks
{
    internal static async Task RunAsync(string directory, Action<bool, string> check)
    {
        var window = new MainWindow(smoke: true, testWithoutAccount: true, startEmpty: true) { Width = 1440, Height = 1000 };
        window.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var menus = window.ApplicationMenu.Items.OfType<MenuItem>().ToArray();
            check(menus.Length == 6 && menus.Select(m => m.Header?.ToString()).SequenceEqual(["파일(_F)", "편집(_E)", "보기(_V)", "삽입(_I)", "설정(_M)", "정보(_A)"]), "Main menu preserves the six final Figma categories");
            var size = menus[2].Items.OfType<MenuItem>().First(m => Equals(m.Header, "글자 크기"));
            check(size.Items.OfType<MenuItem>().Select(m => m.Header?.ToString()).SequenceEqual(["작게", "보통", "크게"]), "Font-size submenu follows the final design and retains its commands");
            menus[0].IsSubmenuOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            var popup = menus[0].Template.FindName("PART_Popup", menus[0]) as Popup;
            var popupBorder = popup?.Child as Border;
            check(popupBorder is { Width: 222 } && !Descendants(popupBorder).OfType<TextBlock>().Any(t => t.Text == "파일(_F)"), "Main menu opens as a 222-pixel menu without a duplicate caption");
            if (popupBorder is not null && Environment.GetEnvironmentVariable("MENU_MAIN_CAPTURE") is { Length: > 0 } mainCapture)
                await WindowsSmoke.RenderElementAsync(popupBorder, mainCapture);
            menus[0].IsSubmenuOpen = false;

            var textMenu = window.PageEditor.Editor.ContextMenu; textMenu.PlacementTarget = window.PageEditor.Editor; textMenu.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); textMenu.UpdateLayout();
            var textItems = textMenu.Items.OfType<MenuItem>().ToArray();
            check(textItems.Select(m => m.Header?.ToString()).SequenceEqual(["메모 추가", "잘라내기", "복사", "붙여넣기", "대체 초안 보기", "적용 규정 보기", "정렬", "수식 입력창"]), "Text context menu has Figma actions without emphasis or page break");
            check(textMenu.ActualWidth == 248 && textItems.All(m => m.ActualHeight == 32), "Text context menu uses Figma's 248-pixel width and 32-pixel action rows");
            check(textItems.Count(m => m.Icon is SvgViewbox { Drawings.Bounds.IsEmpty: false }) == 8, "Text context menu renders its eight original Figma icons");
            check(textItems.First().InputGestureText == "Ctrl+M" && textItems.First(m => Equals(m.Header, "정렬")).Items.Count == 3, "Text context menu exposes the memo shortcut and actual alignment commands");
            if (Environment.GetEnvironmentVariable("MENU_TEXT_CAPTURE") is { Length: > 0 } textCapture) await WindowsSmoke.RenderElementAsync(textMenu, textCapture);
            textMenu.IsOpen = false;

            var brailleMenu = window.Braille.GridView.ContextMenu; brailleMenu.PlacementTarget = window.Braille.GridView; brailleMenu.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); brailleMenu.UpdateLayout();
            var brailleItems = brailleMenu.Items.OfType<MenuItem>().ToArray();
            check(brailleItems.Any(m => Equals(m.Header, "대체 초안 보기")) && brailleItems.Any(m => Equals(m.Header, "적용 규정 보기")) &&
                brailleItems.Any(m => Equals(m.Header, "이 블록 다시 점역")) && brailleItems.Any(m => Equals(m.Header, "현재 줄 지우기")) &&
                !brailleItems.Any(m => m.Header?.ToString()?.Contains("쪽바꿈") == true || m.Header?.ToString()?.Contains("강조") == true),
                "Braille context menu preserves existing actions and omits emphasis and page break");
            var restore = brailleItems.First(m => Equals(m.Header, "직접 고친 점자 되돌리기"));
            check(!restore.IsEnabled && restore.ToolTip?.ToString()?.Contains("없습니다") == true, "Manual braille restore is unavailable without an authoritative prior result");
            var draftItem = brailleItems.First(m => Equals(m.Header, "대체 초안 보기"));
            var draftBlock = window.Session.ActiveBlock!; var beforeDraft = draftBlock.Metadata;
            check(!draftItem.IsEnabled, "Braille grid draft action is disabled when no alternatives exist");
            draftBlock.Metadata = new(Type: "image", Alternatives: [new("대안", "설명", null, "⠁", null)]);
            check(draftItem.IsEnabled, "Braille grid draft action becomes available for a real alternative");
            draftBlock.Metadata = beforeDraft;
            check(!draftItem.IsEnabled, "Removing alternatives disables the braille grid draft action again");
            if (Environment.GetEnvironmentVariable("MENU_BRAILLE_CAPTURE") is { Length: > 0 } brailleCapture) await WindowsSmoke.RenderElementAsync(brailleMenu, brailleCapture);
            brailleMenu.IsOpen = false;
            var block = window.Session.ActiveBlock!; block.Source = "첫 줄\n둘째 줄\n셋째 줄"; block.Braille = "⠃"; block.HasManualBraille = true;
            block.Translation = new BrailleTranslation("⠁", [], [], false, [], SourceText: block.Source);
            brailleMenu.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            check(restore.IsEnabled, "Manual braille restore becomes available when the recorded result matches the current source");
            restore.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); brailleMenu.IsOpen = false;
            check(block.Braille == "⠁" && !block.HasManualBraille, "Manual restore uses the actual stored result");
            window.ReplayHistory();
            check(block.Braille == "⠃" && block.HasManualBraille, "Ordinary undo restores the prior manual edit after a restore");

            var key = window.InputBindings.OfType<KeyBinding>().FirstOrDefault(k => k.Key == Key.M && k.Modifiers == ModifierKeys.Control);
            check(key?.Command is not null, "Ctrl+M is bound to the actual memo command");
            var setter = typeof(MainWindow).GetMethod("SetActiveEditor", BindingFlags.NonPublic | BindingFlags.Instance)!;
            setter.Invoke(window, [window.PageEditor.Editor]);
            window.PageEditor.Refresh(); window.PageEditor.Editor.CaretOffset = window.PageEditor.Editor.Document.GetLineByNumber(2).Offset;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<MemoInputDialog>().FirstOrDefault(d => d.Owner == window && d.IsVisible);
                if (dialog is null) return;
                dialog.Input.Text = "둘째 줄 메모";
                var save = Descendants(dialog).OfType<Button>().First(b => Equals(b.Content, "저장"));
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            timer.Start();
            try { key?.Command?.Execute(null); }
            finally { timer.Stop(); }
            check(block.Memos.Any(m => m.LineIndex == 1 && m.Body == "둘째 줄 메모"), "Ctrl+M adds a memo to the actual selected source line");

            var memo = new MemoInputDialog("김점역") { Owner = window }; memo.Show();
            try
            {
                memo.Input.Text = "여기 그래프랑 본문이 안 맞는 것 같은데";
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); memo.UpdateLayout();
                var author = Descendants(memo).OfType<TextBlock>().FirstOrDefault(t => t.Text == "김점역");
                check(memo.ActualWidth == 300 && memo.ActualHeight == 150 && memo.Input.ActualHeight == 60 && author is { FontSize: 14, FontWeight: var weight } && weight == FontWeights.Bold,
                    "Memo input uses Figma's dimensions and 14-pixel bold author");
                check(Descendants(memo).OfType<Button>().Any(b => Equals(b.Content, "저장") && b.Width == 56) && Descendants(memo).OfType<Button>().Any(b => Equals(b.Content, "취소") && b.Width == 56),
                    "Memo input retains separate save and cancel actions");
                if (Environment.GetEnvironmentVariable("MENU_MEMO_CAPTURE") is { Length: > 0 } memoCapture) await WindowsSmoke.RenderElementAsync(memo, memoCapture);
            }
            finally { memo.Close(); }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
}
