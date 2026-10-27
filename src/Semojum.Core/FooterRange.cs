namespace Semojum.Core;

/// <summary>Ranges use printed braille numbers; unnumbered cover faces are excluded by the engine.</summary>
public sealed record FooterRange(int Start, int? End, string Text, string? Alignment = null)
{
    // Convert explicit legacy end points into change points without changing any printed footer.
    public static FooterRange[] ChangePoints(IReadOnlyList<FooterRange> current, string fallback, string alignment)
    {
        Validate(current);
        var points = new SortedDictionary<int, FooterRange>();
        if (current.Count == 0) return [];
        if (current.Min(r => r.Start) > 1) points[1] = new(1, null, fallback, alignment);
        foreach (var range in current.OrderBy(r => r.Start))
        {
            points[range.Start] = range with { End = null };
            if (range.End is { } end && end < int.MaxValue) points[end + 1] = new(end + 1, null, fallback, alignment);
        }
        return Continue(points.Values.ToArray());
    }
    public static FooterRange[] Continue(IReadOnlyList<FooterRange> points)
    {
        var sorted = points.OrderBy(r => r.Start).ToArray();
        var result = sorted.Select((r, i) => r with { End = i + 1 < sorted.Length ? sorted[i + 1].Start - 1 : null }).ToArray();
        Validate(result); return result;
    }
    public static FooterRange[] Insert(IReadOnlyList<FooterRange> current, FooterRange inserted)
    {
        Validate(current); Validate([inserted]);
        var next = current.Where(r => r.Start > inserted.Start).OrderBy(r => r.Start).FirstOrDefault();
        var values = current.Where(r => r.Start != inserted.Start)
            .Select(r => r.Start < inserted.Start && (r.End is null || r.End >= inserted.Start) ? r with { End = inserted.Start - 1 } : r)
            .Append(inserted with { End = next is null ? inserted.End : Math.Min(inserted.End ?? int.MaxValue, next.Start - 1) })
            .OrderBy(r => r.Start).ToArray();
        Validate(values); return values;
    }
    public static void Validate(IReadOnlyList<FooterRange> ranges)
    {
        if (ranges.Count > 1000) throw new ArgumentException("꼬리말 범위는 1,000개까지 지정할 수 있습니다.");
        var end = 0;
        foreach (var range in ranges.OrderBy(r => r?.Start))
        {
            if (range is null || range.Start < 1 || range.Start <= end || range.End < range.Start || range.Text is null || range.Text.Length > 200 || range.Alignment is not (null or "left" or "center" or "right"))
                throw new ArgumentException("시작 면은 1 이상, 끝 면은 시작 면 이상으로 입력하고 범위가 겹치지 않게 해 주세요. 꼬리말은 200자까지입니다.");
            end = range.End ?? int.MaxValue;
        }
    }
}
