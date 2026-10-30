using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
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
        applied.Show();
        try
        {
            await Task.Delay(100); applied.UpdateLayout();
            check(applied.Width == 900 && applied.Height == 488, "Applied rules dialog uses the final Figma dimensions");
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
        if (Environment.GetEnvironmentVariable("RULES_MATCH_CAPTURE") is { Length: > 0 } matchingCapture)
        {
            var match = new AppliedRulesDialog(api,
                [new("(", "⠦", "한글 점자 제4장 제33항", "소괄호표", "소괄호표는 여는 때 ⠦, 닫는 때 ⠴ 로 적는다.", "한국 점자 규정", "one"),
                 new("~", "⠤", "한글 점자 제4장 제38항", "물결표", "물결표는 ⠤ 로 적고 앞말과 뒷말에 붙여 쓴다.", "한국 점자 규정", "two"),
                 new("줄 전체", "—", "제1장 제2절 2.", "페이지 구성", "페이지는 본문 내용과 페이지행으로 구성된다.", "점자 도서 제작 지침", "three")],
                false, "(가)~(라)", "⠦⡫⠴⠭⠦⠐⡣⠴");
            try { match.Show(); await WindowsSmoke.RenderElementAsync(match, matchingCapture); } finally { match.Close(); }
        }

        var dialog = new RuleDialog(api, [], "", "", "검색"); dialog.Show();
        try
        {
            for (var i = 0; i < 30 && dialog.RuleResults.Children.Count == 0; i++) await Task.Delay(30);
            dialog.UpdateLayout();
            check(dialog.Width == 720 && dialog.Height == 748 && dialog.Title == "점자 규정 찾아보기", "Rule search uses the final Figma dimensions and title");
            CheckIcon(dialog, "rule-browser-close.svg", check); CheckIcon(dialog, "rule-browser-book.svg", check);
            var query = Descendants(dialog).OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "규정 검색어");
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
