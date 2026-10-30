using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop;

public partial class MainWindow
{
    private bool originalWheelBusy;
    private (DocumentSession Owner, int Page, bool Bottom)? originalWheelDestination;

    private void OriginalScroll_MouseWheel(object sender, MouseWheelEventArgs e) => HandleOriginalWheel((ScrollViewer)sender, e);
    private void OriginalPreview_MouseWheel(object sender, MouseWheelEventArgs e) => HandleOriginalWheel(Original.PreviewScroll, e);

    private async void HandleOriginalWheel(ScrollViewer scroll, MouseWheelEventArgs e)
    {
        if (originalWheelBusy) return;
        if (originalWheelDestination is { } stale && (stale.Owner != session || session.ActiveBlock?.Page != stale.Page))
            originalWheelDestination = null;
        if (e.Handled || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None ||
            originalWheelDestination is not null ||
            session?.IsTextJob == true || session?.ActiveBlock is not { } active ||
            !HasReadableOriginal(session, active.Page)) return;

        // Preserve normal scrolling inside a page. Only leave it when the wheel
        // points past the existing top or bottom edge.
        var backwards = e.Delta > 0;
        if (backwards ? scroll.VerticalOffset > .5 : scroll.VerticalOffset < scroll.ScrollableHeight - .5) return;
        var pages = session.OriginalPages.Keys.Concat(session.PageStatuses.Keys).Distinct().Order().ToArray();
        var index = Array.IndexOf(pages, active.Page) + (backwards ? -1 : 1);
        if (index < 0 || index >= pages.Length) return;
        var page = pages[index];
        var target = session.Blocks.FirstOrDefault(b => b.Page == page);
        if (target is null || target.Metadata.IsBlocked || !HasReadableOriginal(session, page) ||
            session.PageStatuses.TryGetValue(page, out var status) && JobStatus.IsFailedPage(status)) return;

        e.Handled = true;
        var owner = session;
        originalWheelBusy = true;
        originalWheelDestination = (owner, page, backwards);
        try
        {
            await NavigateAsync(NavigationTarget.OriginalPage, page.ToString(System.Globalization.CultureInfo.InvariantCulture), SearchPanel.Text, preserveSourceVisibility: true);
            if (owner != session || session.ActiveBlock?.Page != page)
                originalWheelDestination = null;
            else if (smoke)
                FinishOriginalWheelTransition(owner, page, scroll);
        }
        catch (Exception ex) { originalWheelDestination = null; if (owner == session) session.Status = ex.Message; }
        finally
        {
            originalWheelBusy = false;
        }
    }

    private static bool HasReadableOriginal(DocumentSession owner, int page)
    {
        if (!owner.OriginalPages.TryGetValue(page, out var original)) return false;
        return original.Type == "text" ? original.Lines is not null :
            original.AssetName is { Length: > 0 } name && owner.OriginalFiles.ContainsKey(name);
    }

    private void FinishOriginalWheelTransition(DocumentSession owner, int page, ScrollViewer scroll)
    {
        if (originalWheelDestination is not { } pending || pending.Owner != owner || pending.Page != page) return;
        originalWheelDestination = null;
        // Wait for the new image/text to be measured before moving to its edge.
        Dispatcher.BeginInvoke(() =>
        {
            if (session != owner || session.ActiveBlock?.Page != page) return;
            scroll.UpdateLayout();
            if (pending.Bottom) scroll.ScrollToBottom(); else scroll.ScrollToTop();
        }, DispatcherPriority.Loaded);
    }
    private void OriginalPageBar_Loaded(object sender, RoutedEventArgs e)
    {
        OriginalPageBar.SetIcons("panel-original-prev.svg", "panel-original-next.svg");
        OriginalPageBar.PageRequested -= NavigateOriginalPage;
        OriginalPageBar.PageRequested += NavigateOriginalPage;
        OriginalPageBar.InvalidPageRequested -= InvalidPage;
        OriginalPageBar.InvalidPageRequested += InvalidPage;
        RefreshPanelPageBars();
    }
    private void SourcePageBar_Loaded(object sender, RoutedEventArgs e)
    {
        SourcePageBar.SetIcons("panel-page-prev.svg", "panel-page-next.svg");
        SourcePageBar.PageRequested -= NavigateOriginalPage;
        SourcePageBar.PageRequested += NavigateOriginalPage;
        SourcePageBar.InvalidPageRequested -= InvalidPage;
        SourcePageBar.InvalidPageRequested += InvalidPage;
        RefreshPanelPageBars();
    }
    private void RefreshPanelPageBars()
    {
        if (session is null || OriginalPageBar is null || SourcePageBar is null || OriginalUpload is null) return;
        var editable = session.Blocks.Select(b => b.Page).Distinct().Order().ToArray();
        var all = editable.Concat(session.OriginalPages.Keys).Concat(session.PageStatuses.Keys).Distinct().Order().ToArray();
        var original = session.OriginalPages.Keys.Concat(session.PageStatuses.Keys).Distinct().Order().ToArray();
        var selected = session.ActiveBlock?.Page;
        OriginalPageBar.Visibility = OriginalUpload.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        OriginalPageBar.SetPages(original, editable.Intersect(original), selected);
        SourcePageBar.SetPages(all, editable, selected);
    }
    private async void NavigateOriginalPage(int page)
    {
        try { await NavigateAsync(NavigationTarget.OriginalPage, page.ToString(System.Globalization.CultureInfo.InvariantCulture), SearchPanel.Text); }
        catch (Exception ex) { session.Status = ex.Message; }
    }
    private void InvalidPage() => session.Status = "편집할 수 있는 쪽 번호를 입력해 주세요. 실패한 쪽은 쪽별 변환 상태에서 확인할 수 있습니다.";
}
