using System.Text.Json;

using Semojum.Core.Integration;

namespace Semojum.Feature.Rules;

public sealed record BrailleRule(string RuleId, string Source, string Section, string Name, string Contents,
    string? FullText = null, bool? Supported = null)
{
    public bool HasFullText => !string.IsNullOrWhiteSpace(FullText);
    public string ExpandedText => HasFullText ? FullText! : Contents;
    public string SupportLabel => Supported == false ? "점역 미지원 · 검색 전용" : "";
}
public sealed record RuleSearchPage(BrailleRule[] Items, int TotalPages, int TotalElements);

public sealed class RuleClient(SemojumApiClient api)
{
    public async Task<RuleSearchPage> SearchAsync(string query, string? publisher, int page = 0, CancellationToken ct = default)
    {
        if (page < 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (publisher is not (null or "" or "MCST" or "NLD" or "NISE")) throw new ArgumentException("알 수 없는 규정 문서입니다.");
        var path = $"/api/rules?page={page}&size=20";
        if (!string.IsNullOrWhiteSpace(query)) path += "&q=" + Uri.EscapeDataString(query.Trim());
        if (!string.IsNullOrEmpty(publisher)) path += "&publisher=" + publisher;
        return ReadPage(await api.SendAuthenticatedAsync(HttpMethod.Get, path, cancellationToken: ct));
    }

    public static RuleSearchPage ReadPage(JsonElement data)
    {
        static string Field(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
        var rows = data.GetProperty("items").EnumerateArray().Select(row => new BrailleRule(
            Field(row, "ruleId"), Field(row, "source"), Field(row, "section"), Field(row, "ruleName"), Field(row, "contents"),
            Field(row, "fullText"), row.TryGetProperty("supported", out var supported) && supported.ValueKind is JsonValueKind.True or JsonValueKind.False ? supported.GetBoolean() : null)).ToArray();
        return new(rows, data.GetProperty("totalPages").GetInt32(), data.GetProperty("totalElements").GetInt32());
    }
}
