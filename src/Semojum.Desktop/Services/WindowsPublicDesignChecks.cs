using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsPublicDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture); var api = new SemojumApiClient(http);
        var notices = new PublicNoticesDialog(api); notices.Show();
        try
        {
            await WaitUntilAsync(() => Descendants(notices).OfType<Expander>().Count() == 4);
            notices.UpdateLayout(); await notices.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var cards = Descendants(notices).OfType<Expander>().ToArray();
            check(notices.Width == 600 && notices.Height == 488 && cards[0].IsExpanded && !cards[1].IsExpanded,
                "Public notices use the final 600x488 modal and show the first announcement expanded");
            var close = Descendants(notices).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicNoticesClose");
            check(close?.Content is SvgViewbox { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false },
                "Public notices title uses the exact Figma close icon");
            check(HasIcon(cards[0], "public-notice-chevron-down.svg") && HasIcon(cards[1], "public-notice-chevron-right.svg"),
                "Expanded and collapsed notices use the matching Figma 16px icons");
            // Figma A2 pills: 「새 판」 47 and 「자료」 40 in #005FB8, 「점검」 40 in #9D5D00; a notice without a category has none.
            string Pill(Expander card) => Descendants(card).OfType<Border>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicNoticeCategory") is { Child: TextBlock { Foreground: SolidColorBrush ink } label } pill
                && pill.BorderBrush is SolidColorBrush { Color: var edge } && edge == ink.Color && pill.Height == 18 && pill.CornerRadius == new CornerRadius(9) ? $"{label.Text}/{pill.Width:0}/{ink.Color}" : "";
            var pills = cards.Select(Pill).ToArray();
            check(pills.SequenceEqual(["새 판/47/#FF005FB8", "점검/40/#FF9D5D00", "자료/40/#FF005FB8", ""]),
                "Each notice shows its Figma category pill before the title, and an uncategorized notice shows none (" + string.Join(", ", pills) + ")");
            var tabs = Descendants(notices).OfType<Button>().Where(b => b.Content is "전체" or "점검" or "새 판" or "자료").ToArray();
            check(tabs.Select(b => b.Content).SequenceEqual(new object[] { "전체", "점검", "새 판", "자료" }) && tabs.All(b => b.IsEnabled && b.ToolTip is null && b.Opacity == 1)
                && AutomationProperties.GetItemStatus(tabs[0]) == "선택됨" && tabs[0].Background is SolidColorBrush { Color: var all } && all == Color.FromRgb(239, 246, 252),
                "All four notice tabs are available and 「전체」 starts selected");
            if (Environment.GetEnvironmentVariable("PUBLIC_NOTICE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(notices, capture);
            cards[1].IsExpanded = true; notices.UpdateLayout();
            check(cards[1].IsExpanded && Descendants(notices).OfType<TextBlock>().Any(t => t.Text == "두 번째 내용"), "Notice expansion remains functional");
            // The app filters by category (the server has no parameter); 「전체」 also shows the uncategorized notice.
            var shown = new List<string>();
            foreach (var tab in tabs.Skip(1).Append(tabs[0]))
            {
                tab.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                notices.UpdateLayout(); await notices.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                shown.Add(tab.Content + ":" + string.Join("|", Descendants(notices).OfType<Expander>().Select(AutomationProperties.GetName))
                    + (AutomationProperties.GetItemStatus(tab) == "선택됨" && tabs.Count(t => AutomationProperties.GetItemStatus(t) == "선택됨") == 1 ? "" : " (not selected)"));
            }
            check(shown.SequenceEqual(["점검:둘째 공지", "새 판:첫 공지", "자료:셋째 공지", "전체:첫 공지|둘째 공지|셋째 공지|분류 없는 공지"]),
                "Each notice tab shows only its category and 「전체」 shows every notice (" + string.Join(", ", shown) + ")");
        }
        finally { notices.Close(); }

        fixture.HoldNotices = true; var slow = new PublicNoticesDialog(api); slow.Show();
        try
        {
            await WaitUntilAsync(() => fixture.WaitingForRelease);
            var close = Descendants(slow).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicNoticesClose");
            check(close is { IsEnabled: true }, "The notice close control remains enabled during a slow request");
            close?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!slow.IsVisible, "The notice close control cancels a slow request and closes the window");
        }
        finally { fixture.Release(); if (slow.IsVisible) slow.Close(); }

        var inquiry = new PublicInquiryDialog(api); inquiry.Show();
        try
        {
            await Task.Delay(50); inquiry.UpdateLayout();
            check(inquiry.Width == 540 && inquiry.Height == 528, "Public inquiry uses the final 540x528 modal");
            var close = Descendants(inquiry).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicInquiryClose");
            check(close?.Content is SvgViewbox { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false }, "Public inquiry title uses the exact Figma close icon");
            var upload = Descendants(inquiry).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicInquiryAttachment");
            var limit = Descendants(inquiry).OfType<TextBlock>().SingleOrDefault(t => t.Text == "최대 3개 · 10MB 제한");
            check(upload is { IsEnabled: true, Width: 104, Height: 32, Opacity: 1, ToolTip: null } && limit is { IsVisible: true, FontSize: 12, Foreground: SolidColorBrush { Color: var muted } } && muted == Color.FromRgb(95, 95, 95)
                && limit.TransformToAncestor(inquiry).Transform(new Point()).X - upload.TransformToAncestor(inquiry).Transform(new Point(upload.ActualWidth, 0)).X is >= 11 and <= 13
                && !Descendants(inquiry).OfType<TextBlock>().Any(t => t.Text.Contains("지원되지 않습니다", StringComparison.Ordinal)),
                "The inquiry 「파일」 button is enabled with the Figma 「최대 3개 · 10MB 제한」 12px beside it");
            var attached = Descendants(inquiry).OfType<ListBox>().Single(l => AutomationProperties.GetAutomationId(l) == "PublicInquiryAttachments");
            check(!attached.IsVisible, "No attachment list shows before a file is chosen");
            var attachmentPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qa1110-" + Guid.NewGuid() + ".txt");
            System.IO.File.WriteAllText(attachmentPath, "QA attachment");
            try
            {
                inquiry.Attach([attachmentPath]);
                await inquiry.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                inquiry.UpdateLayout();
                var remove = Descendants(attached).OfType<Button>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicInquiryRemoveAttachment");
                check(remove is { IsVisible: true, IsEnabled: true } && Descendants(attached).OfType<TextBlock>().Any(t => t.Text == System.IO.Path.GetFileName(attachmentPath)),
                    "QA1110: attached filename has a visible remove button");
                remove!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(attached.Items.Count == 0 && !attached.IsVisible, "QA1110: attachment remove button clears the selected upload");
            }
            finally
            {
                while (attached.Items.Count > 0)
                {
                    attached.SelectedIndex = 0;
                    attached.ContextMenu!.Items.OfType<MenuItem>().Single().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                }
                System.IO.File.Delete(attachmentPath);
            }
            check(HasIcon(inquiry, "public-inquiry-chevron.svg") && upload is not null && HasIcon(upload, "public-inquiry-upload.svg"),
                "Inquiry category and attachment slots use the matching Figma 16px icons");
            check(Descendants(inquiry).OfType<TextBox>().Any(b => AutomationProperties.GetName(b) == "기관명")
                && Descendants(inquiry).OfType<TextBox>().Any(b => AutomationProperties.GetName(b) == "제목"), "Organization and title fields remain available");
            var category = Descendants(inquiry).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "분류");
            check(category.Items.Cast<string>().SequenceEqual(["계정 · 발급", "점역 결과", "크레딧 · 결제", "사용법"]) && Equals(category.SelectedItem, "계정 · 발급"),
                "Inquiry categories follow the final Figma list and start at 「계정 · 발급」");
            check(!Descendants(inquiry).OfType<TextBlock>().Any(t => t.Text.Contains("전달됩니다", StringComparison.Ordinal) || t.Text.Contains("문의 이메일", StringComparison.Ordinal)),
                "The inquiry form has no delivery-scope or contact-mail lines that the final Figma does not show");
            check(Descendants(inquiry).OfType<CheckBox>().Any(c => Equals(c.Content, "개인정보 수집·이용에 동의합니다 (답변 목적 · 1년 보관)"))
                && Descendants(inquiry).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("1년 보관", StringComparison.Ordinal)),
                "The inquiry consent states the final purpose and one-year retention period");
            var consent = Descendants(inquiry).OfType<CheckBox>().Single(c => AutomationProperties.GetAutomationId(c) == "PublicInquiryConsent");
            check(consent.IsChecked != true && consent.IsEnabled && AutomationProperties.GetName(consent).Contains("동의", StringComparison.Ordinal),
                "Inquiry consent starts unchecked and is available to assistive technology");
            consent.IsChecked = true; inquiry.UpdateLayout();
            check(HasIcon(consent, "public-inquiry-check.svg"), "The checked inquiry consent uses the original Figma 16px icon");
            consent.IsChecked = false;
            var inquiryScroll = Descendants(inquiry).OfType<ScrollViewer>().First(s => s.Content is StackPanel);
            var consentTop = consent.TransformToAncestor(inquiryScroll).Transform(new Point()).Y;
            check(consentTop >= 0 && consentTop + consent.ActualHeight <= inquiryScroll.ViewportHeight + 1,
                $"Default inquiry window fully displays consent above the footer (top={consentTop:F1}, height={consent.ActualHeight:F1}, viewport={inquiryScroll.ViewportHeight:F1})");
            inquiry.Width = 440; inquiry.Height = 400; inquiry.UpdateLayout(); inquiryScroll.ScrollToBottom();
            await inquiry.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); inquiry.UpdateLayout();
            consentTop = consent.TransformToAncestor(inquiryScroll).Transform(new Point()).Y;
            var send = Descendants(inquiry).OfType<Button>().Single(b => Equals(b.Content, "보내기"));
            check(inquiryScroll.ScrollableHeight > 0 && consentTop >= 0 && consentTop + consent.ActualHeight <= inquiryScroll.ViewportHeight + 1 && send.IsVisible && send.IsEnabled,
                "A smaller inquiry window can scroll to consent while the send button remains accessible");
            inquiry.Width = 540; inquiry.Height = 528; inquiryScroll.ScrollToTop();
            await inquiry.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle); inquiry.UpdateLayout();
            if (Environment.GetEnvironmentVariable("PUBLIC_INQUIRY_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(inquiry, capture);
        }
        finally { inquiry.Close(); }
    }
    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < deadline) await Task.Delay(20);
        if (!predicate()) throw new InvalidOperationException("Public notice fixtures did not load");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static bool HasIcon(DependencyObject root, string asset) => Descendants(root).OfType<SvgViewbox>()
        .Any(icon => icon.Source?.ToString().EndsWith(asset, StringComparison.Ordinal) == true
            && icon.Width == 16 && icon.Height == 16 && icon.Drawings is { Bounds.IsEmpty: false });
    private sealed class Fixture : HttpMessageHandler
    {
        internal bool HoldNotices, WaitingForRelease;
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => release.TrySetResult();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (HoldNotices) { WaitingForRelease = true; await release.Task.WaitAsync(token); }
            var body = "{\"isSuccess\":true,\"result\":[{\"title\":\"첫 공지\",\"body\":\"첫 번째 내용\",\"createdAt\":\"2026-10-25T00:00:00Z\",\"category\":\"RELEASE\"},"
                + "{\"title\":\"둘째 공지\",\"body\":\"두 번째 내용\",\"createdAt\":\"2026-10-13T00:00:00Z\",\"category\":\"MAINTENANCE\"},"
                + "{\"title\":\"셋째 공지\",\"body\":\"세 번째 내용\",\"createdAt\":\"2026-10-02T00:00:00Z\",\"category\":\"MATERIAL\"},"
                + "{\"title\":\"분류 없는 공지\",\"body\":\"네 번째 내용\",\"createdAt\":\"2026-10-01T00:00:00Z\",\"category\":null}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
