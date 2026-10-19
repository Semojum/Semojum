using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed record BoardAttachment(long AttachmentId, string FileName, long Size, string Url);
public sealed record BoardAnswer(long AnswerId, string Content, bool Mine, string AnsweredAt, string UpdatedAt);
public sealed record BoardPost(long PostId, string Title, string Content, string Author, bool Mine, bool Secret, bool Hidden,
    string CreatedAt, string UpdatedAt, long ViewCount, BoardAttachment[] Attachments, BoardAnswer[] Answers);
public sealed record BoardRow(long PostId, string Title, int AnswerCount, string Author, bool Secret, string CreatedAt, long ViewCount, string? NoticeId = null)
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

public sealed class BoardClient(SemojumApiClient api)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static T Read<T>(JsonElement e) => e.Deserialize<T>(Json) ?? throw new InvalidDataException("게시판 응답을 읽을 수 없습니다.");
    public async Task<BoardPage> ListAsync(int page, string field, string keyword, bool mine, CancellationToken ct = default) => Read<BoardPage>(
        await api.SendAuthenticatedAsync(HttpMethod.Get, $"/api/board/posts?page={page}&field={Uri.EscapeDataString(field)}&keyword={Uri.EscapeDataString(keyword)}&mine={mine.ToString().ToLowerInvariant()}", cancellationToken: ct));
    public async Task<BoardPost> ReadAsync(long id, CancellationToken ct = default) => Read<BoardPost>(await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/posts/" + id, cancellationToken: ct));
    public Task<JsonElement> NoticeAsync(string id, CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/notices/" + Uri.EscapeDataString(id), cancellationToken: ct);
    public Task DeleteAsync(long id, CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Delete, "/api/board/posts/" + id, cancellationToken: ct);
    public async Task<BoardNotifications> NotificationsAsync(CancellationToken ct = default) => Read<BoardNotifications>(await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/board/notifications", cancellationToken: ct));
    public Task ReadAllAsync(CancellationToken ct = default) => api.SendAuthenticatedAsync(HttpMethod.Patch, "/api/board/notifications/read-all", cancellationToken: ct);
    public async Task<(long Id, BoardPost? Post)> SaveAsync(long? id, string title, string content, bool secret,
        long[] keepAttachmentIds, UploadFile[] files, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 100 || string.IsNullOrWhiteSpace(content) || content.Length > 10000)
            throw new ArgumentException("제목 1–100자, 본문 1–10,000자를 입력해 주세요.");
        var extensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".hwp", ".hwpx", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".brf", ".zip" };
        if (files.Length + keepAttachmentIds.Length > 5 || files.Any(f => f.Bytes.LongLength > 10 * 1024 * 1024 || !extensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant())))
            throw new ArgumentException("첨부는 허용된 문서·이미지 형식으로 최대 5개, 한 개 10 MB까지입니다.");
        Func<HttpContent> factory = () =>
        {
            var body = new MultipartFormDataContent();
            body.Add(new StringContent(title.Trim()), "title"); body.Add(new StringContent(content), "content");
            body.Add(new StringContent(secret.ToString().ToLowerInvariant()), "secret");
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
