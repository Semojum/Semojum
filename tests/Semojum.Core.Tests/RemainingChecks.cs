using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

internal static class RemainingChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Editing grid preserves text and reversible wrapped cell coordinates", GridCoordinates);
        yield return ("Long TXT chunks preserve all characters and Unicode boundaries", TextChunks);
        yield return ("Portable update rejects traversal, duplicate paths and incomplete packages atomically", Updates);
        yield return ("Slow translation locks braille at 400ms and unlocks when the current result arrives", DelayedTranslation);
        yield return ("Partial cross-block replacement preserves unselected text, original bounds and memo provenance", PartialRanges);
        yield return ("Table grid round trips cells and rejects nested structural markers", Tables);
        yield return ("Work file writer lease excludes a second writer and releases on close", Leases);
        yield return ("Resolved memo state survives explicit SMJ save", ResolvedMemos);
        yield return ("SSE refreshes rejected access and parses multiline events without duplicated content", Events);
        yield return ("Appearance rejects invalid colors, nonfinite values and manual correction color", Appearance);
        yield return ("Empty text job sends settings without source or AI input", EmptyTextJob);
        yield return ("Default options PUT retains visual choices, form and page row", Defaults);
        yield return ("Board edit preserves attachment IDs and never submits invalid prose or six attachments", Board);
        yield return ("Account preferences isolate six-dot mappings and snippets by account", Profiles);
        yield return ("Block merge retains bounding box and memo anchors; removed blocks stop dirtying session", Blocks);
        yield return ("Version comparison is numeric and missing metadata is allowed", Versions);
        yield return ("AI alternatives and rule evidence survive page parsing and SMJ persistence", Alternatives);
    }
    private static async Task PartialRanges()
    {
        var document = new DocumentSession();
        var a = new EditorBlock { Id = 1, Page = 1, Source = "가나다", Braille = "⠁⠃⠉", Metadata = new(BoundingBox: [0, 0, 10, 10]) };
        var b = new EditorBlock { Id = 2, Page = 1, Source = "라마바", Braille = "⠙⠑⠋", Metadata = new(BoundingBox: [20, 20, 30, 30]) };
        b.Memos.Add(new("m", null, "TEXT", 0, "라마바", "원문 메모", DateTimeOffset.Now)); document.Add(a); document.Add(b);
        var spans = BlockRange.Select(document.Blocks.ToArray(), SearchPanel.Text, 1, 4);
        BlockRange.Replace(document, spans, SearchPanel.Text, "대체");
        Check(document.Blocks.Count == 1 && a.Source == "가대체마바" && a.Metadata.OriginalSource == "가나다라마바" && a.Metadata.BoundingBox![2] == 30 && a.Memos.Single().LineText == "라마바");
        var old = a.Source; document.IsReadOnly = true;
        await Rejected(() => { BlockRange.Replace(document, [new(a, 0, 1)], SearchPanel.Text, "삭제"); return Task.CompletedTask; }); Check(a.Source == old);
        document.IsReadOnly = false; a.IsBrailleLocked = true;
        await Rejected(() => { BlockRange.Replace(document, [new(a, 0, 1)], SearchPanel.Braille, "⠿"); return Task.CompletedTask; });
    }
    private sealed class DelayedEngine : ITranslationEngine
    {
        public TaskCompletionSource<string> Completion { get; } = new();
        public Task<string> TranslateAsync(string text, int id, CancellationToken ct) => Completion.Task;
    }
    private static async Task DelayedTranslation()
    {
        var engine = new DelayedEngine(); var block = new EditorBlock { Source = "가", Braille = "⠿" };
        var pending = new TranslationCoordinator(engine).TranslateAsync(block, ManualEditDecision.Overwrite, TimeSpan.FromSeconds(3));
        await Task.Delay(550); Check(block.IsBrailleLocked && block.Braille == "⠿");
        engine.Completion.SetResult("⠁"); Check(await pending == TranslationOutcome.Applied && !block.IsBrailleLocked && block.Braille == "⠁");
    }
    private static Task GridCoordinates()
    {
        var first = new EditorBlock { Id = 1, Page = 1, Braille = "⠁⠃⠉⠙⠑⠋⠛⠓⠊\n\n⠚" };
        var second = new EditorBlock { Id = 2, Page = 2, Braille = "⠿" };
        var original = first.Braille; var faces = BrailleGrid.Project([first, second], new(Columns: 8, Lines: 4));
        Check(faces.Count == 2 && first.Braille == original);
        Check(faces[0].Rows[1][0] is { Offset: 8, Value: '⠊' });
        Check(faces[0].Rows[2][0] is { Offset: 10, Value: '⠀' });
        Check(faces[1].Rows[0][0]?.Block == second);
        var exact = BrailleGrid.Project([new() { Braille = "⠁⠁⠁⠁⠁⠁⠁⠁\n⠃" }], new(Columns: 8, Lines: 4));
        Check(exact[0].Rows[1][0] is { Offset: 9, Value: '⠃' });
        return Task.CompletedTask;
    }
    private static Task TextChunks()
    {
        var text = new string('가', 999) + "😀\r\n" + new string('나', 2001);
        var chunks = TextImport.Split(text);
        Check(string.Concat(chunks) == text && chunks.All(s => s.Length <= 1000 && !char.IsHighSurrogate(s[^1]) && !char.IsLowSurrogate(s[0])));
        var tagged = "<!수식>" + new string('x', 1100) + "<!/수식>";
        Check(TextImport.Split(tagged).SequenceEqual(new[] { tagged })); return Task.CompletedTask;
    }
    private static async Task Updates()
    {
        var root = Path.Combine(Path.GetTempPath(), "semojum-update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            foreach (var paths in new[] { new[] { "../escape.txt" }, new[] { "/absolute" }, new[] { "a.txt", "A.txt" }, new[] { "partial.txt" } })
            {
                var zipPath = Path.Combine(root, "test.zip");
                using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
                    foreach (var path in paths) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write("data"); }
                var rejected = false;
                try { await PortableUpdate.StageAsync(zipPath, Path.Combine(root, "versions"), new(1, 0, 1)); }
                catch (InvalidDataException) { rejected = true; }
                Check(rejected && !File.Exists(Path.Combine(root, "versions", "pending.json")) && !File.Exists(Path.Combine(root, "escape.txt")));
                Check(!Directory.EnumerateDirectories(Path.Combine(root, "versions"), ".staging-*").Any());
                File.Delete(zipPath);
            }
        }
        finally { Directory.Delete(root, true); }
    }
    private static async Task Tables()
    {
        var text = TaggedTable.Write(new[] { new[] { "머리", "열" }, new[] { "<!강조>값<!/강조>", "" } });
        var rows = TaggedTable.Read(text); Check(rows.Length == 2 && rows[1][0] == "<!강조>값<!/강조>" && rows[1][1] == "");
        await Rejected(() => { TaggedTable.Write(new[] { new[] { "<!행>오류" } }); return Task.CompletedTask; });
        await Rejected(() => { TaggedTable.Read("<!표>잘못된 표<!/표>"); return Task.CompletedTask; });
    }
    private static Task Leases()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".smj");
        using (var first = WorkFileLease.TryAcquire(path)) { Check(first is not null); using var second = WorkFileLease.TryAcquire(path); Check(second is null); }
        using var third = WorkFileLease.TryAcquire(path); Check(third is not null); return Task.CompletedTask;
    }
    private static async Task ResolvedMemos()
    {
        var model = new DocumentSession(); var block = new EditorBlock { Id = 1, Source = "문장", Page = 1 };
        block.Memos.Add(new("memo", 12, "TEXT", 0, "문장", "해결 메모", DateTimeOffset.Now, true)); model.Add(block);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".smj");
        try { await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); Check((await SmjFile.LoadAsync(path)).Blocks[0].Memos[0].Resolved); }
        finally { File.Delete(path); }
    }
    private static async Task Events()
    {
        var attempts = 0; var refreshed = 0;
        using var http = new HttpClient(new Handler(r =>
        {
            if (r.RequestUri!.AbsolutePath == "/api/auth/login") return Task.FromResult(Login());
            if (r.RequestUri.AbsolutePath == "/api/auth/refresh") { refreshed++; Check(r.Headers.Authorization is null); return Task.FromResult(Response(new { accessToken = "fresh" })); }
            Check(r.Headers.Accept.Any(h => h.MediaType == "text/event-stream")); attempts++;
            if (attempts == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"code\":\"AUTH4003\"}") });
            Check(r.Headers.Authorization?.Parameter == "fresh");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(": heartbeat\nevent: page_done\ndata: {\n data ignored\ndata: \"page_no\":1}\n\nevent: unknown\ndata: {}\n\nevent: job_done\ndata: {\"total_pages\":1}\n\n", Encoding.UTF8, "text/event-stream") });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("user", "password");
        var events = new List<JobEvent>(); await foreach (var item in api.EventsAsync("test")) events.Add(item);
        Check(events.Count == 2 && events[0].Data.GetProperty("page_no").GetInt32() == 1 && events[1].Name == "job_done" && attempts == 2 && refreshed == 1);
    }
    private static async Task Appearance()
    {
        foreach (var appearance in new[] { new EditorAppearance(TextColor: "red"), new EditorAppearance(BrailleColor: "#b22222"), new EditorAppearance(LineSpacing: double.NaN), new EditorAppearance(RecentCount: 51) })
            await Rejected(() => { AccountPreferenceStore.Validate(new(Appearance: appearance)); return Task.CompletedTask; });
        AccountPreferenceStore.Validate(new(Appearance: new(HighContrast: true, RecentCount: 50, CursorWidth: 8)));
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
