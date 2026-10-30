using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
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

            var originalLeft = window.Left; var originalTop = window.Top;
            window.Width = 480; window.UpdateLayout();
            MoveToWorkEdge(window, false);

            menus[2].IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            size.IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var nestedPopup = size.Template.FindName("PART_Popup", size) as Popup;
            var nestedBorder = nestedPopup?.Child as Border;
            check(nestedBorder is { ActualWidth: 220 } && nestedPopup!.IsOpen, "Font-size submenu opens at Figma's 220-pixel width");
            check(nestedBorder?.Effect is DropShadowEffect { BlurRadius: 16, ShadowDepth: 8, Opacity: 0.14 } && nestedBorder.Margin == new Thickness(20) && nestedPopup is { Placement: PlacementMode.Custom, CustomPopupPlacementCallback: not null },
                "Nested menu reserves its shadow margin without displacing its content");
            if (nestedBorder is not null)
            {
                var parentLeft = size.PointToScreen(new Point());
                var parentRight = size.PointToScreen(new Point(size.ActualWidth, 0));
                var left = nestedBorder.PointToScreen(new Point());
                var right = nestedBorder.PointToScreen(new Point(nestedBorder.ActualWidth, 0));
                var gap = Math.Abs(left.X - parentRight.X);
                check(gap <= 2, $"Right-opening submenu is adjacent (gap {gap:0.0}px; parent {parentLeft.X:0.0}..{parentRight.X:0.0}; submenu {left.X:0.0}..{right.X:0.0}; dpi {VisualTreeHelper.GetDpi(size).DpiScaleX:0.00}/{VisualTreeHelper.GetDpi(nestedPopup!).DpiScaleX:0.00})");
                await CheckPointerAsync(size, "right-opening", directory, check);
            }
            if (nestedBorder is not null && Environment.GetEnvironmentVariable("MENU_NESTED_CAPTURE") is { Length: > 0 } nestedCapture)
                await WindowsSmoke.RenderElementAsync(nestedBorder, nestedCapture);
            size.IsSubmenuOpen = false;
            menus[2].IsSubmenuOpen = false;

            // Exercise WPF's automatic right-edge flip with the real popup HWND.
            MoveToWorkEdge(window, true);
            menus[2].IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            size.IsSubmenuOpen = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            nestedPopup = size.Template.FindName("PART_Popup", size) as Popup;
            nestedBorder = nestedPopup?.Child as Border;
            if (nestedBorder is not null)
            {
                var start = size.PointToScreen(new Point(0, 0));
                var parentRight = size.PointToScreen(new Point(size.ActualWidth, 0));
                var surfaceLeft = nestedBorder.PointToScreen(new Point(0, 0));
                var surfaceRight = nestedBorder.PointToScreen(new Point(nestedBorder.ActualWidth, 0));
                var gap = Math.Abs(surfaceRight.X - start.X);
                check(gap <= 2, $"Nested menu stays adjacent to its parent at the screen edge (gap {gap:0.0}px; parent {start.X:0.0}..{parentRight.X:0.0}; submenu {surfaceLeft.X:0.0}..{surfaceRight.X:0.0})");
                await CheckPointerAsync(size, "flipped", directory, check);
            }
            else check(false, "Nested menu is visible at the screen edge");
            size.IsSubmenuOpen = false; menus[2].IsSubmenuOpen = false;
            window.Width = 1440; window.Left = originalLeft; window.Top = originalTop; window.UpdateLayout();

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
                memo.Input.Focus(); memo.UpdateLayout();
                var inputSurface = memo.Input.Template.FindName("Surface", memo.Input) as Border;
                check(memo.Input.IsKeyboardFocusWithin && inputSurface?.BorderBrush is SolidColorBrush focus && focus.Color == Color.FromRgb(0, 95, 184) && inputSurface.BorderThickness == new Thickness(1),
                    "Focused memo input shows the complete Figma blue outline");
                check(AutomationProperties.GetName(memo.Input) == "메모 내용", "Memo input retains its accessible name");
                memo.Input.SelectionBrush = Brushes.Purple;
                check(memo.Input.SelectionBrush == Brushes.Purple, "Memo input template preserves an explicitly supplied selection color");
                memo.ApplyInputAppearance(true); memo.ApplyInputAppearance(false);
                check(memo.Input.SelectionBrush == Brushes.Purple, "Memo input retains an explicit selection color after a contrast-mode transition");
                memo.Input.ClearValue(TextBox.SelectionBrushProperty);
                memo.ApplyInputAppearance(true); memo.UpdateLayout();
                var highContrastSurface = memo.Input.Template.FindName("Surface", memo.Input) as Border;
                check(memo.Input.Background is SolidColorBrush systemBackground && systemBackground.Color == SystemColors.WindowColor &&
                    memo.Input.Foreground is SolidColorBrush systemText && systemText.Color == SystemColors.WindowTextColor &&
                    memo.Input.SelectionBrush is SolidColorBrush systemSelection && systemSelection.Color == SystemColors.HighlightColor &&
                    highContrastSurface?.Background is SolidColorBrush surfaceBackground && surfaceBackground.Color == SystemColors.WindowColor,
                    "Memo input follows system background, text and selection colors in high contrast");
                memo.Input.Focus(); memo.UpdateLayout();
                check(memo.Input.IsKeyboardFocusWithin && highContrastSurface?.BorderBrush is SolidColorBrush highContrastFocus && highContrastFocus.Color == SystemColors.HighlightColor,
                    "Focused memo input uses the system highlight outline in high contrast");
                memo.ApplyInputAppearance(false); memo.Input.Focus(); memo.UpdateLayout();
                memo.Input.Text = string.Join("\n", Enumerable.Repeat("긴 메모 내용", 20)); memo.UpdateLayout(); memo.Input.ScrollToEnd(); memo.UpdateLayout();
                var contentHost = memo.Input.Template.FindName("PART_ContentHost", memo.Input) as ScrollViewer;
                check(contentHost is { ScrollableHeight: > 0, VerticalOffset: > 0 } && memo.Input.Text.Contains("긴 메모 내용"),
                    "Long memo input remains editable and scrolls inside its focus outline");
                memo.Input.Text = "여기 그래프랑 본문이 안 맞는 것 같은데"; memo.UpdateLayout();
                if (Environment.GetEnvironmentVariable("MENU_MEMO_CAPTURE") is { Length: > 0 } memoCapture) await WindowsSmoke.RenderElementAsync(memo, memoCapture);
            }
            finally { memo.Close(); }
        }
        finally { await window.ReleaseResourcesAsync(); window.PrepareSmokeShutdown(); window.Close(); }
    }
    private static async Task CheckPointerAsync(MenuItem parent, string direction, string directory, Action<bool, string> check)
    {
        GetCursorPos(out var cursor);
        try
        {
            var from = parent.PointToScreen(new Point(parent.ActualWidth / 2, parent.ActualHeight / 2));
            var first = parent.Items.OfType<MenuItem>().First();
            var to = first.PointToScreen(new Point(first.ActualWidth / 2, first.ActualHeight / 2));
            for (var i = 0; i <= 12; i++)
            {
                SetCursorPos((int)(from.X + (to.X - from.X) * i / 12), (int)(from.Y + (to.Y - from.Y) * i / 12));
                await Task.Delay(25);
            }
            GetCursorPos(out var actualCursor);
            if (Math.Abs(actualCursor.X - to.X) <= 2 && Math.Abs(actualCursor.Y - to.Y) <= 2)
                check(parent.IsSubmenuOpen && first.IsHighlighted, $"Pointer can move to the {direction} submenu without closing it");
            else
                System.IO.File.AppendAllText(System.IO.Path.Combine(directory, "menu-pointer-unavailable.txt"), $"SKIP {direction}: Requested {to}; actual {actualCursor.X},{actualCursor.Y}. Pointer movement is unavailable.\n");
        }
        finally { SetCursorPos(cursor.X, cursor.Y); }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPosition { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    private static void MoveToWorkEdge(Window window, bool right)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref info) || !GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException("Cannot determine the active monitor work area for the menu test.");
        var x = right ? info.Work.Right - (bounds.Right - bounds.Left) : info.Work.Left;
        if (!SetWindowPos(handle, IntPtr.Zero, x, info.Work.Top, 0, 0, 0x0001 | 0x0004 | 0x0010))
            throw new InvalidOperationException("Cannot position the menu test window.");
        window.UpdateLayout();
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPosition point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
}
