using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Semojum.Core.Integration;

/// <summary>
/// Server status. <see cref="CompletedPages"/> is the server's <c>completedPages</c>, which counts every finished page —
/// successful (COMPLETED/NEEDS_REVIEW) and failed (BLOCKED) alike (BE JobService.getJobStatus). Never add failures to it again.
/// </summary>
public sealed record JobStatus(string JobId, int TotalPages, int CompletedPages, string OverallStatus, Dictionary<int, string> Pages)
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
public sealed record SaveElement(string? Id, string[] Contents, int? SelectedIdx);
public sealed record SaveMemo(long? MemoId, string Panel, string ElementId, int LineIndex, string LineText, string Body, string CreatedAt);
/// <summary>One page of the save request. A null panel means "leave unchanged"; an empty array deletes every block of that panel (BE PageContentSaver).</summary>
public sealed record SavePage(int PageNo, SaveElement[]? Text, SaveElement[]? Braille, SaveMemo[]? Memos);
/// <summary>What one explicit save will send, and what stays local because the current contract cannot carry it.</summary>
public sealed record JobSavePlan(SavePage[] Pages, string[] LocalOnly, int LocalBrailleBlocks, int LocalMemoStates, int EmptiedPages, int SkippedPlaceholderPages)
{
    public int ExpectedPages => Pages.Length;
}
public sealed record JobSaveResult(int SavedPages, int[] FailedPages, string? SavedAt, JobSavePlan Plan)
{
    /// <summary>Every planned page was confirmed by the server. Local-only items are reported separately and never counted as synced.</summary>
    public bool Complete => FailedPages.Length == 0 && SavedPages == Plan.ExpectedPages;
    public IEnumerable<SavePage> SavedPagesInPlan => Plan.Pages.Where(p => !FailedPages.Contains(p.PageNo));
}
public sealed record UploadFile(string FileName, byte[] Bytes);
public sealed record RemotePage(int Number, string Status, EditorBlock[] Blocks, string? OriginalType,
    Uri? OriginalUrl, string[]? OriginalLines, int Width, int Height,
    ServerBlockRecord[]? ServerBlocks = null, QualityIssue[]? Quality = null);

