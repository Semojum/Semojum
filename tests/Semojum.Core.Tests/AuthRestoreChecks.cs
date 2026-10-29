using System.Net;
using System.Text;
using System.Text.Json;
using Semojum.Core.Integration;

internal static class AuthRestoreChecks
{
    public static IEnumerable<(string Name, Func<Task> Run)> Tests()
    {
        yield return ("Remembered login uses refreshed server identity and rejects a different account", RestoreIdentity);
        yield return ("Old refresh responses require manual login without creating a session", LegacyRefresh);
        yield return ("Offline restore does not create a session or erase the remembered credential", OfflineRestore);
        yield return ("Token refresh updates the current server identity and preserves a legacy response", RefreshUpdatesIdentity);
        yield return ("A changed editor-ineligible role does not replay a rejected request", RefreshRejectsChangedRole);
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request); }
    private static HttpResponseMessage Ok(object result) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
    private static async Task RestoreIdentity()
    {
        using var http = new HttpClient(new Handler(req => Task.FromResult(Ok(new { accessToken = "new", loginId = "alice", role = "ROLE_USER", alias = "새 별칭" }))));
        var api = new SemojumApiClient(http);
        var account = await api.RestoreSessionAsync(new SessionCredential("alice", "refresh"));
        if (account?.LoginId != "alice" || account.Role != "ROLE_USER" || account.Alias != "새 별칭") throw new Exception("Server identity was not restored");
        using var otherHttp = new HttpClient(new Handler(req => Task.FromResult(Ok(new { accessToken = "new", loginId = "bob", role = "ROLE_USER" }))));
        var other = new SemojumApiClient(otherHttp);
        try { await other.RestoreSessionAsync(new SessionCredential("alice", "refresh")); throw new Exception("Account mismatch accepted"); }
        catch (SemojumApiException ex) when (ex.Code == "INVALID_RESPONSE") { }
        if (other.Account is not null) throw new Exception("Account mismatch created a session");
    }
    private static async Task LegacyRefresh()
    {
        using var http = new HttpClient(new Handler(req => Task.FromResult(Ok(new { accessToken = "new" }))));
        var api = new SemojumApiClient(http);
        if (await api.RestoreSessionAsync(new SessionCredential("alice", "refresh")) is not null || api.Account is not null)
            throw new Exception("Old server must require manual login");
    }
    private static async Task OfflineRestore()
    {
        var remembered = new SessionCredential("alice", "refresh");
        using var http = new HttpClient(new Handler(req => throw new HttpRequestException("offline")));
        var api = new SemojumApiClient(http);
        try { await api.RestoreSessionAsync(remembered); throw new Exception("Expected offline error"); }
        catch (HttpRequestException) { }
        if (api.Account is not null || remembered.RefreshToken != "refresh") throw new Exception("Offline restore lost credentials or created a session");
    }
    private static async Task RefreshUpdatesIdentity()
    {
        var retry = false;
        using var http = new HttpClient(new Handler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Ok(new { accessToken = "old", refreshToken = "refresh", role = "ROLE_USER", alias = "이전 이름" }));
            if (path.EndsWith("/refresh")) return Task.FromResult(Ok(new { accessToken = "new", loginId = "alice", role = "ROLE_ORG_ADMIN", alias = "변경된 이름" }));
            if (!retry) { retry = true; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}") }); }
            return Task.FromResult(Ok(new { value = 1 }));
        }));
        var api = new SemojumApiClient(http); await api.LoginAsync("alice", "pw");
        await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/usage");
        if (api.Account is not { LoginId: "alice", Role: "ROLE_ORG_ADMIN", Alias: "변경된 이름" })
            throw new Exception("The refreshed account identity was not updated");
    }
    private static async Task RefreshRejectsChangedRole()
    {
        var requestCount = 0;
        using var http = new HttpClient(new Handler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/login")) return Task.FromResult(Ok(new { accessToken = "old", refreshToken = "refresh", role = "ROLE_USER" }));
            if (path.EndsWith("/refresh")) return Task.FromResult(Ok(new { accessToken = "new", loginId = "alice", role = "ROLE_ORG_ADMIN" }));
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"AUTH4003\",\"message\":\"expired\"}") });
        }));
        var api = new SemojumApiClient(http) { RefreshedIdentityAllowed = identity => identity.Role != "ROLE_ORG_ADMIN" };
        await api.LoginAsync("alice", "pw");
        try { await api.SendAuthenticatedAsync(HttpMethod.Post, "/api/jobs/test/save", new { value = "saved" }); throw new Exception("Expected role rejection"); }
        catch (SemojumApiException ex) when (ex.Code == "AUTH_DESKTOP_ROLE") { }
        if (api.Account is not null || requestCount != 1) throw new Exception("Forbidden refreshed role replayed a request or retained the session");
    }
}
