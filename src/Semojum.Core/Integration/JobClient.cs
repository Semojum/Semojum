using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed record JobStatus(string JobId, int TotalPages, int CompletedPages, string OverallStatus, Dictionary<int, string> Pages)
{
    public bool IsTerminal => OverallStatus is "COMPLETED" or "FAILED" or "CANCELLED" or "CANCELED";
}
public sealed record JobCreated(string JobId, int TotalPages, string Status);
public sealed record JobSaveResult(int SavedPages, int[] FailedPages, string? SavedAt)
{
    public bool Complete(int expectedPages) => FailedPages.Length == 0 && SavedPages == expectedPages;
}
public sealed record UploadFile(string FileName, byte[] Bytes);
public sealed record RemotePage(int Number, string Status, EditorBlock[] Blocks, string? OriginalType,
    Uri? OriginalUrl, string[]? OriginalLines, int Width, int Height);

/// <summary>V3.1 job boundary. Saving does not create a job or invoke AI.</summary>
public sealed class JobClient(SemojumApiClient api)
{
    public const long MaximumUploadBytes = 100L * 1024 * 1024;
    public async Task<JobCreated> CreateTextAsync(string text, string fileName, DocumentSession settings, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("AI 변환할 본문을 입력해 주세요.");
        if (Encoding.UTF8.GetByteCount(text) > MaximumUploadBytes) throw new ArgumentException("텍스트는 100 MB까지 변환할 수 있습니다.");
        var result = await api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs/text", new
        {
            text, fileName, insertPageNumber = settings.Layout.ShowSourcePageNumber, footerText = settings.FooterText,
            layoutOptions = LayoutBody(settings.Layout), brailleOptions = OptionsBody(settings.Options)
        }, ct);
        return Created(result);
    }

    public async Task<JobCreated> UploadAsync(IReadOnlyList<UploadFile> files, DocumentSession settings, CancellationToken ct = default)
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
        if (data.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
        {
            var braille = Array(result, "braille_text_list").ToDictionary(x => Required(x, "id"));
            var boxes = Array(result, "bounding_box_list").ToDictionary(x => Required(x, "id"));
            if (result.TryGetProperty("image_resolution", out var size)) { width = Number(size, "width"); height = Number(size, "height"); }
            foreach (var item in Array(result, "text_list"))
            {
                var id = Required(item, "id"); var source = Contents(item);
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
                        type == "table" ? source : null),
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
        return new(page, status, blocks.ToArray(), originalType, url, lines, width, height);
    }

    public async Task<JobSaveResult> SaveAsync(SmjSnapshot snapshot, CancellationToken ct = default)
    {
        var m = snapshot.Manifest;
        if (string.IsNullOrWhiteSpace(m.JobId)) throw new InvalidOperationException("서버 작업이 없는 로컬 초안입니다. AI 변환으로 작업을 만든 후 서버에 저장할 수 있습니다.");
        if (api.Account?.LoginId != m.OwnerLoginId) throw new InvalidOperationException("이 작업을 만든 계정으로 로그인해 주세요.");
        if (m.JobStatus is "PENDING" or "IN_PROGRESS") throw new InvalidOperationException("변환이 끝난 뒤 서버에 저장해 주세요.");
        if (m.Blocks.Any(b => b.ServerId is null)) throw new InvalidOperationException("서버 id가 없는 새 블록의 저장 규칙이 아직 확정되지 않았습니다. 로컬 파일은 보존됩니다.");
        var pages = m.Blocks.GroupBy(b => b.Page).Select(page => new
        {
            pageNo = page.Key,
            text = page.Select(b => new { id = b.ServerId, contents = Lines(b.Source), selectedIdx = b.Metadata.SelectedIndex }).ToArray(),
            braille = page.Select(b => new { id = b.ServerId, contents = Lines(b.Braille), selectedIdx = (int?)null }).ToArray(),
            memos = page.SelectMany(b => b.Memos.Select(memo => new
            {
                memoId = memo.ServerId, panel = memo.Panel, elementId = b.ServerId, lineIndex = memo.LineIndex,
                lineText = memo.LineText, body = memo.Body, createdAt = memo.CreatedAt
            })).ToArray()
        }).ToArray();
        var data = await api.SendAuthenticatedAsync(HttpMethod.Post, JobPath(m.JobId, "save"), new
        {
            layoutOptions = LayoutBody(m.Layout), brailleOptions = OptionsBody(m.Options),
            insertPageNumber = m.Layout.ShowSourcePageNumber, footerText = m.FooterText, pages
        }, ct);
        return new(Number(data, "savedPages"), data.GetProperty("failedPages").Deserialize<int[]>() ?? throw new InvalidDataException("서버 저장 결과가 없습니다."), Text(data, "savedAt"));
    }

    public static Dictionary<string, object> LayoutBody(PageLayout l) => new()
    {
        ["cellsPerLine"] = l.Columns, ["linesPerPage"] = l.Lines, ["braillePageStart"] = l.StartPage,
        ["pageNumberLine"] = l.PageNumbers switch { "홀수 페이지" => "odd", "모든 페이지" => "every", _ => "none" },
        ["coverPages"] = l.CoverPages, ["sourcePageStart"] = l.SourcePageStart, ["showSourcePageNumber"] = l.ShowSourcePageNumber,
        ["showBraillePageNumber"] = l.ShowBraillePageNumber, ["showChangeLine"] = l.ShowChangeLine, ["footerAlign"] = l.FooterAlign,
        ["editScope"] = l.EditScope, ["advancedAi"] = l.AdvancedAi
    };
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
        Text(l, "pageNumberLine") switch { "every" => "모든 페이지", "none" => "출력 안 함", _ => "홀수 페이지" },
        CoverPages: Number(l, "coverPages"), SourcePageStart: Number(l, "sourcePageStart", 1), ShowSourcePageNumber: Bool(l, "showSourcePageNumber", true),
        ShowBraillePageNumber: Bool(l, "showBraillePageNumber", true), ShowChangeLine: Bool(l, "showChangeLine", true), FooterAlign: Text(l, "footerAlign") ?? "center",
        EditScope: Text(l, "editScope") ?? "all", AdvancedAi: Bool(l, "advancedAi"));
    public static BrailleOptions ReadOptions(JsonElement o) => new(Text(o, "hangulGrade") == "grade1", Text(o, "englishGrade") == "grade1",
        Text(o, "brailleRule") == "english", Text(o, "lineBreakUnit") ?? "syllable", Text(o, "mixedRule") ?? "korean",
        Text(o, "consecutiveSpace") ?? "single", Text(o, "choiceLayout") ?? "merge",
        o.TryGetProperty("visualDescriptions", out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Deserialize<Dictionary<string, VisualDescription>>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) : null);
    private static JobCreated Created(JsonElement e) => new(Required(e, "jobId"), Number(e, "totalPages"), Required(e, "status"));
    private static string JobPath(string id, string suffix) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return $"/api/jobs/{Uri.EscapeDataString(id)}/{suffix}"; }
    private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    private static string Contents(JsonElement e) => string.Join("\n", Array(e, "contents").Select(x => x.GetString() ?? ""));
    private static JsonElement[] Array(JsonElement e, string name) => e.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToArray() : [];
    private static string Required(JsonElement e, string name) => Text(e, name) ?? throw new InvalidDataException("서버 응답에 필수 항목이 없습니다: " + name);
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static int Number(JsonElement e, string name, int fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : fallback;
    private static bool Bool(JsonElement e, string name, bool fallback = false) => e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;
}