/// <summary>V3.1 job boundary. Saving does not create a job or invoke AI.</summary>
public sealed class JobClient(SemojumApiClient api)
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
        return new(jobId, Number(data, "totalPages"), Number(data, "completedPages"), Required(data, "overallStatus"), pages);
    }

    public Task<JsonElement> CancelAsync(string jobId, CancellationToken ct = default) =>
        api.SendAuthenticatedAsync(HttpMethod.Post, JobPath(jobId, "cancel"), cancellationToken: ct);

    public async Task ReadSettingsAsync(DocumentSession model, CancellationToken ct = default)
    {
        var data = await api.SendAuthenticatedAsync(HttpMethod.Get, JobPath(model.JobId!, "options"), cancellationToken: ct);
        if (data.TryGetProperty("layoutOptions", out var l)) model.Layout = ReadLayout(l);
        if (data.TryGetProperty("brailleOptions", out var b)) model.Options = ReadOptions(b);
        model.FooterText = Text(data, "footerText") ?? "";
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
                    block.Memos.Add(new(Guid.NewGuid().ToString("N"), memo.TryGetProperty("memoId", out var mid) && mid.TryGetInt64(out var n) ? n : null,
                        Required(memo, "panel"), Number(memo, "lineIndex"), Text(memo, "lineText") ?? "", Required(memo, "body"),
                        DateTimeOffset.TryParse(Text(memo, "createdAt"), out var date) ? date : DateTimeOffset.Now));
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
    }

    /// <summary>
    /// Builds the save request from a frozen snapshot without touching the server. Rules under the current contract
    /// (BE 5d8dda0a, PageContentSaver): a block missing from a sent panel is deleted, a null panel is left alone, and ids must
    /// already exist. So every server block of a page — blocked ones included — is sent with its current source, the braille
    /// panel is sent only for ids the server is known to hold, and failed/pending placeholder blocks (no server id) are never
    /// sent. Anything the contract cannot express is listed in <see cref="JobSavePlan.LocalOnly"/> instead of being dropped.
    /// </summary>
    public static JobSavePlan PlanSave(SmjManifest m)
    {
        var fresh = m.Blocks.Where(b => b.ServerId is null && !b.Metadata.IsBlocked).ToArray();
        if (fresh.Length > 0)
            throw new InvalidOperationException($"서버 id가 없는 새 블록 {fresh.Length}개({string.Join(", ", fresh.Select(b => b.Page).Distinct().OrderBy(p => p).Take(5).Select(p => p + "쪽"))})의 저장 규칙(BE B-01)이 아직 확정되지 않았습니다. 서버 저장을 중단하고 로컬 파일만 보존합니다.");
        var duplicated = m.Blocks.Where(b => b.ServerId is not null).GroupBy(b => (b.Page, b.ServerId)).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
        if (duplicated.Length > 0)
            throw new InvalidOperationException($"같은 서버 id를 가진 블록이 {duplicated.Length}건 중복되어 서버가 저장을 거부합니다({duplicated[0].Page}쪽). 블록을 정리한 뒤 다시 저장해 주세요. 로컬 파일은 보존됩니다.");
        var known = m.ServerPages ?? []; // v1/v2 files and pages never read from the server have no record
        var pages = new List<SavePage>(); var notes = new List<string>();
        int localBraille = 0, localMemoStates = 0, emptied = 0, skipped = 0, placeholderMemos = 0;
        foreach (var page in m.Blocks.Select(b => b.Page).Concat(known.Keys).Distinct().OrderBy(p => p))
        {
            var blocks = m.Blocks.Where(b => b.Page == page).ToArray();
            var serverBlocks = blocks.Where(b => b.ServerId is not null).ToArray();
            var placeholders = blocks.Where(b => b.ServerId is null).ToArray();
            placeholderMemos += placeholders.Sum(b => b.Memos.Length);
            var record = known.TryGetValue(page, out var r) ? r : null;
            if (serverBlocks.Length == 0)
            {
                if (record is { Length: > 0 })
                {
                    // Every server block of this page was deleted locally. The contract can empty a page but cannot delete it.
                    SaveElement[]? emptyBraille = record.Any(x => x.HasBraille) ? [] : null;
                    pages.Add(new SavePage(page, [], emptyBraille, []));
                    emptied++; notes.Add($"{page}쪽: 블록을 모두 지워 서버에는 빈 쪽으로 남습니다(쪽 삭제는 서버 계약 미지원).");
                }
                else if (placeholders.Length > 0) skipped++;
                continue;
            }
            var text = serverBlocks.Select(b => new SaveElement(b.ServerId, Lines(b.Source), b.Metadata.SelectedIndex)).ToArray();
            SaveElement[]? braille;
            if (record is null)
            {
                // No record of which ids have braille on the server (file saved before tracking, or page never read): sending
                // any braille array could delete or reject, so the panel is left untouched and reported.
                braille = null;
                var count = serverBlocks.Count(b => b.Braille.Length > 0);
                if (count > 0) { localBraille += count; notes.Add($"{page}쪽: 서버 점자 보유 정보가 없어 점자 {count}개 블록은 .smj에만 보존합니다."); }
            }
            else
            {
                var held = record.Where(x => x.HasBraille).Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
                braille = held.Count == 0 ? null
                    : serverBlocks.Where(b => held.Contains(b.ServerId!)).Select(b => new SaveElement(b.ServerId, Lines(b.Braille), null)).ToArray();
                localBraille += serverBlocks.Count(b => !held.Contains(b.ServerId!) && b.Braille.Length > 0);
            }
            var memos = serverBlocks.SelectMany(b => b.Memos.Select(memo => new SaveMemo(memo.ServerId, memo.Panel, b.ServerId!, memo.LineIndex,
                memo.LineText, memo.Body, memo.CreatedAt.DateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)))).ToArray();
            localMemoStates += serverBlocks.Sum(b => b.Memos.Count(x => x.Resolved));
            pages.Add(new(page, text, braille, memos));
        }
        if (localBraille > 0) notes.Insert(0, $"점자 {localBraille}개 블록은 서버가 아직 같은 id의 점자 항목을 만들지 못해(BE B-01) .smj에만 보존됩니다.");
        if (localMemoStates > 0) notes.Add($"메모 해결 상태 {localMemoStates}건은 서버 계약(BE B-05)에 없어 .smj에만 보존됩니다.");
        if (placeholderMemos > 0) notes.Add($"변환 실패 표시 블록의 메모 {placeholderMemos}건은 서버 블록이 없어 .smj에만 보존됩니다.");
        notes.AddRange(ServerContract.LocalOnlyLayout(m.Layout, m.FooterRanges));
        return new(pages.ToArray(), notes.ToArray(), localBraille, localMemoStates, emptied, skipped);
    }

    /// <summary>Applies a server-confirmed save to the original document: pages the server accepted now hold exactly what was sent.</summary>
    public static void ApplySaved(DocumentSession model, JobSaveResult result)
    {
        foreach (var page in result.SavedPagesInPlan)
        {
            var previous = model.ServerPages.GetValueOrDefault(page.PageNo) ?? [];
            var sentBraille = page.Braille?.Select(b => b.Id!).ToHashSet(StringComparer.Ordinal);
            model.ServerPages[page.PageNo] = (page.Text ?? []).Select(t => new ServerBlockRecord(t.Id!,
                sentBraille?.Contains(t.Id!) ?? previous.FirstOrDefault(p => p.Id == t.Id)?.HasBraille ?? false)).ToArray();
        }
    }

    public async Task<JobSaveResult> SaveAsync(SmjSnapshot snapshot, CancellationToken ct = default)
    {
        var m = snapshot.Manifest;
        if (string.IsNullOrWhiteSpace(m.JobId)) throw new InvalidOperationException("서버 작업이 없는 로컬 초안입니다. 새 텍스트 작업을 만든 후 서버에 저장할 수 있습니다.");
        if (api.Account?.LoginId != m.OwnerLoginId) throw new InvalidOperationException("이 작업을 만든 계정으로 로그인해 주세요.");
        if (m.JobStatus is "PENDING" or "IN_PROGRESS") throw new InvalidOperationException("변환이 끝난 뒤 서버에 저장해 주세요.");
        var plan = PlanSave(m);
        var data = await api.SendAuthenticatedAsync(HttpMethod.Post, JobPath(m.JobId, "save"), new
        {
            layoutOptions = LayoutBody(m.Layout), brailleOptions = OptionsBody(m.Options),
            insertPageNumber = m.Layout.ShowSourcePageNumber, footerText = m.FooterText, pages = plan.Pages
        }, ct);
        return new(Number(data, "savedPages"), data.GetProperty("failedPages").Deserialize<int[]>() ?? throw new InvalidDataException("서버 저장 결과가 없습니다."), Text(data, "savedAt"), plan);
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
        return items.Select(r => new RuleEvidence(Text(r, "rule_id") ?? "", Text(r, "source") ?? "", Text(r, "section") ?? "",
            Text(r, "title") ?? "", Text(r, "excerpt") ?? "", Number(r, "line_no"), Number(r, "col_start"), Number(r, "col_end")))
            .Distinct().ToArray();
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
