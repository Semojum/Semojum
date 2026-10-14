using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Semojum.Core.Integration;

public sealed class SemojumApiException(HttpStatusCode status, string code, string message, JsonElement? details = null)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
    public string Code { get; } = code;
    public JsonElement? Details { get; } = details;
}

public sealed record AccountIdentity(string LoginId, string Role, string? AdminScope);

/// <summary>V3.1 envelope and in-memory authentication. The caller owns the HttpClient.</summary>
public sealed class SemojumApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly Uri origin;
    private readonly SemaphoreSlim authGate = new(1, 1);
    private Session? session;
    private sealed record Session(Guid Id, AccountIdentity Account, string AccessToken, string RefreshToken);
    private sealed record LoginResult(string AccessToken, string RefreshToken, string Role, string? AdminScope);
    private sealed record RefreshResult(string AccessToken);
    public AccountIdentity? Account => Volatile.Read(ref session)?.Account;

    public SemojumApiClient(HttpClient http, Uri? baseAddress = null)
    {
        this.http = http;
        origin = baseAddress ?? new Uri("https://api.semojum.app/");
        if (!origin.IsAbsoluteUri || (origin.Scheme != "https" && !(origin.Scheme == "http" && origin.IsLoopback))
            || origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 || origin.UserInfo.Length != 0)
            throw new ArgumentException("API 주소는 HTTPS 서버의 기본 주소여야 합니다.", nameof(baseAddress));
    }

    public async Task<AccountIdentity> LoginAsync(string loginId, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginId);
        ArgumentException.ThrowIfNullOrEmpty(password);
        await authGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await SendOnceAsync(HttpMethod.Post, "/api/auth/login", new { loginId, password }, null, cancellationToken).ConfigureAwait(false);
            var tokens = result.Deserialize<LoginResult>(Json) ?? throw InvalidResponse();
            if (string.IsNullOrWhiteSpace(tokens.AccessToken) || string.IsNullOrWhiteSpace(tokens.RefreshToken)
                || tokens.Role is not ("ROLE_USER" or "ROLE_ADMIN" or "ROLE_ORG_ADMIN")) throw InvalidResponse();
            var account = new AccountIdentity(loginId, tokens.Role, tokens.AdminScope);
            Volatile.Write(ref session, new Session(Guid.NewGuid(), account, tokens.AccessToken, tokens.RefreshToken));
            return account;
        }
        finally { authGate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await authGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Session? old;
        try { old = Interlocked.Exchange(ref session, null); }
        finally { authGate.Release(); }
        if (old is not null)
            await SendOnceAsync(HttpMethod.Post, "/api/auth/logout", new { refreshToken = old.RefreshToken }, null, cancellationToken).ConfigureAwait(false);
    }

    public Task<JsonElement> GetPublicAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!path.StartsWith("/api/public/", StringComparison.Ordinal)) throw new ArgumentException("공개 API 경로가 아닙니다.", nameof(path));
        return SendOnceAsync(HttpMethod.Get, path, null, null, cancellationToken);
    }

    public async Task<JsonElement> SendAuthenticatedAsync(HttpMethod method, string path, object? body = null,
        CancellationToken cancellationToken = default)
    {
        var before = Volatile.Read(ref session) ?? throw AuthenticationRequired();
        try { return await SendOnceAsync(method, path, body, before.AccessToken, cancellationToken).ConfigureAwait(false); }
        catch (SemojumApiException ex) when (ex.Status == HttpStatusCode.Unauthorized && ex.Code is "AUTH4003" or "COMMON4001")
        {
            var refreshed = await RefreshAsync(before, cancellationToken).ConfigureAwait(false);
            // Only a rejected authentication may be retried; network errors and partial saves are never replayed.
            try { return await SendOnceAsync(method, path, body, refreshed.AccessToken, cancellationToken).ConfigureAwait(false); }
            catch (SemojumApiException retryError) when (retryError.Code is "AUTH4003" or "AUTH4004")
            { ClearIfCurrent(refreshed); throw; }
        }
        catch (SemojumApiException ex) when (ex.Code == "AUTH4004")
        { ClearIfCurrent(before); throw; }
    }

    private async Task<Session> RefreshAsync(Session rejected, CancellationToken cancellationToken)
    {
        await authGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = Volatile.Read(ref session);
            if (current is null || current.Id != rejected.Id) throw AuthenticationRequired();
            if (current.AccessToken != rejected.AccessToken) return current;
            try
            {
                var result = await SendOnceAsync(HttpMethod.Post, "/api/auth/refresh", new { refreshToken = current.RefreshToken }, null, cancellationToken).ConfigureAwait(false);
                var tokens = result.Deserialize<RefreshResult>(Json) ?? throw InvalidResponse();
                if (string.IsNullOrWhiteSpace(tokens.AccessToken)) throw InvalidResponse();
                var updated = current with { AccessToken = tokens.AccessToken };
                Volatile.Write(ref session, updated);
                return updated;
            }
            catch (SemojumApiException ex) when (ex.Code is "AUTH4003" or "AUTH4004" or "USER4001")
            { ClearIfCurrent(current); throw; }
        }
        finally { authGate.Release(); }
    }

    private void ClearIfCurrent(Session rejected)
    {
        // A late response must not sign out a newer login or a newer token.
        Interlocked.CompareExchange(ref session, null, rejected);
    }

    private async Task<JsonElement> SendOnceAsync(HttpMethod method, string path, object? body, string? token, CancellationToken cancellationToken)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('#'))
            throw new ArgumentException("API 경로가 올바르지 않습니다.", nameof(path));
        var destination = new Uri(origin, path);
        if (destination.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority)
            || !destination.AbsolutePath.StartsWith("/api/", StringComparison.Ordinal))
            throw new ArgumentException("API 서버 밖으로 요청할 수 없습니다.", nameof(path));
        using var request = new HttpRequestMessage(method, destination);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is Func<HttpContent> contentFactory) request.Content = contentFactory();
        else if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        JsonDocument document;
        try { document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)); }
        catch (JsonException)
        {
            throw new SemojumApiException(response.StatusCode, "INVALID_RESPONSE",
                response.StatusCode == HttpStatusCode.RequestEntityTooLarge ? "업로드 용량 제한을 초과했습니다." : "서버 응답을 읽을 수 없습니다.");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            var success = root.TryGetProperty("isSuccess", out var ok) && ok.ValueKind == JsonValueKind.True;
            var result = root.TryGetProperty("result", out var value) ? value.Clone() : JsonSerializer.SerializeToElement<object?>(null);
            if (!response.IsSuccessStatusCode || !success)
            {
                var code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "INVALID_RESPONSE";
                var message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : "요청을 처리하지 못했습니다.";
                throw new SemojumApiException(response.StatusCode, code, message, result);
            }
            return result;
        }
    }

    private static SemojumApiException AuthenticationRequired() => new(HttpStatusCode.Unauthorized, "AUTH4003", "다시 로그인해 주세요.");
    private static SemojumApiException InvalidResponse() => new(HttpStatusCode.BadGateway, "INVALID_RESPONSE", "서버 응답 형식이 올바르지 않습니다.");
}
