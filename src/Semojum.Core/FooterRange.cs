namespace Semojum.Core;

/// <summary>Ranges use printed braille numbers; unnumbered cover faces are excluded by the engine.</summary>
public sealed record FooterRange(int Start, int? End, string Text)
{
    public static void Validate(IReadOnlyList<FooterRange> ranges)
    {
        if (ranges.Count > 1000) throw new ArgumentException("꼬리말 범위는 1,000개까지 지정할 수 있습니다.");
        var end = 0;
        foreach (var range in ranges.OrderBy(r => r?.Start))
        {
            if (range is null || range.Start < 1 || range.Start <= end || range.End < range.Start || range.Text is null || range.Text.Length > 200)
                throw new ArgumentException("시작 면은 1 이상, 끝 면은 시작 면 이상으로 입력하고 범위가 겹치지 않게 해 주세요. 꼬리말은 200자까지입니다.");
            end = range.End ?? int.MaxValue;
        }
    }
}
