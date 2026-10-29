using System.Globalization;
using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed partial class JobClient
{
    private static string BlockId(SmjBlock block) => block.ServerId ?? block.ClientId
        ?? throw new InvalidOperationException("작업 파일을 다시 열어 블록 식별자를 준비한 뒤 저장해 주세요.");

    /// <summary>V3.1 (2026-10-21): both panels share an app-generated stable ID. Never infer IDs from prose or order.</summary>
    public static JobSavePlan PlanSave(SmjManifest manifest)
    {
        if (manifest.PendingServerPages is { Count: > 0 } || manifest.PendingRestart is not null)
            throw new InvalidOperationException("재개 결과와 로컬 교정이 충돌합니다. 작업 파일은 로컬에 저장되며, 결과를 확인하기 전에는 서버에 저장할 수 없습니다.");
        var blocks = manifest.Blocks.Where(b => b.ServerId is not null || !b.Metadata.IsBlocked).ToArray();
        foreach (var block in blocks)
        {
            var id = BlockId(block);
            if (id.Length is < 1 or > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                throw new InvalidOperationException($"{block.Page}쪽 블록 id 형식이 올바르지 않습니다. 로컬 파일은 보존됩니다.");
        }
        if (blocks.GroupBy(b => (b.Page, Id: BlockId(b))).FirstOrDefault(g => g.Count() > 1) is { } duplicate)
            throw new InvalidOperationException($"{duplicate.Key.Page}쪽에 중복 블록 id가 있습니다. 로컬 파일은 보존됩니다.");

        var known = manifest.ServerPages ?? [];
        var totalPages = manifest.IsTextJob == true ? manifest.Blocks.Max(b => b.Page) : (int?)null;
        var pages = new List<SavePage>(); var notes = new List<string>();
        var emptied = 0; var skipped = 0;
        foreach (var pageNo in manifest.Blocks.Select(b => b.Page).Concat(known.Keys).Distinct().Order())
        {
            if (totalPages.HasValue && pageNo > totalPages.Value) continue;
            var pageBlocks = blocks.Where(b => b.Page == pageNo).ToArray();
            var placeholders = manifest.Blocks.Where(b => b.Page == pageNo && b.ServerId is null && b.Metadata.IsBlocked).ToArray();
            var placeholderMemos = placeholders.Sum(b => b.Memos.Length);
            if (placeholderMemos > 0) notes.Add($"{pageNo}쪽: 변환 실패 표시 블록의 메모 {placeholderMemos}건은 서버 블록이 없어 .smj에만 보존됩니다.");
            if (pageBlocks.Length == 0)
            {
                if (placeholders.Length > 0) { skipped++; continue; }
                if (!known.ContainsKey(pageNo)) continue;
                emptied++;
            }
            var text = pageBlocks.Select(b => new SaveElement(BlockId(b), Lines(b.Source), b.Metadata.SelectedIndex)).ToArray();
            // The first braille block is created with its text block's ID, including empty/blocked blocks.
            var braille = pageBlocks.Select(b => new SaveElement(BlockId(b), Lines(b.Braille), b.Metadata.SelectedIndex)).ToArray();
            var memos = pageBlocks.SelectMany(b => b.Memos.Select(m => new SaveMemo(m.ServerId, m.Panel, BlockId(b), m.LineIndex,
                m.LineText, m.Body, Timestamp(m.CreatedAt), m.Author, m.Resolved,
                m.Resolved && m.ResolvedAt.HasValue ? Timestamp(m.ResolvedAt.Value) : null,
                (m.Replies ?? []).Select(r => new SaveReply(r.Author, r.Body, Timestamp(r.CreatedAt))).ToArray(),
                m.ServerId is null ? m.LocalId : null))).ToArray();
            pages.Add(new(pageNo, text, braille, memos));
        }
        notes.AddRange(ServerContract.LocalOnlyLayout(manifest.Layout, manifest.FooterRanges));
        return new(pages.ToArray(), notes.ToArray(), 0, 0, emptied, skipped, totalPages);
    }

    public static void ApplySaved(DocumentSession model, JobSaveResult result)
    {
        foreach (var page in result.SavedPagesInPlan)
        {
            var previous = model.ServerPages.GetValueOrDefault(page.PageNo) ?? [];
            var braille = page.Braille?.Select(b => b.Id!).ToHashSet(StringComparer.Ordinal);
            model.ServerPages[page.PageNo] = (page.Text ?? []).Select(t => new ServerBlockRecord(t.Id!,
                braille?.Contains(t.Id!) ?? previous.FirstOrDefault(p => p.Id == t.Id)?.HasBraille ?? false)).ToArray();
            foreach (var block in model.Blocks.Where(b => b.Page == page.PageNo && b.ServerId is null))
                if (page.Text?.Any(t => t.Id == block.ClientId) == true) block.ServerId = block.ClientId;
        }
        // BE B-05: new memos get their server id from pageResults[].memoIds; memos re-keyed by a copy job come back as previousMemoId.
        foreach (var page in result.PageResults ?? [])
        {
            if (page.Status != "SAVED" || page.MemoIds is not { Length: > 0 }) continue;
            foreach (var block in model.Blocks.Where(b => b.Page == page.PageNo))
                for (var i = 0; i < block.Memos.Count; i++)
                {
                    var memo = block.Memos[i];
                    var mapping = page.MemoIds.FirstOrDefault(m => memo.ServerId is null ? m.ClientMemoId == memo.LocalId : m.PreviousMemoId == memo.ServerId);
                    if (mapping is not null && memo.ServerId != mapping.MemoId) block.Memos[i] = memo with { ServerId = mapping.MemoId };
                }
        }
        if (result.Plan.TotalPages is { } total)
            foreach (var number in model.ServerPages.Keys.Where(n => n > total).ToArray()) model.ServerPages.Remove(number);
        if (result.JobId is not null) model.JobId = result.JobId;
        if (result.OwnerLoginId is not null) model.OwnerLoginId = result.OwnerLoginId;
    }

    /// <summary>Update only the saved snapshot's server identity; concurrent live edits remain unsaved.</summary>
    public static SmjSnapshot ApplySaved(SmjSnapshot snapshot, JobSaveResult result)
    {
        var model = SmjFile.Restore(snapshot.Manifest, "saved.smj");
        ApplySaved(model, result);
        var manifest = SmjFile.Snapshot(model, includeOriginals: false).Manifest with { Originals = snapshot.Manifest.Originals };
        return new(manifest, snapshot.Assets);
    }

    public Task<JobSaveResult> SaveAsync(SmjSnapshot snapshot, CancellationToken ct = default) =>
        SendSaveAsync(snapshot, PlanSave(snapshot.Manifest), snapshot.Manifest.JobId, ct);

    /// <summary>Explicit retry only: reuse frozen contents and send failed pages without totalPages (no repeated truncation).</summary>
    public Task<JobSaveResult> RetryFailedAsync(SmjSnapshot snapshot, JobSaveResult previous, CancellationToken ct = default)
    {
        if (previous.FailedPages.Length == 0) throw new InvalidOperationException("다시 저장할 실패 쪽이 없습니다.");
        var plan = previous.Plan with { Pages = previous.Plan.Pages.Where(p => previous.FailedPages.Contains(p.PageNo)).ToArray(), TotalPages = null };
        return SendSaveAsync(snapshot, plan, previous.JobId ?? snapshot.Manifest.JobId, ct);
    }

    private async Task<JobSaveResult> SendSaveAsync(SmjSnapshot snapshot, JobSavePlan plan, string? jobId, CancellationToken ct)
    {
        var m = snapshot.Manifest;
        if (string.IsNullOrWhiteSpace(jobId)) throw new InvalidOperationException("서버 작업이 없는 로컬 초안입니다. 새 텍스트 작업을 만든 후 서버에 저장할 수 있습니다.");
        var account = api.Account ?? throw new InvalidOperationException("서버에 저장하려면 로그인해 주세요.");
        if (m.JobStatus is "PENDING" or "IN_PROGRESS") throw new InvalidOperationException("변환이 끝난 뒤 서버에 저장해 주세요.");
        var body = new Dictionary<string, object?>
        {
            ["layoutOptions"] = LayoutBody(m.Layout), ["brailleOptions"] = OptionsBody(m.Options),
            ["insertPageNumber"] = m.Layout.ShowSourcePageNumber, ["footerText"] = m.FooterText, ["pages"] = plan.Pages
        };
        if (plan.TotalPages.HasValue) body["totalPages"] = plan.TotalPages.Value;
        var data = await api.SendAuthenticatedAsync(HttpMethod.Post, JobPath(jobId, "save"), body, ct);
        var failed = data.GetProperty("failedPages").Deserialize<int[]>() ?? throw new InvalidDataException("서버 저장 결과가 없습니다.");
        var saved = Number(data, "savedPages", -1);
        var expected = plan.Pages.Select(p => p.PageNo).ToHashSet();
        if (failed.Distinct().Count() != failed.Length || failed.Any(n => !expected.Contains(n)) || saved < 0 || saved + failed.Length != expected.Count)
            throw new InvalidDataException("서버 저장 결과의 쪽 수가 요청과 일치하지 않습니다. 로컬 파일은 보존됩니다.");
        SavePageResult[]? details = null;
        if (data.TryGetProperty("pageResults", out var pageResults) && pageResults.ValueKind != JsonValueKind.Null)
        {
            details = pageResults.EnumerateArray().Select(p => new SavePageResult(Number(p, "pageNo"), Required(p, "status"), Text(p, "errorCode"), ReadMemoIds(p))).ToArray();
            if (details.Length != expected.Count || details.Select(p => p.PageNo).Distinct().Count() != expected.Count
                || details.Any(p => !expected.Contains(p.PageNo) || p.Status is not ("SAVED" or "FAILED") || (p.Status == "FAILED") != failed.Contains(p.PageNo)))
                throw new InvalidDataException("서버의 쪽별 저장 결과가 일치하지 않습니다. 로컬 파일은 보존됩니다.");
        }
        var savedJobId = Text(data, "jobId") ?? jobId;
        var copiedFrom = Text(data, "copiedFromJobId");
        if (copiedFrom is not null && (copiedFrom != jobId || savedJobId == jobId)
            || savedJobId != jobId && copiedFrom != jobId
            || account.LoginId != m.OwnerLoginId && savedJobId == m.JobId)
            throw new InvalidDataException("서버 사본의 작업 ID를 확인하지 못했습니다. 원본 파일은 보존됩니다.");
        return new(saved, failed, Text(data, "savedAt"), plan, savedJobId, copiedFrom, account.LoginId, details);
    }

    private static MemoIdMapping[]? ReadMemoIds(JsonElement page) => page.TryGetProperty("memoIds", out var list) && list.ValueKind == JsonValueKind.Array
        ? list.EnumerateArray().Select(m => new MemoIdMapping(Text(m, "clientMemoId"),
            m.TryGetProperty("previousMemoId", out var previous) && previous.ValueKind == JsonValueKind.Number ? previous.GetInt64() : null,
            m.GetProperty("memoId").GetInt64())).ToArray() : null;
    private static string Timestamp(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(9)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    private static DateTimeOffset? ReadTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Server LocalDateTime values are KST, independent of the workstation's time zone.
        var time = value.Length > 10 ? value[10..] : "";
        if (!time.Contains('+') && !time.Contains('-') && !time.EndsWith('Z')) value += "+09:00";
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }
}
