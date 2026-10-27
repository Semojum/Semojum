using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

/// <summary>Client-side checks for the C-01..C-04 handoff items against the 2026-10-21 V3.1 contract (fake HTTP, not production).</summary>
internal static class SyncChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Save plan keeps blocked blocks and memos, skips placeholders, empties deleted pages and pairs the first braille by stable id", SavePlan);
        yield return ("Save accepts stable new block ids, rejects duplicate ids and confirms only accepted pages", SaveGuards);
        yield return ("Job status separates finished, succeeded, failed and remaining pages without double counting", Progress);
        yield return ("Quality report survives page parsing, unknown types, unlinked ids and SMJ v3 round trip", QualityReport);
        yield return ("Layout values the server cannot keep are omitted and reported instead of rewritten", LayoutContract);
    }

    private static HttpResponseMessage Ok(object? result) => new(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Tokens() => Ok(new { accessToken = "first", refreshToken = "refresh", role = "ROLE_USER" });
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private static DocumentSession MixedDocument()
    {
        var session = new DocumentSession { JobId = "job_mixed", OwnerLoginId = "test" };
        // Page 1: an editable block with server braille, a blocked block (server, no braille) carrying a memo and an edited source.
        var normal = new EditorBlock { Id = 1, Page = 1, ServerId = "t1", Source = "정상 원문", Braille = "⠫" };
        var blocked = new EditorBlock { Id = 2, Page = 1, ServerId = "t2", Source = "", Metadata = new(IsBlocked: true) };
        blocked.Memos.Add(new("m1", 7, "TEXT", 0, "", "차단 블록 메모", new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(9)), Resolved: true));
        session.Add(normal); session.Add(blocked);
        session.ServerPages[1] = [new("t1", true), new("t2", false)];
        // Page 2: entirely blocked.
        session.Add(new EditorBlock { Id = 3, Page = 2, ServerId = "t3", Source = "차단", Metadata = new(IsBlocked: true) });
        session.ServerPages[2] = [new("t3", false)];
        // Page 3: only the app's failure placeholder (no server id); its memo cannot be sent.
        var placeholder = new EditorBlock { Id = 4, Page = 3, Heading = "변환 실패 · 원본 확인", Metadata = new(IsBlocked: true) };
        placeholder.Memos.Add(new("m2", null, "TEXT", 0, "", "실패 쪽 메모", DateTimeOffset.Now));
        session.Add(placeholder); session.ServerPages[3] = [];
        // Page 4: the server held a braille-bearing block that was deleted locally.
        session.ServerPages[4] = [new("t4", true)];
        // Page 6: block known from an older .smj without server records; locally translated braille.
        session.Add(new EditorBlock { Id = 6, Page = 6, ServerId = "t6", Source = "기록 없음", Braille = "⠁" });
        return session;
    }

    private static async Task SavePlan()
    {
        var session = MixedDocument();
        var plan = JobClient.PlanSave(SmjFile.Snapshot(session).Manifest);
        Equal("1,2,4,6", string.Join(",", plan.Pages.Select(p => p.PageNo)));
        var first = plan.Pages[0];
        Equal("t1,t2", string.Join(",", first.Text!.Select(t => t.Id)));
        Equal(0, first.Text![1].Contents.Length);
        Equal("t1,t2", string.Join(",", first.Braille!.Select(b => b.Id))); Equal("⠫", first.Braille![0].Contents[0]);
        Equal("t2", first.Memos!.Single().ElementId); Equal(7L, first.Memos![0].MemoId); Equal("2026-10-14T12:00:00", first.Memos![0].CreatedAt);
        True(plan.Pages[1].Text!.Single().Id == "t3" && plan.Pages[1].Braille!.Single().Id == "t3" && plan.Pages[1].Memos!.Length == 0);
        var emptied = plan.Pages[2];
        True(emptied.Text!.Length == 0 && emptied.Braille!.Length == 0 && emptied.Memos!.Length == 0);
        Equal("t6", plan.Pages[3].Braille!.Single().Id);
        Equal(1, plan.EmptiedPages); Equal(1, plan.SkippedPlaceholderPages); Equal(0, plan.LocalBrailleBlocks); Equal(0, plan.LocalMemoStates);
        True(plan.LocalOnly.Any(n => n.Contains("실패 표시 블록의 메모")));
        // The request body carries null for untouched panels and camelCase fields the server reads.
        string? body = null;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Tokens();
            body = await request.Content!.ReadAsStringAsync();
            return Ok(new { savedPages = 3, failedPages = new[] { 4 }, savedAt = "2026-10-20T01:00:00" });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        var result = await new JobClient(api).SaveAsync(SmjFile.Snapshot(session));
        using var json = JsonDocument.Parse(body!);
        var pages = json.RootElement.GetProperty("pages");
        Equal(4, pages.GetArrayLength());
        Equal("t3", pages[1].GetProperty("braille")[0].GetProperty("id").GetString());
        Equal("t2", pages[0].GetProperty("memos")[0].GetProperty("elementId").GetString());
        True(pages[0].GetProperty("memos")[0].GetProperty("resolved").GetBoolean());
        True(!result.Complete); Equal(4, result.Plan.ExpectedPages);
        JobClient.ApplySaved(session, result);
        Equal("t1:True,t2:True", string.Join(",", session.ServerPages[1].Select(r => $"{r.Id}:{r.HasBraille}")));
        Equal("t4:True", string.Join(",", session.ServerPages[4].Select(r => $"{r.Id}:{r.HasBraille}")));
        Equal("t6:True", string.Join(",", session.ServerPages[6].Select(r => $"{r.Id}:{r.HasBraille}")));
        // A fully successful retry of the emptied page clears its record.
        JobClient.ApplySaved(session, result with { SavedPages = 4, FailedPages = [] });
        Equal(0, session.ServerPages[4].Length);
    }

    private static async Task SaveGuards()
    {
        var session = MixedDocument();
        session.Add(new EditorBlock { Id = 9, Page = 5, Source = "새 블록" });
        var fresh = JobClient.PlanSave(SmjFile.Snapshot(session).Manifest);
        var added = fresh.Pages.Single(p => p.PageNo == 5);
        Equal(added.Text![0].Id, added.Braille![0].Id);
        True(added.Text[0].Id!.StartsWith("app_"));
        session.Blocks.Remove(session.Blocks.Single(b => b.Id == 9));
        session.Add(new EditorBlock { Id = 10, Page = 1, ServerId = "t1", Source = "중복" });
        var duplicate = await Rejected(() => Task.FromResult(JobClient.PlanSave(SmjFile.Snapshot(session).Manifest)));
        True(duplicate.Message.Contains("중복"));
        // Neither guard reaches the network.
        var calls = 0;
        using var http = new HttpClient(new Handler(request => { if (!request.RequestUri!.AbsolutePath.EndsWith("login")) calls++; return Task.FromResult(Tokens()); }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        await Rejected(() => new JobClient(api).SaveAsync(SmjFile.Snapshot(session)));
        Equal(0, calls);
        // A page count that only matches the sent pages counts as complete; local-only items stay reported.
        var ok = new JobSaveResult(2, [], null, new([new(1, [], null, []), new(2, [], null, [])], ["note"], 0, 0, 0, 0));
        True(ok.Complete && ok.Plan.LocalOnly.Length == 1);
        True(!(ok with { SavedPages = 1 }).Complete);
    }

    private static async Task Progress()
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens()
            : Ok(new { totalPages = 10, completedPages = 3, overallStatus = "IN_PROGRESS", pages = new Dictionary<string, string>
            {
                ["page:1"] = "COMPLETED", ["page:2"] = "NEEDS_REVIEW", ["page:3"] = "BLOCKED", ["page:4"] = "RUNNING",
                ["page:5"] = "PENDING", ["page:6"] = "PENDING", ["page:7"] = "PENDING", ["page:8"] = "PENDING", ["page:9"] = "PENDING", ["page:10"] = "PENDING"
            } }))));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        var status = await new JobClient(api).StatusAsync("job_progress");
        Equal(3, status.FinishedPages); Equal(2, status.SucceededPages); Equal(1, status.FailedPages); Equal(1, status.RunningPages); Equal(7, status.RemainingPages);
        var progress = status.ToProgress("");
        Equal(3, progress.Finished); Equal(7, progress.Remaining);
        Equal("10쪽 중 3쪽 처리 · 성공 2 · 확인 필요 1 · 변환 중 1", progress.Label);
        True(!status.IsTerminal); Equal("변환 중", status.Label);
        // Without a page map the server's finished count is split by the known failures only.
        var bare = new JobStatus("j", 10, 3, "IN_PROGRESS", []);
        Equal(3, bare.SucceededPages); Equal(0, bare.FailedPages); Equal(3, bare.FinishedPages);
        // All pages blocked after cancel: finished equals total, nothing succeeded, terminal.
        var cancelled = new JobStatus("j", 2, 2, "FAILED", new() { [1] = "BLOCKED", [2] = "BLOCKED" });
        Equal(2, cancelled.FinishedPages); Equal(0, cancelled.SucceededPages); Equal(0, cancelled.RemainingPages); True(cancelled.IsTerminal);
        // Finished never exceeds the total even if the map is ahead of total_pages.
        Equal(1, new JobStatus("j", 1, 1, "COMPLETED", new() { [1] = "COMPLETED", [2] = "COMPLETED" }).FinishedPages);
    }

    private static async Task QualityReport()
    {
        using var doc = JsonDocument.Parse("""
        {"status":"NEEDS_REVIEW","result":{"text_list":[{"id":"t1","contents":["원문"]},{"id":"t2","contents":["차단"],"is_blocked":true}],
          "braille_text_list":[{"id":"t1","contents":["⠁"]}],
          "quality_report":{"critical_errors":[{"type":"unsupported_char","element_id":"t1","message":"지원하지 않는 문자"}],
            "review_flags":[{"type":"low_confidence","element_id":"deleted","message":"신뢰도 낮음"},{"element_id":null,"message":"쪽 전체"}]}}}
        """);
        var page = JobClient.ReadPage(doc.RootElement, 3, 1);
        Equal(3, page.Quality!.Length);
        Equal("error", page.Quality[0].Severity); Equal("t1", page.Quality[0].ElementId);
        Equal("review", page.Quality[1].Severity); Equal("deleted", page.Quality[1].ElementId);
        Equal("unknown", page.Quality[2].Type); True(page.Quality[2].ElementId is null);
        Equal("t1:True,t2:False", string.Join(",", page.ServerBlocks!.Select(r => $"{r.Id}:{r.HasBraille}")));
        var model = new DocumentSession { JobId = "job_q", OwnerLoginId = "test" };
        foreach (var block in page.Blocks) model.Add(block);
        JobClient.Remember(model, page); JobClient.Remember(model, page); // repeated reads replace, never duplicate
        Equal(3, model.QualityReports[3].Length);
        using var empty = JsonDocument.Parse("""{"status":"COMPLETED","result":{"text_list":[{"id":"t9","contents":["x"]}],"quality_report":{"critical_errors":[],"review_flags":[]}}}""");
        JobClient.Remember(model, JobClient.ReadPage(empty.RootElement, 4, 10));
        Equal(0, model.QualityReports[4].Length);
        using var none = JsonDocument.Parse("""{"status":"COMPLETED","result":{"text_list":[{"id":"t8","contents":["x"]}]}}""");
        Equal(0, JobClient.ReadPage(none.RootElement, 5, 20).Quality!.Length);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            var snapshot = SmjFile.Snapshot(model); Equal(SmjFile.Version, snapshot.Manifest.Version);
            await SmjFile.SaveAsync(path, snapshot); var loaded = await SmjFile.LoadAsync(path);
            Equal(3, loaded.QualityReports[3].Length); Equal("deleted", loaded.QualityReports[3][1].ElementId);
            Equal("t1:True,t2:False", string.Join(",", loaded.ServerPages[3].Select(r => $"{r.Id}:{r.HasBraille}")));
            // v2 readers would not know these fields; a v2 file still opens and plans without server records.
            var legacy = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(snapshot.Manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
            legacy["version"] = 2; legacy.AsObject().Remove("serverPages"); legacy.AsObject().Remove("qualityReports");
            File.Delete(path);
            using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) await writer.WriteAsync(legacy.ToJsonString());
            var old = await SmjFile.LoadAsync(path);
            Equal(0, old.ServerPages.Count); Equal(0, old.QualityReports.Count);
            var plan = JobClient.PlanSave(SmjFile.Snapshot(old).Manifest);
            Equal("t1,t2", string.Join(",", plan.Pages.SelectMany(p => p.Braille!).Select(b => b.Id)));
            Equal(0, plan.LocalBrailleBlocks);
        }
        finally { File.Delete(path); }
    }

    private static async Task LayoutContract()
    {
        var even = JobClient.LayoutBody(new PageLayout(PageNumbers: "짝수 페이지"));
        True(!even.ContainsKey("pageNumberLine"));
        Equal("none", JobClient.LayoutBody(new PageLayout(PageNumbers: "출력 안 함"))["pageNumberLine"]);
        Equal("every", JobClient.LayoutBody(new PageLayout(PageNumbers: "모든 페이지"))["pageNumberLine"]);
        using var doc = JsonDocument.Parse("""{"pageNumberLine":"even"}""");
        Equal("짝수 페이지", JobClient.ReadLayout(doc.RootElement).PageNumbers);
        Equal(0, ServerContract.LocalOnlyLayout(new PageLayout(), []).Length);
        // BE 2026-10-27: footerPages odd/every round-trips; other footer policies and the rest stay local.
        Equal("every", JobClient.LayoutBody(new PageLayout(Footer: "모든 페이지"))["footerPages"]);
        True(!JobClient.LayoutBody(new PageLayout(Footer: "짝수 페이지")).ContainsKey("footerPages"));
        using var footerDoc = JsonDocument.Parse("""{"footerPages":"every"}"""); Equal("모든 페이지", JobClient.ReadLayout(footerDoc.RootElement).Footer);
        var notes = ServerContract.LocalOnlyLayout(new PageLayout(PageNumbers: "짝수 페이지", Footer: "모든 페이지", PadToEven: true, NumberRanges: PageNumberRange.Parse("1-3")), [new(2, null, "단원")]);
        Equal(4, notes.Length); True(notes.All(n => n.Contains(".smj")));
        Equal(5, ServerContract.LocalOnlyLayout(new PageLayout(PageNumbers: "짝수 페이지", Footer: "짝수 페이지", PadToEven: true, NumberRanges: PageNumberRange.Parse("1-3")), [new(2, null, "단원")]).Length);
        // Forms are rejected before any request; a supported layout still reaches the server.
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Tokens());
            calls++;
            return Task.FromResult(Ok(new { formId = "f1", name = "양식", builtin = false, layoutOptions = new { pageNumberLine = "every" }, brailleOptions = new { } }));
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw"); var client = new SettingsClient(api);
        var rejected = await Rejected<ArgumentException>(() => client.SaveFormAsync(null, "양식", null, new PageLayout(PageNumbers: "짝수 페이지", PadToEven: true), new()));
        True(rejected.Message.Contains("짝수") && rejected.Message.Contains("보정")); Equal(0, calls);
        var saved = await client.SaveFormAsync(null, "양식", null, new PageLayout(PageNumbers: "모든 페이지"), new());
        Equal("모든 페이지", saved.Layout.PageNumbers); Equal(1, calls);
    }

    private static Task<InvalidOperationException> Rejected(Func<Task> action) => Rejected<InvalidOperationException>(action);
    private static async Task<T> Rejected<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T ex) { return ex; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void True(bool value) { if (!value) throw new Exception("Expected true"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
