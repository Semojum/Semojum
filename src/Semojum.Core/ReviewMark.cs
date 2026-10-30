namespace Semojum.Core;

/// <summary>A local, editor-authored review annotation. Offsets are UTF-16 offsets in one block.</summary>
public enum ReviewKind { SuggestCorrection, SuggestDeletion, HumanConfirmed }
public enum ReviewPanel { Text, Braille }

/// <param name="OriginalText">The text the author actually reviewed, retained even when the annotation is stale.</param>
public sealed record ReviewMark(string Id, ReviewKind Kind, ReviewPanel Panel, int Start, int Length, string OriginalText, bool IsStale = false);

internal static class ReviewAnchors
{
    // The editor often replaces an entire buffer. Determine a conservative single changed
    // interval; separate changes within it make the mark stale rather than guessing its target.
    internal static ReviewMark Reanchor(ReviewMark mark, string before, string after)
    {
        if (before == after) return mark;
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        // The longest common prefix is not necessarily the edit location: inserting or
        // deleting a repeated character can yield the same result at several offsets.
        // Compare the extreme valid locations, which bound every equivalent single edit.
        var suffix = 0;
        while (suffix < before.Length && suffix < after.Length && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        var sharedSuffix = Math.Min(suffix, Math.Min(before.Length - prefix, after.Length - prefix));
        var removed = before.Length - prefix - sharedSuffix;
        var inserted = after.Length - prefix - sharedSuffix;
        var earliest = Math.Max(0, before.Length - suffix - removed);
        var first = Map(mark, earliest, removed, inserted, after);
        var last = Map(mark, prefix, removed, inserted, after);
        if (first != last)
            return mark with { Start = SafeBoundary(after, Math.Min(first.Start, last.Start)), Length = 0, IsStale = true };
        return first;
    }

    private static ReviewMark Map(ReviewMark mark, int editStart, int removed, int inserted, string after)
    {
        var removedEnd = editStart + removed;
        var start = mark.Start;
        var end = mark.Start + mark.Length;
        if (removedEnd <= start && (removed > 0 || editStart <= start)) start += inserted - removed;
        else if (editStart < end && (removedEnd > start || removed == 0 && editStart > start))
            return mark with { Start = SafeBoundary(after, editStart), Length = 0, IsStale = true };
        start = Math.Clamp(start, 0, after.Length);
        var updated = mark with { Start = start, Length = Math.Min(mark.Length, after.Length - start) };
        if (SplitsSurrogate(after, updated.Start) || SplitsSurrogate(after, updated.Start + updated.Length)
            || !updated.IsStale && (updated.Length == 0 || !after.AsSpan(updated.Start, updated.Length).SequenceEqual(updated.OriginalText.AsSpan())))
            return updated with { Start = SafeBoundary(after, updated.Start), Length = 0, IsStale = true };
        return updated;
    }

    internal static bool Valid(ReviewMark? mark, string source, string braille)
    {
        if (mark is null || string.IsNullOrWhiteSpace(mark.Id) || !Enum.IsDefined(mark.Kind) || !Enum.IsDefined(mark.Panel)
            || string.IsNullOrEmpty(mark.OriginalText) || mark.Start < 0 || mark.Length < 0) return false;
        var text = mark.Panel == ReviewPanel.Text ? source : braille;
        if (mark.Start > text.Length || mark.Length > text.Length - mark.Start) return false;
        if (SplitsSurrogate(text, mark.Start) || SplitsSurrogate(text, mark.Start + mark.Length)) return false;
        return mark.IsStale || mark.Length > 0 && text.AsSpan(mark.Start, mark.Length).SequenceEqual(mark.OriginalText.AsSpan());
    }

    private static bool SplitsSurrogate(string text, int offset) => offset > 0 && offset < text.Length
        && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
    private static int SafeBoundary(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        return SplitsSurrogate(text, offset) ? offset - 1 : offset;
    }
}
