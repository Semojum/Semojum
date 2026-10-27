using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

internal static class Qa4Checks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("QA4 rules preserve full text and search-only flags, and encode query/filter/page", Rules);
        yield return ("QA4 applied rules use actual engine fields and select only overlapping current cells", Applied);
        yield return ("QA4 inserting a footer continues to the next start without overlap or loss", Footers);
        yield return ("QA4 initial visual choice retains every alternative and protects saved manual results", VisualChoices);
        yield return ("QA4 user forms create, update, copy and delete without changing the built-in form", Forms);
        yield return ("QA4 cancellation distinguishes accepted, finished and already-completed server responses", Cancellation);
    }
    private static async Task Cancellation()
    {
        using var http = new HttpClient(new CancellationHandler()); var api = new SemojumApiClient(http);
        await api.LoginAsync("qa", "fixture"); var client = new JobClient(api);
        var running = await client.CancelAsync("qa");
        Check(running.Accepted && !running.IsTerminal && running.InFlightPages == 1 && running.Notice().Contains("마무리되면"));
        Check(running.Notice(alreadyFinished: true).Contains("처리가 끝났습니다"));
        var finished = await client.CancelAsync("qa");
        Check(finished.Accepted && finished.IsTerminal && finished.Status == "FAILED" && finished.Notice().Contains("처리가 끝났습니다"));
        var late = await client.CancelAsync("qa");
        Check(!late.Accepted && late.IsTerminal && late.Notice().StartsWith("이미 변환이 끝난"));
    }
    private sealed class CancellationHandler : HttpMessageHandler
    {
        private int calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object result = request.RequestUri!.AbsolutePath.EndsWith("login")
                ? new { accessToken = "fixture", refreshToken = "refresh", role = "ROLE_USER" }
                : ++calls switch
                {
                    1 => new { canceled = true, status = "IN_PROGRESS", inFlightPages = 1 },
                    2 => new { canceled = true, status = "FAILED", inFlightPages = 0 },
                    _ => new { canceled = false, status = "COMPLETED", inFlightPages = 0 }
                };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") });
        }
    }
    private static async Task Rules()
    {
        var handler = new Handler(); using var http = new HttpClient(handler); var api = new SemojumApiClient(http);
        await api.LoginAsync("qa", "fixture");
        var page = await new RuleClient(api).SearchAsync(" 음성 & / ⠿ ", "MCST", 2);
        var query = handler.Last!.Query;
        Check(query.Contains("page=2") && query.Contains("publisher=MCST") && query.Contains("%26") && query.Contains("%2F"));
        Check(page.Items[0].ExpandedText == "첫 줄\n  ⠁⠃ 원문\n마지막 줄" && page.Items[0].Supported == false && page.Items[0].HasFullText);
        Check(!page.Items[1].HasFullText && page.Items[1].ExpandedText == "발췌" && page.Items[1].Supported is null);
    }
    private static Task Applied()
    {
        using var json = JsonDocument.Parse("""{"rule_id":"MCST-1","source":"한국 점자 규정","section":"제1항","rule_name":"기호 규정","contents":"본문\n⠿","line_no":0,"col_start":1,"col_end":3,"tag":"symbol"}""");
        var rule = AppliedRules.Read(json.RootElement);
        Check(rule.Title == "기호 규정" && rule.Excerpt == "본문\n⠿");
        var block = new EditorBlock { Id = 1, Source = "기호", Braille = "⠁⠃⠉⠙", Translation = BrailleTranslation.Plain("⠁⠃⠉⠙") with { SourceText = "기호", RuleTrail = [rule] } };
        Check(AppliedRules.Select(block, 2, 0).Single().Cells == "⠃⠉");
        Check(AppliedRules.Select(block, 3, 0).Length == 0 && AppliedRules.Select(block, 0, 2).Length == 1);
        block.HasManualBraille = true; Check(AppliedRules.Select(block, 2, 0).Length == 0);
        block.HasManualBraille = false; block.Source = "바뀜"; Check(AppliedRules.Select(block, 2, 0).Length == 0);
        block.Source = "기호"; block.Translation = null; block.Braille = "⠿\n⠁⠃⠉⠙"; block.Metadata = new(Rules: [rule with { LineNo = 1 }]);
        Check(AppliedRules.Select(block, 3, 0).Single().Cells == "⠃⠉");
        using var legacy = JsonDocument.Parse("""{"rule_id":"old","title":"제목","excerpt":"옛 본문","line_no":-1}""");
        Check(AppliedRules.Read(legacy.RootElement).Excerpt == "옛 본문");
        return Task.CompletedTask;
    }
    private static Task Footers()
    {
        FooterRange[] original = [new(1, 9, "첫 장"), new(10, null, "뒤 장")];
        var result = FooterRange.Insert(original, new(5, null, "새 장", "right"));
        Check(result.Length == 3 && result[0].End == 4 && result[1].End == 9 && result[2] == original[1]);
        result = FooterRange.Insert(result, new(5, null, "고친 장")); Check(result.Length == 3 && result[1].Text == "고친 장");
        Check(original[0].End == 9);
        var legacy = new[] { new FooterRange(3, 5, "장 제목", "right"), new FooterRange(9, 12, "다음 장", "left") };
        var points = FooterRange.ChangePoints(legacy, "기본", "center");
        string At(IEnumerable<FooterRange> ranges, int page) => ranges.FirstOrDefault(r => r.Start <= page && (r.End is null || r.End >= page))?.Text ?? "기본";
        for (var page = 1; page <= 20; page++) Check(At(points, page) == At(legacy, page));
        var continued = FooterRange.Continue(points.Where(p => p.Start != 6).ToArray());
        Check(At(continued, 7) == "장 제목" && At(continued, 9) == "다음 장");
        try { FooterRange.Continue([new(3, null, "a"), new(3, null, "b")]); throw new Exception("Duplicate start accepted"); } catch (ArgumentException) { }
        return Task.CompletedTask;
    }
    private static async Task VisualChoices()
    {
        AlternativeDraft[] drafts = [new("기본", "설명 원문", "<!주>설명<!/주>", null, null), new("그래프 생략", "생략 원문", null, null, null), new("그림 1-1 참조", "참조 원문", null, null, null)];
        var policies = VisualDescriptions.Defaults(); policies["chart"] = new(["별책 참조"], "별책 참조");
        var options = new BrailleOptions(VisualDescriptions: policies);
        var block = new EditorBlock { Id = 1, Page = 1, Source = "서버 원문", Metadata = new(Type: "image", VisualSubtype: "chart", Alternatives: drafts, SelectedIndex: 0) };
        Check(VisualDraftSelection.ApplyInitial(block, options) && block.Metadata.SelectedIndex == 2 && block.Source == "참조 원문");
        Check(ReferenceEquals(drafts, block.Metadata.Alternatives) && block.Metadata.OriginalSource == "서버 원문");
        // Production AI uses chart_graph without visual_subtype (verified on a real upload).
        var graph = new EditorBlock { Id = 3, Page = 1, Source = "그래프 원문", Metadata = new(Type: "chart_graph", Alternatives: drafts, SelectedIndex: 0) };
        Check(VisualDraftSelection.ApplyInitial(graph, options) && graph.Metadata.SelectedIndex == 2 && graph.Source == "참조 원문");
        Check(ReferenceEquals(drafts, graph.Metadata.Alternatives));
        block.HasManualBraille = true; block.Braille = "⠿"; policies["chart"] = new(["생략"], "생략");
        Check(!VisualDraftSelection.ApplyInitial(block, options) && block.Source == "참조 원문" && block.Braille == "⠿");
        block.HasManualBraille = false;
        Check(VisualDraftSelection.ApplyInitial(block, options) && block.Metadata.SelectedIndex == 1);
        policies["chart"] = new(["개조식"], "개조식"); Check(VisualDraftSelection.ApplyInitial(block, options) && block.Source == "<!주>설명<!/주>");
        var table = new EditorBlock { Id = 2, Source = "<!표>표<!/표>", Metadata = new(Type: "table") };
        Check(VisualDraftSelection.ApplyInitial(table, new()) && table.Metadata.RenderMode == "table_grid");
        var session = new DocumentSession { Options = options }; session.Add(block);
        var path = Path.Combine(Path.GetTempPath(), "visual-policy-" + Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(session)); var restored = await SmjFile.LoadAsync(path);
            Check(restored.Blocks[0].Metadata.Alternatives?.Length == 3 && restored.Blocks[0].Metadata.SelectedIndex == 0 && restored.Options.VisualDescriptions!["chart"].DefaultOption == "개조식");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("QA4 assertion failed"); }
    private static async Task Forms()
    {
        using var handler = new FormHandler(); using var http = new HttpClient(handler); var api = new SemojumApiClient(http); await api.LoginAsync("qa", "fixture");
        var client = new SettingsClient(api); var baseline = (await client.FormsAsync()).Single();
        var created = await client.SaveFormAsync(null, "  사용자  ", baseline.FormId, baseline.Layout with { Columns = 30 }, new(EnglishGrade1: true));
        Check(created.Name == "사용자" && created.Layout.Columns == 30 && created.Options.EnglishGrade1 && !created.Builtin);
        var updated = await client.SaveFormAsync(created.FormId, "수정", baseline.FormId, created.Layout with { Lines = 25 }, created.Options);
        Check(updated.FormId == created.FormId && updated.Layout.Lines == 25);
        var copy = await client.SaveFormAsync(null, "다른 이름", baseline.FormId, updated.Layout, updated.Options);
        Check(copy.FormId != created.FormId && copy.Layout == updated.Layout);
        await client.DeleteFormAsync(updated); var remaining = await client.FormsAsync();
        Check(remaining.Length == 2 && remaining.Single(f => f.Builtin) == baseline && remaining.Any(f => f.FormId == copy.FormId));
        var requests = handler.Requests;
        try { await client.DeleteFormAsync(baseline); throw new Exception("Deleted built-in form"); } catch (ArgumentException) { }
        Check(handler.Requests == requests);
    }
    private sealed class FormHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, object> forms = [];
        private int counter;
        public int Requests { get; private set; }
        public FormHandler() => forms["builtin:EXAM"] = new { formId = "builtin:EXAM", name = "시험지", builtin = true, baseFormId = (string?)null, layoutOptions = JobClient.LayoutBody(new()), brailleOptions = JobClient.OptionsBody(new()) };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; var path = request.RequestUri!.AbsolutePath; object? result;
            if (path.EndsWith("login")) result = new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" };
            else if (request.Method == HttpMethod.Get) result = new { items = forms.Values.ToArray() };
            else if (request.Method == HttpMethod.Delete) { forms.Remove(Uri.UnescapeDataString(path.Split('/')[^1])); result = null; }
            else
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                var id = request.Method == HttpMethod.Post ? "user:" + ++counter : Uri.UnescapeDataString(path.Split('/')[^1]);
                result = new { formId = id, name = body.GetProperty("name").GetString(), builtin = false, baseFormId = body.GetProperty("baseFormId").GetString(), layoutOptions = body.GetProperty("layoutOptions").Clone(), brailleOptions = body.GetProperty("brailleOptions").Clone() };
                forms[id] = result;
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public Uri? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request.RequestUri;
            object result = Last!.AbsolutePath.EndsWith("login") ? new { accessToken = "fixture", refreshToken = "refresh", role = "ROLE_USER" } :
                new { totalPages = 3, totalElements = 41, items = new object[] {
                    new { ruleId = "r1", source = "한국 점자 규정", section = "제1항", ruleName = "규정", contents = "요약", fullText = "첫 줄\n  ⠁⠃ 원문\n마지막 줄", supported = false },
                    new { ruleId = "r2", contents = "발췌" } } };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") });
        }
    }
}
