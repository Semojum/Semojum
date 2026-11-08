using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

using Semojum.Core.Integration;

namespace Semojum.Feature.Board;

public sealed record BoardAttachment(long AttachmentId, string FileName, long Size, string Url);
/// <summary>FromAuthor marks the writer's own follow-up question; operator answers and follow-ups arrive in writing order (2026-11-01).</summary>
public sealed record BoardAnswer(long AnswerId, string Content, bool Mine, string AnsweredAt, string UpdatedAt, bool FromAuthor = false);
/// <summary>Category (ACCOUNT · RESULT · BILLING · USAGE) and Status (WAITING · ANSWERED) are null from older servers and for posts written before categories.</summary>
public sealed record BoardPost(long PostId, string Title, string Content, string Author, bool Mine, bool Secret, bool Hidden,
    string CreatedAt, string UpdatedAt, long ViewCount, BoardAttachment[] Attachments, BoardAnswer[] Answers, string? Category = null, string? Status = null);
public sealed record BoardRow(long PostId, string Title, int AnswerCount, string Author, bool Secret, string CreatedAt, long ViewCount, string? NoticeId = null,
    string? Category = null, string? Status = null)
{
    public string Number => NoticeId is null ? PostId.ToString(CultureInfo.InvariantCulture) : "공지";
    public string DisplayTitle => (Secret ? "비밀 · " : "") + Title + (AnswerCount > 0 ? $" ({AnswerCount})" : "");
}
public sealed record BoardPage(int Page, int TotalCount, BoardRow[] Notices, BoardRow[] Items);
public sealed record BoardNotification(long NotificationId, long PostId, string Title, int AnswerCount, string LastAnsweredAt, bool Read)
{
    public string Display => $"{(Read ? "" : "● ")}{PostId}. {Title}\n운영자 답변 {AnswerCount}건 · {LastAnsweredAt}";
}
public sealed record BoardNotifications(int UnreadCount, BoardNotification[] Items);

/// <summary>Post categories in the Figma order; the server takes and returns the code (null for posts written before categories).</summary>
public static class BoardCategories
{
    public static readonly (string Code, string Label)[] All = [("ACCOUNT", "계정 · 발급"), ("RESULT", "점역 결과"), ("BILLING", "크레딧 · 결제"), ("USAGE", "사용법")];
    /// <summary>「점역 결과」, the Figma default and the server's when a new post sends none.</summary>
    public const string Default = "RESULT";
    public static string? Label(string? code) => All.FirstOrDefault(c => c.Code == code).Label;
}

public interface IBoardListSource
{
    Task<BoardPage> ListAsync(int page, string field, string keyword, bool mine, CancellationToken ct = default);
    /// <summary>Posts with at least one operator answer, anyone's public posts included and no notices (「답변 받은 글」).</summary>
    Task<BoardPage> ListAnsweredAsync(int page, string field, string keyword, CancellationToken ct = default);
}

public sealed class BoardClient(SemojumApiClient api) : IBoardListSource
{
    public string? CurrentLoginId => api.Account?.LoginId;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static T Read<T>(JsonElement e) => e.Deserialize<T>(Json) ?? throw new InvalidDataException("게시판 응답을 읽을 수 없습니다.");
    public Task<BoardPage> ListAsync(int page, string field, string keyword, bool mine, CancellationToken ct = default)
        => ListAsync($"page={page}&field={Uri.EscapeDataString(field)}&keyword={Uri.EscapeDataString(keyword)}&mine={mine.ToString().ToLowerInvariant()}", ct);
    public Task<BoardPage> ListAnsweredAsync(int page, string field, string keyword, CancellationToken ct = default)
        => ListAsync($"page={page}&field={Uri.EscapeDataString(field)}&keyword={Uri.EscapeDataString(keyword)}&mine=false&answered=true", ct);
    private async Task<BoardPage> ListAsync(string query, CancellationToken ct) => Read<BoardPage>(await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/posts?" + query, cancellationToken: ct));
    public async Task<BoardPost> ReadAsync(long id, CancellationToken ct = default) => Read<BoardPost>(await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/posts/" + id, cancellationToken: ct));
    public Task<JsonElement> NoticeAsync(string id, CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/notices/" + Uri.EscapeDataString(id), cancellationToken: ct);
    public Task DeleteAsync(long id, CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Delete, "/api/board/posts/" + id, cancellationToken: ct);
    /// <summary>The writer's follow-up on their own post; the post returns to WAITING until an operator answers again.</summary>
    public async Task<BoardAnswer> FollowUpAsync(long id, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 10000) throw new ArgumentException("이어 물을 내용을 1–10,000자로 입력해 주세요.");
        return Read<BoardAnswer>(await api.SendAuthenticatedAsync(HttpMethod.Post, $"/api/board/posts/{id}/follow-ups", new { content }, ct));
    }
    public async Task<BoardNotifications> NotificationsAsync(CancellationToken ct = default)
    {
        var result = Read<BoardNotifications>(await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/notifications", cancellationToken: ct));
        if (result.Items is null || result.UnreadCount < 0 || result.Items.Any(n => n is null || n.PostId < 1 || n.NotificationId < 1 || n.Title is null))
            throw new InvalidDataException("알림 목록의 서버 응답을 확인할 수 없습니다. 다시 불러와 주세요.");
        return result;
    }
    public Task ReadAllAsync(CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Patch, "/api/board/notifications/read-all", cancellationToken: ct);
    /// <summary>A null category sends none: a new post becomes 「점역 결과」 and an edited post keeps its category.</summary>
    public async Task<(long Id, BoardPost? Post)> SaveAsync(long? id, string title, string content, bool secret,
        long[] keepAttachmentIds, UploadFile[] files, string? category = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 100 || string.IsNullOrWhiteSpace(content) || content.Length > 10000)
            throw new ArgumentException("제목 1–100자, 본문 1–10,000자를 입력해 주세요.");
        if (category is not null && BoardCategories.Label(category) is null) throw new ArgumentException("게시글 분류를 다시 골라 주세요.");
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".hwp", ".hwpx", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".brf", ".zip" };
        if (files.Length + keepAttachmentIds.Length > 5 || files.Any(f => f.Bytes.LongLength > 10 * 1024 * 1024 || !extensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())))
            throw new ArgumentException("첨부는 허용된 문서·이미지 형식으로 최대 5개, 한 개 10 MB까지입니다.");
        Func<HttpContent> factory = () =>
        {
            var body = new MultipartFormDataContent();
            body.Add(new StringContent(title.Trim()), "title"); body.Add(new StringContent(content), "content");
            body.Add(new StringContent(secret.ToString().ToLowerInvariant()), "secret");
            if (category is not null) body.Add(new StringContent(category), "category");
            foreach (var key in keepAttachmentIds) body.Add(new StringContent(key.ToString(CultureInfo.InvariantCulture)), "keepAttachmentIds");
            foreach (var file in files)
            { var bytes = new ByteArrayContent(file.Bytes); bytes.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); body.Add(bytes, "files", Path.GetFileName(file.FileName)); }
            return body;
        };
        var result = await api.SendAuthenticatedAsync(id is null ? HttpMethod.Post : HttpMethod.Put,
            "/api/board/posts" + (id is null ? "" : "/" + id), factory, ct);
        return (result.GetProperty("postId").GetInt64(), id is null ? null : Read<BoardPost>(result));
    }
}
