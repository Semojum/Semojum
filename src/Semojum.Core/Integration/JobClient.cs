using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Semojum.Core.Integration;

/// <summary>
/// Server status. <see cref="CompletedPages"/> is the server's <c>completedPages</c>, which counts every finished page —
/// successful (COMPLETED/NEEDS_REVIEW) and failed (BLOCKED) alike (BE JobService.getJobStatus). Never add failures to it again.
/// </summary>
public sealed record JobStatus(string JobId, int TotalPages, int CompletedPages, string OverallStatus, Dictionary<int, string> Pages, bool CreditExhausted = false)
{
    public bool IsTerminal => OverallStatus is "COMPLETED" or "FAILED" or "CANCELLED" or "CANCELED";
    public static bool IsFailedPage(string status) => status is "FAILED" or "BLOCKED" or "CANCELLED" or "CANCELED";
    public static bool IsSucceededPage(string status) => status is "COMPLETED" or "NEEDS_REVIEW";
    public int FailedPages => Pages.Values.Count(IsFailedPage);
    public int SucceededPages => Pages.Count > 0 ? Pages.Values.Count(IsSucceededPage) : Math.Max(0, CompletedPages - FailedPages);
    public int FinishedPages => Math.Min(TotalPages, SucceededPages + FailedPages);
    public int RunningPages => Pages.Values.Count(s => s is "RUNNING" or "PROCESSING");
    public int RemainingPages => Math.Max(0, TotalPages - FinishedPages);
    public ConversionProgress ToProgress(string estimate) => new(TotalPages, SucceededPages, FailedPages, RunningPages, estimate);
    public string Label => OverallStatus switch
    {
        "PENDING" => "대기 중", "IN_PROGRESS" => "변환 중", "COMPLETED" => "변환 완료", "FAILED" => "변환 실패",
        "CANCELLED" or "CANCELED" => "취소됨", _ => OverallStatus
    };
}
public sealed record JobCreated(string JobId, int TotalPages, string Status);
public sealed record JobCancellation(bool Accepted, string Status, int InFlightPages)
{
    public bool IsTerminal => Status is "COMPLETED" or "FAILED" or "CANCELLED" or "CANCELED";
    public string Notice(bool alreadyFinished = false) => !Accepted ? "이미 변환이 끝난 작업입니다. 변환 결과를 확인해 주세요." :
        IsTerminal || alreadyFinished ? "변환 중단 처리가 끝났습니다. 처리된 쪽은 계속 편집할 수 있습니다." :
        "변환 중단을 요청했습니다. 처리 중인 쪽이 마무리되면 반영됩니다.";
}
public sealed record SaveElement(string? Id, string[] Contents, int? SelectedIdx);
public sealed record SaveReply(string? Author, string Body, string CreatedAt);
public sealed record SaveMemo(long? MemoId, string Panel, string ElementId, int LineIndex, string LineText, string Body, string CreatedAt,
    string? Author = null, bool Resolved = false, string? ResolvedAt = null, SaveReply[]? Replies = null);
/// <summary>One page of the save request. A null panel means "leave unchanged"; an empty array deletes every block of that panel (BE PageContentSaver).</summary>
public sealed record SavePage(int PageNo, SaveElement[]? Text, SaveElement[]? Braille, SaveMemo[]? Memos);
/// <summary>What one explicit save will send, and what stays local because the current contract cannot carry it.</summary>
public sealed record JobSavePlan(SavePage[] Pages, string[] LocalOnly, int LocalBrailleBlocks, int LocalMemoStates, int EmptiedPages, int SkippedPlaceholderPages, int? TotalPages = null)
{
    public int ExpectedPages => Pages.Length;
}
public sealed record SavePageResult(int PageNo, string Status, string? ErrorCode);
public sealed record JobSaveResult(int SavedPages, int[] FailedPages, string? SavedAt, JobSavePlan Plan,
    string? JobId = null, string? CopiedFromJobId = null, string? OwnerLoginId = null, SavePageResult[]? PageResults = null)
{
    /// <summary>Every planned page was confirmed by the server. Local-only items are reported separately and never counted as synced.</summary>
    public bool Complete => FailedPages.Length == 0 && SavedPages == Plan.ExpectedPages;
    public IEnumerable<SavePage> SavedPagesInPlan => PageResults is not null
        ? Plan.Pages.Where(p => PageResults.Any(r => r.PageNo == p.PageNo && r.Status == "SAVED"))
        : SavedPages + FailedPages.Length == Plan.ExpectedPages ? Plan.Pages.Where(p => !FailedPages.Contains(p.PageNo)) : [];
}
public sealed record UploadFile(string FileName, byte[] Bytes);
public sealed record RemotePage(int Number, string Status, EditorBlock[] Blocks, string? OriginalType,
    Uri? OriginalUrl, string[]? OriginalLines, int Width, int Height,
    ServerBlockRecord[]? ServerBlocks = null, QualityIssue[]? Quality = null);

