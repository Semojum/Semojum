using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

public sealed record InstitutionMonthlyUsage(string Month, long Credits);
public sealed record InstitutionDashboard(string OrgName, string OrgCode, string ContractType, string? ContractStartedAt, string? ContractExpiresAt,
    long CreditAllocated, long CreditUsed, long CreditRemaining, InstitutionMonthlyUsage[] MonthlyUsage);
public sealed record InstitutionAccount(string LoginId, string? Alias, string Status, string Role, string? LastLoginAt, long UsedCredits, bool Self);
public sealed record InstitutionAccounts(string? UsageSince, InstitutionAccount[] Items);
public sealed record InstitutionAccountJob(string JobId, string FileName, string Mode, string Status, int TotalPages, int? DonePages,
    int? FailedPages, long? Credits, string? StartedAt, string? FinishedAt);
public sealed record InstitutionAccountJobs(string LoginId, string? Alias, string From, string To, InstitutionAccountJob[] Items, long TotalPages, long TotalCredits);
public sealed record InstitutionRequest(Guid Id, string Type, string Status, string? Message, string CreatedAt);
public sealed record InstitutionLockResult(string LoginId, string Status, int CanceledJobs);
public sealed record InstitutionOrder(Guid Id, string OrderDate, string Description, long AmountKrw, long? CreditAmount,
    string? PaidAt, string InvoiceStatus, string? ReceiptFileName);
public sealed record InstitutionOrders(string? ReceiptEmail, InstitutionOrder[] Items);
public sealed record InstitutionReceipt(string FileName, string Url);
public sealed record InstitutionNotice(Guid Id, string Scope, string Title, string Body, string? StartsOn, string? EndsOn, string CreatedAt);

