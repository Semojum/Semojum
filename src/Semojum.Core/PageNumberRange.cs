namespace Semojum.Core;

public sealed record PageNumberRange(int Start, int? End)
{
    public static void Validate(PageNumberRange[]? ranges)
    {
        if (ranges is null) return;
        if (ranges.Length == 0 || ranges.Length > 1000) throw new ArgumentException("페이지 번호 범위를 1개 이상 지정해 주세요.");
        var end = 0;
        foreach (var r in ranges.OrderBy(r => r?.Start))
        {
            if (r is null || r.Start < 1 || r.Start <= end || r.End < r.Start) throw new ArgumentException("페이지 번호 범위는 1 이상이며 서로 겹치지 않아야 합니다.");
            end = r.End ?? int.MaxValue;
        }
    }
    public static PageNumberRange[]? Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var ranges = value.Split(',').Select(part =>
        {
            var bounds = part.Trim().Split('-');
            if (bounds.Length > 2 || !int.TryParse(bounds[0], out var start)) throw new ArgumentException("범위를 1-5, 8, 10- 형식으로 입력해 주세요.");
            int? end = start;
            if (bounds.Length == 2)
            {
                if (string.IsNullOrWhiteSpace(bounds[1])) end = null;
                else if (int.TryParse(bounds[1], out var n)) end = n;
                else throw new ArgumentException("끝 면을 숫자로 입력해 주세요.");
            }
            return new PageNumberRange(start, end);
        }).OrderBy(r => r.Start).ToArray();
        Validate(ranges); return ranges;
    }
    public static string Format(PageNumberRange[]? ranges) => string.Join(", ", (ranges ?? []).Select(r => r.End == r.Start ? r.Start.ToString() : $"{r.Start}-{r.End}"));
}
