using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

internal static class RemainingChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Empty text job sends settings without source or AI input", EmptyTextJob);
        yield return ("Default options PUT retains visual choices, form and page row", Defaults);
        yield return ("Board edit preserves attachment IDs and never submits invalid prose or six attachments", Board);
        yield return ("Account preferences isolate six-dot mappings and snippets by account", Profiles);
        yield return ("Block merge retains bounding box and memo anchors; removed blocks stop dirtying session", Blocks);
        yield return ("Version comparison is numeric and missing metadata is allowed", Versions);
        yield return ("AI alternatives and rule evidence survive page parsing and SMJ persistence", Alternatives);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => send(r); }
    private static HttpResponseMessage Response(object? result) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Login() => Response(new { accessToken = "token", refreshToken = "refresh", role = "ROLE_USER" });
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static async Task Rejected(Func<Task> action) { try { await action(); } catch (ArgumentException) { return; } throw new Exception("Expected validation failure"); }
    private static async Task EmptyTextJob()
    {
        using var http = new HttpClient(new Handler(async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/auth/login") return Login();
            Check(r.Method == HttpMethod.Post && r.RequestUri.AbsolutePath == "/api/jobs/text");
            var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement;
            Check(!body.TryGetProperty("text", out _) && body.GetProperty("formId").GetString() == "builtin:EXAM");
            return Response(new { jobId = "empty", totalPages = 0, status = "COMPLETED" });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("user", "password");
        var created = await new JobClient(api).CreateTextAsync("새 문서", new(), formId: "builtin:EXAM");
        Check(created.TotalPages == 0 && created.Status == "COMPLETED");
    }
    private static async Task Defaults()
    {
        using var http = new HttpClient(new Handler(async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/auth/login") return Login();
            Check(r.Method == HttpMethod.Put);
            var body = JsonDocument.Parse(await r.Content!.ReadAsStringAsync()).RootElement;
            Check(body.GetProperty("visualDescriptions").GetProperty("figure").GetProperty("defaultOption").GetString() == "개조식");
            Check(body.GetProperty("pageNumberLine").GetString() == "every"); return Response(body);
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("user", "password");
        var saved = await new SettingsClient(api).SaveDefaultsAsync(new(new(EnglishGrade1: true, VisualDescriptions: VisualDescriptions.Defaults()), "builtin:EXAM", "every"));
        Check(saved.Options.EnglishGrade1 && saved.FormId == "builtin:EXAM");
        await Rejected(() => { VisualDescriptions.Validate(new Dictionary<string, VisualDescription> { ["table"] = new(["없는 설명"], "없는 설명") }); return Task.CompletedTask; });
    }
    private static async Task Board()
    {
        var posts = 0;
        using var http = new HttpClient(new Handler(async r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/auth/login") return Login();
            posts++; Check(r.Method == HttpMethod.Put); var body = await r.Content!.ReadAsStringAsync();
            Check(body.Contains("keepAttachmentIds") && body.Contains("42") && body.Contains("new.txt"));
            return Response(new { postId = 7, title = "제목", content = "본문", author = "user", mine = true, secret = true, hidden = false, createdAt = "2026-10-19", updatedAt = "2026-10-19", viewCount = 1, attachments = Array.Empty<object>(), answers = Array.Empty<object>() });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("user", "password"); var client = new BoardClient(api);
        await client.SaveAsync(7, "제목", "본문", true, [42], [new("new.txt", [1])]); Check(posts == 1);
        await Rejected(() => client.SaveAsync(null, "", "본문", false, [], []));
        await Rejected(() => client.SaveAsync(null, "제목", "본문", false, [1, 2, 3, 4, 5], [new("extra.txt", [1])])); Check(posts == 1);
    }
    private static async Task Profiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "semojum-profile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AccountPreferenceStore(directory); var snippets = Enumerable.Repeat("", 10).ToArray(); snippets[9] = "자주 쓰는 문장";
            await store.SaveAsync("first", new(SixDotKeys: "A S D J K L", TextSnippets: snippets));
            Check(store.Load("first").TextSnippets![9] == snippets[9] && store.Load("second").SixDotKeys == "F D S J K L");
            await Rejected(() => store.SaveAsync("first", new(BrailleSnippets: Enumerable.Repeat("한글", 10).ToArray())));
            Check(!File.ReadAllText(Directory.GetFiles(directory)[0]).Contains("password"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static Task Blocks()
    {
        var model = new DocumentSession();
        var a = new EditorBlock { Id = 1, Page = 1, Source = "가\n나", Braille = "⠫\n⠉", Metadata = new(BoundingBox: [10, 20, 30, 40]) };
        var b = new EditorBlock { Id = 2, Page = 1, Source = "다", Braille = "⠊", HasManualBraille = true, Metadata = new(BoundingBox: [2, 10, 50, 60]) };
        b.Memos.Add(new("m", null, "TEXT", 0, "다", "검수", DateTimeOffset.Now)); model.Add(a); model.Add(b);
        BlockEditing.Merge(model, a, b, true);
        Check(model.Blocks.Count == 1 && a.Source == "가\n나다" && a.HasManualBraille && a.Memos[0].LineIndex == 1);
        Check(a.Metadata.BoundingBox!.SequenceEqual([2d, 10d, 50d, 60d]));
        var version = model.ChangeVersion; b.Source = "제거된 블록"; Check(version == model.ChangeVersion);
        var added = BlockEditing.AddAfter(model, a); Check(model.ActiveBlock == added && added.ServerId is null);
        BlockEditing.Delete(model, model.Blocks.ToArray()); Check(model.Blocks.Count == 1 && model.ActiveBlock is not null);
        return Task.CompletedTask;
    }
    private static Task Versions()
    {
        var info = AppVersionInfo.Read(JsonSerializer.SerializeToElement(new { latestVersion = "1.10.0", minSupportedVersion = "1.1.0", downloadUrl = "https://semojum.app/update", releaseNotes = "변경" }))!;
        Check(info.Available(new(1, 9, 9)) && info.Required(new(1, 0, 0)) && !info.Required(new(1, 1, 0)));
        Check(AppVersionInfo.Read(JsonSerializer.SerializeToElement<object?>(null)) is null); return Task.CompletedTask;
    }
    private static async Task Alternatives()
    {
        var page = JobClient.ReadPage(JsonDocument.Parse("""
            {"status":"COMPLETED","result":{"text_list":[{"id":"x","type":"image","contents":["원문"],"selected_idx":1,
            "drafts":[{"label":"짧은 제목","text":"제목","tn_text":"<!주>제목<!/주>","reason":"주요 정보"}],
            "rule_trail":[{"rule_id":"r","source":"규정","section":"항","title":"명칭","excerpt":"발췌","line_no":0,"col_start":1,"col_end":3}]}]}}
            """).RootElement, 1, 1);
        var model = new DocumentSession(); model.Add(page.Blocks[0]); model.Blocks[0].Source = "고친 문장";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".smj");
        try
        {
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); var loaded = await SmjFile.LoadAsync(path);
            var metadata = loaded.Blocks[0].Metadata;
            Check(metadata.OriginalSource == "원문" && metadata.Alternatives![0].TaggedText == "<!주>제목<!/주>" && metadata.Rules![0].RuleId == "r");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
