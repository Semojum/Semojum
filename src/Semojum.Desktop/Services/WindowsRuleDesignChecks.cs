using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Semojum.Core;
using Semojum.Core.Integration;
using Semojum.Desktop.Dialogs;
using SharpVectors.Converters;

namespace Semojum.Desktop.Services;

internal static class WindowsRuleDesignChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Fixture(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var evidence = new AppliedRuleRow("원본 자료 (원문)", "", "제1항", "근거", "제공된 원문", "규정", "id", true);
        var applied = new AppliedRulesDialog(api, [evidence], false, "원문", "⠁", "1줄 1칸");
        check(applied.Width == 900 && applied.Height == 488, "Applied rules requests its final Figma dimensions");
        applied.Show();
        try
        {
            await Task.Delay(100); applied.UpdateLayout();
            WindowsDialogBoundsChecks.CheckFitted(applied, 900, 488, "Applied rules dialog", check);
            check(applied.Rules.Columns.Select(c => c.Header?.ToString()).SequenceEqual(["걸린 자리", "점형", "규정", "조항 원문"]),
                "Applied rules exposes the final Figma four-column headings");
            var source = Descendants(applied).OfType<TextBlock>().FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == "RulesSourcePreview");
            var braille = Descendants(applied).OfType<TextBlock>().FirstOrDefault(t => AutomationProperties.GetAutomationId(t) == "RulesBraillePreview");
            check(source is not null && braille is not null && source.Text == "원문" && braille.Text == "⠁" && Math.Abs(source.TransformToAncestor(applied).Transform(new Point()).Y - braille.TransformToAncestor(applied).Transform(new Point()).Y) < 2,
                "Selected original text and braille are preserved side by side");
            check(Descendants(applied).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("위치는 확인되지 않았습니다", StringComparison.Ordinal)),
                "Unverified server evidence retains its position warning");
            var warning = Descendants(applied).OfType<TextBlock>().First(t => t.IsVisible && t.Text.Contains("위치는 확인되지 않았습니다", StringComparison.Ordinal));
            var open = Descendants(applied).OfType<Button>().First(b => Equals(b.Content, "규정 전문 열기"));
            check(warning.TransformToAncestor(applied).Transform(new Point()).Y < open.TransformToAncestor(applied).Transform(new Point()).Y,
                "The unverified-position warning remains in the content above the footer actions");
            CheckIcon(applied, "applied-rules-close.svg", check);
            if (Environment.GetEnvironmentVariable("RULES_APPLIED_CAPTURE") is { Length: > 0 } appliedCapture)
                await WindowsSmoke.RenderElementAsync(applied, appliedCapture);
        }
        finally { applied.Close(); }
        await CheckAppliedRuleAccessAsync(api, http, fixture, check);
        if (Environment.GetEnvironmentVariable("RULES_MATCH_CAPTURE") is { Length: > 0 } matchingCapture)
        {
            var match = new AppliedRulesDialog(api,
                [new("(", "⠦", "한글 점자 제4장 제33항", "소괄호표", "소괄호표는 여는 때 ⠦, 닫는 때 ⠴ 로 적는다.", "한국 점자 규정", "one"),
                 new("~", "⠤", "한글 점자 제4장 제38항", "물결표", "물결표는 ⠤ 로 적고 앞말과 뒷말에 붙여 쓴다.", "한국 점자 규정", "two"),
                 new("줄 전체", "—", "제1장 제2절 2.", "페이지 구성", "페이지는 본문 내용과 페이지행으로 구성된다.", "점자 도서 제작 지침", "three")],
                false, "(가)~(라)", "⠦⡫⠴⠭⠦⠐⡣⠴");
            try { match.Show(); await WindowsSmoke.RenderElementAsync(match, matchingCapture); } finally { match.Close(); }
        }

        var dialog = new RuleDialog(api, [], "", "", "검색");
        check(dialog.Width == 720 && dialog.Height == 748 && dialog.Title == "점자 규정 찾아보기", "Rule search requests the final Figma dimensions and title");
        dialog.Show();
        try
        {
            for (var i = 0; i < 30 && dialog.RuleResults.Children.Count == 0; i++) await Task.Delay(30);
            dialog.UpdateLayout();
            WindowsDialogBoundsChecks.CheckFitted(dialog, 720, 748, "Rule search", check);
            CheckIcon(dialog, "rule-browser-close.svg", check); CheckIcon(dialog, "rule-browser-book.svg", check);
            var query = Descendants(dialog).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "규정 검색어");
            var queryBounds = query.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, query.ActualWidth, query.ActualHeight));
            check(query.IsEnabled && query.Focusable && queryBounds.Left >= 0 && queryBounds.Right <= dialog.ActualWidth + 1 && queryBounds.Top >= 0 && queryBounds.Bottom <= dialog.ActualHeight + 1,
                "Fitted rule search keeps the query field reachable inside the window");
            var hint = Descendants(dialog).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "RuleSearchPlaceholder");
            check(query.Text == "검색" && hint.Visibility == Visibility.Collapsed && !hint.IsHitTestVisible, "The search hint does not obscure existing text or intercept input");
            query.Text = ""; dialog.UpdateLayout();
            check(hint.Visibility == Visibility.Visible && hint.Text == "점역 규정 검색..." && AutomationProperties.GetName(query) == "규정 검색어", "The empty search field exposes the Figma hint and retains its accessible name");
            query.Text = "검색"; dialog.UpdateLayout();
            check(hint.Visibility == Visibility.Collapsed && query.Text == "검색", "Typing hides the search hint without changing the input");
            var rows = dialog.RuleResults.Children.OfType<Expander>().ToArray();
            check(rows.Length == 2, "Rule search preserves the server's two results");
            if (rows.Length > 1)
            {
                rows[0].IsExpanded = true; dialog.UpdateLayout();
                if (Environment.GetEnvironmentVariable("RULES_SEARCH_CAPTURE") is { Length: > 0 } searchCapture)
                    await WindowsSmoke.RenderElementAsync(dialog, searchCapture);
                CheckIcon(rows[0], "rule-browser-down.svg", check);
                CheckIcon(rows[1], "rule-browser-right.svg", check);
                check(Descendants(rows[0]).OfType<TextBlock>().Any(t => t.Text == "첫 줄\n  ⠁ 원문\n마지막 줄"), "Full rule text retains line breaks and indentation");
                rows[1].IsExpanded = true; dialog.UpdateLayout();
                check(!rows[0].IsExpanded && rows[1].IsExpanded, "Only one rule result is expanded at a time");
            }
            var filter = Descendants(dialog).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "한국 점자 규정"));
            filter?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 30 && !fixture.Requests.Any(p => p.Contains("publisher=MCST", StringComparison.Ordinal)); i++) await Task.Delay(30);
            await Task.Delay(100);
            check(fixture.Requests.Any(p => p.Contains("publisher=MCST", StringComparison.Ordinal)), "The Figma filter chip sends the selected publisher to the existing API");
            var next = Descendants(dialog).OfType<Button>().FirstOrDefault(b => Equals(b.Content, "다음"));
            next?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 30 && !fixture.Requests.Any(p => p.Contains("page=1", StringComparison.Ordinal)); i++) await Task.Delay(30);
            await Task.Delay(100);
            check(fixture.Requests.Any(p => p.Contains("page=1", StringComparison.Ordinal) && p.Contains("publisher=MCST", StringComparison.Ordinal)),
                "Next page keeps the selected publisher and requests the next result page");
            var evidenceMenu = Descendants(dialog).OfType<DockPanel>().Select(p => p.ContextMenu)
                .OfType<ContextMenu>().SelectMany(m => m.Items.OfType<MenuItem>()).FirstOrDefault(i => Equals(i.Header, "적용 규정"));
            evidenceMenu?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            check(Descendants(dialog).OfType<TextBlock>().Any(t => t.IsVisible && t.Text.Contains("이 블록에 전달된 적용 규정이 없습니다", StringComparison.Ordinal)),
                "Applied rule evidence remains available from the context menu when no evidence exists");
        }
        finally { dialog.Close(); }
    }

    private static async Task CheckAppliedRuleAccessAsync(SemojumApiClient api, HttpClient http, Fixture fixture, Action<bool, string> check)
    {
        var noId = new AppliedRuleRow("원문", "", "제1항", "식별자 없음", "근거", "규정", "  ", true);
        var withId = new AppliedRuleRow("점자", "⠁", "제2항", "식별자 있음", "근거", "규정", "specific-rule-id");
        var dialog = new AppliedRulesDialog(api, [noId, withId], false) { Width = 480 };
        dialog.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); dialog.UpdateLayout();
            var open = Descendants(dialog).OfType<Button>().Single(b => Equals(b.Content, "규정 전문 열기"));
            var reason = Descendants(dialog).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "AppliedRulesOpenUnavailableReason");
            check(!open.IsEnabled && reason.IsVisible && reason.Text.Contains("식별자", StringComparison.Ordinal)
                && AutomationProperties.GetHelpText(open) == reason.Text && ToolTipService.GetShowOnDisabled(open),
                "An applied rule with no identifier explains why full text cannot be opened");
            var bounds = reason.TransformToAncestor(dialog).TransformBounds(new Rect(0, 0, reason.ActualWidth, reason.ActualHeight));
            check(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= dialog.ActualWidth + 1 && bounds.Bottom <= dialog.ActualHeight + 1,
                "The unavailable-rule explanation stays inside a narrow dialog");
            var requestCount = fixture.Requests.Count;
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.Requests.Count == requestCount && !Application.Current.Windows.OfType<RuleDialog>().Any(w => w.Owner == dialog && w.IsVisible),
                "An empty rule identifier cannot open an unfiltered search");
            dialog.Rules.SelectedIndex = 1; dialog.UpdateLayout();
            check(open.IsEnabled && reason.Visibility == Visibility.Collapsed && string.IsNullOrEmpty(AutomationProperties.GetHelpText(open)),
                "Selecting an identified rule enables the existing full-text action");
            var found = false; var tick = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) =>
            {
                var child = Application.Current.Windows.OfType<RuleDialog>().FirstOrDefault(w => w.Owner == dialog && w.IsVisible);
                if (child is not null && fixture.Requests.Any(p => p.Contains("q=specific-rule-id", StringComparison.Ordinal)))
                { found = true; timer.Stop(); child.Close(); }
                else if (++tick >= 250)
                { timer.Stop(); foreach (var childWindow in dialog.OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) childWindow.Close(); }
            };
            timer.Start();
            try { open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            finally { timer.Stop(); }
            check(found, "The identified applied rule opens a search for its actual identifier");
            await api.LogoutAsync();
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (open.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(!open.IsEnabled && reason.Text.Contains("로그인", StringComparison.Ordinal) && ReferenceEquals(dialog.Rules.SelectedItem, withId),
                "An open applied-rule dialog disables its action after sign-out without changing its selection");
            requestCount = fixture.Requests.Count;
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.Requests.Count == requestCount && !Application.Current.Windows.OfType<RuleDialog>().Any(w => w.Owner == dialog && w.IsVisible),
                "A rule action cannot open a search if authentication expires while the dialog is open");
            await api.LoginAsync("fixture", "fixture");
            deadline = DateTime.UtcNow.AddSeconds(2);
            while (!open.IsEnabled && DateTime.UtcNow < deadline) await Task.Delay(25);
            check(open.IsEnabled && ReferenceEquals(dialog.Rules.SelectedItem, withId),
                "Signing in again restores the action for the still-selected identified rule");
            dialog.Rules.SelectedIndex = -1; dialog.UpdateLayout();
            check(!open.IsEnabled && reason.Text.Contains("선택", StringComparison.Ordinal),
                "Clearing the applied-rule selection disables full-text access with an explanation");
        }
        finally { dialog.Close(); }

        var closedDialog = new AppliedRulesDialog(api, [withId], false);
        closedDialog.Show();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var closedOpen = Descendants(closedDialog).OfType<Button>().Single(b => Equals(b.Content, "규정 전문 열기"));
        closedDialog.Close();
        await api.LogoutAsync(); await Task.Delay(350);
        check(closedOpen.IsEnabled, "A closed applied-rule dialog stops checking account state");
        await api.LoginAsync("fixture", "fixture");

        var signedOutApi = new SemojumApiClient(http);
        var signedOut = new AppliedRulesDialog(signedOutApi, [withId], false);
        signedOut.Show();
        try
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var open = Descendants(signedOut).OfType<Button>().Single(b => Equals(b.Content, "규정 전문 열기"));
            var reason = Descendants(signedOut).OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == "AppliedRulesOpenUnavailableReason");
            check(!open.IsEnabled && reason.IsVisible && reason.Text.Contains("로그인", StringComparison.Ordinal)
                && AutomationProperties.GetHelpText(open) == reason.Text,
                "Signed-out users receive an accessible explanation instead of an enabled rule action");
            var requestCount = fixture.Requests.Count;
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(fixture.Requests.Count == requestCount && !Application.Current.Windows.OfType<RuleDialog>().Any(w => w.Owner == signedOut && w.IsVisible),
                "The signed-out action cannot open a rule search");
        }
        finally { signedOut.Close(); }
    }

    private static void CheckIcon(DependencyObject root, string name, Action<bool, string> check)
    {
        var icon = Descendants(root).OfType<SvgViewbox>().FirstOrDefault(i => i.Source?.ToString().EndsWith("/" + name, StringComparison.Ordinal) == true);
        check(icon is { Width: 16, Height: 16, Drawings.Bounds.IsEmpty: false }, $"Figma rule icon {name} renders from its original 16px SVG");
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private sealed class Fixture : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);
            var result = request.RequestUri!.AbsolutePath.EndsWith("/login")
                ? "{\"accessToken\":\"fixture\",\"refreshToken\":\"fixture\",\"role\":\"ROLE_USER\"}"
                : "{\"items\":[{\"ruleId\":\"1\",\"source\":\"규정\",\"section\":\"제1항\",\"ruleName\":\"첫 규정\",\"contents\":\"요약\",\"fullText\":\"첫 줄\\n  ⠁ 원문\\n마지막 줄\"},{\"ruleId\":\"2\",\"source\":\"규정\",\"section\":\"제2항\",\"ruleName\":\"두번째\",\"contents\":\"요약\"}],\"totalPages\":3,\"totalElements\":45}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"isSuccess\":true,\"result\":" + result + "}", Encoding.UTF8, "application/json") });
        }
    }
}
