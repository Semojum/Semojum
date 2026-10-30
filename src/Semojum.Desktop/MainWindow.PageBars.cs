using System.Windows;
using Semojum.Core;
using Semojum.Desktop.Controls;

namespace Semojum.Desktop;

public partial class MainWindow
{
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
