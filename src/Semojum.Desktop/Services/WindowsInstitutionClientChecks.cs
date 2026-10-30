using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Semojum.Core.Integration;

namespace Semojum.Desktop.Services;

internal static class WindowsInstitutionClientChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        var fixture = new Handler(); using var http = new HttpClient(fixture);
        var api = new SemojumApiClient(http); await api.LoginAsync("org_admin", "fixture");
        var client = new InstitutionClient(api);
        var dashboard = await client.DashboardAsync(); var accounts = await client.AccountsAsync();
        check(dashboard.OrgName == "기관 A" && dashboard.CreditRemaining == -5 && dashboard.MonthlyUsage.Single().Credits == 2
            && accounts.Items.Single() is { LoginId: "a b", Alias: null, Self: true, UsedCredits: 7 },
            "Institution dashboard and account data retain server values including a negative balance and nulls");
        var jobs = await client.AccountJobsAsync("a b", new(2026, 10, 1), new(2026, 10, 30));
        check(jobs.Items.Single() is { JobId: "job-1", DonePages: null, FailedPages: 1, Credits: 4 } && jobs.TotalPages == 3
            && fixture.LastUri!.Contains("a%20b/jobs?from=2026-10-01&to=2026-10-30", StringComparison.OrdinalIgnoreCase),
            "Institution account detail escapes the login ID and retains actual per-job counts and dates");
        var orders = await client.OrdersAsync(); var receipt = await client.ReceiptAsync(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82"));
        var notices = await client.NoticesAsync(); var requests = await client.RequestsAsync();
        check(orders.Items.Single() is { AmountKrw: 1200, CreditAmount: null, ReceiptFileName: "receipt.pdf" }
            && receipt.Url.StartsWith("https://", StringComparison.Ordinal) && notices.Single().Scope == "ORG" && requests.Single().Status == "OPEN",
            "Institution orders, receipts, notices and request status use server data");
        check(fixture.Writes == 0, "Read-only institution screens make no mutation request");
        var created = await client.CreateRequestAsync("ACCOUNT_ISSUE", "계정 2개 요청");
        using var body = JsonDocument.Parse(fixture.LastBody!);
        check(created.Type == "ACCOUNT_ISSUE" && body.RootElement.GetProperty("type").GetString() == "ACCOUNT_ISSUE"
            && body.RootElement.GetProperty("message").GetString() == "계정 2개 요청" && fixture.LastMethod == HttpMethod.Post,
            "An explicit account request sends the confirmed request contract without creating an account");
        await client.SetAliasAsync("a b", "서가"); await client.SetLockAsync("a b", true); await client.CancelRequestAsync(created.Id);
        check(fixture.Writes == 4 && fixture.LastMethod == HttpMethod.Delete, "Alias, lock and request cancellation run only when explicitly called");
        var before = fixture.Writes;
        try { await client.CreateRequestAsync("UNKNOWN", "x"); check(false, "Unknown request type must be rejected"); } catch (ArgumentException) { }
        try { await client.CreateRequestAsync("CREDIT_ADD", new string('x', 1001)); check(false, "Oversize request must be rejected"); } catch (ArgumentException) { }
        try { await client.AccountJobsAsync("..", null, null); check(false, "Invalid account path must be rejected"); } catch (ArgumentException) { }
        check(fixture.Writes == before, "Invalid request type, size and account path never issue a write");
        fixture.Malformed = true;
        try { await client.AccountsAsync(); check(false, "Malformed account response must be rejected"); } catch (InvalidDataException) { check(true, "Malformed required institution account fields are rejected rather than invented"); }
        fixture.Malformed = false; fixture.BadReceipt = true;
        try { await client.ReceiptAsync(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82")); check(false, "Unsafe receipt URL must be rejected"); } catch (InvalidDataException) { check(true, "A non-HTTPS receipt URL is rejected"); }
        fixture.BadReceipt = false;
        var refreshFixture = new Handler { RejectOnce = true, RefreshRole = "ROLE_USER" };
        using (var refreshHttp = new HttpClient(refreshFixture))
        {
            var refreshApi = new SemojumApiClient(refreshHttp);
            refreshApi.RefreshedIdentityAllowed = next => AutomaticLogin.IsSameDesktopArea(refreshApi.Account, next);
            await refreshApi.LoginAsync("org_admin", "fixture");
            try { await new InstitutionClient(refreshApi).CreateRequestAsync("CREDIT_ADD", "fixture"); check(false, "Changed administrator role must stop request replay"); }
            catch (SemojumApiException ex) when (ex.Code == "AUTH_DESKTOP_ROLE")
            { check(refreshFixture.RequestPostCount == 1 && refreshApi.Account is null, "A refresh changing institution-admin role never replays the write"); }
        }
        var tokenOnlyFixture = new Handler { RejectOnce = true, TokenOnly = true };
        using (var tokenOnlyHttp = new HttpClient(tokenOnlyFixture))
        {
            var tokenOnlyApi = new SemojumApiClient(tokenOnlyHttp) { RefreshRequiresIdentity = account => account.Role == "ROLE_ORG_ADMIN" };
            tokenOnlyApi.RefreshedIdentityAllowed = next => AutomaticLogin.IsSameDesktopArea(tokenOnlyApi.Account, next);
            await tokenOnlyApi.LoginAsync("org_admin", "fixture");
            try { await new InstitutionClient(tokenOnlyApi).CreateRequestAsync("CREDIT_ADD", "fixture"); check(false, "An unverified role must stop request replay"); }
            catch (SemojumApiException ex) when (ex.Code == "AUTH_DESKTOP_ROLE")
            { check(tokenOnlyFixture.RequestPostCount == 1 && tokenOnlyApi.Account is null, "Token-only refresh clears the institution session and never replays the write"); }
        }
        fixture.Role = "ROLE_USER";
        using var otherHttp = new HttpClient(fixture); var otherApi = new SemojumApiClient(otherHttp); await otherApi.LoginAsync("member", "fixture");
        var other = new InstitutionClient(otherApi); var reads = fixture.Reads;
        try { await other.DashboardAsync(); check(false, "Non-admin dashboard access must be rejected"); } catch (InvalidOperationException) { check(fixture.Reads == reads, "A non-institution-admin cannot enter the institution management client"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal string Role = "ROLE_ORG_ADMIN"; internal int Writes, Reads; internal string? LastUri, LastBody; internal HttpMethod? LastMethod;
        internal bool Malformed, BadReceipt, RejectOnce, TokenOnly; internal string RefreshRole = "ROLE_ORG_ADMIN"; internal int RequestPostCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/login") return Reply(new { accessToken = "fixture", refreshToken = "fixture", role = Role });
            if (path == "/api/auth/refresh") return TokenOnly ? Reply(new { accessToken = "refreshed" }) : Reply(new { accessToken = "refreshed", loginId = "org_admin", role = RefreshRole });
            if (path == "/api/org/requests" && request.Method == HttpMethod.Post)
            {
                RequestPostCount++;
                if (RejectOnce) { RejectOnce = false; return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"AUTH4003\"}", Encoding.UTF8, "application/json") }; }
            }
            LastMethod = request.Method; LastUri = request.RequestUri.PathAndQuery;
            if (request.Method == HttpMethod.Get) Reads++; else Writes++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            object? result = path switch
            {
                "/api/org/dashboard" => new { orgName = "기관 A", orgCode = "A", contractType = "BASIC", contractStartedAt = "2026-01-01", contractExpiresAt = (string?)null, creditAllocated = 10, creditUsed = 15, creditRemaining = -5, monthlyUsage = new[] { new { month = "2026-10", credits = 2 } } },
                "/api/org/accounts" when Malformed => new { usageSince = (string?)null, items = new[] { new { loginId = "a", status = "ACTIVE", role = "ROLE_USER", usedCredits = 1 } } },
                "/api/org/accounts" => new { usageSince = "2026-01-01", items = new[] { new { loginId = "a b", alias = (string?)null, status = "ACTIVE", role = "ROLE_USER", lastLoginAt = (string?)null, usedCredits = 7, self = true } } },
                var p when p.EndsWith("/jobs", StringComparison.Ordinal) => new { loginId = "a b", alias = (string?)null, from = "2026-10-01", to = "2026-10-30", items = new[] { new { jobId = "job-1", fileName = "test.pdf", mode = "a", status = "COMPLETED", totalPages = 3, donePages = (int?)null, failedPages = 1, credits = 4, startedAt = "2026-10-01T00:00:00", finishedAt = (string?)null } }, totalPages = 3, totalCredits = 4 },
                "/api/org/orders" => new { receiptEmail = (string?)null, items = new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", orderDate = "2026-10-01", description = "충전", amountKrw = 1200, creditAmount = (long?)null, paidAt = (string?)null, invoiceStatus = "ISSUED", receiptFileName = "receipt.pdf" } } },
                var p when p.EndsWith("/receipt", StringComparison.Ordinal) => new { fileName = "receipt.pdf", url = BadReceipt ? "http://untrusted.invalid/receipt" : "https://storage.invalid/receipt" },
                "/api/org/notices" => new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", scope = "ORG", title = "공지", body = "내용", startsOn = (string?)null, endsOn = (string?)null, createdAt = "2026-10-01T00:00:00Z" } },
                "/api/org/requests" when request.Method == HttpMethod.Get => new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", type = "CREDIT_ADD", status = "OPEN", message = (string?)null, createdAt = "2026-10-01T00:00:00Z" } },
                "/api/org/requests" when request.Method == HttpMethod.Post => new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", type = "ACCOUNT_ISSUE", status = "OPEN", message = "계정 2개 요청", createdAt = "2026-10-01T00:00:00Z" },
                var p when p.EndsWith("/lock", StringComparison.Ordinal) => new { loginId = "a b", status = "INACTIVE", canceledJobs = 1 },
                _ => null
            };
            return Reply(result);
        }
        private static HttpResponseMessage Reply(object? result) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
    }
}
