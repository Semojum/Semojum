namespace Semojum.Core;

public enum ExportScope { All, OriginalPages, BraillePages }
public sealed record ExportRange(ExportScope Scope = ExportScope.All, int Start = 1, int End = 1)
{
    public void Validate(int minimum, int maximum)
    {
        if (Scope == ExportScope.All) return;
        if (Start < minimum || End > maximum || Start > End)
            throw new ArgumentException($"범위는 {minimum}–{maximum} 안에서 시작 번호가 끝 번호보다 크지 않게 입력해 주세요.");
    }
}

public static class DocumentExport
{
    public static int[] OriginalNumbers(SmjManifest source) => source.Blocks.Select(b => b.Page)
        .Concat(source.PageStatuses?.Keys ?? Enumerable.Empty<int>())
        .Concat(source.OriginalPages?.Keys ?? Enumerable.Empty<int>()).Distinct().Order().ToArray();

    public static SmjManifest SelectOriginal(SmjManifest source, ExportRange range)
    {
        if (range.Scope != ExportScope.OriginalPages) return source;
        var numbers = OriginalNumbers(source);
        if (numbers.Length == 0) throw new InvalidOperationException("내보낼 원본 쪽이 없습니다.");
        range.Validate(numbers[0], numbers[^1]);
        var selected = numbers.Where(n => n >= range.Start && n <= range.End).ToArray();
        var blocks = source.Blocks.Where(b => b.Page >= range.Start && b.Page <= range.End).ToArray();
        if (blocks.Length == 0) throw new InvalidOperationException("선택한 범위에 내보낼 블록이 없습니다.");
        var before = Array.IndexOf(numbers, selected[0]);
        var covers = Math.Min(selected.Length, Math.Max(0, source.Layout.CoverPages - before));
        return source with
        {
            Blocks = blocks,
            Layout = source.Layout with
            {
                CoverPages = covers,
                SourcePageStart = source.Layout.SourcePageStart + Math.Max(0, before - source.Layout.CoverPages)
            },
            PageStatuses = source.PageStatuses?.Where(p => selected.Contains(p.Key)).ToDictionary(),
            OriginalPages = source.OriginalPages?.Where(p => selected.Contains(p.Key)).ToDictionary()
        };
    }

    public static void RequireCompletedPages(SmjManifest source)
    {
        var missing = source.PageStatuses?.Where(p => p.Value is not ("COMPLETED" or "NEEDS_REVIEW")).Select(p => p.Key).Order().ToArray() ?? [];
        if (missing.Length > 0)
            throw new InvalidOperationException($"변환되지 않은 쪽({string.Join(", ", missing)})이 포함되어 있습니다. 완료된 원본 쪽 범위를 선택해 주세요.");
    }

    public static string[] BrfPages(string brf)
    {
        if (brf.Length == 0) return [];
        if (!brf.EndsWith('\f') || brf.Any(c => c > 127)) throw new InvalidDataException("완전한 BRF 면 구분을 확인할 수 없습니다.");
        // Preserve blank pages and exact CRLF/footer contents. Only remove the final terminator's empty segment.
        return brf.Split('\f')[..^1];
    }

    public static string SelectBraille(string brf, ExportRange range)
    {
        if (range.Scope != ExportScope.BraillePages) return brf;
        var pages = BrfPages(brf); range.Validate(1, pages.Length);
        return string.Concat(pages.Skip(range.Start - 1).Take(range.End - range.Start + 1).Select(p => p + '\f'));
    }

    public static string BrailleText(IEnumerable<string[]> pages) =>
        string.Concat(pages.Select(lines => string.Join("\r\n", lines) + "\r\n\f"));
}