/// <summary>V3.1 job boundary. Saving does not create a job or invoke AI.</summary>
public sealed partial class JobClient(SemojumApiClient api)
{
    public const long MaximumUploadBytes = 100L * 1024 * 1024;
    public async Task<JobCreated> CreateTextAsync(string fileName, DocumentSession settings, CancellationToken ct = default, string? formId = null)
    {
        var result = await api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs/text", new
        {
            fileName, formId, insertPageNumber = settings.Layout.ShowSourcePageNumber, footerText = settings.FooterText,
            layoutOptions = LayoutBody(settings.Layout), brailleOptions = OptionsBody(settings.Options)
        }, ct);
        return Created(result);
    }

    public async Task<JobCreated> UploadAsync(IReadOnlyList<UploadFile> files, DocumentSession settings, CancellationToken ct = default, string? formId = null)
    {
        var allowed = new[] { ".pdf", ".hwp", ".hwpx", ".txt", ".png", ".jpg", ".jpeg" };
        if (files.Count == 0 || files.Any(f => !allowed.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())))
            throw new ArgumentException("PDF, HWP, HWPX, TXT, PNG, JPG 파일을 선택해 주세요.");
        if (files.Sum(f => f.Bytes.LongLength) > MaximumUploadBytes) throw new ArgumentException("업로드 전체 크기는 100 MB까지입니다.");
        if (files.Count > 1 && files.Any(f => Path.GetExtension(f.FileName).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg")))
            throw new ArgumentException("여러 파일은 이미지일 때만 함께 업로드할 수 있습니다.");
        // Each retry needs fresh content; requests are replayed only after an authentication rejection.
        Func<HttpContent> body = () =>
        {
            var form = new MultipartFormDataContent();
            foreach (var file in files)
            {
                var content = new ByteArrayContent(file.Bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(Path.GetExtension(file.FileName).ToLowerInvariant() switch
                { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".pdf" => "application/pdf", ".txt" => "text/plain", _ => "application/octet-stream" });
                form.Add(content, "file", Path.GetFileName(file.FileName));
            }
            foreach (var pair in LayoutBody(settings.Layout).Concat(OptionsBody(settings.Options)).Where(p => p.Key != "visualDescriptions"))
                form.Add(new StringContent(pair.Value is string s ? s : JsonSerializer.Serialize(pair.Value)), pair.Key);
            if (formId is not null) form.Add(new StringContent(formId), "formId");
            form.Add(new StringContent(settings.FooterText), "footerText");
            form.Add(new StringContent(settings.Layout.ShowSourcePageNumber ? "true" : "false"), "insertPageNumber");
            return form;
        };
        return Created(await api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs", body, ct));
    }

    public async Task<JobStatus> StatusAsync(string jobId, CancellationToken ct = default)
    {
        var data = await api.SendAuthenticatedAsync(HttpMethod.Get, JobPath(jobId, "status"), cancellationToken: ct);
        var pages = new Dictionary<int, string>();
        if (data.TryGetProperty("pages", out var items))
            foreach (var item in items.EnumerateObject())
            {
                if (!int.TryParse(item.Name.Replace("page:", "", StringComparison.Ordinal), out var page) || page < 1)
                    throw new InvalidDataException("서버 쪽 번호가 올바르지 않습니다.");
                pages.Add(page, item.Value.GetString() ?? "UNKNOWN");
            }
        return new(jobId, Number(data, "totalPages"), Number(data, "completedPages"), Required(data, "overallStatus"), pages, Bool(data, "creditExhausted"));
    }

    public async Task<JobCancellation> CancelAsync(string jobId, CancellationToken ct = default)
    {
        var result = await api.SendAuthenticatedAsync(HttpMethod.Post, JobPath(jobId, "cancel"), cancellationToken: ct);
        return new(result.GetProperty("canceled").GetBoolean(), Required(result, "status"), Number(result, "inFlightPages"));
    }

    public async Task ReadSettingsAsync(DocumentSession model, CancellationToken ct = default, DocumentSession? uploadSettings = null)
    {
        var data = await api.SendAuthenticatedAsync(HttpMethod.Get, JobPath(model.JobId!, "options"), cancellationToken: ct);
        if (data.TryGetProperty("layoutOptions", out var l)) model.Layout = ReadLayout(l);
        if (data.TryGetProperty("brailleOptions", out var b)) model.Options = ReadOptions(b);
        model.FooterText = Text(data, "footerText") ?? "";
        // The upload dialog is the source of truth for this new local document. Server B-06 omits
        // settings such as even pages and number ranges; its defaults must not erase the user's choices.
        // Opening an existing server job has no local override and continues to use server values.
        if (uploadSettings is not null)
        {
            model.Layout = uploadSettings.Layout with { NumberRanges = uploadSettings.Layout.NumberRanges?.ToArray() };
            model.Options = uploadSettings.Options;
            model.FooterText = uploadSettings.FooterText;
            model.FooterRanges = uploadSettings.FooterRanges.ToArray();
        }
    }

    public async Task<RemotePage> PageAsync(string jobId, int page, int firstLocalId, CancellationToken ct = default)
    {
        if (page < 1) throw new ArgumentOutOfRangeException(nameof(page));
        var data = await api.SendAuthenticatedAsync(HttpMethod.Get,
            $"/api/users/jobs/{Uri.EscapeDataString(jobId)}/pages/{page}", cancellationToken: ct);
        return ReadPage(data, page, firstLocalId);
    }

    public static RemotePage ReadPage(JsonElement data, int page, int firstLocalId)
    {
        var blocks = new List<EditorBlock>();
        var status = Text(data, "status") ?? "COMPLETED";
        var width = 0; var height = 0;
        var records = new List<ServerBlockRecord>(); var quality = new List<QualityIssue>();
        if (data.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            var braille = Array(result, "braille_text_list").ToDictionary(x => Required(x, "id"));
            var boxes = Array(result, "bounding_box_list").ToDictionary(x => Required(x, "id"));
            if (result.TryGetProperty("image_resolution", out var size)) { width = Number(size, "width"); height = Number(size, "height"); }
            if (result.TryGetProperty("quality_report", out var report) && report.ValueKind == JsonValueKind.Object)
            {
                // Only fields the server already serializes (type/element_id/message). OCR confidence is not exposed and is never invented here.
                foreach (var (name, severity) in new[] { ("critical_errors", "error"), ("review_flags", "review") })
                    foreach (var issue in Array(report, name))
                        quality.Add(new(severity, Text(issue, "type") ?? "unknown", Text(issue, "element_id"), Text(issue, "message") ?? ""));
            }
            foreach (var item in Array(result, "text_list"))
            {
                var id = Required(item, "id"); var source = Contents(item);
                records.Add(new(id, braille.ContainsKey(id)));
                var type = Text(item, "type") ?? "text";
                var block = new EditorBlock
                {
                    Id = firstLocalId++, ServerId = id, Page = page,
                    Heading = source.Replace('\n', ' ') is var label && label.Length > 40 ? label[..40] : label,
                    Source = source, Braille = braille.TryGetValue(id, out var b) ? Contents(b) : "",
                    State = Bool(item, "is_blocked") ? "점역 불가 · 원문 확인" : braille.ContainsKey(id) ? "서버 저장 점자 · 검수 전" : "점역 전",
                    Metadata = new(type, Number(item, "heading_level"), Bool(item, "math_page") || Bool(result, "math_page"),
                        Text(item, "table_source"), Text(item, "render_mode"), Text(item, "tn_text"), Number(item, "table_box_level"),
                        boxes.TryGetValue(id, out var box) ? [Number(box, "x"), Number(box, "y"), Number(box, "x2"), Number(box, "y2")] : null,
                        item.TryGetProperty("selected_idx", out var index) && index.ValueKind == JsonValueKind.Number && index.TryGetInt32(out var i) ? i : null,
                        type == "table" ? source : null,
                        ReadDrafts(item, braille.GetValueOrDefault(id)), ReadRules(item, braille.GetValueOrDefault(id)), source,
                        Text(item, "visual_subtype"), Bool(item, "is_blocked")),
                    // A restored server result may contain manual edits. Do not overwrite on open.
                    HasManualBraille = braille.ContainsKey(id)
                };
                foreach (var memo in Array(data, "memos").Where(m => Text(m, "elementId") == id))
                    block.Memos.Add(new(Guid.NewGuid().ToString("N"), memo.TryGetProperty("memoId", out var mid) && mid.ValueKind == JsonValueKind.Number && mid.TryGetInt64(out var n) ? n : null,
                        Required(memo, "panel"), Number(memo, "lineIndex"), Text(memo, "lineText") ?? "", Required(memo, "body"),
                        ReadTimestamp(Text(memo, "createdAt")) ?? DateTimeOffset.Now, Bool(memo, "resolved"), Text(memo, "author"),
                        ReadTimestamp(Text(memo, "resolvedAt")), Array(memo, "replies").Select(reply => new MemoReply(Text(reply, "author"),
                            Required(reply, "body"), ReadTimestamp(Text(reply, "createdAt")) ?? DateTimeOffset.Now)).ToArray()));
                blocks.Add(block);
            }
        }
        string? originalType = null; Uri? url = null; string[]? lines = null;
        if (data.TryGetProperty("original", out var original) && original.ValueKind == JsonValueKind.Object)
        {
            originalType = Text(original, "type");
            if (Uri.TryCreate(Text(original, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https") url = uri;
            if (original.TryGetProperty("lines", out var value)) lines = value.Deserialize<string[]>();
        }
        return new(page, status, blocks.ToArray(), originalType, url, lines, width, height, records.ToArray(), quality.ToArray());
    }

    /// <summary>Records what the server holds for a page so a later save can plan deletions and the braille panel safely.</summary>
    public static void Remember(DocumentSession model, RemotePage page)
    {
        model.ServerPages[page.Number] = page.ServerBlocks ?? [];
        model.QualityReports[page.Number] = page.Quality ?? [];
        if (page.OriginalType is "text" or "txt") model.IsTextJob = true;
        else if (page.OriginalType is "pdf" or "image") model.IsTextJob = false;
    }

    /// <summary>Only values the server validates and stores (BE LayoutOptions). "짝수 페이지" has no server value, so the key is omitted rather than rewritten as "none".</summary>
    public static Dictionary<string, object> LayoutBody(PageLayout l)
    {
        var body = new Dictionary<string, object>
        {
            ["cellsPerLine"] = l.Columns, ["linesPerPage"] = l.Lines, ["braillePageStart"] = l.StartPage,
            ["coverPages"] = l.CoverPages, ["sourcePageStart"] = l.SourcePageStart, ["showSourcePageNumber"] = l.ShowSourcePageNumber,
            ["showBraillePageNumber"] = l.ShowBraillePageNumber, ["showChangeLine"] = l.ShowChangeLine, ["footerAlign"] = l.FooterAlign,
            ["editScope"] = l.EditScope, ["advancedAi"] = l.AdvancedAi
        };
        if (ServerContract.PageNumberLine(l.PageNumbers) is { } line) body["pageNumberLine"] = line;
        return body;
    }
    public static Dictionary<string, object> OptionsBody(BrailleOptions o)
    {
        var result = new Dictionary<string, object>
        {
        ["hangulGrade"] = o.KoreanGrade1 ? "grade1" : "grade2", ["englishGrade"] = o.EnglishGrade1 ? "grade1" : "grade2",
        ["brailleRule"] = o.EnglishDocument ? "english" : "korean", ["mixedRule"] = o.MixedRule,
        ["consecutiveSpace"] = o.ConsecutiveSpace, ["lineBreakUnit"] = o.LineBreakUnit, ["choiceLayout"] = o.ChoiceLayout
        };
        if (o.VisualDescriptions is not null) result["visualDescriptions"] = o.VisualDescriptions;
        return result;
    }
    public static PageLayout ReadLayout(JsonElement l) => new(Number(l, "cellsPerLine", 32), Number(l, "linesPerPage", 26), Number(l, "braillePageStart", 1),
        Text(l, "pageNumberLine") switch { "every" => "모든 페이지", "none" => "출력 안 함", "even" => "짝수 페이지", _ => "홀수 페이지" },
        CoverPages: Number(l, "coverPages"), SourcePageStart: Number(l, "sourcePageStart", 1), ShowSourcePageNumber: Bool(l, "showSourcePageNumber", true),
        ShowBraillePageNumber: Bool(l, "showBraillePageNumber", true), ShowChangeLine: Bool(l, "showChangeLine", true), FooterAlign: Text(l, "footerAlign") ?? "center",
        EditScope: Text(l, "editScope") ?? "all", AdvancedAi: Bool(l, "advancedAi"));
    public static BrailleOptions ReadOptions(JsonElement o) => new(Text(o, "hangulGrade") == "grade1", Text(o, "englishGrade") == "grade1",
        Text(o, "brailleRule") == "english", Text(o, "lineBreakUnit") ?? "syllable", Text(o, "mixedRule") ?? "korean",
        Text(o, "consecutiveSpace") ?? "single", Text(o, "choiceLayout") ?? "merge",
        o.TryGetProperty("visualDescriptions", out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Deserialize<Dictionary<string, VisualDescription>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null);
    private static JobCreated Created(JsonElement e) => new(Required(e, "jobId"), Number(e, "totalPages"), Required(e, "status"));
    private static AlternativeDraft[] ReadDrafts(JsonElement text, JsonElement braille)
    {
        var items = Array(text, "drafts");
        if (items.Length == 0 && braille.ValueKind == JsonValueKind.Object) items = Array(braille, "drafts");
        return items.Select(d => new AlternativeDraft(Text(d, "label") ?? "초안", Text(d, "text") ?? "",
            Text(d, "tn_text"), d.TryGetProperty("contents", out _) ? Contents(d) : null,
            Text(d, "reason") ?? Text(d, "rationale"))).ToArray();
    }
    private static RuleEvidence[] ReadRules(JsonElement text, JsonElement braille)
    {
        var items = Array(text, "rule_trail").Concat(braille.ValueKind == JsonValueKind.Object ? Array(braille, "rule_trail") : []);
        return items.Select(AppliedRules.Read).Distinct().ToArray();
    }
    private static string JobPath(string id, string suffix) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return $"/api/jobs/{Uri.EscapeDataString(id)}/{suffix}"; }
    // An empty panel body is sent as no lines; a server block that was never given contents stays unchanged that way.
    private static string[] Lines(string text) => text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static string Contents(JsonElement e) => string.Join("\n", Array(e, "contents").Select(x => x.GetString() ?? ""));
    private static JsonElement[] Array(JsonElement e, string name) => e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToArray() : [];
    private static string Required(JsonElement e, string name) => Text(e, name) ?? throw new InvalidDataException("서버 응답에 필수 항목이 없습니다: " + name);
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Number(JsonElement e, string name, int fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    private static bool Bool(JsonElement e, string name, bool fallback = false) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
}
