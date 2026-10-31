using System.Text.Json;

using Semojum.Core;
using Semojum.Core.Integration;

namespace Semojum.Feature.Settings;

public sealed record AccountDefaults(BrailleOptions Options, string? FormId, string PageNumberLine);
public sealed record BrailleForm(string FormId, string Name, bool Builtin, string? BaseFormId, PageLayout Layout, BrailleOptions Options)
{
    public override string ToString() => Name + (Builtin ? " · 기본" : " · 사용자");
}

public sealed class SettingsClient(SemojumApiClient api)
{
    public async Task<AccountDefaults> DefaultsAsync(CancellationToken ct = default) => ReadDefaults(
        await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/braille-options", cancellationToken: ct));
    public async Task<AccountDefaults> SaveDefaultsAsync(AccountDefaults value, CancellationToken ct = default)
    {
        var body = JobClient.OptionsBody(value.Options);
        body["formId"] = value.FormId!; body["pageNumberLine"] = value.PageNumberLine;
        return ReadDefaults(await api.SendAuthenticatedAsync(HttpMethod.Put, "/api/users/braille-options", body, ct));
    }
    public async Task<BrailleForm[]> FormsAsync(CancellationToken ct = default) =>
        (await api.SendAuthenticatedAsync(HttpMethod.Get, "/api/users/braille-forms", cancellationToken: ct))
        .GetProperty("items").EnumerateArray().Select(ReadForm).ToArray();
    public async Task<BrailleForm> SaveFormAsync(string? id, string name, string? baseId, PageLayout layout, BrailleOptions options, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100) throw new ArgumentException("양식 이름은 1–100자입니다.");
        if (id?.StartsWith("builtin:", StringComparison.Ordinal) == true) throw new ArgumentException("기본 양식은 수정할 수 없습니다.");
        layout.Validate();
        // A form lives only on the server: a value it cannot hold would be dropped or rewritten, so it is rejected up front (C-04).
        if (ServerContract.UnsupportedInForm(layout) is { Length: > 0 } unsupported)
            throw new ArgumentException("서버 양식에 저장할 수 없는 설정입니다: " + string.Join(", ", unsupported) + ". 값을 바꾸거나 작업 설정으로만 사용해 주세요.");
        return ReadForm(await api.SendAuthenticatedAsync(id is null ? HttpMethod.Post : HttpMethod.Patch,
            "/api/users/braille-forms" + (id is null ? "" : "/" + Uri.EscapeDataString(id)),
            new { name = name.Trim(), baseFormId = baseId, layoutOptions = JobClient.LayoutBody(layout), brailleOptions = JobClient.OptionsBody(options) }, ct));
    }
    public Task DeleteFormAsync(BrailleForm form, CancellationToken ct = default)
    {
        if (form.Builtin) throw new ArgumentException("기본 양식은 삭제할 수 없습니다.");
        return api.SendAuthenticatedAsync(HttpMethod.Delete, "/api/users/braille-forms/" + Uri.EscapeDataString(form.FormId), cancellationToken: ct);
    }
    private static AccountDefaults ReadDefaults(JsonElement j) => new(JobClient.ReadOptions(j),
        j.TryGetProperty("formId", out var f) ? f.GetString() : null,
        j.TryGetProperty("pageNumberLine", out var p) ? p.GetString() ?? "odd" : "odd");
    private static BrailleForm ReadForm(JsonElement j) => new(j.GetProperty("formId").GetString()!, j.GetProperty("name").GetString()!,
        j.GetProperty("builtin").GetBoolean(), j.TryGetProperty("baseFormId", out var b) ? b.GetString() : null,
        JobClient.ReadLayout(j.GetProperty("layoutOptions")), JobClient.ReadOptions(j.GetProperty("brailleOptions")));
}
