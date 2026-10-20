namespace Semojum.Core.Integration;

/// <summary>
/// What the verified server contract (Semojum/BE dev 5d8dda0a) can store. Values outside it are kept in .smj and reported,
/// never rewritten to a different value and never sent as unknown fields that the server would silently drop.
/// </summary>
public static class ServerContract
{
    /// <summary>BE LayoutOptions.pageNumberLine accepts odd/every/none. "짝수 페이지" has no server value → null (key omitted).</summary>
    public static string? PageNumberLine(string pageNumbers) => pageNumbers switch
    {
        "홀수 페이지" => "odd", "모든 페이지" => "every", "출력 안 함" => "none", _ => null
    };

    /// <summary>Layout settings the server cannot keep, as user-facing notes. Empty when everything round-trips.</summary>
    public static string[] LocalOnlyLayout(PageLayout layout, IReadOnlyList<FooterRange>? footerRanges)
    {
        var notes = new List<string>();
        if (PageNumberLine(layout.PageNumbers) is null)
            notes.Add($"페이지행 ‘{layout.PageNumbers}’는 서버가 홀수/전체/출력 안 함만 저장하므로 서버 값은 바뀌지 않고 .smj에만 보존됩니다(BE B-06).");
        if (layout.NumberRanges is { Length: > 0 })
            notes.Add($"번호 표시 범위({PageNumberRange.Format(layout.NumberRanges)})는 서버 계약에 없어 .smj에만 보존됩니다(BE B-06).");
        if (layout.Footer != new PageLayout().Footer)
            notes.Add($"꼬리말 출력 면 ‘{layout.Footer}’는 서버 계약에 없어 .smj에만 보존됩니다(BE B-06).");
        if (footerRanges is { Count: > 0 })
            notes.Add($"꼬리말 범위 {footerRanges.Count}건과 범위별 정렬은 서버가 기본 꼬리말·정렬만 저장하므로 .smj에만 보존됩니다(BE B-06).");
        if (layout.PadToEven)
            notes.Add("마지막 짝수 면 보정은 서버 계약에 없어 .smj에만 보존됩니다(BE B-06).");
        return notes.ToArray();
    }

    /// <summary>Settings a server-side braille form cannot hold. Forms have no local file, so these are rejected instead of dropped.</summary>
    public static string[] UnsupportedInForm(PageLayout layout)
    {
        var notes = new List<string>();
        if (PageNumberLine(layout.PageNumbers) is null) notes.Add($"페이지행 ‘{layout.PageNumbers}’ (서버 양식은 홀수/전체/출력 안 함만 지원)");
        if (layout.NumberRanges is { Length: > 0 }) notes.Add("번호 표시 범위");
        if (layout.Footer != new PageLayout().Footer) notes.Add($"꼬리말 출력 면 ‘{layout.Footer}’");
        if (layout.PadToEven) notes.Add("마지막 짝수 면 보정");
        return notes.ToArray();
    }
}
