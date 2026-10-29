using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

// Regression cases from Windows acceptance. Fake HTTP is intentionally separate from production QA.
internal static class AcceptanceChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Version metadata accepts absent downloads but rejects unsafe and malformed addresses", Versions);
        yield return ("Uploaded local-only settings survive successful and failed jobs and SMJ reopen", UploadSettings);
        yield return ("Image multipart retains ordered filenames, MIME types and every input byte", ImageMultipart);
        yield return ("Credit exhaustion is read without conflating blocked pages with successful pages", CreditStatus);
        yield return ("Six-dot presets support the specified physical keys including comma", SixDotPresets);
        yield return ("Six-dot custom digit and punctuation keys survive account preference reload", SixDotCustomKeys);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Acceptance regression failed"); }
    private static HttpResponseMessage Ok(object result) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, code = "COMMON2000", result }), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request);
    }
    private static HttpResponseMessage Login() => Ok(new { accessToken = "fixture", refreshToken = "fixture", role = "ROLE_USER" });
    private static Task Versions()
    {
        foreach (var raw in new[] { "{\"latestVersion\":\"1.0.0\",\"minSupportedVersion\":\"1.0.0\",\"downloadUrl\":null}", "{\"latestVersion\":\"1.0.0\",\"minSupportedVersion\":\"1.0.0\"}" })
        {
            var info = AppVersionInfo.Read(JsonDocument.Parse(raw).RootElement)!;
            Check(info.DownloadUrl is null && !info.Available(new(1, 0, 0)));
        }
        foreach (var url in new object[] { "http://example.com/app.zip", "https://user:secret@example.com/a", "", "relative.zip", 42 })
        {
            try { AppVersionInfo.Read(JsonSerializer.SerializeToElement(new { latestVersion = "2.0.0", minSupportedVersion = "1.0.0", downloadUrl = url })); }
            catch (InvalidDataException) { continue; }
            throw new Exception("Unsafe download accepted");
        }
        var required = AppVersionInfo.Read(JsonSerializer.SerializeToElement(new { latestVersion = "2.0.0", minSupportedVersion = "2.0.0", downloadUrl = (string?)null }))!;
        Check(required.Required(new(1, 0, 0)) && required.DownloadUrl is null);
        return Task.CompletedTask;
    }
    private static async Task UploadSettings()
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login") ? Login() :
            Ok(new { layoutOptions = new { pageNumberLine = "odd", cellsPerLine = 32, linesPerPage = 26 }, footerText = "" }))));
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture"); var jobs = new JobClient(api);
        var chosen = new DocumentSession { Layout = new() { PageNumbers = "짝수 페이지", NumberRanges = [new(1, 5)], PadToEven = true },
            FooterText = "꼬리말", FooterRanges = [new(2, null, "두 번째", "right")] };
        foreach (var state in new[] { "COMPLETED", "FAILED" })
        {
            var model = new DocumentSession { JobId = "fixture", JobStatus = state };
            await jobs.ReadSettingsAsync(model, uploadSettings: chosen);
            model.Add(new EditorBlock { Id = 1, Page = 1, Source = "검사", Metadata = new(IsBlocked: state == "FAILED") });
            var path = Path.Combine(Path.GetTempPath(), "semojum-acceptance-" + Guid.NewGuid() + ".smj");
            try
            {
                await SmjFile.SaveAsync(path, SmjFile.Snapshot(model)); var restored = await SmjFile.LoadAsync(path);
                Check(restored.Layout.PageNumbers == "짝수 페이지" && restored.Layout.NumberRanges!.Single() == new PageNumberRange(1, 5)
                    && restored.Layout.PadToEven && restored.FooterText == "꼬리말" && restored.FooterRanges.Single().Alignment == "right");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        var remote = new DocumentSession { JobId = "fixture" }; await jobs.ReadSettingsAsync(remote);
        Check(remote.Layout.PageNumbers == "홀수 페이지" && remote.Layout.NumberRanges is null);
    }
    private static async Task ImageMultipart()
    {
        UploadFile[] files = [new("page-2.png", [137, 80, 78, 71, 0, 255, 10]), new("page-1.jpg", [255, 216, 255, 0, 23])];
        using var http = new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("login")) return Login();
            var parts = ((MultipartFormDataContent)request.Content!).Where(p => p.Headers.ContentDisposition!.Name!.Trim('"') == "file").ToArray();
            Check(parts.Length == 2);
            for (var i = 0; i < parts.Length; i++)
            {
                Check(parts[i].Headers.ContentDisposition!.FileName!.Trim('"') == files[i].FileName);
                Check((await parts[i].ReadAsByteArrayAsync()).SequenceEqual(files[i].Bytes));
                Check(parts[i].Headers.ContentType!.MediaType == (i == 0 ? "image/png" : "image/jpeg"));
            }
            return Ok(new { jobId = "fixture", totalPages = 2, status = "PENDING" });
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        await new JobClient(api).UploadAsync(files, new());
    }
    private static async Task CreditStatus()
    {
        using var http = new HttpClient(new Handler(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("login") ? Login() :
            Ok(new { totalPages = 3, completedPages = 3, overallStatus = "COMPLETED", creditExhausted = true,
                pages = new Dictionary<string, string> { ["page:1"] = "COMPLETED", ["page:2"] = "BLOCKED", ["page:3"] = "BLOCKED" } }))));
        var api = new SemojumApiClient(http); await api.LoginAsync("fixture", "fixture");
        var status = await new JobClient(api).StatusAsync("fixture");
        Check(status.CreditExhausted && status.SucceededPages == 1 && status.FailedPages == 2 && status.FinishedPages == 3);
    }

    private static Task SixDotPresets()
    {
        var input = new SixDotInput();
        foreach (var keys in new[] { "W E R U I O", "F D S J K L", "X C V N M ," })
        {
            input.Configure(keys);
            var dots = keys.Where(c => !char.IsWhiteSpace(c)).ToArray();
            var expected = new[] { "⠁", "⠂", "⠄", "⠈", "⠐", "⠠" };
            for (var i = 0; i < dots.Length; i++)
            {
                Check(input.Press(dots[i]));
                Check(input.Release(dots[i]) == expected[i]);
            }
        }
        return Task.CompletedTask;
    }

    private static async Task SixDotCustomKeys()
    {
        var dir = Path.Combine(Path.GetTempPath(), "semojum-sixdot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AccountPreferenceStore(dir);
            await store.SaveAsync("fixture", new(SixDotKeys: "Q 1 , 4 - /"));
            var input = new SixDotInput();
            input.Configure(store.Load("fixture").SixDotKeys);
            foreach (var (key, expected) in new[] { ('q', "⠁"), ('1', "⠂"), (',', "⠄"), ('4', "⠈"), ('-', "⠐"), ('/', "⠠") })
            {
                Check(input.Press(key));
                Check(input.Release(key) == expected);
            }
            foreach (var invalid in new[] { "Q 1 ! 4 - /", "Q 1 , 4 - 가", "Q 1 , 4 -" })
            {
                try { input.Configure(invalid); }
                catch (ArgumentException) { continue; }
                throw new Exception("Invalid six-dot mapping accepted");
            }
            Check(input.Press(',') && input.Release(',') == "⠄");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
