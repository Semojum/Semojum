using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

internal static class IntegrationChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Sidecar Unicode, concurrent requests, stderr drain and remote error", SidecarRoundTrip);
        yield return ("Sidecar timeout and cancellation restart before next request", SidecarRecovery);
        yield return ("Sidecar process exit and invalid response ID discard the process", SidecarFailure);
        yield return ("API concurrent expiry refreshes once without Bearer on refresh", ApiRefresh);
        yield return ("API revoked session clears credentials without replay", ApiRevoked);
        yield return ("API errors retain lockout details and handle HTML 413", ApiErrors);
        yield return ("API network failure does not replay a save", ApiNoReplay);
        yield return ("V3.1 page maps IDs, multiline contents, metadata and memos", JobPageMapping);
        yield return ("Job save freezes lines and reports partial failure without replay", JobSave);
        yield return ("Multipart uploads use repeated file parts and reject mixed documents", JobUpload);
        yield return ("SMJ round trip preserves original, options, table metadata and manual edits", SmjRoundTrip);
        yield return ("SMJ cancellation preserves prior file and foreign version is rejected", SmjFailure);
        yield return ("Recent files stay ordered, capped, deduplicated and separate from document contents", WorkspacePreferences);
        yield return ("Work folder lists only SMJ files and explicit subdirectories", WorkspaceFolder);
        yield return ("SMJ restores saved block and carets without treating navigation as editing", SavedCarets);
        yield return ("Braille folding respects syllables, empty lines and note indicators", Folding);
        yield return ("Tags distinguish pairs, singletons and layout-only footer", Tags);
        if (Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_PYTHON") is { Length: > 0 } python)
            yield return ("Real v3.4.24 Korean grades, table drafts, review flags and BRF round trip", () => RealEngine(python));
        else Console.WriteLine("SKIP real engine: set SEMOJUM_ENGINE_PYTHON to the v3.4.24 Python executable");
        if (Environment.GetEnvironmentVariable("SEMOJUM_ENGINE_EXECUTABLE") is { Length: > 0 } executable)
            yield return ("Packaged engine grades, table drafts and BRF round trip", () => RealEngine(executable, true));
    }

    private static BrailleSidecar Fixture()
    {
        var executable = Environment.ProcessPath!;
        var arguments = new List<string>();
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet") arguments.Add(Assembly.GetExecutingAssembly().Location);
        arguments.Add("--sidecar-fixture");
        return new(new(executable, arguments));
    }

    public static async Task<int> RunFixtureAsync()
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var json = JsonDocument.Parse(line); var root = json.RootElement;
            var id = root.GetProperty("id").GetInt64(); var op = root.GetProperty("op").GetString();
            if (op == "exit") return 0;
            if (op == "hang") await Task.Delay(30000);
            if (op == "stderr") await Console.Error.WriteAsync(new string('x', 128 * 1024));
            object result = op switch
            {
                "ping" => new { id, ok = true, version = "fixture", protocol = 1 },
                "wrong_id" => new { id = -1 },
                "error" => new { id, error = "수식 변환 불가", code = "BAD_LATEX" },
                _ => new { id, text = root.TryGetProperty("text", out var text) ? text.GetString() : "응답" }
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(result)); await Console.Out.FlushAsync();
        }
        return 0;
    }

    private static async Task SidecarRoundTrip()
    {
        await using var sidecar = Fixture();
        var requests = Enumerable.Range(0, 8).Select(i => sidecar.InvokeAsync("echo", new { text = $"한글 ⠿ 😀\n{i}" })).ToArray();
        var responses = await Task.WhenAll(requests);
        for (var i = 0; i < responses.Length; i++) Equal($"한글 ⠿ 😀\n{i}", responses[i].GetProperty("text").GetString());
        await sidecar.InvokeAsync("stderr");
        var error = await Reject<SidecarException>(() => sidecar.InvokeAsync("error")); Equal("BAD_LATEX", error.Code);
        Equal("응답", (await sidecar.InvokeAsync("echo")).GetProperty("text").GetString());
    }

    private static async Task SidecarRecovery()
    {
        await using var sidecar = Fixture(); await sidecar.InvokeAsync("echo");
        await Reject<TimeoutException>(() => sidecar.InvokeAsync("hang", timeout: TimeSpan.FromMilliseconds(30)));
        Equal("응답", (await sidecar.InvokeAsync("echo")).GetProperty("text").GetString());
        using var cancel = new CancellationTokenSource(30);
        await Reject<OperationCanceledException>(() => sidecar.InvokeAsync("hang", cancellationToken: cancel.Token));
        Equal("응답", (await sidecar.InvokeAsync("echo")).GetProperty("text").GetString());
    }

    private static async Task SidecarFailure()
    {
        await using var sidecar = Fixture();
        await Reject<IOException>(() => sidecar.InvokeAsync("exit"));
        await sidecar.InvokeAsync("echo");
        await Reject<IOException>(() => sidecar.InvokeAsync("wrong_id"));
        await sidecar.InvokeAsync("echo");
    }

    private static HttpResponseMessage Response(HttpStatusCode status, object body) => new(status)
        { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Ok(object? result) => Response(HttpStatusCode.OK, new { isSuccess = true, code = "COMMON2000", result });
    private static HttpResponseMessage Tokens() => Ok(new { accessToken = "first", refreshToken = "refresh", role = "ROLE_USER" });
    private static HttpResponseMessage Expired() => Response(HttpStatusCode.Unauthorized, new { isSuccess = false, code = "AUTH4003", message = "만료" });
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private static async Task ApiRefresh()
    {
        var refreshes = 0; var expired = 0;
        var bothExpired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("login")) { True(request.Headers.Authorization is null); return Tokens(); }
            if (path.EndsWith("refresh"))
            {
                Interlocked.Increment(ref refreshes); True(request.Headers.Authorization is null);
                Equal("{\"refreshToken\":\"refresh\"}", await request.Content!.ReadAsStringAsync());
                return Ok(new { accessToken = "new" });
            }
            if (request.Headers.Authorization?.Parameter == "first")
            {
                if (Interlocked.Increment(ref expired) == 2) bothExpired.SetResult();
                await bothExpired.Task; return Expired();
            }
            Equal("new", request.Headers.Authorization?.Parameter); return Ok(new { value = 1 });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        await Task.WhenAll(api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage"), api.SendAuthenticatedAsync(HttpMethod.Get, "/api/rules"));
        Equal(1, refreshes);
    }

    private static async Task ApiRevoked()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++; return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login") ? Tokens() : Expired());
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        await Reject<SemojumApiException>(() => api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs/test/save", new { pages = Array.Empty<object>() }));
        True(api.Account is null); Equal(3, calls);
    }

    private static async Task ApiErrors()
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login")
            ? Response(HttpStatusCode.TooManyRequests, new { isSuccess = false, code = "AUTH4006", message = "잠김", result = new { unlockAt = "2026-10-14T13:10:00" } })
            : new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge) { Content = new StringContent("<html>413</html>") })));
        var api = new SemojumApiClient(http);
        var error = await Reject<SemojumApiException>(() => api.LoginAsync("test", "pw"));
        Equal("AUTH4006", error.Code); Equal("2026-10-14T13:10:00", error.Details!.Value.GetProperty("unlockAt").GetString());
        error = await Reject<SemojumApiException>(() => api.GetPublicAsync("/api/public/notices")); Equal(HttpStatusCode.RequestEntityTooLarge, error.Status);
    }

    private static async Task ApiNoReplay()
    {
        var saves = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Task.FromResult(Tokens());
            saves++; throw new HttpRequestException("connection lost after write");
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw");
        await Reject<HttpRequestException>(() => api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs/test/save", new { pages = Array.Empty<object>() }));
        Equal(1, saves);
    }

    private static Task JobPageMapping()
    {
        using var doc = JsonDocument.Parse("""
        {"status":"COMPLETED","result":{"image_resolution":{"width":1000,"height":2000},
          "text_list":[{"id":"t1","type":"table","contents":["첫 줄\n둘째 줄"],"heading_level":null,"selected_idx":null,"table_source":"표 구조","math_page":true}],
          "braille_text_list":[],"bounding_box_list":[{"id":"t1","x":2,"y":3,"x2":40,"y2":60}]},
          "original":{"type":"image","url":"https://example.com/page.jpg"},
          "memos":[{"memoId":4,"panel":"TEXT","elementId":"t1","lineIndex":1,"lineText":"둘째 줄","body":"검토","createdAt":"2026-10-14T12:00:00+09:00"}]}
        """);
        var page = JobClient.ReadPage(doc.RootElement, 2, 7);
        Equal(1, page.Blocks.Length); Equal(7, page.Blocks[0].Id); Equal("t1", page.Blocks[0].ServerId);
        Equal("첫 줄\n둘째 줄", page.Blocks[0].Source); Equal("", page.Blocks[0].Braille);
        True(page.Blocks[0].Metadata.MathPage); Equal("표 구조", page.Blocks[0].Metadata.TableSource);
        Equal(1, page.Blocks[0].Memos[0].LineIndex); Equal(1000, page.Width);
        using var opts = JsonDocument.Parse("""{"englishGrade":"grade1","visualDescriptions":{"figure":{"options":["개조식"],"defaultOption":"개조식"}}}""");
        var settings = JobClient.ReadOptions(opts.RootElement);
        True(settings.EnglishGrade1); Equal("개조식", settings.VisualDescriptions!["figure"].DefaultOption);
        return Task.CompletedTask;
    }

    private static async Task JobSave()
    {
        var saves = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Tokens();
            saves++;
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var page = json.RootElement.GetProperty("pages")[0];
            Equal("old", page.GetProperty("text")[0].GetProperty("contents")[0].GetString());
            Equal("second", page.GetProperty("text")[0].GetProperty("contents")[1].GetString());
            Equal("t1", page.GetProperty("braille")[0].GetProperty("id").GetString());
            True(!page.GetProperty("text")[0].TryGetProperty("type", out _));
            return Ok(new { savedPages = 0, failedPages = new[] { 1 }, savedAt = "2026-10-14T12:00:00" });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw"); var client = new JobClient(api);
        var session = new DocumentSession { JobId = "job_test", OwnerLoginId = "test" };
        session.Add(new EditorBlock { Id = 1, Page = 1, ServerId = "t1", Source = "old\r\nsecond", Braille = "⠫" });
        session.ServerPages[1] = [new("t1", true)];
        var snapshot = SmjFile.Snapshot(session); session.ActiveBlock!.Source = "changed";
        var saved = await client.SaveAsync(snapshot); True(!saved.Complete); Equal(1, saved.Plan.ExpectedPages); Equal(1, saves);
        session.OwnerLoginId = "someone";
        await Reject<InvalidOperationException>(() => client.SaveAsync(SmjFile.Snapshot(session))); Equal(1, saves);
        session.OwnerLoginId = "test"; session.ActiveBlock.ServerId = null;
        await Reject<InvalidOperationException>(() => client.SaveAsync(SmjFile.Snapshot(session))); Equal(1, saves);
    }

    private static async Task JobUpload()
    {
        var uploaded = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Tokens();
            uploaded++;
            var form = (MultipartFormDataContent)request.Content!;
            var files = form.Where(p => p.Headers.ContentDisposition?.FileName is not null).ToArray();
            Equal(2, files.Length); True(files.All(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "file"));
            Equal("image/jpeg", files[0].Headers.ContentType!.MediaType);
            Equal((byte)1, (await files[0].ReadAsByteArrayAsync())[0]);
            return Ok(new { jobId = "job_new", totalPages = 2, status = "PENDING" });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("test", "pw"); var client = new JobClient(api);
        var result = await client.UploadAsync([new("1.jpg", [1]), new("2.png", [2])], new());
        Equal("PENDING", result.Status);
        await Reject<ArgumentException>(() => client.UploadAsync([new("one.pdf", [1]), new("two.png", [2])], new()));
        Equal(1, uploaded);
    }

    private static async Task SmjRoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            var model = DocumentSession.Sample(); model.JobId = "job_1"; model.OwnerLoginId = "tester";
            model.Options = new(KoreanGrade1: true); model.FooterText = "꼬리말 Ⅳ";
            model.OriginalFiles.Add("원본.pdf", [1, 2, 3]); model.ActiveBlock = model.Blocks[1];
            model.Blocks[0].ServerId = "t1"; model.Blocks[0].Metadata = new(Type: "table", TableSource: "<!표><!행><!칸>가<!/행><!/표>");
            model.Blocks[0].EditBraille("⠿");
            model.Blocks[0].Memos.Add(new("local-1", null, "TEXT", 0, "첫 줄", "검토 메모", DateTimeOffset.UtcNow));
            var frozen = SmjFile.Snapshot(model); model.Blocks[0].Source = "저장 중 바뀐 내용"; model.OriginalFiles["원본.pdf"][0] = 9;
            await SmjFile.SaveAsync(path, frozen); var loaded = await SmjFile.LoadAsync(path);
            Equal("job_1", loaded.JobId); Equal("t1", loaded.Blocks[0].ServerId); Equal("⠿", loaded.Blocks[0].Braille);
            True(loaded.Blocks[0].HasManualBraille); True(loaded.Options.KoreanGrade1); Equal(2, loaded.ActiveBlock!.Id);
            Equal((byte)1, loaded.OriginalFiles["원본.pdf"][0]); Equal("검토 메모", loaded.Blocks[0].Memos[0].Body);
            True(loaded.Blocks[0].Source != model.Blocks[0].Source); True(!loaded.IsDirty);
        }
        finally { File.Delete(path); }
    }

    private static async Task SmjFailure()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            var snapshot = SmjFile.Snapshot(DocumentSession.Sample()); await SmjFile.SaveAsync(path, snapshot);
            var before = await File.ReadAllBytesAsync(path);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Reject<OperationCanceledException>(() => SmjFile.SaveAsync(path, snapshot, cancelled.Token));
            var after = await File.ReadAllBytesAsync(path);
            True(before.SequenceEqual(after));
            await Reject<InvalidDataException>(() => SmjFile.SaveAsync(path, snapshot with { Manifest = snapshot.Manifest with { Version = 999 } }));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) zip.CreateEntry("manifest.json");
            await Reject<InvalidDataException>(() => SmjFile.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    private static async Task WorkspacePreferences()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "preferences.json"); var store = new LocalWorkspace(path);
            var state = store.Load(); Equal(0, state.RecentFiles?.Length ?? 0);
            for (var i = 0; i < LocalWorkspace.RecentLimit + 2; i++) state = LocalWorkspace.Remember(state, Path.Combine(folder, $"{i}.smj"));
            state = LocalWorkspace.Remember(state, Path.Combine(folder, "4.smj"));
            Equal(LocalWorkspace.RecentLimit, state.RecentFiles!.Length); Equal("4.smj", Path.GetFileName(state.RecentFiles[0]));
            Equal(1, state.RecentFiles.Count(p => Path.GetFileName(p) == "4.smj"));
            state = state with { WorkFolder = folder, SixDotKeys = "A S D J K L", SpaceEnabled = false };
            await store.SaveAsync(state);
            var loaded = store.Load(); Equal(folder, loaded.WorkFolder); Equal("A S D J K L", loaded.SixDotKeys); True(!loaded.SpaceEnabled);
            var json = await File.ReadAllTextAsync(path); True(!json.Contains("source", StringComparison.OrdinalIgnoreCase)); True(!json.Contains("token", StringComparison.OrdinalIgnoreCase));
            var before = await File.ReadAllBytesAsync(path);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Reject<OperationCanceledException>(() => store.SaveAsync(new(), cancelled.Token));
            var after = await File.ReadAllBytesAsync(path); True(before.SequenceEqual(after));
            await File.WriteAllTextAsync(path, "{\"version\":1,\"sixDotKeys\":null}");
            Equal("F D S J K L", store.Load().SixDotKeys); True(store.LoadWarning is not null);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task WorkspaceFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            await File.WriteAllTextAsync(Path.Combine(folder, "one.smj"), "contents");
            await File.WriteAllTextAsync(Path.Combine(folder, "two.txt"), "hidden");
            await File.WriteAllTextAsync(Path.Combine(folder, "nested", "child.smj"), "nested");
            var entries = LocalWorkspace.ListFolder(folder); Equal(2, entries.Length); True(entries[0].IsDirectory);
            Equal("one.smj", entries[1].Name); Equal(8L, entries[1].Size);
            File.Delete(entries[1].Path); Equal(1, LocalWorkspace.ListFolder(folder).Length);
            Equal("child.smj", LocalWorkspace.ListFolder(entries[0].Path).Single().Name);
        }
        finally { Directory.Delete(folder, true); }
    }

    private static async Task SavedCarets()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".smj");
        try
        {
            var model = DocumentSession.Sample(); var version = model.ChangeVersion;
            model.ActiveBlock = model.Blocks[1]; model.ActiveBlock.SourceCaret = 9; model.ActiveBlock.BrailleCaret = 2;
            Equal(version, model.ChangeVersion); True(!model.IsDirty);
            await SmjFile.SaveAsync(path, SmjFile.Snapshot(model));
            model.ActiveBlock.SourceCaret = 0;
            var restored = await SmjFile.LoadAsync(path);
            Equal(2, restored.ActiveBlock!.Id); Equal(9, restored.ActiveBlock.SourceCaret); Equal(2, restored.ActiveBlock.BrailleCaret);
        }
        finally { File.Delete(path); }
    }

    private static Task Folding()
    {
        Equal("⠁⠃⠉⠙⠑⠋|⠛⠓⠊⠚", string.Join("|", BrailleWrapping.Fold("⠁⠃⠉⠙⠑⠋⠛⠓⠊⠚", [6, 9], 8)));
        Equal("⠁⠃||⠉", string.Join("|", BrailleWrapping.Fold("⠁⠃\n\n⠉", [], 8)));
        Equal("⠁⠁⠁⠁⠁⠁⠁|⠠⠄⠃", string.Join("|", BrailleWrapping.Fold("⠁⠁⠁⠁⠁⠁⠁⠠⠄⠃", [], 8)));
        Equal("⠁⠃⠉⠙|⠑⠋⠛⠓⠊", string.Join("|", BrailleWrapping.Fold("⠁⠃⠉⠙⠀⠑⠋⠛⠓⠊", [], 8)));
        return Task.CompletedTask;
    }

    private static async Task Tags()
    {
        Equal("<!강조>내용<!/강조>", BrailleTags.Get("강조").Insert("내용"));
        Equal("<!쪽바꿈>", BrailleTags.Get("쪽바꿈").Insert(""));
        Equal("<!상자>제목<!/상자>", BrailleTags.Get("상자").Insert("제목"));
        await Reject<ArgumentException>(() => Task.FromResult(BrailleTags.Get("꼬리말")));
    }

    private static async Task RealEngine(string python, bool packaged = false)
    {
        await using var sidecar = new BrailleSidecar(new(python, packaged ? [] : ["-u", Path.Combine(AppContext.BaseDirectory, "engine-adapter", "engine-entry.py")]));
        var engine = new LocalBrailleEngine(sidecar);
        var grade2 = await engine.TranslateElementAsync("가", new(), new()); Equal("⠫", grade2.Cells.Trim('⠀'));
        Equal("3.4.24", sidecar.EngineVersion);
        var grade1 = await engine.TranslateElementAsync("가", new(), new(KoreanGrade1: true)); Equal("⠈⠣", grade1.Cells.Trim('⠀'));
        var dropped = await engine.TranslateElementAsync("가▶나", new(), new()); True(dropped.NeedsReview); Equal("▶", dropped.Dropped[0].Text);
        var table = await engine.TranslateElementAsync("", new(Type: "table", TableSource: "<!표>\n<!행><!칸>구분<!칸>가<!/행>\n<!행><!칸>나<!칸>다<!/행>\n<!/표>", RenderMode: "table_grid"), new());
        Equal(5, table.Drafts.Length); Equal("table_grid", table.RenderMode);
        var model = new DocumentSession(); model.Add(new EditorBlock { Id = 1, Page = 1, Source = "가", Braille = grade2.Cells, Translation = grade2 });
        var brf = await engine.BuildBrfAsync(model); True(brf.All(c => c < 128)); True(brf.EndsWith('\f'));
        var pages = await engine.ReadBrfAsync(brf); True(pages[0].Any(line => line.Contains('⠫')));
        var multi = new DocumentSession { Layout = new(Columns: 16, Lines: 4, StartPage: 7, PageNumbers: "모든 페이지", ShowSourcePageNumber: false), FooterText = "Ⅳ" };
        multi.Add(new EditorBlock { Id = 1, Page = 1, Braille = string.Join("\n", Enumerable.Repeat("⠁⠃", 9)), HasManualBraille = true });
        var whole = await engine.BuildBrfAsync(multi);
        Equal(3, DocumentExport.BrfPages(whole).Length);
        var wholePages = await engine.ReadBrfAsync(whole);
        var selected = await engine.ReadBrfAsync(DocumentExport.SelectBraille(whole, new(ExportScope.BraillePages, 2, 3)));
        Equal(2, selected.Length);
        Equal(string.Join("|", wholePages[1]), string.Join("|", selected[0]));
        Equal(string.Join("|", wholePages[2]), string.Join("|", selected[1]));
        True(DocumentExport.BrailleText(selected).StartsWith("⠁⠃\r\n"));
        True(wholePages[0][^1] != selected[0][^1]); // Printed page numbers remain 8 and 9, rather than restarting at 7.
        var footer = await engine.TranslateElementAsync("Ⅳ", new(Type: "header_footer"), new()); True(footer.Cells.StartsWith('⠴'));
        var symbols = await engine.SymbolsAsync(); True(symbols.Any(s => s.Char == "※" && s.Cells.Length > 0));
        var unknown = await engine.TranslateElementAsync("<!모르는태그>가", new(), new()); True(unknown.NeedsReview && unknown.UnknownTags!.Contains("모르는태그"));
        var breaks = new DocumentSession { Layout = new(Columns: 16, Lines: 4, ShowSourcePageNumber: false, ShowBraillePageNumber: false) };
        breaks.Add(new EditorBlock { Id = 1, Page = 1, Source = "가", Braille = grade2.Cells });
        breaks.Add(new EditorBlock { Id = 2, Page = 1, Source = "<!쪽바꿈>", Braille = "" });
        breaks.Add(new EditorBlock { Id = 3, Page = 1, Source = "나", Braille = "⠉" });
        Equal(2, DocumentExport.BrfPages(await engine.BuildBrfAsync(breaks)).Length);
        var priority = await engine.ReviewPriorityAsync(grade1.Cells, "가", new(), new(KoreanGrade1: true));
        True(new[] { "먼저 확인", "확인", "나중에 확인" }.Contains(priority));
        True((await engine.DecodeAsync(grade1.Cells, new(KoreanGrade1: true))).Contains('가'));
        var rangeDoc = new DocumentSession { Layout = new(Columns: 16, Lines: 4, StartPage: 7, PageNumbers: "모든 페이지", Footer: "모든 페이지", ShowSourcePageNumber: false, ShowBraillePageNumber: false, FooterAlign: "left"), FooterText = "가", FooterRanges = [new(8, 8, "나"), new(9, null, "")] };
        rangeDoc.Add(new EditorBlock { Id = 1, Page = 1, Braille = string.Join("\n", Enumerable.Repeat("⠿", 9)), HasManualBraille = true });
        var rangePages = await engine.ReadBrfAsync(await engine.BuildBrfAsync(rangeDoc));
        Equal(3, rangePages.Length);
        var footerA = (await engine.TranslateElementAsync("가", new(Type: "header_footer"), new())).Cells;
        var footerB = (await engine.TranslateElementAsync("나", new(Type: "header_footer"), new())).Cells;
        True(rangePages[0][^1].StartsWith(footerA)); True(rangePages[1][^1].StartsWith(footerB)); True(rangePages[2][^1].All(c => c is '⠀' or ' '));
        rangeDoc.Layout = rangeDoc.Layout with { Footer = "출력 안 함" };
        True((await engine.ReadBrfAsync(await engine.BuildBrfAsync(rangeDoc))).All(p => p[^1].All(c => c is '⠀' or ' ')));
        var headerDoc = new DocumentSession { Layout = new(Columns: 16, Lines: 4, StartPage: 7, PageNumbers: "모든 페이지", ShowSourcePageNumber: false, ShowBraillePageNumber: false) };
        headerDoc.Add(new EditorBlock { Id = 1, Page = 1, Source = "", Braille = "⠿", HasManualBraille = true });
        headerDoc.Add(new EditorBlock { Id = 2, Page = 1, Source = "<!머리말>가<!/머리말>나", Braille = "⠉", HasManualBraille = true });
        var headerCells = (await engine.TranslateElementAsync("가", new(Type: "header_footer"), new())).Cells.Trim('⠀', ' ');
        var headerPages = await engine.ReadBrfAsync(await engine.BuildBrfAsync(headerDoc));
        True(headerPages[0][0].Trim('⠀', ' ') == headerCells); // Inserting mid-face changes the current face's top.
        headerDoc.Blocks[0].Braille = "⠿\n⠿\n⠿";
        headerPages = await engine.ReadBrfAsync(await engine.BuildBrfAsync(headerDoc));
        True(headerPages[0][0].Contains('⠿') && headerPages[1][0].Trim('⠀', ' ') == headerCells); // Earlier face unaffected.
        headerDoc.Add(new EditorBlock { Id = 3, Page = 1, Source = "<!머리말><!/머리말>라", Braille = "⠐⠣", HasManualBraille = true });
        headerPages = await engine.ReadBrfAsync(await engine.BuildBrfAsync(headerDoc));
        True(headerPages[0][0].Contains('⠿') && headerPages[1][0].Trim('⠀', ' ') == "⠉"); // Clearing on the same face wins from that face.
        headerDoc.Blocks.RemoveAt(2);
        headerDoc.Blocks[1].Source = "가나\n<!머리말>가<!/머리말>다라";
        var anchored = await engine.TranslateElementAsync(headerDoc.Blocks[1].Source, new(), new());
        headerDoc.Blocks[1].Braille = anchored.Cells; headerDoc.Blocks[1].Translation = anchored; headerDoc.Blocks[1].HasManualBraille = false;
        headerPages = await engine.ReadBrfAsync(await engine.BuildBrfAsync(headerDoc));
        True(headerPages[0][0].Contains('⠿') && headerPages[1][0].Trim('⠀', ' ') == headerCells); // Prefix inside a translated block maps to the current face.
        var composed = await engine.ComposeAsync(SmjFile.Snapshot(multi).Manifest);
        var projected = BrailleGrid.FromComposition(composed, multi.Blocks, multi.Layout);
        Equal(string.Join("|", (await engine.ReadBrfAsync(composed.Brf)).SelectMany(p => p)),
            string.Join("|", projected.SelectMany(f => f.Text!)));
        True(projected.Any(f => f.Kinds!.Contains("page")));
        foreach (var face in projected)
            for (var row = 0; row < face.Rows.Length; row++)
                if (face.Kinds![row] == "page") True(face.Rows[row].All(c => c is null));
        multi.Blocks[0].Braille = "⠙";
        await Reject<InvalidDataException>(() => { BrailleGrid.FromComposition(composed, multi.Blocks, multi.Layout); return Task.CompletedTask; });
        var inline = await engine.TranslateElementAsync("가<!쪽바꿈><!쪽바꿈>나", new(), new());
        var inlineDoc = new DocumentSession { Layout = new(Columns: 16, Lines: 4, PageNumbers: "출력 안 함", Footer: "출력 안 함") };
        inlineDoc.Add(new EditorBlock { Id = 1, Page = 1, Source = "가<!쪽바꿈><!쪽바꿈>나", Braille = inline.Cells, Translation = inline });
        var inlineResult = await engine.ComposeAsync(SmjFile.Snapshot(inlineDoc).Manifest);
        Equal(3, inlineResult.Pages!.Length);
        True(inlineResult.Pages[1].Lines.All(l => l.All(c => c == '⠀')));
        var inlineGrid = BrailleGrid.FromComposition(inlineResult, inlineDoc.Blocks, inlineDoc.Layout);
        var blank = inlineGrid[1].Rows[1][2]!;
        True(blank.IsInsertion);
        BlockRange.Replace(inlineDoc, [new(blank.Block, blank.Offset, 0)], SearchPanel.Braille, blank.Prefix + "⠿");
        var filled = await engine.ComposeAsync(SmjFile.Snapshot(inlineDoc).Manifest);
        True(filled.Pages![1].Lines[1][2] == '⠿' && filled.Pages[2].Lines[0].Any(c => c != '⠀'));
        headerDoc.Blocks[1].Source = "다<!머리말>가<!/머리말>나";
        await Reject<ArgumentException>(() => engine.BuildBrfAsync(headerDoc)); // Unknown manual position cannot silently become global.
    }

    private static async Task<T> Reject<T>(Func<Task> action) where T : Exception
    { try { await action(); } catch (T error) { return error; } throw new Exception("Expected " + typeof(T).Name); }
    private static void True(bool value) { if (!value) throw new Exception("Expected true"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
}
