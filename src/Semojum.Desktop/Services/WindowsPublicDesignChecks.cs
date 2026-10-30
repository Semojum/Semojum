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
            await WaitUntilAsync(() => Descendants(notices).OfType<Expander>().Count() == 2);
            var cards = Descendants(notices).OfType<Expander>().ToArray();
            check(notices.Width == 600 && notices.Height == 488 && cards[0].IsExpanded && !cards[1].IsExpanded,
                "Public notices use the final 600x488 modal and show the first announcement expanded");
            var close = Descendants(notices).OfType<Button>().FirstOrDefault(b => AutomationProperties.GetAutomationId(b) == "PublicNoticesClose");
            check(close?.Content is SvgViewbox { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false },
                "Public notices title uses the exact Figma close icon");
            check(HasIcon(cards[0], "public-notice-chevron-down.svg") && HasIcon(cards[1], "public-notice-chevron-right.svg"),
                "Expanded and collapsed notices use the matching Figma 16px icons");
            var unsupported = Descendants(notices).OfType<Button>().Where(b => AutomationProperties.GetHelpText(b).Contains("지원하지", StringComparison.Ordinal)).ToArray();
            check(unsupported.Length == 3 && unsupported.All(b => !b.IsEnabled && ToolTipService.GetShowOnDisabled(b)), "Unavailable notice category filters are visibly disabled with an explanation");
            if (Environment.GetEnvironmentVariable("PUBLIC_NOTICE_CAPTURE") is { Length: > 0 } capture) await WindowsSmoke.RenderElementAsync(notices, capture);
            cards[1].IsExpanded = true; notices.UpdateLayout();
            check(cards[1].IsExpanded && Descendants(notices).OfType<TextBlock>().Any(t => t.Text == "두 번째 내용"), "Notice expansion remains functional");
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
            check(upload is { IsEnabled: false } && ToolTipService.GetShowOnDisabled(upload) && AutomationProperties.GetHelpText(upload).Contains("지원", StringComparison.Ordinal),
                "Unsupported inquiry attachments remain clearly disabled");
            check(HasIcon(inquiry, "public-inquiry-chevron.svg") && upload is not null && HasIcon(upload, "public-inquiry-upload.svg"),
                "Inquiry category and attachment slots use the matching Figma 16px icons");
            check(Descendants(inquiry).OfType<TextBox>().Any(b => AutomationProperties.GetName(b) == "기관명")
                && Descendants(inquiry).OfType<TextBox>().Any(b => AutomationProperties.GetName(b) == "제목"), "Organization and title fields remain available");
            check(!Descendants(inquiry).OfType<TextBlock>().Any(t => t.Text.Contains("1년 보관", StringComparison.Ordinal)),
                "The inquiry does not claim an unconfirmed retention period");
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
            var body = "{\"isSuccess\":true,\"result\":[{\"title\":\"첫 공지\",\"body\":\"첫 번째 내용\",\"createdAt\":\"2026-10-25T00:00:00Z\"},{\"title\":\"둘째 공지\",\"body\":\"두 번째 내용\",\"createdAt\":\"2026-10-13T00:00:00Z\"}]}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