/// <summary>Institution-scoped API; server authorization and response validation remain authoritative.</summary>
public sealed class InstitutionClient(SemojumApiClient api)
{
    private void RequireAdmin()
    {
        if (api.Account?.Role != "ROLE_ORG_ADMIN") throw new InvalidOperationException("기관 관리자 계정으로 다시 로그인해 주세요.");
    }
    private async Task<JsonElement> Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        RequireAdmin(); return await api.SendAuthenticatedAsync(method, path, body, ct);
    }
    private static InvalidDataException Invalid() => new("기관 관리 응답을 확인할 수 없습니다. 다시 시도해 주세요.");
    private static JsonElement Field(JsonElement value, string name)
    { if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var field)) throw Invalid(); return field; }
    private static string Text(JsonElement value, string name)
    { var field = Field(value, name); if (field.ValueKind != JsonValueKind.String) throw Invalid(); return field.GetString() ?? throw Invalid(); }
    private static string? OptionalText(JsonElement value, string name)
    { if (value.ValueKind != JsonValueKind.Object) throw Invalid(); if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null; return field.ValueKind == JsonValueKind.String ? field.GetString() : throw Invalid(); }
    private static long Number(JsonElement value, string name)
    { var field = Field(value, name); return field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var n) ? n : throw Invalid(); }
    private static int Int(JsonElement value, string name)
    { var n = Number(value, name); return n is >= int.MinValue and <= int.MaxValue ? (int)n : throw Invalid(); }
    private static int? OptionalInt(JsonElement value, string name)
    { if (value.ValueKind != JsonValueKind.Object) throw Invalid(); if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null; return field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var n) ? n : throw Invalid(); }
    private static long? OptionalLong(JsonElement value, string name)
    { if (value.ValueKind != JsonValueKind.Object) throw Invalid(); if (!value.TryGetProperty(name, out var field) || field.ValueKind == JsonValueKind.Null) return null; return field.ValueKind == JsonValueKind.Number && field.TryGetInt64(out var n) ? n : throw Invalid(); }
    private static bool Boolean(JsonElement value, string name)
    { var field = Field(value, name); return field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.GetBoolean() : throw Invalid(); }
    private static Guid Id(JsonElement value, string name) => Guid.TryParse(Text(value, name), out var id) && id != Guid.Empty ? id : throw Invalid();
    private static JsonElement.ArrayEnumerator Array(JsonElement value)
    { if (value.ValueKind != JsonValueKind.Array) throw Invalid(); return value.EnumerateArray(); }
    private static string LoginPath(string loginId)
    { if (string.IsNullOrWhiteSpace(loginId) || loginId is "." or ".." || loginId.Contains('/') || loginId.Contains('\\')) throw new ArgumentException("계정 ID를 확인해 주세요.", nameof(loginId)); return Uri.EscapeDataString(loginId); }
    private static string IdPath(Guid id) => id == Guid.Empty ? throw new ArgumentException("요청 식별자가 필요합니다.", nameof(id)) : id.ToString("D");

    public async Task<InstitutionDashboard> DashboardAsync(CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Get, "/api/org/dashboard", null, ct);
        return new(Text(r,"orgName"), Text(r,"orgCode"), Text(r,"contractType"), OptionalText(r,"contractStartedAt"), OptionalText(r,"contractExpiresAt"),
            Number(r,"creditAllocated"), Number(r,"creditUsed"), Number(r,"creditRemaining"),
            Array(Field(r,"monthlyUsage")).Select(x => new InstitutionMonthlyUsage(Text(x,"month"), Number(x,"credits"))).ToArray());
    }
    public async Task<InstitutionAccounts> AccountsAsync(CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Get, "/api/org/accounts", null, ct);
        return new(OptionalText(r,"usageSince"), Array(Field(r,"items")).Select(x => new InstitutionAccount(Text(x,"loginId"), OptionalText(x,"alias"),
            Text(x,"status"), Text(x,"role"), OptionalText(x,"lastLoginAt"), Number(x,"usedCredits"), Boolean(x,"self"))).ToArray());
    }
    public async Task<InstitutionAccountJobs> AccountJobsAsync(string loginId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        if (from is not null && to is not null && from > to) throw new ArgumentException("시작일은 종료일보다 늦을 수 없습니다.");
        var query = new List<string>();
        if (from is not null) query.Add("from=" + from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (to is not null) query.Add("to=" + to.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var r = await Send(HttpMethod.Get, "/api/org/accounts/" + LoginPath(loginId) + "/jobs" + (query.Count == 0 ? "" : "?" + string.Join("&", query)), null, ct);
        return new(Text(r,"loginId"), OptionalText(r,"alias"), Text(r,"from"), Text(r,"to"), Array(Field(r,"items")).Select(x => new InstitutionAccountJob(
            Text(x,"jobId"), Text(x,"fileName"), Text(x,"mode"), Text(x,"status"), Int(x,"totalPages"), OptionalInt(x,"donePages"), OptionalInt(x,"failedPages"),
            OptionalLong(x,"credits"), OptionalText(x,"startedAt"), OptionalText(x,"finishedAt"))).ToArray(), Number(r,"totalPages"), Number(r,"totalCredits"));
    }
    private static InstitutionRequest Request(JsonElement r) => new(Id(r,"id"), Text(r,"type"), Text(r,"status"), OptionalText(r,"message"), Text(r,"createdAt"));
    public async Task<InstitutionRequest[]> RequestsAsync(CancellationToken ct = default) => Array(await Send(HttpMethod.Get,"/api/org/requests",null,ct)).Select(Request).ToArray();
    public async Task<InstitutionRequest> CreateRequestAsync(string type, string? message, CancellationToken ct = default)
    {
        if (type is not ("CREDIT_ADD" or "ACCOUNT_ISSUE" or "ERROR_REPORT")) throw new ArgumentException("지원하지 않는 요청 유형입니다.", nameof(type));
        if (message?.Length > 1000) throw new ArgumentException("요청 내용은 1,000자까지 입력할 수 있습니다.", nameof(message));
        return Request(await Send(HttpMethod.Post,"/api/org/requests",new { type, message },ct));
    }
    public async Task CancelRequestAsync(Guid id, CancellationToken ct = default) => _ = await Send(HttpMethod.Delete,"/api/org/requests/" + IdPath(id),null,ct);
    public async Task SetAliasAsync(string loginId, string? alias, CancellationToken ct = default)
    {
        if (alias?.Length > 50) throw new ArgumentException("별칭은 50자까지 입력할 수 있습니다.", nameof(alias));
        _ = await Send(HttpMethod.Patch,"/api/org/accounts/" + LoginPath(loginId) + "/alias",new { alias },ct);
    }
    public async Task<InstitutionLockResult> SetLockAsync(string loginId, bool locked, CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Patch,"/api/org/accounts/" + LoginPath(loginId) + "/lock",new { locked },ct);
        return new(Text(r,"loginId"), Text(r,"status"), Int(r,"canceledJobs"));
    }
    public async Task<InstitutionOrders> OrdersAsync(CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Get,"/api/org/orders",null,ct);
        return new(OptionalText(r,"receiptEmail"), Array(Field(r,"items")).Select(x => new InstitutionOrder(Id(x,"id"), Text(x,"orderDate"), Text(x,"description"),
            Number(x,"amountKrw"), OptionalLong(x,"creditAmount"), OptionalText(x,"paidAt"), Text(x,"invoiceStatus"), OptionalText(x,"receiptFileName"))).ToArray());
    }
    public async Task<InstitutionReceipt> ReceiptAsync(Guid id, CancellationToken ct = default)
    {
        var r = await Send(HttpMethod.Get,"/api/org/orders/" + IdPath(id) + "/receipt",null,ct);
        var name = Text(r,"fileName"); var url = Text(r,"url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0) throw Invalid();
        return new(name,url);
    }
    public async Task<InstitutionNotice[]> NoticesAsync(CancellationToken ct = default) => Array(await Send(HttpMethod.Get,"/api/org/notices",null,ct))
        .Select(x => new InstitutionNotice(Id(x,"id"), Text(x,"scope"), Text(x,"title"), Text(x,"body"), OptionalText(x,"startsOn"), OptionalText(x,"endsOn"), Text(x,"createdAt"))).ToArray();
}
