using System.Text.Json;
using Semojum.Core;
using Semojum.Core.Integration;

// Separate consent from the document/AI run: this mode creates exactly one private
// synthetic post, verifies it, and removes only its recorded post ID.
if (args.Length == 2 && args[0] == "--authorized-board")
{
    await BoardLiveChecks.RunAsync(args[1]);
    return;
}

// Explicit opt-in only. Credentials never enter files or diagnostics. Reuse the output
// directory to resume known jobs; never replace a running job because a poll timed out.
if (args.Length != 4 || args[0] != "--authorized-live")
    throw new ArgumentException("Usage: --authorized-live OUTPUT_DIRECTORY ENGINE_EXE SYNTHETIC_IMAGE");
var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
var statePath = Path.Combine(output, "state.json");
var state = File.Exists(statePath) ? JsonSerializer.Deserialize<RunState>(File.ReadAllText(statePath))! : new RunState();
var logPath = Path.Combine(output, "checks.txt");
void Log(string text) { var line = $"{DateTimeOffset.Now:O} {text}"; Console.WriteLine(line); File.AppendAllText(logPath, line + Environment.NewLine); }
void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); Log("PASS " + name); }
void Persist() => File.WriteAllText(statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
var login = Environment.GetEnvironmentVariable("SEMOJUM_TEST_LOGIN") ?? throw new InvalidOperationException("Test login required");
var password = Environment.GetEnvironmentVariable("SEMOJUM_TEST_PASSWORD") ?? throw new InvalidOperationException("Test password required");
using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(45) };
var api = new SemojumApiClient(http);
var jobs = new JobClient(api);
try
{
    var identity = await api.LoginAsync(login, password);
    password = ""; Environment.SetEnvironmentVariable("SEMOJUM_TEST_PASSWORD", null);
    Check(identity.Role == "ROLE_USER", "live user authentication");
    var settings = new SettingsClient(api);
    var defaults = await settings.DefaultsAsync();
    var forms = await settings.FormsAsync();
    Check(forms.Length > 0 && forms.Any(f => f.Builtin), "account defaults and built-in forms deserialize");
    if (!state.FormsVerified)
    {
        var baseline = forms.First(f => f.Builtin);
        if (state.FormId is null)
        {
            var form = await settings.SaveFormAsync(null, "QA4-20261027-검증", baseline.FormId, baseline.Layout with { Columns = 30 }, baseline.Options);
            state.FormId = form.FormId; Persist();
        }
        var createdForm = (await settings.FormsAsync()).Single(f => f.FormId == state.FormId);
        Check(!createdForm.Builtin && createdForm.Name.StartsWith("QA4-20261027-") && createdForm.Layout.Columns == 30, "live user form create and list");
        var updatedForm = await settings.SaveFormAsync(createdForm.FormId, "QA4-20261027-수정", baseline.FormId, createdForm.Layout with { Lines = 25 }, createdForm.Options);
        Check(updatedForm.FormId == state.FormId && updatedForm.Layout.Lines == 25, "live user form update");
        if (state.CopyFormId is null)
        {
            var copy = await settings.SaveFormAsync(null, "QA4-20261027-사본", baseline.FormId, updatedForm.Layout, updatedForm.Options);
            state.CopyFormId = copy.FormId; Persist();
        }
        var copiedForm = (await settings.FormsAsync()).Single(f => f.FormId == state.CopyFormId);
        Check(!copiedForm.Builtin && copiedForm.Name.StartsWith("QA4-20261027-") && copiedForm.FormId != updatedForm.FormId && copiedForm.Layout == updatedForm.Layout, "live user form copy");
        // Only IDs created and recorded by this run are eligible for cleanup.
        await settings.DeleteFormAsync(copiedForm);
        await settings.DeleteFormAsync(updatedForm);
        var afterForms = await settings.FormsAsync();
        Check(afterForms.All(f => f.FormId != state.FormId && f.FormId != state.CopyFormId)
            && afterForms.Any(f => f.FormId == baseline.FormId && JsonSerializer.Serialize(f) == JsonSerializer.Serialize(baseline)), "live test form deletion preserves built-in form");
        state.FormsVerified = true; Persist();
    }
    var rules = await new RuleClient(api).SearchAsync("", null);
    Check(rules.Items.Length > 0 && rules.TotalElements > 0, "live rule search");
    Log($"INFO rule index: {rules.TotalElements}; fullText on first page: {rules.Items.Count(r => r.HasFullText)}/{rules.Items.Length}");
    state.RuleCount = rules.TotalElements; Persist();
    var usage = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage/daily?period=MONTH");
    Check(usage.TryGetProperty("orgRemaining", out var remaining) && usage.TryGetProperty("daily", out _) && usage.TryGetProperty("bySource", out _), "live usage fields");
    var balance = remaining.GetInt64();
    Log($"INFO organization balance before verification: {balance}");
    var board = new BoardClient(api);
    var posts = await board.ListAsync(1, "TITLE_CONTENT", "", false);
    var notifications = await board.NotificationsAsync();
    Check(posts.Items is not null && posts.Notices is not null && notifications.Items is not null, "board list and notifications read without writes");
    var readable = posts.Items!.FirstOrDefault(p => !p.Secret);
    if (readable is not null)
    {
        var post = await board.ReadAsync(readable.PostId);
        Check(post.PostId == readable.PostId && post.Attachments is not null && post.Answers is not null, "public board detail and attachment/answer metadata");
    }
    else Log("SKIP no public board detail available in the first page");

    await using var sidecar = new BrailleSidecar(new(args[2], []));
    var engine = new LocalBrailleEngine(sidecar);
    var translator = new TranslationCoordinator(engine);
    await sidecar.InvokeAsync("ping");
    Check(sidecar.EngineVersion == "3.4.35", "frozen engine v3.4.35");
    var textModel = new DocumentSession { OwnerLoginId = login, IsTextJob = true, Options = defaults.Options,
        Title = "QA4-live-text-20261027.smj" };
    if (!state.TextVerified)
    {
        if (state.TextJobId is null)
        {
            var created = await jobs.CreateTextAsync("QA4-live-text-20261027.txt", textModel);
            state.TextJobId = created.JobId; Persist(); Log("CREATED text job " + created.JobId);
        }
        textModel.JobId = state.TextJobId;
        var first = new EditorBlock { Id = 1, Page = 1, Heading = "실서버 저장 검증", Source = "세모점 저장 검증입니다.\n<!주>검사용 문서입니다.<!/주>" };
        var second = new EditorBlock { Id = 2, Page = 1, Heading = "수식 검증", Source = "<!수식>x+1=2<!/수식>" };
        textModel.Add(first); textModel.Add(second);
        foreach (var block in textModel.Blocks)
            Check(await translator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(35)) == TranslationOutcome.Applied && block.Braille.Length > 0, "text local translation block " + block.Id);
        first.Memos.Add(new("qa4-live-memo", null, "TEXT", 0, first.Source.Split('\n')[0],
            "QA4 검증 메모", DateTimeOffset.Now, Author: login, Replies: [new(login, "QA4 검증 답글", DateTimeOffset.Now)]));
        var result = await jobs.SaveAsync(SmjFile.Snapshot(textModel));
        Check(result.Complete && result.JobId == state.TextJobId, "first server save including new text/braille IDs");
        JobClient.ApplySaved(textModel, result);
        var restored = await jobs.PageAsync(state.TextJobId, 1, 1);
        Check(restored.Blocks.Length == 2 && restored.Blocks.Select(b => b.ServerId).SequenceEqual(textModel.Blocks.Select(b => b.ServerId)), "reopen preserves paired IDs and block order");
        Check(restored.Blocks.Zip(textModel.Blocks).All(p => p.First.Source == p.Second.Source && p.First.Braille == p.Second.Braille), "reopen preserves source and braille");
        Check(restored.Blocks[0].Memos.Count == 1 && restored.Blocks[0].Memos[0].Body == first.Memos[0].Body, "memo server round-trip");
        Check(restored.Blocks[0].Memos[0].Replies?.SingleOrDefault()?.Body == "QA4 검증 답글", "memo reply server round-trip");
        JobClient.Remember(textModel, restored);
        first.EditBraille(first.Braille + "⠁");
        first.Memos[0] = restored.Blocks[0].Memos[0] with { Resolved = true, ResolvedAt = DateTimeOffset.Now };
        result = await jobs.SaveAsync(SmjFile.Snapshot(textModel));
        Check(result.Complete, "second explicit save with manual braille and resolved memo");
        JobClient.ApplySaved(textModel, result);
        restored = await jobs.PageAsync(state.TextJobId, 1, 1);
        Check(restored.Blocks[0].Braille == first.Braille && restored.Blocks[0].HasManualBraille, "manual braille survives reopening");
        Check(restored.Blocks[0].Memos[0].Resolved && restored.Blocks[0].Memos[0].ResolvedAt.HasValue, "resolved memo timestamp survives reopening");
        var textPath = Path.Combine(output, "QA4-live-text.smj");
        await SmjFile.SaveAsync(textPath, SmjFile.Snapshot(textModel));
        var local = await SmjFile.LoadAsync(textPath);
        Check(local.JobId == state.TextJobId && local.Blocks[0].Braille == first.Braille, "live text SMJ round-trip");
        state.TextVerified = true; Persist();
    }

    if (state.ImageJobId is null)
    {
        if (balance < 5) throw new InvalidOperationException("Insufficient balance for the authorized one-page AI test");
        var created = await jobs.UploadAsync([new(Path.GetFileName(args[3]), await File.ReadAllBytesAsync(args[3]))], textModel);
        state.ImageJobId = created.JobId; Persist(); Log("CREATED image job " + created.JobId);
        Check(created.TotalPages == 1, "one-page AI upload");
    }
    if (!state.ImageVerified)
    {
        var deadline = DateTime.UtcNow.AddMinutes(8);
        JobStatus status;
        do
        {
            status = await jobs.StatusAsync(state.ImageJobId);
            Log($"POLL {state.ImageJobId} {status.OverallStatus} {status.CompletedPages}/{status.TotalPages}");
            state.ImageStatus = status.OverallStatus; Persist();
            if (status.IsTerminal) break;
            if (DateTime.UtcNow >= deadline) { Log("WAIT existing image job remains live; rerun with the same directory to continue"); return; }
            await Task.Delay(TimeSpan.FromSeconds(8));
        } while (true);
        Check(status.OverallStatus == "COMPLETED" && status.SucceededPages == 1 && status.FailedPages == 0, "real AI page completes");
        var page = await jobs.PageAsync(state.ImageJobId, 1, 1);
        Check(page.Blocks.Length > 0 && page.Blocks.Any(b => b.Source.Contains("검증") || b.Source.Contains("사과")), "real OCR returns synthetic page content");
        Log("INFO AI block types: " + string.Join(", ", page.Blocks.GroupBy(b => b.Metadata.Type).Select(g => g.Key + "=" + g.Count())));
        var model = new DocumentSession { JobId = state.ImageJobId, OwnerLoginId = login, IsTextJob = false, Title = "QA4-live-image.smj" };
        await jobs.ReadSettingsAsync(model);
        JobClient.Remember(model, page);
        foreach (var block in page.Blocks)
        {
            var draftsBefore = JsonSerializer.Serialize(block.Metadata.Alternatives);
            VisualDraftSelection.ApplyInitial(block, defaults.Options);
            Check(JsonSerializer.Serialize(block.Metadata.Alternatives) == draftsBefore, "AI alternatives retained block " + block.Id);
            model.Add(block);
            if (block.Braille.Length == 0 && !block.Metadata.IsBlocked)
                Check(await translator.TranslateAsync(block, ManualEditDecision.Keep, TimeSpan.FromSeconds(35)) == TranslationOutcome.Applied, "AI source locally translated block " + block.Id);
        }
        model.OriginalFiles[Path.GetFileName(args[3])] = await File.ReadAllBytesAsync(args[3]);
        model.OriginalPages[1] = new("image", Path.GetFileName(args[3]), Width: page.Width, Height: page.Height);
        var saved = await jobs.SaveAsync(SmjFile.Snapshot(model));
        Check(saved.Complete && saved.Plan.TotalPages is null, "AI document explicit save without text-only totalPages");
        JobClient.ApplySaved(model, saved);
        var reopened = await jobs.PageAsync(state.ImageJobId, 1, 1);
        Check(reopened.Blocks.Length == model.Blocks.Count && reopened.Blocks.Zip(model.Blocks).All(p => p.First.Source == p.Second.Source && p.First.Braille == p.Second.Braille), "AI document source and braille server round-trip");
        var imagePath = Path.Combine(output, "QA4-live-image.smj");
        await SmjFile.SaveAsync(imagePath, SmjFile.Snapshot(model));
        var restored = await SmjFile.LoadAsync(imagePath);
        Check(restored.OriginalFiles.Count == 1 && restored.Blocks.Count == model.Blocks.Count, "AI document self-contained SMJ round-trip");
        var composed = await engine.ComposeAsync(SmjFile.Snapshot(restored).Manifest);
        Check(composed.Brf.Length > 0, "reopened AI document produces BRF");
        File.WriteAllText(Path.Combine(output, "QA4-live-image.brf"), composed.Brf);
        state.ImageVerified = true; Persist();
    }
    var persistedImage = await SmjFile.LoadAsync(Path.Combine(output, "QA4-live-image.smj"));
    var serverImage = await jobs.PageAsync(state.ImageJobId, 1, 1);
    Check(serverImage.Blocks.Zip(persistedImage.Blocks).All(p =>
        JsonSerializer.Serialize(p.First.Metadata.Alternatives) == JsonSerializer.Serialize(p.Second.Metadata.Alternatives)
        && p.First.Metadata.SelectedIndex == p.Second.Metadata.SelectedIndex), "every AI alternative and its selection survives server save/reopen");
    var chart = serverImage.Blocks.Single(b => b.Metadata.Type == "chart_graph");
    var freshChart = new EditorBlock { Id = chart.Id, Page = 1, Source = chart.Metadata.OriginalSource ?? chart.Source, Metadata = chart.Metadata };
    var originalAlternatives = JsonSerializer.Serialize(freshChart.Metadata.Alternatives);
    var choicePolicy = VisualDescriptions.Defaults(); choicePolicy["chart"] = new(["별책 참조"], "별책 참조");
    Check(VisualDraftSelection.ApplyInitial(freshChart, new(VisualDescriptions: choicePolicy))
        && freshChart.Metadata.SelectedIndex is { } selected && freshChart.Metadata.Alternatives![selected].Label.Contains("참조"),
        "production chart_graph response honors non-default initial choice");
    Check(JsonSerializer.Serialize(freshChart.Metadata.Alternatives) == originalAlternatives, "non-default initial choice retains every production chart draft");
    if (!state.CancellationVerified)
    {
        if (state.CancellationJobId is null)
        {
            if (balance < 5) throw new InvalidOperationException("Insufficient balance for the one-page cancellation check");
            var created = await jobs.UploadAsync([new("QA4-cancel-20261027.png", await File.ReadAllBytesAsync(args[3]))], textModel);
            state.CancellationJobId = created.JobId; Persist(); Log("CREATED cancellation job " + created.JobId);
        }
        if (!state.CancellationAccepted)
        {
            var response = await jobs.CancelAsync(state.CancellationJobId);
            state.CancellationAccepted = response.Accepted; Persist();
            Check(state.CancellationAccepted, "live conversion cancellation accepted before completion");
        }
        var deadline = DateTime.UtcNow.AddMinutes(8);
        var canceledStatus = await jobs.StatusAsync(state.CancellationJobId);
        while (!canceledStatus.IsTerminal)
        {
            Log($"POLL cancel {state.CancellationJobId} {canceledStatus.OverallStatus} {canceledStatus.CompletedPages}/{canceledStatus.TotalPages}");
            if (DateTime.UtcNow >= deadline) { Log("WAIT cancellation is still finishing; retain the recorded job ID"); return; }
            await Task.Delay(TimeSpan.FromSeconds(8));
            canceledStatus = await jobs.StatusAsync(state.CancellationJobId);
        }
        Log($"INFO cancellation terminal: {canceledStatus.OverallStatus}; success={canceledStatus.SucceededPages}; blocked={canceledStatus.FailedPages}");
        // BE finishes already-running pages and represents a fully canceled, unprocessed page as FAILED/BLOCKED.
        Check(canceledStatus.RemainingPages == 0 && canceledStatus.FinishedPages == canceledStatus.TotalPages, "live cancellation converges to terminal page accounting");
        var repeated = await jobs.CancelAsync(state.CancellationJobId);
        Check(!repeated.Accepted && repeated.IsTerminal, "live repeated cancel of terminal job is idempotent");
        state.CancellationVerified = true; Persist();
    }
    Check(!(await jobs.CancelAsync(state.CancellationJobId!)).Accepted, "live terminal cancellation response is parsed without claiming a new cancellation");
    var usageAfter = await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage/daily?period=MONTH");
    Log("INFO organization balance after verification: " + usageAfter.GetProperty("orgRemaining"));
    Log("COMPLETE live client/server checks; no board posts or inquiries submitted");
}
catch (Exception ex)
{
    // Do not serialize request headers, bodies, tokens, credentials, or arbitrary server content.
    Log("FAIL " + ex.GetType().Name + (ex is SemojumApiException apiError ? " " + apiError.Code : " " + ex.Message));
    Environment.ExitCode = 1;
}
finally
{
    if (api.Account is not null)
    {
        try { await api.LogoutAsync(); Log("PASS live session logout"); }
        catch (Exception ex) { Log("WARN logout " + ex.GetType().Name); }
    }
}

sealed class RunState
{
    public string? TextJobId { get; set; }
    public bool TextVerified { get; set; }
    public string? ImageJobId { get; set; }
    public string? ImageStatus { get; set; }
    public bool ImageVerified { get; set; }
    public int RuleCount { get; set; }
    public string? FormId { get; set; }
    public string? CopyFormId { get; set; }
    public bool FormsVerified { get; set; }
    public string? CancellationJobId { get; set; }
    public bool CancellationAccepted { get; set; }
    public bool CancellationVerified { get; set; }
}
