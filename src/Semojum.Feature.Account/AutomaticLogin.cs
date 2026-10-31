using System.ComponentModel;
using System.IO;
using System.Net.Http;
using Semojum.Core.Integration;

namespace Semojum.Feature.Account;

public sealed record AutomaticLoginResult(AccountIdentity? Account, string? Message);
public static class AutomaticLogin
{
    public static bool IsEditorAllowed(AccountIdentity account) => account.Role != "ROLE_ORG_ADMIN" && account is not { Role: "ROLE_ADMIN", AdminScope: "WEB" };
    public static bool IsDesktopAllowed(AccountIdentity account) => account.Role == "ROLE_ORG_ADMIN" || IsEditorAllowed(account);
    public static bool IsSameDesktopArea(AccountIdentity? current, AccountIdentity next) =>
        current?.Role == "ROLE_ORG_ADMIN" ? next.Role == "ROLE_ORG_ADMIN" : IsEditorAllowed(next);
    public static async Task<string?> RememberAsync(SemojumApiClient api, IAccountCredentialStore credentials)
    {
        try
        {
            credentials.Write(api.GetSessionCredential() ?? throw new InvalidOperationException("로그인 세션이 없습니다."));
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await api.LogoutAsync(timeout.Token); }
            catch (Exception logoutError) when (logoutError is HttpRequestException or SemojumApiException or OperationCanceledException) { }
            return "로그인 정보를 안전하게 저장하지 못해 로그인을 취소했습니다. " + ex.Message;
        }
    }
    public static async Task LogoutAsync(SemojumApiClient api, IAccountCredentialStore credentials)
    {
        var credential = api.GetOwnedSessionCredential();
        if (credential is not null) credentials.DeleteIfMatches(credential);
        try { await api.LogoutAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or SemojumApiException) { }
    }
    public static async Task<AutomaticLoginResult> RestoreAsync(SemojumApiClient api, IAccountCredentialStore credentials)
    {
        SessionCredential? credential = null;
        try
        {
            credential = credentials.Read();
            if (credential is null) return new(null, null);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var account = await api.RestoreSessionAsync(credential, timeout.Token);
            if (account is null) return new(null, "서버에서 현재 계정 권한을 확인할 수 없어 다시 로그인해 주세요.");
            if (!IsDesktopAllowed(account))
            {
                using var logoutTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                try { await api.LogoutAsync(logoutTimeout.Token); }
                catch (Exception ex) when (ex is HttpRequestException or SemojumApiException or OperationCanceledException) { }
                credentials.DeleteIfMatches(credential);
                return new(null, "현재 계정은 에디터를 사용할 수 없습니다. 다른 계정으로 로그인해 주세요.");
            }
            return new(account, null);
        }
        catch (SemojumApiException ex) when (ex.Code is "AUTH4003" or "AUTH4004" or "USER4001")
        {
            try { if (credential is not null) credentials.DeleteIfMatches(credential); }
            catch (Exception deleteError) when (deleteError is Win32Exception or IOException or UnauthorizedAccessException)
            { return new(null, "만료된 로그인 정보를 삭제하지 못했습니다. " + deleteError.Message); }
            return new(null, "로그인 정보가 만료되었거나 계정을 사용할 수 없습니다. 다시 로그인해 주세요.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { return new(null, "서버에 연결하지 못해 자동 로그인하지 못했습니다. 연결 상태를 확인해 주세요."); }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or SemojumApiException)
        { return new(null, "자동 로그인을 확인하지 못했습니다. 다시 로그인해 주세요. " + ex.Message); }
    }
}
