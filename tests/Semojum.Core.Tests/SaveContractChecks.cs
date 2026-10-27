using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

internal static class SaveContractChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("New repeated-text blocks keep distinct paired IDs through SMJ, repeated saves and undo", StableIds);
        yield return ("Partial save retry sends only failed frozen pages, omits truncation and records confirmed baseline", Retry);
        yield return ("Foreign SMJ copy updates persisted identity without capturing concurrent edits", Copy);
        yield return ("Save rejects contradictory counts, unknown pages and inconsistent per-page results", InvalidResults);
        yield return ("Memo author, replies, resolution and KST timestamps survive API and SMJ round trip", MemoRoundTrip);
        yield return ("Text jobs truncate trailing pages while PDF and unknown jobs never request truncation", TextPages);
        yield return ("SMJ v3 migrates stable IDs and legacy memos without changing authors", Legacy);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private static HttpResponseMessage Ok(object result) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Tokens() => Ok(new { accessToken = "test", refreshToken = "refresh", role = "ROLE_USER", alias = "현재 사용자" });
    private static DocumentSession Document()
    {
        var model = new DocumentSession { JobId = "original", OwnerLoginId = "me", IsTextJob = true };
        model.Add(new EditorBlock { Id = 1, Page = 1, Source = "같은 문장", Braille = "⠁", Metadata = new(SelectedIndex: 0) });
        model.Add(new EditorBlock { Id = 2, Page = 2, Source = "같은 문장", Braille = "⠃", Metadata = new(SelectedIndex: -1) });
        return model;
    }
    private static async Task StableIds()
    {
        var model = Document(); var snapshot = SmjFile.Snapshot(model);
        var ids = JobClient.PlanSave(snapshot.Manifest).Pages.Select(p => p.Text![0].Id).ToArray();
        True(ids[0] != ids[1]); True(ids.All(id => id!.Length <= 64));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, snapshot); var reopened = await SmjFile.LoadAsync(path);
            var plan = JobClient.PlanSave(SmjFile.Snapshot(reopened).Manifest);
            Equal(string.Join(",", ids), string.Join(",", plan.Pages.Select(p => p.Text![0].Id)));
            foreach (var page in plan.Pages) Equal(page.Text![0].Id, page.Braille![0].Id);
            Equal(-1, plan.Pages[1].Braille![0].SelectedIdx);
            JobClient.ApplySaved(reopened, new(2, [], null, plan));
            Equal(ids[0], reopened.Blocks[0].ServerId);
            var removed = reopened.Blocks[1]; reopened.Blocks.Remove(removed); reopened.Blocks.Add(removed);
            Equal(ids[1], JobClient.PlanSave(SmjFile.Snapshot(reopened).Manifest).Pages[1].Text![0].Id);
        }
        finally { File.Delete(path); }
    }
    private static async Task Retry()
    {
        var model = Document(); var frozen = SmjFile.Snapshot(model); var requests = new List<JsonElement>();
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Tokens();
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone(); requests.Add(body);
            return requests.Count == 1
                ? Ok(new { jobId = "original", savedPages = 1, failedPages = new[] { 2 }, pageResults = new[] { new { pageNo = 1, status = "SAVED", errorCode = (string?)null }, new { pageNo = 2, status = "FAILED", errorCode = (string?)"COMMON5000" } } })
                : Ok(new { jobId = "original", savedPages = 1, failedPages = Array.Empty<int>(), pageResults = new[] { new { pageNo = 2, status = "SAVED", errorCode = (string?)null } } });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("me", "pw"); var client = new JobClient(api);
        var first = await client.SaveAsync(frozen); True(!first.Complete); JobClient.ApplySaved(model, first);
        True(model.ServerPages.ContainsKey(1) && !model.ServerPages.ContainsKey(2));
        model.Blocks[1].Source = "저장 요청 뒤 편집";
        var second = await client.RetryFailedAsync(frozen, first); True(second.Complete); JobClient.ApplySaved(model, second);
        Equal(2, requests[0].GetProperty("totalPages").GetInt32()); True(!requests[1].TryGetProperty("totalPages", out _));
        Equal(1, requests[1].GetProperty("pages").GetArrayLength()); Equal(2, requests[1].GetProperty("pages")[0].GetProperty("pageNo").GetInt32());
        Equal("같은 문장", requests[1].GetProperty("pages")[0].GetProperty("text")[0].GetProperty("contents")[0].GetString());
        Equal("저장 요청 뒤 편집", model.Blocks[1].Source); True(model.ServerPages[2][0].HasBraille);
        Equal(2, requests.Count);
    }
    private static async Task Copy()
    {
        var model = Document(); model.OwnerLoginId = "original-author"; var frozen = SmjFile.Snapshot(model); var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Tokens());
            paths.Add(request.RequestUri.AbsolutePath);
            return Task.FromResult(Ok(new { jobId = "copy", copiedFromJobId = paths.Count == 1 ? "original" : null, savedPages = 2, failedPages = Array.Empty<int>() }));
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("me", "pw"); var client = new JobClient(api);
        var result = await client.SaveAsync(frozen); model.Blocks[0].Source = "동시 편집";
        JobClient.ApplySaved(model, result); var saved = JobClient.ApplySaved(frozen, result);
        Equal("copy", model.JobId); Equal("me", model.OwnerLoginId); Equal("같은 문장", saved.Manifest.Blocks[0].Source);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, saved); var loaded = await SmjFile.LoadAsync(path);
            Equal("copy", loaded.JobId); Equal("me", loaded.OwnerLoginId);
            await client.SaveAsync(SmjFile.Snapshot(loaded));
            Equal("/api/jobs/original/save,/api/jobs/copy/save", string.Join(",", paths));
        }
        finally { File.Delete(path); }
    }
    private static async Task InvalidResults()
    {
        object[] results = [
            new { savedPages = 1, failedPages = Array.Empty<int>() },
            new { savedPages = 1, failedPages = new[] { 99 } },
            new { savedPages = 0, failedPages = new[] { 1, 1 } },
            new { savedPages = 2, failedPages = Array.Empty<int>(), pageResults = new[] { new { pageNo = 1, status = "SAVED" }, new { pageNo = 1, status = "SAVED" } } },
            new { savedPages = 2, failedPages = Array.Empty<int>(), pageResults = new[] { new { pageNo = 1, status = "FAILED" }, new { pageNo = 2, status = "SAVED" } } },
            new { jobId = "unexpected-copy", savedPages = 2, failedPages = Array.Empty<int>() }
        ];
        foreach (var response in results)
        {
            using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens() : Ok(response))));
            var api = new SemojumApiClient(http); await api.LoginAsync("me", "pw");
            try { await new JobClient(api).SaveAsync(SmjFile.Snapshot(Document())); throw new Exception("Invalid save accepted"); }
            catch (InvalidDataException) { }
        }
    }
    private static async Task MemoRoundTrip()
    {
        using var json = JsonDocument.Parse("""
        {"status":"COMPLETED","original":{"type":"text"},"result":{"text_list":[{"id":"t1","contents":["검토"]}]},
        "memos":[{"memoId":null,"panel":"TEXT","elementId":"t1","lineIndex":0,"lineText":"검토","body":"원 메모","createdAt":"2026-10-21T10:00:00",
        "author":"원 작성자","resolved":true,"resolvedAt":"2026-10-21T12:00:00","replies":[{"author":"다른 검수자","body":"답글","createdAt":"2026-10-21T02:00:00Z"}]}]}
        """);
        var page = JobClient.ReadPage(json.RootElement, 1, 1); var model = new DocumentSession { JobId = "original", OwnerLoginId = "me" };
        model.Add(page.Blocks[0]); JobClient.Remember(model, page);
        var memo = model.Blocks[0].Memos[0]; Equal(TimeSpan.FromHours(9), memo.CreatedAt.Offset); True(memo.Resolved);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); var loaded = await SmjFile.LoadAsync(path);
            var saved = JobClient.PlanSave(SmjFile.Snapshot(loaded).Manifest).Pages[0].Memos![0];
            Equal("원 작성자", saved.Author); Equal("다른 검수자", saved.Replies![0].Author);
            Equal("2026-10-21T11:00:00", saved.Replies[0].CreatedAt); Equal("2026-10-21T12:00:00", saved.ResolvedAt); True(saved.Resolved);
            Equal("답글", loaded.Blocks[0].Memos[0].Replies![0].Body); True(loaded.IsTextJob == true);
        }
        finally { File.Delete(path); }
    }
    private static Task TextPages()
    {
        var model = Document(); model.ServerPages[3] = [new("old", true)];
        var plan = JobClient.PlanSave(SmjFile.Snapshot(model).Manifest); Equal(2, plan.TotalPages); Equal(2, plan.ExpectedPages);
        model.IsTextJob = false; plan = JobClient.PlanSave(SmjFile.Snapshot(model).Manifest);
        True(plan.TotalPages is null); Equal(0, plan.Pages.Single(p => p.PageNo == 3).Text!.Length);
        model.IsTextJob = null; True(JobClient.PlanSave(SmjFile.Snapshot(model).Manifest).TotalPages is null);
        return Task.CompletedTask;
    }
    private static async Task Legacy()
    {
        var snapshot = SmjFile.Snapshot(Document());
        var legacy = snapshot with { Manifest = snapshot.Manifest with { Version = 3, IsTextJob = null,
            Blocks = snapshot.Manifest.Blocks.Select(b => b with { ClientId = null, Memos = [new("legacy", null, "TEXT", 0, "", "기존 메모", DateTimeOffset.Now, true)] }).ToArray() } };
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, legacy); var loaded = await SmjFile.LoadAsync(path);
            var migrated = SmjFile.Snapshot(loaded); var id = migrated.Manifest.Blocks[0].ClientId;
            True(id is not null); True(migrated.Manifest.Blocks[0].Memos[0].Author is null);
            await SmjFile.SaveAsync(path, migrated); loaded = await SmjFile.LoadAsync(path);
            Equal(id, loaded.Blocks[0].ClientId); True(loaded.Blocks[0].Memos[0].Resolved);
        }
        finally { File.Delete(path); }
    }
    private static void True(bool condition) { if (!condition) throw new Exception("Expected true"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
