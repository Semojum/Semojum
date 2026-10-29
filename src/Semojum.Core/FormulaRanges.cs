namespace Semojum.Core;

public enum FormulaRangeStatus { None, Selected, Malformed }
public sealed record FormulaRange(FormulaRangeStatus Status, int Start = 0, int Length = 0, string Latex = "");

public static class FormulaRanges
{
    public static FormulaRange Find(string source, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (start < 0 || length < 0 || start > source.Length || length > source.Length - start)
            return new(FormulaRangeStatus.None);
        const string open = "<!수식>", close = "<!/수식>";
        var ranges = new List<FormulaRange>();
        var offset = 0;
        while (offset < source.Length)
        {
            var firstOpen = source.IndexOf(open, offset, StringComparison.Ordinal);
            var firstClose = source.IndexOf(close, offset, StringComparison.Ordinal);
            var openPrefix = source.IndexOf("<!수식", offset, StringComparison.Ordinal);
            var closePrefix = source.IndexOf("<!/수식", offset, StringComparison.Ordinal);
            if (openPrefix >= 0 && openPrefix != firstOpen || closePrefix >= 0 && closePrefix != firstClose)
                return new(FormulaRangeStatus.Malformed);
            if (firstClose >= 0 && (firstOpen < 0 || firstClose < firstOpen)) return new(FormulaRangeStatus.Malformed);
            if (firstOpen < 0) break;
            var contentStart = firstOpen + open.Length;
            var nextClose = source.IndexOf(close, contentStart, StringComparison.Ordinal);
            var nested = source.IndexOf("<!수식", contentStart, StringComparison.Ordinal);
            var closingPrefix = source.IndexOf("<!/수식", contentStart, StringComparison.Ordinal);
            if (nextClose < 0 || closingPrefix != nextClose || nested >= 0 && nested < nextClose) return new(FormulaRangeStatus.Malformed);
            var end = nextClose + close.Length;
            ranges.Add(new(FormulaRangeStatus.Selected, firstOpen, end - firstOpen, source[contentStart..nextClose]));
            offset = end;
        }
        foreach (var range in ranges)
        {
            var end = range.Start + range.Length;
            if (start >= range.Start && (length == 0 ? start < end || start == end && end == source.Length : start + length <= end))
                return range;
        }
        return new(FormulaRangeStatus.None);
    }
}
