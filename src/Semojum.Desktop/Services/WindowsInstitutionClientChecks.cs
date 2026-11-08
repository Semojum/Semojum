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
            && accounts.Items.Length == 2 && accounts.Items[0] is { LoginId: "a b", Alias: null, Self: true, UsedCredits: 7 },
            "Institution dashboard and account data retain server values including a negative balance and nulls");
        check(accounts.Items[1] is { LoginId: "kblib02", UsedCredits: 3120, Email: "math@kblib.or.kr", CreditLimit: 0, MonthUsedCredits: 1140, CreatedAt: "2026-02-24T10:00:00" }
            && accounts.Items[0] is { Email: null, CreditLimit: null, MonthUsedCredits: null, CreatedAt: null },
            "Account rows carry the email, the monthly limit (0 kept apart from null = 제한 없음), this month's usage and the creation time; an older row leaves them empty");
        var jobs = await client.AccountJobsAsync("a b", new(2026, 10, 1), new(2026, 10, 30));
        check(jobs.Items.Single() is { JobId: "job-1", FileName: "국어.pdf", DonePages: null, FailedPages: 1, Credits: 4 } && jobs.TotalPages == 3
            && fixture.LastUri!.Contains("a%20b/jobs?from=2026-10-01&to=2026-10-30", StringComparison.OrdinalIgnoreCase),
            "Institution account detail escapes the login ID, composes decomposed Korean file names and retains actual per-job counts and dates");
        var orders = await client.OrdersAsync(); var receipt = await client.ReceiptAsync(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82"));
        var notices = await client.NoticesAsync(); var requests = await client.RequestsAsync();
        check(orders.Items.Single() is { AmountKrw: 1200, CreditAmount: null, ReceiptFileName: "receipt.pdf" }
            && receipt.Url.StartsWith("https://", StringComparison.Ordinal) && notices.Single().Scope == "ORG" && requests.Single().Status == "OPEN",
            "Institution orders, receipts, notices and request status use server data");
        check(orders.Items.Single() is { RequestedBy: "kblib00", RequestedByRole: "ROLE_ORG_ADMIN", RequestedAt: "2026-06-02T02:20:00Z", QuoteFileName: "견적서.pdf" }
            && notices.Single().Category == "RELEASE" && requests.Single().Details is { CreditAmount: 3000, InvoiceType: "ORG", AccountAlias: null },
            "Orders carry the requester and quote file, notices their category and requests the request window's fields");
        var quote = await client.QuoteAsync(Guid.Parse("3f56ae22-d121-48b1-a804-a3b48e3d3b82"));
        check(quote.Url.StartsWith("https://", StringComparison.Ordinal) && fixture.LastUri == "/api/org/orders/3f56ae22-d121-48b1-a804-a3b48e3d3b82/quote",
            "견적서 내려받기 asks GET /api/org/orders/{orderId}/quote for its presigned URL");
        var estimate = await client.CreditEstimateAsync(3000);
        check(estimate is { Credits: 3000, UnitPriceKrw: 260, AmountKrw: 780000 } && fixture.LastUri == "/api/org/credit-estimate?credits=3000",
            "The credit estimate is the server's VAT-inclusive amount from GET /api/org/credit-estimate?credits=N");
        var estimateReads = fixture.Reads;
        try { await client.CreditEstimateAsync(0); check(false, "A credit estimate below one must be rejected"); } catch (ArgumentOutOfRangeException) { }
        check(fixture.Reads == estimateReads, "A credit estimate below one is rejected before any request");
        fixture.WrongEstimate = true;
        try { await client.CreditEstimateAsync(3000); check(false, "An estimate for another amount must be rejected"); }
        catch (InvalidDataException) { check(true, "An estimate answering a different credit amount is rejected rather than shown"); }
        fixture.WrongEstimate = false;
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
        check(created.CreatedAt is null && created.Details is { AccountAlias: "국어 담당", CreditScope: "SHARED" }, "A just-submitted request may lack createdAt and still returns its fields");
        await client.CreateRequestAsync("CREDIT_ADD", null, new(AccountAlias: "버릴 칸", CreditAmount: 3000, InvoiceType: "ORG"));
        using (var creditBody = JsonDocument.Parse(fixture.LastBody!))
            check(creditBody.RootElement.GetProperty("creditAmount").GetInt64() == 3000 && creditBody.RootElement.GetProperty("invoiceType").GetString() == "ORG"
                && creditBody.RootElement.GetProperty("message").ValueKind == JsonValueKind.Null && !creditBody.RootElement.TryGetProperty("accountAlias", out _),
                "A credit request sends creditAmount and invoiceType as fields, no message text and none of the account-request fields");
        await client.CreateRequestAsync("ACCOUNT_ISSUE", null, new(AccountAlias: "국어 담당", RecipientEmail: "reader@example.test", CreditScope: "NONE", CreditAmount: 5));
        using (var issueBody = JsonDocument.Parse(fixture.LastBody!))
            check(issueBody.RootElement.GetProperty("accountAlias").GetString() == "국어 담당" && issueBody.RootElement.GetProperty("recipientEmail").GetString() == "reader@example.test"
                && issueBody.RootElement.GetProperty("creditScope").GetString() == "NONE" && !issueBody.RootElement.TryGetProperty("creditAmount", out _),
                "An account request sends accountAlias, recipientEmail and creditScope as fields");
        before = fixture.Writes;
        foreach (var bad in new InstitutionRequestDetails[] { new(CreditAmount: 0), new(CreditAmount: 1, InvoiceType: "COMPANY") })
            try { await client.CreateRequestAsync("CREDIT_ADD", null, bad); check(false, "Out-of-range credit request fields must be rejected"); } catch (ArgumentException) { }
        foreach (var bad in new InstitutionRequestDetails[] { new(AccountAlias: new string('가', 51)), new(RecipientEmail: "reader@"), new(CreditScope: "LIMITED") })
            try { await client.CreateRequestAsync("ACCOUNT_ISSUE", null, bad); check(false, "Out-of-range account request fields must be rejected"); } catch (ArgumentException) { }
        check(fixture.Writes == before, "Out-of-range request fields are rejected before any write");
        await client.UpdateAccountAsync("kblib02", " 수학 담당 ", "math@kblib.or.kr", 2000);
        using (var putBody = JsonDocument.Parse(fixture.LastBody!))
            check(fixture.LastMethod == HttpMethod.Put && fixture.LastUri == "/api/org/accounts/kblib02" && putBody.RootElement.GetProperty("alias").GetString() == "수학 담당"
                && putBody.RootElement.GetProperty("email").GetString() == "math@kblib.or.kr" && putBody.RootElement.GetProperty("creditLimit").GetInt64() == 2000,
                "The account save sends PUT /api/org/accounts/{loginId} with alias, email and creditLimit");
        await client.UpdateAccountAsync("kblib02", " ", null, null);
        using (var clearedBody = JsonDocument.Parse(fixture.LastBody!))
            check(clearedBody.RootElement.GetProperty("alias").ValueKind == JsonValueKind.Null && clearedBody.RootElement.GetProperty("email").ValueKind == JsonValueKind.Null
                && clearedBody.RootElement.GetProperty("creditLimit").ValueKind == JsonValueKind.Null,
                "The whole-account PUT sends cleared fields as null, so a null limit means 제한 없음");
        before = fixture.Writes;
        foreach (var (alias, email, limit) in new (string?, string?, long?)[] { (new string('가', 51), null, null), (null, "math@", null), (null, null, -1) })
            try { await client.UpdateAccountAsync("kblib02", alias, email, limit); check(false, "Invalid account fields must be rejected"); } catch (ArgumentException) { }
        check(fixture.Writes == before, "An over-long alias, a malformed email or a negative limit is rejected before the PUT");
        await client.SetReceiptEmailAsync(" account@kblib.or.kr ");
        using (var receiptBody = JsonDocument.Parse(fixture.LastBody!))
            check(fixture.LastMethod == HttpMethod.Patch && fixture.LastUri == "/api/org/receipt-email" && receiptBody.RootElement.GetProperty("email").GetString() == "account@kblib.or.kr",
                "The receipt recipient is changed with PATCH /api/org/receipt-email and a trimmed { email } body");
        await client.SetReceiptEmailAsync("  ");
        using (var clearBody = JsonDocument.Parse(fixture.LastBody!))
            check(fixture.LastMethod == HttpMethod.Patch && clearBody.RootElement.GetProperty("email").ValueKind == JsonValueKind.Null, "A blank receipt recipient is sent as email null, which removes it");
        before = fixture.Writes;
        foreach (var bad in new[] { "account@", "account", "회계 <account@kblib.or.kr>" })
            try { await client.SetReceiptEmailAsync(bad); check(false, $"Malformed receipt address {bad} must be rejected"); } catch (ArgumentException) { }
        check(fixture.Writes == before, "A malformed receipt address is rejected before any write");
        fixture.ForbidReceipt = true;
        try { await client.SetReceiptEmailAsync("account@kblib.or.kr"); check(false, "A server 403 on the receipt recipient must surface"); }
        catch (SemojumApiException ex) { check(ex.Code == "COMMON4003" && api.Account is not null, "A 403 COMMON4003 receipt change surfaces the server error without clearing the session"); }
        fixture.ForbidReceipt = false;
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
        var receiptRefreshFixture = new Handler { RejectReceiptOnce = true };
        using (var receiptRefreshHttp = new HttpClient(receiptRefreshFixture))
        {
            var receiptRefreshApi = new SemojumApiClient(receiptRefreshHttp);
            receiptRefreshApi.RefreshedIdentityAllowed = next => AutomaticLogin.IsSameDesktopArea(receiptRefreshApi.Account, next);
            await receiptRefreshApi.LoginAsync("org_admin", "fixture");
            await new InstitutionClient(receiptRefreshApi).SetReceiptEmailAsync("account@kblib.or.kr");
            check(receiptRefreshFixture.ReceiptPatchCount == 2 && receiptRefreshFixture.LastBody?.Contains("account@kblib.or.kr") == true && receiptRefreshApi.Account is not null,
                "An expired token on the receipt change refreshes the same administrator and sends the change once more");
        }
        fixture.Role = "ROLE_USER";
        using var otherHttp = new HttpClient(fixture); var otherApi = new SemojumApiClient(otherHttp); await otherApi.LoginAsync("member", "fixture");
        var other = new InstitutionClient(otherApi); var reads = fixture.Reads;
        try { await other.DashboardAsync(); check(false, "Non-admin dashboard access must be rejected"); } catch (InvalidOperationException) { check(fixture.Reads == reads, "A non-institution-admin cannot enter the institution management client"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal string Role = "ROLE_ORG_ADMIN"; internal int Writes, Reads; internal string? LastUri, LastBody; internal HttpMethod? LastMethod;
        internal bool Malformed, BadReceipt, RejectOnce, TokenOnly, ForbidReceipt, RejectReceiptOnce, WrongEstimate; internal string RefreshRole = "ROLE_ORG_ADMIN"; internal int RequestPostCount, ReceiptPatchCount;
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
            if (path == "/api/org/receipt-email" && request.Method == HttpMethod.Patch)
            {
                ReceiptPatchCount++;
                if (RejectReceiptOnce) { RejectReceiptOnce = false; return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"AUTH4003\"}", Encoding.UTF8, "application/json") }; }
                if (ForbidReceipt) return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"isSuccess\":false,\"code\":\"COMMON4003\",\"message\":\"권한이 없습니다.\",\"result\":null}", Encoding.UTF8, "application/json") };
            }
            LastMethod = request.Method; LastUri = request.RequestUri.PathAndQuery;
            if (request.Method == HttpMethod.Get) Reads++; else Writes++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            object? result = path switch
            {
                "/api/org/dashboard" => new { orgName = "기관 A", orgCode = "A", contractType = "BASIC", contractStartedAt = "2026-01-01", contractExpiresAt = (string?)null, creditAllocated = 10, creditUsed = 15, creditRemaining = -5, monthlyUsage = new[] { new { month = "2026-10", credits = 2 } } },
                "/api/org/accounts" when Malformed => new { usageSince = (string?)null, items = new[] { new { loginId = "a", status = "ACTIVE", role = "ROLE_USER", usedCredits = 1 } } },
                "/api/org/accounts" => new { usageSince = "2026-01-01", items = new object[] { new { loginId = "a b", alias = (string?)null, status = "ACTIVE", role = "ROLE_USER", lastLoginAt = (string?)null, usedCredits = 7, self = true },
                    new { loginId = "kblib02", alias = "수학 담당", status = "ACTIVE", role = "ROLE_USER", lastLoginAt = (string?)null, usedCredits = 3120, self = false, email = "math@kblib.or.kr", creditLimit = 0, monthUsedCredits = 1140, createdAt = "2026-02-24T10:00:00" } } },
                "/api/org/credit-estimate" when long.TryParse(request.RequestUri.Query.Split('=').Last(), out var credits) => new { credits = WrongEstimate ? credits + 1 : credits, unitPriceKrw = 260, amountKrw = credits * 260 },
                var p when p.EndsWith("/jobs", StringComparison.Ordinal) => new { loginId = "a b", alias = (string?)null, from = "2026-10-01", to = "2026-10-30", items = new[] { new { jobId = "job-1", fileName = "국어.pdf".Normalize(NormalizationForm.FormD), mode = "a", status = "COMPLETED", totalPages = 3, donePages = (int?)null, failedPages = 1, credits = 4, startedAt = "2026-10-01T00:00:00", finishedAt = (string?)null } }, totalPages = 3, totalCredits = 4 },
                "/api/org/orders" => new { receiptEmail = (string?)null, items = new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", orderDate = "2026-10-01", description = "충전", amountKrw = 1200, creditAmount = (long?)null, paidAt = (string?)null, invoiceStatus = "ISSUED", receiptFileName = "receipt.pdf",
                    requestedBy = "kblib00", requestedByRole = "ROLE_ORG_ADMIN", requestedAt = "2026-06-02T02:20:00Z", quoteFileName = "견적서.pdf".Normalize(NormalizationForm.FormD) } } },
                var p when p.EndsWith("/receipt", StringComparison.Ordinal) || p.EndsWith("/quote", StringComparison.Ordinal) => new { fileName = "receipt.pdf", url = BadReceipt ? "http://untrusted.invalid/receipt" : "https://storage.invalid/receipt" },
                "/api/org/notices" => new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", scope = "ORG", title = "공지", body = "내용", startsOn = (string?)null, endsOn = (string?)null, createdAt = "2026-10-01T00:00:00Z", category = "RELEASE" } },
                "/api/org/requests" when request.Method == HttpMethod.Get => new[] { new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", type = "CREDIT_ADD", status = "OPEN", message = (string?)null, createdAt = "2026-10-01T00:00:00Z",
                    details = new { accountAlias = (string?)null, recipientEmail = (string?)null, creditScope = (string?)null, creditAmount = 3000, invoiceType = "ORG" } } },
                // The server may answer a fresh request before its createdAt is filled in.
                "/api/org/requests" when request.Method == HttpMethod.Post => new { id = "3f56ae22-d121-48b1-a804-a3b48e3d3b82", type = "ACCOUNT_ISSUE", status = "OPEN", message = "계정 2개 요청", createdAt = (string?)null,
                    details = new { accountAlias = "국어 담당", recipientEmail = "reader@example.test", creditScope = "SHARED", creditAmount = (long?)null, invoiceType = (string?)null } },
                var p when p.EndsWith("/lock", StringComparison.Ordinal) => new { loginId = "a b", status = "INACTIVE", canceledJobs = 1 },
                _ => null
            };
            return Reply(result);
        }
        private static HttpResponseMessage Reply(object? result) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { isSuccess = true, result }), Encoding.UTF8, "application/json") };
    }
}
